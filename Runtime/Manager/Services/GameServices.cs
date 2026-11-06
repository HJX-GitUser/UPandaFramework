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
        public SceneService SceneService { get; }
        public InputService InputService { get; }
        public GameTimeService TimeService { get; }
        private readonly AudioMgr audio;
        private Task shutdown;
        public GameServices(ResourcesLoader resources, AssetsLoader assets, UPGameRootConfig config, string saveDirectory)
        {
            Assets = new AssetService(resources, assets);
            TimeService = new GameTimeService();
            InputService = new InputService(new UnityLegacyInputBackend(), TimeService, config.inputBindings);
            Saves = new FileSaveService(saveDirectory);
            audio = AudioMgr.Instance; audio.Configure(Assets); Audio = audio;
            SceneService = new SceneService(new UnitySceneBackend(), assets);
        }
        public Task ShutdownAsync() => shutdown ?? (shutdown = ShutdownCoreAsync());
        private async Task ShutdownCoreAsync()
        {
            var errors = new List<Exception>();
            SceneMgr.Instance.Configure(null);
            try { await SceneService.ShutdownAsync(); } catch (Exception e) { errors.Add(e); }
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
