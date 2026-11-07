/* ==========================================================================================
 * GameServices —— 服务组合根（Composition Root）
 *
 * 【职责】
 *   在一个地方把框架的各个服务 new 出来并接好依赖；上层（UPGameRoot / 场景里的
 *   IGameServicesConsumer）只通过 IGameServices 接口拿服务，不关心具体实现。
 *
 * 【设计约束】
 *   1) 只提供“启动时装配”，不提供运行中替换服务的全局注册表 —— 避免全局可变状态。
 *   2) 所有 Unity 对象操作必须在主线程；本类不负责切线程。
 *   3) 关闭是幂等的（多次调用只真正执行一次），且逆序释放：
 *      SceneService → GameFlow → 移除计时器帧驱动 → Timer → AppLifecycle → Audio → Input → Time。
 *      某一步失败会聚合成 AggregateException，不会中断后续释放。
 *
 * 【计时器帧驱动】
 *   TimerService 本身不知道帧循环，由本类把它挂到 PublicMono.Update 上；
 *   测试里可以不挂，直接手动调 TimerService.Tick 推进时间。
 * ========================================================================================== */

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>
    /// 启动装配结果。持有各服务的<b>接口</b>视图（供业务使用）与<b>具体类型</b>视图（供框架内部/诊断使用）。
    /// <para>不提供运行中替换服务的能力；生命周期与 UPGameRoot 一致。</para>
    /// </summary>
    public sealed class GameServices : IGameServices
    {
        // ---- 接口视图：业务代码应该只用这些 ----
        /// <summary>资源获取（统一持有协议 + 取消安全）</summary>
        public IAssetService Assets { get; }
        /// <summary>音频（委托给旧的 AudioMgr，接口化后便于替换）</summary>
        public IAudioService Audio { get; }
        /// <summary>存档（版本化序列化适配）</summary>
        public ISaveService Saves { get; }
        /// <summary>场景加载/卸载（串行队列 + 资源作用域）</summary>
        public ISceneService Scenes => SceneService;
        /// <summary>输入（动作映射 + 上下文栈）</summary>
        public IInputService Input => InputService;
        /// <summary>暂停（与 Clock 是同一个 GameTimeService 的两个视角）</summary>
        public IPauseService Pause => TimeService;
        /// <summary>游戏时钟（暂停时 DeltaTime 为 0）</summary>
        public IGameClock Clock => TimeService;
        /// <summary>计时器（Delay / Every / DelayAsync）</summary>
        public ITimerService Timer => TimerService;
        /// <summary>配置注册中心</summary>
        public IConfigService Config => ConfigService;
        /// <summary>游戏流程状态机</summary>
        public IGameFlowService Flow => FlowController;
        /// <summary>应用生命周期事件</summary>
        public IAppLifecycle AppLifecycle => AppLifecycleService;

        // ---- 具体类型视图：框架内部、诊断、扩展方法用 ----
        public SceneService SceneService { get; }
        public InputService InputService { get; }
        public GameTimeService TimeService { get; }
        public TimerService TimerService { get; }
        public ConfigService ConfigService { get; }
        public GameFlowController FlowController { get; }
        public AppLifecycleService AppLifecycleService { get; }
        /// <summary>底层资源加载器（ResourcesLoader），扩展方法/诊断会用到</summary>
        public ResourcesLoader ResourceLoader { get; }

        /// <summary>旧音频管理器（AudioMgr 是单例，这里只是引用，不负责创建）</summary>
        private readonly AudioMgr audio;
        /// <summary>计时器的帧驱动源（PublicMono 是主线程统一 Update 分发器）</summary>
        private readonly PublicMono timerDriver;
        /// <summary>关闭任务缓存：保证 ShutdownAsync 幂等（重复调用返回同一个 Task）</summary>
        private Task shutdown;
        /// <summary>
        /// 装配全部服务并接好依赖。
        /// <para>顺序有讲究：TimeService 必须早于 InputService（输入服务要靠它判断暂停），
        /// Assets 必须早于 Audio/SceneService（后两者通过 IAssetService 取资源），
        /// FlowController 最后 Configure（它需要完整的 IGameServices）。</para>
        /// </summary>
        public GameServices(ResourcesLoader resources, AssetsLoader assets, UPGameRootConfig config, string saveDirectory)
        {
            ResourceLoader = resources;
            Assets = new AssetService(resources, assets);
            TimeService = new GameTimeService();
            // 输入服务默认用旧版 Input 后端（UnityLegacyInputBackend），绑定表可被配置覆盖
            InputService = new InputService(new UnityLegacyInputBackend(), TimeService, config.inputBindings);
            Saves = new FileSaveService(saveDirectory);
            // AudioMgr 是旧单例：这里把它接到新的 IAssetsService 上，之后的音频加载也走统一资源层
            audio = AudioMgr.Instance; audio.Configure(Assets); Audio = audio;
            // 场景服务：Unity 原生后端 + 用 AssetsLoader 做“加载场景前先准备 AB”。
            // Editor 直读模式不装配 provider：场景直接从 Build Settings 加载，不经过 AB/清单。
            SceneService = new SceneService(new UnitySceneBackend(), SceneService.SelectAssetProvider(config.method, assets));
            ConfigService = new ConfigService();
            TimerService = new TimerService();
            // 前后台暂停接入同一个 GameTimeService（嵌套暂停，不会与菜单暂停打架）
            AppLifecycleService = new AppLifecycleService(TimeService);
            FlowController = new GameFlowController();
            FlowController.Configure(this);
            // 计时器自己不跑帧循环，由 PublicMono.Update 驱动；用 scaled 时间，所以暂停时自动停
            timerDriver = PublicMono.Instance;
            timerDriver.AddUpdateListener(TickTimers);
        }
        /// <summary>每帧推进计时器：scaled 时间受暂停/timeScale 影响，unscaled 是真实帧时间。</summary>
        private void TickTimers() => TimerService.Tick(TimeService.DeltaTime, TimeService.UnscaledDeltaTime);
        /// <summary>关闭全部服务。幂等：并发/重复调用返回同一个 Task，不会重复释放。</summary>
        public Task ShutdownAsync() => shutdown ?? (shutdown = ShutdownCoreAsync());
        /// <summary>
        /// 逆序关闭。
        /// <para>为什么是逆序：依赖方先死、被依赖方后死。例如场景服务会还资源，必须在计时器/音频之前关掉；
        /// 计时器的帧驱动要先摘掉，否则释放后 PublicMono 还在调 Tick。</para>
        /// <para>每一步单独 try/catch：某一步失败不能阻断后续释放，最后聚合抛出。</para>
        /// </summary>
        private async Task ShutdownCoreAsync()
        {
            var errors = new List<Exception>();
            try { await SceneService.ShutdownAsync(); } catch (Exception e) { errors.Add(e); }
            try { await FlowController.ShutdownAsync(); } catch (Exception e) { errors.Add(e); }
            if (timerDriver != null) timerDriver.RemoveUpdateListener(TickTimers);
            try { TimerService.Dispose(); } catch (Exception e) { errors.Add(e); }
            try { AppLifecycleService.Dispose(); } catch (Exception e) { errors.Add(e); }
            try { AudioMgr.Release(); } catch (Exception e) { errors.Add(e); }
            try { InputService.Dispose(); } catch (Exception e) { errors.Add(e); }
            try { TimeService.Dispose(); } catch (Exception e) { errors.Add(e); }
            if (errors.Count > 0) throw new AggregateException("服务关闭失败。", errors);
        }
    }
    /// <summary>GameServices 的扩展：把“取资源/建对象池”与作用域绑定，作用域释放时自动归还。</summary>
    public static class GameServicesExtensions
    {
        /// <summary>
        /// 创建一个对象池，并把它的释放挂到作用域上（作用域 Dispose 时调 DisposeAllInstances）。
        /// <para>适合“一个场景/一个 UI 面板一个池”的用法，避免池里的实例跨场景泄漏。</para>
        /// </summary>
        public static GameObjectPoolMgr CreatePool(this IGameServices services, ILifetimeScope scope)
        {
            var pool = new GameObjectPoolMgr(); pool.Configure(services.Assets);
            scope.Own(new DisposableAction(pool.DisposeAllInstances));
            return pool;
        }
        /// <summary>
        /// 取一份资源持有权并挂到作用域：作用域释放时会自动 Dispose（归还引用计数/释放 AB）。
        /// <para>取消（token）时抛出；此时若资源已“迟到”加载完，仍会被正确归还，不会泄漏。</para>
        /// </summary>
        public static async Task<AssetLease<T>> AcquireInScopeAsync<T>(this IGameServices services, ILifetimeScope scope,
            string path, AssetLoadMethod method = AssetLoadMethod.Resources) where T : UnityEngine.Object
        {
            var lease = await services.Assets.AcquireAsync<T>(path, method, scope.Token);
            return scope.Own(lease);
        }
    }
}
