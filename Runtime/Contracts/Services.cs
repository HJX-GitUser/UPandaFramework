using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UPandaGF
{
    public interface IAssetService
    {
        Task<AssetLease<T>> AcquireAsync<T>(string path, AssetLoadMethod method = AssetLoadMethod.Resources,
            CancellationToken token = default) where T : UnityEngine.Object;
    }
    public interface IAudioService
    {
        void PlayBKMusic(string path, AssetLoadMethod method = AssetLoadMethod.Resources, float fadeDuration = -1f, bool restartIfSame = false);
        void StopBKMusic(float fadeDuration = -1f);
        void PlayUniqueSound(string path, AssetLoadMethod method = AssetLoadMethod.Resources);
        void StopAllSound();
        void ChangeBKValue(float volume);
        void ChangeSoundValue(float volume);
    }
    public interface ISaveService
    {
        void Save<T>(string slot, T data) where T : class;
        T Load<T>(string slot) where T : class;
        bool Exists(string slot);
        /// <summary>删除槽位；文件不存在返回 false。槽位校验规则与 Save/Load 一致。</summary>
        bool Delete(string slot);
    }
    public interface IInputService
    {
        float GetAxis(string action, string context = "Gameplay");
        bool GetButton(string action, string context = "Gameplay");
        bool GetButtonDown(string action, string context = "Gameplay");
        IDisposable PushContext(string context, bool blockLower = true);
    }
    public interface IPauseService
    {
        bool IsPaused { get; }
        IDisposable Pause(string reason = "Gameplay");
    }
    public interface IGameClock
    {
        float DeltaTime { get; }
        float UnscaledDeltaTime { get; }
        void SetTimeScale(float scale);
    }
    public enum SceneTransitionPhase { Queued, Preparing, Loading, Completed, Failed, Canceled }
    public readonly struct SceneTransitionProgress
    {
        public readonly long RequestId;
        public readonly string Scene;
        public readonly SceneTransitionPhase Phase;
        public readonly float Progress;
        public SceneTransitionProgress(long id, string scene, SceneTransitionPhase phase, float progress)
        { RequestId = id; Scene = scene; Phase = phase; Progress = progress; }
    }
    public interface ILifetimeScope
    {
        CancellationToken Token { get; }
        bool IsDisposed { get; }
        T Own<T>(T value) where T : IDisposable;
    }
    public interface IGameServicesConsumer { void Configure(IGameServices services); }
    public interface ISceneService
    {
        event Action<SceneTransitionProgress> Progress;
        ILifetimeScope GetScope(Scene scene);
        Task<Scene> LoadAsync(string path, LoadSceneMode mode = LoadSceneMode.Single,
            CancellationToken token = default, float timeoutSeconds = 0, Action<SceneTransitionProgress> progress = null);
        Task UnloadAsync(Scene scene, CancellationToken token = default);
    }
    public interface IGameServices
    {
        IAssetService Assets { get; }
        IAudioService Audio { get; }
        ISaveService Saves { get; }
        ISceneService Scenes { get; }
        IInputService Input { get; }
        IPauseService Pause { get; }
        IGameClock Clock { get; }
        ITimerService Timer { get; }
        IConfigService Config { get; }
        IGameFlowService Flow { get; }
        IAppLifecycle AppLifecycle { get; }
        /// <summary>HTTP 请求（全局头、并发上限、可取消的 Task 接口）。</summary>
        IHttpService Http { get; }
        /// <summary>对象池工厂：根生命周期共享池 + 作用域池。</summary>
        IPoolService Pools { get; }
        /// <summary>可注入日志服务（不参与 OPEN_PLOG 编译期剔除）。</summary>
        ILogService Log { get; }
    }
}
