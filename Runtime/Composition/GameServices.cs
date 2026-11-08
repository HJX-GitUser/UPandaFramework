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
 *      SceneService → GameFlow → Pools → Http（取消并销毁宿主）→ 移除计时器帧驱动
 *      → Timer → AppLifecycle → Audio → Input → Time。
 *      某一步失败会聚合成 AggregateException，不会中断后续释放。
 *
 * 【服务所有权】
 *   音频、HTTP 宿主、对象池共享实例都由本类创建并释放；UIManager 由 UPGameRoot 创建/关闭，
 *   QuestManager 由场景持有。业务侧只拿接口，不接触静态单例。
 *
 * 【计时器帧驱动】
 *   TimerService 本身不知道帧循环，由本类把它挂到 PublicMono.Update 上；
 *   测试里可以不挂，直接手动调 TimerService.Tick 推进时间。
 * ========================================================================================== */

using System;
using System.Collections.Generic;
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
        /// <summary>音频（容器创建的 AudioMgr；接口化后便于替换）</summary>
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
        /// <summary>HTTP（Task 契约 + 全局头/并发上限；高级数据包能力见具体 HttpManager）</summary>
        public IHttpService Http { get; }
        /// <summary>对象池工厂（共享池 + 作用域池）</summary>
        public IPoolService Pools { get; }
        /// <summary>可注入日志（不参与 OPEN_PLOG 编译期剔除）</summary>
        public ILogService Log { get; }

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
        /// <summary>HTTP 具体实现（进度/OnRetry/自定义数据包等高级能力用）</summary>
        public HttpManager HttpManager { get; }
        /// <summary>对象池具体实现（诊断/自定义容量用）</summary>
        public PoolService PoolService { get; }

        /// <summary>音频管理器（由容器创建并持有，关闭时 Dispose）</summary>
        private readonly AudioMgr audio;
        /// <summary>计时器的帧驱动源（PublicMono 是主线程统一 Update 分发器）</summary>
        private readonly PublicMono timerDriver;
        /// <summary>HTTP 宿主对象（容器创建，关闭时销毁）</summary>
        private readonly GameObject httpHost;
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
            Log = new LogService();
            // 音频：依赖全部从容器注入（资源 / 帧驱动 / 日志）；provider 让驱动重建后能自动恢复
            timerDriver = PublicMono.Instance;
            audio = new AudioMgr(Assets, timerDriver, Log, () => PublicMono.Instance);
            Audio = audio;
            // HTTP：容器创建宿主，服务实例由容器持有；关闭时取消请求并销毁宿主
            httpHost = new GameObject("HttpService");
            if (Application.isPlaying) UnityEngine.Object.DontDestroyOnLoad(httpHost);
            HttpManager = httpHost.AddComponent<HttpManager>();
            Http = HttpManager;
            // 对象池：容器持有共享池；作用域池由 IPoolService.CreatePool 创建
            PoolService = new PoolService(Assets, Log);
            Pools = PoolService;
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
            // 池持有资源租约：必须在场景作用域归还之后、资源后端关闭之前释放
            try { PoolService.Dispose(); } catch (Exception e) { errors.Add(e); }
            try { Http.CancelAll(); } catch (Exception e) { errors.Add(e); }
            if (httpHost != null) UnityEngine.Object.Destroy(httpHost);
            if (timerDriver != null) timerDriver.RemoveUpdateListener(TickTimers);
            try { TimerService.Dispose(); } catch (Exception e) { errors.Add(e); }
            try { AppLifecycleService.Dispose(); } catch (Exception e) { errors.Add(e); }
            try { audio.Dispose(); } catch (Exception e) { errors.Add(e); }
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
        /// <para>等价于 <c>services.Pools.CreatePool(scope)</c>，保留此扩展只为旧代码可继续编译。</para>
        /// </summary>
        public static IPool CreatePool(this IGameServices services, ILifetimeScope scope)
        {
            return services.Pools.CreatePool(scope);
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
        /// <summary>
        /// 给运行期 Instantiate 出来的对象注入服务（场景扫描覆盖不到它们）。
        /// <para>只注入实现了 IGameServicesConsumer / IUIServiceConsumer 的组件；
        /// 单个组件抛异常会被隔离，不影响其余组件。</para>
        /// </summary>
        public static void Inject(this IGameServices services, GameObject root, IUIService ui = null)
        {
            if (services == null || root == null) return;
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null) continue;
                try
                {
                    if (behaviour is IGameServicesConsumer consumer) consumer.Configure(services);
                    if (ui != null && behaviour is IUIServiceConsumer uiConsumer) uiConsumer.ConfigureUi(ui);
                }
                catch (Exception e)
                {
                    services.Log?.LogException(e);
                }
            }
        }
    }
}
