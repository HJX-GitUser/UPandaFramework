using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>启动装配结果，不提供运行中替换服务的全局注册表。</summary>
    public sealed class GameServices : IGameServices
    {
        public IAssetService Assets { get; }
        public IAudioService Audio { get; }
        public ISaveService Saves { get; }
        public ISceneService Scenes => SceneService;
        public IInputService Input => InputService;
        public IPauseService Pause => TimeService;
        public IGameClock Clock => TimeService;
        public ITimerService Timer => TimerService;
        public IConfigService Config => ConfigService;
        public IGameFlowService Flow => FlowController;
        public IAppLifecycle AppLifecycle => AppLifecycleService;
        public SceneService SceneService { get; }
        public InputService InputService { get; }
        public GameTimeService TimeService { get; }
        public TimerService TimerService { get; }
        public ConfigService ConfigService { get; }
        public GameFlowController FlowController { get; }
        public AppLifecycleService AppLifecycleService { get; }
        public ResourcesLoader ResourceLoader { get; }
        private readonly AudioMgr audio;
        private readonly PublicMono timerDriver;
        private Task shutdown;
        public GameServices(ResourcesLoader resources, AssetsLoader assets, UPGameRootConfig config, string saveDirectory)
        {
            ResourceLoader = resources;
            Assets = new AssetService(resources, assets);
            TimeService = new GameTimeService();
            InputService = new InputService(new UnityLegacyInputBackend(), TimeService, config.inputBindings);
            Saves = new FileSaveService(saveDirectory);
            audio = AudioMgr.Instance; audio.Configure(Assets); Audio = audio;
            SceneService = new SceneService(new UnitySceneBackend(), assets);
            ConfigService = new ConfigService();
            TimerService = new TimerService();
            AppLifecycleService = new AppLifecycleService(TimeService);
            FlowController = new GameFlowController();
            FlowController.Configure(this);
            timerDriver = PublicMono.Instance;
            timerDriver.AddUpdateListener(TickTimers);
        }
        private void TickTimers() => TimerService.Tick(TimeService.DeltaTime, TimeService.UnscaledDeltaTime);
        public Task ShutdownAsync() => shutdown ?? (shutdown = ShutdownCoreAsync());
        private async Task ShutdownCoreAsync()
        {
            var errors = new List<Exception>();
#pragma warning disable CS0618 // 兼容层反向解绑
            SceneMgr.Instance.Configure(null);
#pragma warning restore CS0618
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
    public static class GameServicesExtensions
    {
        public static GameObjectPoolMgr CreatePool(this IGameServices services, ILifetimeScope scope)
        {
            var pool = new GameObjectPoolMgr(); pool.Configure(services.Assets);
            scope.Own(new DisposableAction(pool.DisposeAllInstances));
            return pool;
        }
        public static async Task<AssetLease<T>> AcquireInScopeAsync<T>(this IGameServices services, ILifetimeScope scope,
            string path, AssetLoadMethod method = AssetLoadMethod.Resources) where T : UnityEngine.Object
        {
            var lease = await services.Assets.AcquireAsync<T>(path, method, scope.Token);
            return scope.Own(lease);
        }
    }
}
