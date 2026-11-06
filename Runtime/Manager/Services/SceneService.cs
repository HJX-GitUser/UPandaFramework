using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UPandaGF
{
    public interface ISceneAssetProvider
    {
        Task<IDisposable> PrepareSceneAsync(string path, CancellationToken token);
    }
    public interface ISceneBackend
    {
        Task<Scene> LoadAsync(string path, LoadSceneMode mode, Action<float> progress);
        Task UnloadAsync(Scene scene);
    }
    public sealed class UnitySceneBackend : ISceneBackend
    {
        public async Task<Scene> LoadAsync(string path, LoadSceneMode mode, Action<float> progress)
        {
            if (!Application.CanStreamedLevelBeLoaded(path)) throw new ArgumentException("场景不在构建列表或已加载的 AB 中：" + path);
            var previous = new HashSet<int>();
            for (int i = 0; i < SceneManager.sceneCount; i++) previous.Add(SceneManager.GetSceneAt(i).handle);
            var operation = SceneManager.LoadSceneAsync(path, mode);
            if (operation == null) throw new InvalidOperationException("Unity 未创建场景操作。");
            while (!operation.isDone) { progress?.Invoke(Mathf.Clamp01(operation.progress / .9f)); await Task.Yield(); }
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!previous.Contains(scene.handle) && scene.isLoaded) return scene;
            }
            throw new InvalidOperationException("未找到刚刚完成加载的场景：" + path);
        }
        public async Task UnloadAsync(Scene scene)
        {
            var operation = SceneManager.UnloadSceneAsync(scene);
            if (operation == null) throw new InvalidOperationException("Unity 未创建场景卸载操作。");
            while (!operation.isDone) await Task.Yield();
        }
    }
    /// <summary>串行场景操作。取消等待不释放原生操作的队列锁，资源持续持有至场景卸载。</summary>
    public sealed class SceneService : ISceneService
    {
        private readonly ISceneBackend backend;
        private readonly ISceneAssetProvider assets;
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly Dictionary<int, LifetimeScope> scopes = new Dictionary<int, LifetimeScope>();
        private readonly HashSet<Task> pending = new HashSet<Task>();
        private readonly Dictionary<long, Action<SceneTransitionProgress>> requestProgress = new Dictionary<long, Action<SceneTransitionProgress>>();
        private long nextRequest;
        private bool stopping;
        private Task shutdown;
        public event Action<SceneTransitionProgress> Progress;
        public bool HasPendingRequests => pending.Count > 0;
        public SceneService(ISceneBackend backend, ISceneAssetProvider assets = null)
        {
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
            this.assets = assets;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }
        ILifetimeScope ISceneService.GetScope(Scene scene) => GetScope(scene);
        public LifetimeScope GetScope(Scene scene)
        {
            if (stopping) throw new ObjectDisposedException(nameof(SceneService));
            if (!scene.IsValid() || !scene.isLoaded) throw new ArgumentException("场景未加载。", nameof(scene));
            if (!scopes.TryGetValue(scene.handle, out var scope)) scopes.Add(scene.handle, scope = new LifetimeScope(scene.path + "#" + scene.handle));
            return scope;
        }
        private void OnSceneUnloaded(Scene scene)
        {
            if (!scopes.TryGetValue(scene.handle, out var scope)) return;
            scopes.Remove(scene.handle);
            try { scope.Dispose(); } catch (Exception e) { Debug.LogException(e); }
        }
        private void Report(long id, string scene, SceneTransitionPhase phase, float value)
        {
            var progress = new SceneTransitionProgress(id, scene, phase, value);
            if (requestProgress.TryGetValue(id, out var specific) && specific != null)
                try { specific(progress); } catch (Exception e) { Debug.LogException(e); }
            if (Progress != null)
                foreach (Action<SceneTransitionProgress> listener in Progress.GetInvocationList())
                    try { listener(progress); } catch (Exception e) { Debug.LogException(e); }
        }
        public Task<Scene> LoadAsync(string path, LoadSceneMode mode = LoadSceneMode.Single, CancellationToken token = default, float timeoutSeconds = 0, Action<SceneTransitionProgress> progress = null)
        {
            if (stopping) return Task.FromException<Scene>(new ObjectDisposedException(nameof(SceneService)));
            if (string.IsNullOrWhiteSpace(path)) return Task.FromException<Scene>(new ArgumentException("场景名称不能为空。"));
            if (float.IsNaN(timeoutSeconds) || float.IsInfinity(timeoutSeconds) || timeoutSeconds < 0)
                return Task.FromException<Scene>(new ArgumentOutOfRangeException(nameof(timeoutSeconds)));
            var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
            if (timeoutSeconds > 0) linked.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            long id = ++nextRequest;
            if (progress != null) requestProgress[id] = progress;
            var native = LoadCoreAsync(id, path, mode, linked.Token);
            Track(native);
            return DeliverAsync(native, linked);
        }
        private async Task<Scene> DeliverAsync(Task<Scene> native, CancellationTokenSource linked)
        {
            try { await StartupLifecycle.WaitAsync(native, linked.Token); return await native; }
            finally { _ = DisposeAfterAsync(native, linked); }
        }
        private static async Task DisposeAfterAsync(Task task, CancellationTokenSource linked)
        {
            try { await task; } catch (Exception) { /* 原始结果由调用 Task 与进度保留。 */ }
            finally { linked.Dispose(); }
        }
        private async Task<Scene> LoadCoreAsync(long id, string path, LoadSceneMode mode, CancellationToken token)
        {
            IDisposable assetLease = null;
            bool acquiredGate = false;
            try
            {
                // 先让调用者拿到请求 Task，避免进度回调在注册 pending 前重入关闭。
                await Task.Yield();
                Report(id, path, SceneTransitionPhase.Queued, 0);
                await gate.WaitAsync(token); acquiredGate = true;
                token.ThrowIfCancellationRequested();
                Report(id, path, SceneTransitionPhase.Preparing, 0);
                if (assets != null) assetLease = await assets.PrepareSceneAsync(path, token);
                token.ThrowIfCancellationRequested();
                Report(id, path, SceneTransitionPhase.Loading, 0);
                var scene = await backend.LoadAsync(path, mode, progress => {
                    if (!token.IsCancellationRequested) Report(id, path, SceneTransitionPhase.Loading, progress);
                });
                if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("场景后端没有返回有效已加载场景。");
                // 即使等待者已取消，原生加载产生的场景仍真实存在，资源交给该场景的作用域。
                if (!scopes.TryGetValue(scene.handle, out var scope)) scopes.Add(scene.handle, scope = new LifetimeScope(path + "#" + scene.handle));
                scope.Own(assetLease); assetLease = null;
                token.ThrowIfCancellationRequested();
                Report(id, path, SceneTransitionPhase.Completed, 1);
                return scene;
            }
            catch (OperationCanceledException) { Report(id, path, SceneTransitionPhase.Canceled, 0); throw; }
            catch (Exception) { Report(id, path, SceneTransitionPhase.Failed, 0); throw; }
            finally { requestProgress.Remove(id); assetLease?.Dispose(); if (acquiredGate) gate.Release(); }
        }
        public Task UnloadAsync(Scene scene, CancellationToken token = default)
        {
            if (stopping) return Task.FromException(new ObjectDisposedException(nameof(SceneService)));
            var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
            var task = UnloadCoreAsync(scene, linked.Token); Track(task);
            return DeliverUnloadAsync(task, linked);
        }
        private async Task DeliverUnloadAsync(Task task, CancellationTokenSource linked)
        {
            try { await StartupLifecycle.WaitAsync(task, linked.Token); }
            finally { _ = DisposeAfterAsync(task, linked); }
        }
        private async Task UnloadCoreAsync(Scene scene, CancellationToken token)
        {
            await Task.Yield();
            await gate.WaitAsync(token);
            try
            {
                token.ThrowIfCancellationRequested();
                if (scopes.TryGetValue(scene.handle, out var scope))
                    try { scope.Cancel(); } catch (Exception e) { Debug.LogException(e); }
                // 资源凭据直到原生卸载完成再释放；卸载期间场景自己的对象仍可读取资源。
                await backend.UnloadAsync(scene);
                OnSceneUnloaded(scene);
            }
            finally { gate.Release(); }
        }
        private void Track(Task task) { pending.Add(task); _ = ForgetAsync(task); }
        private async Task ForgetAsync(Task task)
        {
            try { await task; } catch (Exception) { }
            finally { pending.Remove(task); }
        }
        public Task ShutdownAsync()
        {
            if (shutdown != null) return shutdown;
            stopping = true; lifetime.Cancel();
            return shutdown = ShutdownCoreAsync();
        }
        private async Task ShutdownCoreAsync()
        {
            try { await Task.WhenAll(new List<Task>(pending)); } catch (Exception) { }
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            var errors = new List<Exception>();
            foreach (var scope in scopes.Values)
                try { scope.Dispose(); } catch (Exception e) { errors.Add(e); }
            scopes.Clear(); Progress = null; lifetime.Dispose(); gate.Dispose();
            if (errors.Count > 0) throw new AggregateException(errors);
        }
    }
}
