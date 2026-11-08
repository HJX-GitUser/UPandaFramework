/* ==========================================================================================
 * SceneService —— 场景加载/卸载（串行队列 + 资源作用域）
 *
 * 【本文件里的三个角色】
 *   ISceneAssetProvider：加载场景前先把它依赖的 AB 准备好（由 AssetsLoader 实现）。
 *   ISceneBackend：真正调用 SceneManager 的那一层（可替换，便于单测）。
 *   SceneService：门面。负责排队、取消、超时、进度阶段，以及“场景 ↔ 资源作用域”的绑定。
 *   Editor 直读模式（UPGameRootConfig.method = Editor）不装配 provider：场景直接由 Build Settings 加载，不经过 AB。
 *
 * 【为什么拆两层】让 SceneService 不依赖资源层；单测可只替换 backend，而不拉起整个资源系统。
 *
 * 【资源归属】每个已加载场景对应一个 LifetimeScope；场景包凭据挂在作用域上，
 *   由 SceneManager.sceneUnloaded 触发释放 —— “场景卸载”就等于“归还该场景占用的资源”。
 *
 * 【取消的边界】取消只停止“等待与进度上报”，<b>不会撤销已经提交给 Unity 的原生加载</b>；
 *   原生加载产生的场景与资源仍会被正确登记并最终释放，不会泄漏。
 * ========================================================================================== */

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UPandaGF
{
    /// <summary>
    /// 场景资源提供者：在真正调用 SceneManager.LoadSceneAsync 之前，把场景所属的资源包准备就绪。
    /// <para>返回的 <see cref="IDisposable"/> 是资源凭据（AB 模式下是 bundle 的 lease）；返回 null 表示该模式
    /// 不需要预加载（例如编辑器直读资源）。</para>
    /// <para>由 <see cref="AssetsLoader"/> 实现。拆成接口是为了让 <see cref="SceneService"/> 不依赖资源层，
    /// 单测可以只替换 <see cref="ISceneBackend"/> 而不拉起整个资源系统。</para>
    /// </summary>
    public interface ISceneAssetProvider
    {
        /// <summary>准备场景资源。<paramref name="token"/> 取消时不应留下已加载的包。</summary>
        Task<IDisposable> PrepareSceneAsync(string path, CancellationToken token);
    }

    /// <summary>
    /// 场景加载 / 卸载的底层后端抽象。只负责"调用 Unity 并等待完成"；
    /// 排队、取消、进度阶段等语义全部由 <see cref="SceneService"/> 负责。
    /// </summary>
    public interface ISceneBackend
    {
        /// <summary>加载场景并返回加载完成的场景。<paramref name="progress"/> 收到 0..1 的归一化进度，可为 null。</summary>
        Task<Scene> LoadAsync(string path, LoadSceneMode mode, Action<float> progress);
        Task UnloadAsync(Scene scene);
    }

    /// <summary>
    /// <see cref="ISceneBackend"/> 的 Unity 原生实现。
    /// <para>必须跑在主线程：内部用 Task.Yield 逐帧轮询 AsyncOperation，依赖 Unity 主线程同步上下文。</para>
    /// </summary>
    public sealed class UnitySceneBackend : ISceneBackend
    {
        public async Task<Scene> LoadAsync(string path, LoadSceneMode mode, Action<float> progress)
        {
            // 先把"场景没进 Build Settings / AB 没加载"变成明确异常；否则 LoadSceneAsync 返回 null，失败原因很难查。
            if (!Application.CanStreamedLevelBeLoaded(path)) throw new ArgumentException("场景不在构建列表或已加载的 AB 中：" + path);

            // 记录加载前已存在的场景 handle。LoadSceneAsync 不返回 Scene，Additive 模式又会有多个场景，
            // 结束后靠"新增的 handle"反推这一次加载的是哪个场景。
            var previous = new HashSet<int>();
            for (int i = 0; i < SceneManager.sceneCount; i++) previous.Add(SceneManager.GetSceneAt(i).handle);

            var operation = SceneManager.LoadSceneAsync(path, mode);
            if (operation == null) throw new InvalidOperationException("Unity 未创建场景操作。");

            // operation.progress 到 0.9 就停（剩下 0.1 是激活阶段），除以 0.9 归一化到 0..1 再上报。
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
    /// <summary>
    /// 串行场景操作。取消等待不释放原生操作的队列锁，资源持续持有至场景卸载。
    /// <para>并发模型：公开方法都要求在 Unity 主线程调用（内部不切线程）。<see cref="gate"/> 保证同一时刻只有
    /// 一个场景加载或卸载在跑，其余请求排队；<see cref="pending"/> 只用于关闭时等待所有在飞任务。</para>
    /// <para>资源归属：每个已加载场景对应一个 <see cref="LifetimeScope"/>，场景包凭据挂在作用域上，
    /// 由 SceneManager.sceneUnloaded 触发释放 —— 所以"场景卸载"就等于"归还该场景占用的资源"。</para>
    /// <para>模式差异：Editor 直读模式装配时传入 null provider（见 <see cref="SelectAssetProvider"/>），
    /// 加载只走 <see cref="UnitySceneBackend"/>，即从 Build Settings 直接加载场景。</para>
    /// </summary>
    public sealed class SceneService : ISceneService
    {
        private readonly ISceneBackend backend;
        /// <summary>可为 null：为 null 时只做原生加载、不做资源准备（纯逻辑单测常用）。</summary>
        private readonly ISceneAssetProvider assets;
        /// <summary>场景操作串行闸门。加载与卸载共用同一个，避免两者同时改动场景结构。</summary>
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        /// <summary>服务生命周期令牌：ShutdownAsync 取消它，排队中与进行中的请求随之取消。</summary>
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        /// <summary>场景 handle → 该场景的资源作用域。handle 在会话内唯一，场景卸载后移除。</summary>
        private readonly Dictionary<int, LifetimeScope> scopes = new Dictionary<int, LifetimeScope>();
        /// <summary>所有在飞的内部任务（含调用方已放弃等待的那些）。关闭时统一等待，避免释放时还有任务在跑。</summary>
        private readonly HashSet<Task> pending = new HashSet<Task>();
        /// <summary>请求 id → 该请求专属的进度回调。刻意不复用 <see cref="Progress"/>：单请求回调只在请求存续期存在。</summary>
        private readonly Dictionary<long, Action<SceneTransitionProgress>> requestProgress = new Dictionary<long, Action<SceneTransitionProgress>>();
        private long nextRequest;
        /// <summary>置位后不再接受新请求（加载与卸载都直接返回异常 Task）。</summary>
        private bool stopping;
        private Task shutdown;

        /// <summary>全局进度事件，覆盖所有请求。订阅者抛异常会被逐个隔离，不影响场景加载本身。</summary>
        public event Action<SceneTransitionProgress> Progress;
        /// <summary>是否还有未完成的场景操作（含调用方已放弃等待的那些）。</summary>
        public bool HasPendingRequests => pending.Count > 0;
        public int DiagnosticScopeCount => scopes.Count;
        public int DiagnosticPendingRequestCount => pending.Count;

        /// <param name="backend">真正的加载/卸载实现；测试可替换为自己的后端。</param>
        /// <param name="assets">可选的资源准备者；传 null 表示只做原生加载。</param>
        public SceneService(ISceneBackend backend, ISceneAssetProvider assets = null)
        {
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
            this.assets = assets;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }
        /// <summary>
        /// 按资源加载模式决定要不要接“加载前准备场景包”。
        /// <para>Editor 直读模式下场景由 Build Settings 直接提供（<see cref="UnitySceneBackend"/> 里就是 SceneManager），
        /// 返回 null 让加载流程完全不触碰 AB/清单 —— 不依赖 provider 内部是否记得判模式。</para>
        /// </summary>
        public static ISceneAssetProvider SelectAssetProvider(AssetLoaddingMethod method, ISceneAssetProvider assets)
        {
            return method == AssetLoaddingMethod.Editor || method == AssetLoaddingMethod.NoAssetBundle ? null : assets;
        }
        ILifetimeScope ISceneService.GetScope(Scene scene) => GetScope(scene);
        /// <summary>
        /// 取（或懒创建）某个已加载场景的资源作用域。
        /// 句柄已在 <see cref="LoadCoreAsync"/> 里登记；这里是给"场景里临时取作用域"的调用方用的兜底入口。
        /// </summary>
        public LifetimeScope GetScope(Scene scene)
        {
            if (stopping) throw new ObjectDisposedException(nameof(SceneService));
            if (!scene.IsValid() || !scene.isLoaded) throw new ArgumentException("场景未加载。", nameof(scene));
            if (!scopes.TryGetValue(scene.handle, out var scope)) scopes.Add(scene.handle, scope = new LifetimeScope(scene.path + "#" + scene.handle));
            return scope;
        }
        /// <summary>场景卸载（无论是本服务还是外部触发）时释放其作用域，从而归还该场景持有的全部资源凭据。</summary>
        private void OnSceneUnloaded(Scene scene)
        {
            if (!scopes.TryGetValue(scene.handle, out var scope)) return;
            scopes.Remove(scene.handle);
            try { scope.Dispose(); } catch (Exception e) { Debug.LogException(e); }
        }
        /// <summary>
        /// 广播一次阶段变化：先调该请求的专属回调，再调全局 <see cref="Progress"/>。
        /// 每个监听者单独 try/catch —— 业务侧的 UI/动画抛异常不能把场景加载带崩。
        /// </summary>
        private void Report(long id, string scene, SceneTransitionPhase phase, float value)
        {
            var progress = new SceneTransitionProgress(id, scene, phase, value);
            if (requestProgress.TryGetValue(id, out var specific) && specific != null)
                try { specific(progress); } catch (Exception e) { Debug.LogException(e); }
            if (Progress != null)
                foreach (Action<SceneTransitionProgress> listener in Progress.GetInvocationList())
                    try { listener(progress); } catch (Exception e) { Debug.LogException(e); }
        }
        /// <summary>
        /// 异步加载场景。请求进入串行队列，阶段依次为
        /// Queued → Preparing（准备场景包）→ Loading（原生加载，带 0..1 进度）→ Completed；
        /// 失败是 Failed，取消/超时是 Canceled。
        /// </summary>
        /// <param name="path">场景路径或名称。AB 模式下应是清单里登记的完整路径（如 Assets/Scenes/GameScene.unity）。</param>
        /// <param name="mode">Single 会替换当前场景，Additive 叠加。</param>
        /// <param name="token">只取消本次等待与后续进度上报；<b>不能撤销已提交给 Unity 的原生加载</b>。</param>
        /// <param name="timeoutSeconds">大于 0 时超时取消本次等待；同样不撤销原生加载。</param>
        /// <param name="progress">仅本请求的进度回调，可与全局 <see cref="Progress"/> 同时使用。</param>
        public Task<Scene> LoadAsync(string path, LoadSceneMode mode = LoadSceneMode.Single, CancellationToken token = default, float timeoutSeconds = 0, Action<SceneTransitionProgress> progress = null)
        {
            // 参数问题返回"已失败的 Task"而不是同步抛：调用方可以统一用 await + try/catch 处理。
            if (stopping) return Task.FromException<Scene>(new ObjectDisposedException(nameof(SceneService)));
            if (string.IsNullOrWhiteSpace(path)) return Task.FromException<Scene>(new ArgumentException("场景名称不能为空。"));
            if (float.IsNaN(timeoutSeconds) || float.IsInfinity(timeoutSeconds) || timeoutSeconds < 0)
                return Task.FromException<Scene>(new ArgumentOutOfRangeException(nameof(timeoutSeconds)));
            // 合并调用方令牌与生命周期令牌：任一取消都终止本次等待；超时复用同一个令牌源。
            var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
            if (timeoutSeconds > 0) linked.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            long id = ++nextRequest;
            if (progress != null) requestProgress[id] = progress;
            // 原生任务与"调用方等待"分离：调用方取消不会中断原生加载，也不会漏掉场景作用域的资源登记。
            var native = LoadCoreAsync(id, path, mode, linked.Token);
            Track(native);
            return DeliverAsync(native, linked);
        }
        /// <summary>把原生任务包成"可取消的等待"；成功、失败、取消都要走到 DisposeAfterAsync 释放令牌源。</summary>
        private async Task<Scene> DeliverAsync(Task<Scene> native, CancellationTokenSource linked)
        {
            try { await StartupLifecycle.WaitAsync(native, linked.Token); return await native; }
            finally { _ = DisposeAfterAsync(native, linked); }
        }
        /// <summary>等原生任务真正结束再释放令牌源，避免原生代码还在读已经被 Dispose 的 CTS。</summary>
        private static async Task DisposeAfterAsync(Task task, CancellationTokenSource linked)
        {
            try { await task; } catch (Exception) { /* 原始结果由调用 Task 与进度保留。 */ }
            finally { linked.Dispose(); }
        }
        /// <summary>
        /// 真正的加载流程：排队 → 准备场景资源 → 原生加载 → 把资源凭据交给该场景的作用域。
        /// 它的 Task 不直接交给调用方（调用方拿到的是 <see cref="DeliverAsync"/> 包装的那层），
        /// 这样"调用方取消等待"和"原生加载/资源登记"互不牵连。
        /// </summary>
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
                    // 取消之后不再上报进度，避免加载界面在取消后又回弹。
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
            // lease 交给场景作用域后会置 null，不会在这里被误释放；准备成功但加载失败时由这里兜底归还。
            finally { requestProgress.Remove(id); assetLease?.Dispose(); if (acquiredGate) gate.Release(); }
        }
        /// <summary>
        /// 卸载场景。与加载共用同一个闸门，所以会排在已有加载之后执行。
        /// 资源凭据在原生卸载完成后才释放 —— 卸载过程中场景内的对象仍能安全读资源。
        /// </summary>
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
                // 先 Cancel 让作用域里的订阅、计时器停下；Dispose（真正还资源）留到原生卸载之后再执行。
                if (scopes.TryGetValue(scene.handle, out var scope))
                    try { scope.Cancel(); } catch (Exception e) { Debug.LogException(e); }
                // 资源凭据直到原生卸载完成再释放；卸载期间场景自己的对象仍可读取资源。
                await backend.UnloadAsync(scene);
                OnSceneUnloaded(scene);
            }
            finally { gate.Release(); }
        }
        /// <summary>登记在飞任务，并由 <see cref="ForgetAsync"/> 观察异常（避免 UnobservedTaskException）后自动摘除。</summary>
        private void Track(Task task) { pending.Add(task); _ = ForgetAsync(task); }
        private async Task ForgetAsync(Task task)
        {
            try { await task; } catch (Exception) { }
            finally { pending.Remove(task); }
        }
        /// <summary>关闭服务：拒绝新请求、取消全部排队与等待中的请求、等所有在飞任务结束后统一释放资源。可重复调用。</summary>
        public Task ShutdownAsync()
        {
            if (shutdown != null) return shutdown;
            stopping = true; lifetime.Cancel();
            return shutdown = ShutdownCoreAsync();
        }
        private async Task ShutdownCoreAsync()
        {
            // 必须先等在飞任务收尾：它们可能还会把 lease 登记进 scopes，等完再统一 Dispose 才不会漏。
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
