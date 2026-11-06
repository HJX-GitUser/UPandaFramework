// =============================================================================
//  PublicMono.cs —— 帧调度中心（Update / FixedUpdate / LateUpdate + 协程托管）
// -----------------------------------------------------------------------------
//  作用：让"不是 MonoBehaviour 的类"也能拿到帧更新与协程能力。
//      · AddUpdateListener / AddFixedUpdateListener / AddLateUpdateListener
//      · RunCoroutine(IEnumerator) → Task（可 await）
//
//  【改这个文件前必读】
//   1) 帧回调逐个 try/catch：多播委托一旦某个监听器抛异常，同一帧排在它后面的监听器
//      全部都会被跳过（这正是旧实现把"某个模块的小 bug"放大成"整个框架不更新"的原因）。
//   2) 监听器存在 List 里而不是多播委托：可查重、可注销、可按 owner 存活状态自动清理、
//      可隔离异常 —— 这四件事多播委托都做不到。
//   3) 注册时带 owner（UnityEngine.Object）→ owner 被销毁后自动注销，避免"僵尸监听者"
//      每帧抛 MissingReferenceException，也避免委托强引用让已销毁对象无法被回收。
//      ⚠ 不带 owner 时是强引用：必须在 OnDisable/OnDestroy 里显式注销，或用 ClearAllListeners()。
//   4) ⚠ lambda / 闭包注销不掉（每次求值都是新的委托实例）：注册与注销必须用同一个方法引用
//      （命名方法，或把委托存进字段）。
//   5) 同一方法重复注册会被忽略并给出警告（幂等），避免"每帧跑两遍"。
//   6) RunCoroutine 返回的 Task **一定会结束**：
//        正常跑完 → 完成；协程内部抛异常 → Task 抛异常；
//        宿主被销毁/停用、StopAllCoroutines → Task 取消；超时 → TimeoutException。
//      不要再写成"只在协程末尾 SetResult" —— 一旦被中断，调用方会永久 await 卡死且没有任何日志。
//   7) Task 续体默认在同线程同步执行（与旧行为一致，且保证 await 之后仍在主线程，可安全调 Unity API）。
//      想改成异步续体，可给 TaskCompletionSource 传 RunContinuationsAsynchronously，
//      但要先确认调用方不依赖"续体在主线程同步执行"。
// =============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 公共 Mono：统一托管帧更新（Update / FixedUpdate / LateUpdate）与协程。<br/>
/// 单例由 <see cref="EagerMonoSingletonBase{T}"/> 提供，缺失时自动创建并 DontDestroyOnLoad。<br/>
/// 注意：退出播放时 <c>Instance</c> 会返回 null，调用方需判空。
/// </summary>
public class PublicMono : EagerMonoSingletonBase<PublicMono>
{
    #region 字段

    private ListenerList updateListeners;
    private ListenerList fixedUpdateListeners;
    private ListenerList lateUpdateListeners;

    private List<CoroutineJob> runningJobs = new List<CoroutineJob>();
    private readonly List<CoroutineJob> processingJobs = new List<CoroutineJob>();
    private bool processing, destroyed;
    private int cancellationDepth;

    #endregion

    #region 查询

    /// <summary>当前 Update 监听器数量</summary>
    public int UpdateListenerCount { get { return updateListeners != null ? updateListeners.Count : 0; } }

    /// <summary>当前 FixedUpdate 监听器数量</summary>
    public int FixedUpdateListenerCount { get { return fixedUpdateListeners != null ? fixedUpdateListeners.Count : 0; } }

    /// <summary>当前 LateUpdate 监听器数量</summary>
    public int LateUpdateListenerCount { get { return lateUpdateListeners != null ? lateUpdateListeners.Count : 0; } }

    /// <summary>当前仍在运行的托管协程数量</summary>
    public int RunningCoroutineCount { get { return runningJobs != null ? runningJobs.Count : 0; } }

    #endregion

    #region 生命周期

    /// <summary>字段兜底初始化（不依赖字段初始化器：域重载等场景下它未必执行）</summary>
    protected override void OnAwake()
    {
        EnsureInitialized();
    }

    private void EnsureInitialized()
    {
        if (updateListeners == null) updateListeners = new ListenerList("Update");
        if (fixedUpdateListeners == null) fixedUpdateListeners = new ListenerList("FixedUpdate");
        if (lateUpdateListeners == null) lateUpdateListeners = new ListenerList("LateUpdate");
        if (runningJobs == null) runningJobs = new List<CoroutineJob>();
    }

    private void OnDisable()
    {
        // GameObject 被停用/销毁时 Unity 会停止它上面的协程。这里把还没结束的 Task 一起取消，
        // 否则调用方会永久 await（旧实现的表现就是"游戏卡在初始化，Console 干净"）。
        CancelAllJobs("PublicMono 被停用或销毁");
    }

    protected override void OnDestroyed()
    {
        destroyed = true;
        CancelAllJobs("PublicMono 被销毁");
        ClearAllListeners();
        base.OnDestroyed();
    }

    #endregion

    #region Unity 消息：帧派发

    private void Update()
    {
        if (updateListeners != null) updateListeners.Invoke();
        ProcessCoroutineJobs();
    }

    private void FixedUpdate()
    {
        if (fixedUpdateListeners != null) fixedUpdateListeners.Invoke();
    }

    private void LateUpdate()
    {
        if (lateUpdateListeners != null) lateUpdateListeners.Invoke();
    }

    #endregion

    #region 注册 / 注销（Update）

    /// <summary>
    /// 注册帧更新回调（强引用，不会自动注销）。
    /// <para>⚠️ 注销必须用同一个方法引用（lambda / 闭包注销不掉），且同一方法重复注册会被忽略。</para>
    /// </summary>
    public void AddUpdateListener(UnityAction fun)
    {
        AddUpdateListener(fun, null);
    }

    /// <summary>
    /// 注册帧更新回调，并绑定一个 Unity 对象作为"存活依据"：
    /// owner 被销毁后本回调会自动注销（MonoBehaviour 通常传 <c>this</c>）。
    /// </summary>
    public void AddUpdateListener(UnityAction fun, UnityEngine.Object owner)
    {
        EnsureInitialized();
        updateListeners.Add(fun, owner);
    }

    /// <summary>注销帧更新回调（必须与注册时是同一个方法引用）</summary>
    public void RemoveUpdateListener(UnityAction fun)
    {
        if (updateListeners == null) return;
        updateListeners.Remove(fun);
    }

    #endregion

    #region 注册 / 注销（FixedUpdate / LateUpdate）

    /// <summary>注册 FixedUpdate 回调（强引用，需手动注销）</summary>
    public void AddFixedUpdateListener(UnityAction fun)
    {
        AddFixedUpdateListener(fun, null);
    }

    /// <summary>注册 FixedUpdate 回调，owner 被销毁后自动注销</summary>
    public void AddFixedUpdateListener(UnityAction fun, UnityEngine.Object owner)
    {
        EnsureInitialized();
        fixedUpdateListeners.Add(fun, owner);
    }

    /// <summary>注销 FixedUpdate 回调</summary>
    public void RemoveFixedUpdateListener(UnityAction fun)
    {
        if (fixedUpdateListeners == null) return;
        fixedUpdateListeners.Remove(fun);
    }

    /// <summary>注册 LateUpdate 回调（强引用，需手动注销）</summary>
    public void AddLateUpdateListener(UnityAction fun)
    {
        AddLateUpdateListener(fun, null);
    }

    /// <summary>注册 LateUpdate 回调，owner 被销毁后自动注销</summary>
    public void AddLateUpdateListener(UnityAction fun, UnityEngine.Object owner)
    {
        EnsureInitialized();
        lateUpdateListeners.Add(fun, owner);
    }

    /// <summary>注销 LateUpdate 回调</summary>
    public void RemoveLateUpdateListener(UnityAction fun)
    {
        if (lateUpdateListeners == null) return;
        lateUpdateListeners.Remove(fun);
    }

    /// <summary>
    /// 清空全部帧监听器（三个通道）。
    /// 切换场景、模块重置、单例 Release 等"整批作废"的场合用它兜底，
    /// 避免已销毁对象的回调留在链上。
    /// </summary>
    public void ClearAllListeners()
    {
        if (updateListeners != null) updateListeners.Clear();
        if (fixedUpdateListeners != null) fixedUpdateListeners.Clear();
        if (lateUpdateListeners != null) lateUpdateListeners.Clear();
    }

    #endregion

    #region 过时 API（仅为兼容旧调用保留，新代码请用上面的名字）

    /// <summary>[过时] 名字拼写错误（Updatte），请用 <see cref="AddFixedUpdateListener(UnityAction)"/></summary>
    [Obsolete("拼写已修正，请改用 AddFixedUpdateListener")]
    public void AddFixedUpdatteEventListener(UnityAction fun)
    {
        AddFixedUpdateListener(fun);
    }

    /// <summary>[过时] 名字拼写错误且与成对方法不一致，请用 <see cref="RemoveFixedUpdateListener"/></summary>
    [Obsolete("拼写已修正，请改用 RemoveFixedUpdateListener")]
    public void RemoveFixedUpdatteListener(UnityAction fun)
    {
        RemoveFixedUpdateListener(fun);
    }

    /// <summary>[过时] 命名不统一，请用 <see cref="AddLateUpdateListener(UnityAction)"/></summary>
    [Obsolete("命名已统一，请改用 AddLateUpdateListener")]
    public void AddLateUpdateEventListener(UnityAction fun)
    {
        AddLateUpdateListener(fun);
    }

    /// <summary>[过时] 命名不统一，请用 <see cref="RemoveLateUpdateListener"/></summary>
    [Obsolete("命名已统一，请改用 RemoveLateUpdateListener")]
    public void RemoveLateUpdateEventListener(UnityAction fun)
    {
        RemoveLateUpdateListener(fun);
    }

    #endregion

    #region 协程托管

    /// <summary>
    /// 启动协程并返回可 await 的 <see cref="Task"/>（不限时、不可取消）。
    /// <para>协程抛异常 → Task 抛异常；宿主被停用/销毁 → Task 取消。一定会结束，不会静默挂死。</para>
    /// </summary>
    public Task RunCoroutine(IEnumerator routine)
    {
        return RunCoroutine(routine, 0f, CancellationToken.None);
    }

    /// <summary>
    /// 启动协程并指定超时（秒，&lt;= 0 表示不限时）。超时会停止协程并让 Task 抛 <see cref="TimeoutException"/>。
    /// </summary>
    public Task RunCoroutine(IEnumerator routine, float timeoutSeconds)
    {
        return RunCoroutine(routine, timeoutSeconds, CancellationToken.None);
    }

    /// <summary>
    /// 启动协程并支持取消。取消令牌的回调只置标志，实际的停止动作在下一帧由主线程执行
    /// （避免在工作线程上碰 Unity API）。
    /// </summary>
    public Task RunCoroutine(IEnumerator routine, CancellationToken cancellationToken)
    {
        return RunCoroutine(routine, 0f, cancellationToken);
    }

    /// <summary>启动协程：同时支持超时与取消</summary>
    public Task RunCoroutine(IEnumerator routine, float timeoutSeconds, CancellationToken cancellationToken)
    {
        if (routine == null) return Task.FromException(new ArgumentNullException(nameof(routine)));
        if (destroyed || this == null || cancellationDepth > 0 || !isActiveAndEnabled || cancellationToken.IsCancellationRequested)
            return Task.FromCanceled(new CancellationToken(true));
        EnsureInitialized();
        var job = new CoroutineJob { timeoutSeconds = timeoutSeconds };
        job.routines.Push(routine);
        if (timeoutSeconds > 0f) job.deadline = Time.realtimeSinceStartup + timeoutSeconds;
        if (cancellationToken.CanBeCanceled)
        {
            job.tokenRegistration = cancellationToken.Register(() => job.cancelRequested = true);
            job.hasTokenRegistration = true;
        }
        // StartCoroutine 会立即执行首次 MoveNext；必须先登记，才能覆盖首次迭代里的停用/取消。
        runningJobs.Add(job);
        try
        {
            job.handle = StartCoroutine(RunCoroutineTracked(job));
            if (job.finished) job.handle = null;
            else if (job.handle == null) FinishJob(job, JobEnd.Canceled);
        }
        catch (Exception e) { FinishJob(job, JobEnd.Faulted, e); }
        return job.completion.Task;
    }

    /// <summary>PublicMono 类型上的停止入口同步结束托管任务；基类引用的原始停止 API 不经过此入口。</summary>
    public new void StopAllCoroutines()
    {
        cancellationDepth++;
        try { CancelAllJobs("StopAllCoroutines"); base.StopAllCoroutines(); }
        finally { cancellationDepth--; }
    }

    private IEnumerator RunCoroutineTracked(CoroutineJob job)
    {
        while (!job.finished)
        {
            if (job.cancelRequested) { FinishJob(job, JobEnd.Canceled); yield break; }
            var iterator = job.routines.Peek();
            bool hasNext = false; object current = null; Exception failure = null;
            job.executing = true;
            try { hasNext = iterator.MoveNext(); if (hasNext) current = iterator.Current; }
            catch (Exception e) { failure = e; }
            finally { job.executing = false; }
            // MoveNext 内的 OnDisable 已标记终态；等迭代器退出执行态后再 Dispose 和通知等待者。
            if (job.finished) { FinalizeJob(job); yield break; }
            if (failure != null) { FinishJob(job, JobEnd.Faulted, failure); yield break; }
            if (job.cancelRequested) { FinishJob(job, JobEnd.Canceled); yield break; }
            if (!hasNext)
            {
                job.routines.Pop();
                try { (iterator as IDisposable)?.Dispose(); } catch (Exception e) { failure = e; }
                if (failure != null) { FinishJob(job, JobEnd.Faulted, failure); yield break; }
                if (job.routines.Count == 0) { FinishJob(job, JobEnd.Completed); yield break; }
                continue;
            }
            // Unity 直接执行 yield 出去的 IEnumerator 时，外层无法捕获其异常；由同一驱动接管嵌套栈。
            if (current is IEnumerator nested)
            {
                if (job.routines.Contains(nested))
                { FinishJob(job, JobEnd.Faulted, new InvalidOperationException("协程不能递归 yield 自己的迭代器")); yield break; }
                job.routines.Push(nested); continue;
            }
            yield return current;
        }
    }
    private void ProcessCoroutineJobs()
    {
        if (processing || runningJobs == null || runningJobs.Count == 0) return;
        processing = true; processingJobs.Clear(); processingJobs.AddRange(runningJobs);
        try
        {
            float now = Time.realtimeSinceStartup;
            foreach (var job in processingJobs)
            {
                if (job.finished) continue;
                if (job.cancelRequested) FinishJob(job, JobEnd.Canceled);
                else if (job.deadline > 0 && now >= job.deadline) FinishJob(job, JobEnd.TimedOut);
            }
        }
        finally { processingJobs.Clear(); processing = false; }
    }
    private enum JobEnd { Completed, Faulted, Canceled, TimedOut }
    private void FinishJob(CoroutineJob job, JobEnd end, Exception exception = null)
    {
        if (job == null || job.finished) return;
        job.finished = true; job.end = end; job.exception = exception;
        runningJobs.Remove(job);
        if (job.hasTokenRegistration) { job.tokenRegistration.Dispose(); job.hasTokenRegistration = false; }
        FinalizeJob(job);
    }
    private void FinalizeJob(CoroutineJob job)
    {
        if (job.executing || job.finalized) return;
        job.finalized = true;
        if (job.handle != null) { StopCoroutine(job.handle); job.handle = null; }
        while (job.routines.Count > 0)
        {
            var iterator = job.routines.Pop();
            try { (iterator as IDisposable)?.Dispose(); }
            catch (Exception e)
            {
                if (job.end == JobEnd.Completed) { job.end = JobEnd.Faulted; job.exception = e; }
                else Debug.LogException(e);
            }
        }
        switch (job.end)
        {
            case JobEnd.Completed: job.completion.TrySetResult(true); break;
            case JobEnd.Faulted:
                Debug.LogError("[PublicMono] 协程内部抛异常：" + job.exception);
                job.completion.TrySetException(job.exception); break;
            case JobEnd.Canceled: job.completion.TrySetCanceled(); break;
            case JobEnd.TimedOut:
                var timeout = new TimeoutException($"[PublicMono] 协程超时（{job.timeoutSeconds} 秒）");
                Debug.LogError(timeout.Message); job.completion.TrySetException(timeout); break;
        }
    }
    private void CancelAllJobs(string reason)
    {
        if (runningJobs == null || runningJobs.Count == 0) return;
        cancellationDepth++;
        try
        {
            var canceled = runningJobs.ToArray();
            foreach (var job in canceled) FinishJob(job, JobEnd.Canceled);
        }
        finally { cancellationDepth--; }
    }

    #endregion

    #region 内部类型

    /// <summary>一次托管协程的状态</summary>
    private sealed class CoroutineJob
    {
        public readonly Stack<IEnumerator> routines = new Stack<IEnumerator>();
        public readonly TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>();
        public Coroutine handle;
        public CancellationTokenRegistration tokenRegistration;
        public bool hasTokenRegistration;
        public float timeoutSeconds, deadline;
        public volatile bool cancelRequested;
        public bool finished, executing, finalized;
        public JobEnd end;
        public Exception exception;
    }

    /// <summary>一条帧监听记录</summary>
    private sealed class ListenerEntry
    {
        public UnityAction action;
        public UnityEngine.Object owner;    // 可为 null（强引用，需手动注销）
        public bool hasOwner;               // 区分未提供 owner 与 Unity 对象已销毁
        public bool removed;                // 派发过程中被标记移除，派发结束后统一压缩
    }

    /// <summary>
    /// 一路帧监听器。
    /// <para>用 List 而不是多播委托，是为了做到：查重注册、按需注销、按 owner 存活状态自动清理、
    /// 逐个异常隔离、派发过程中增删安全（不会因为"边遍历边改集合"抛异常）。</para>
    /// </summary>
    private sealed class ListenerList
    {
        private readonly string channelName;
        private readonly List<ListenerEntry> entries = new List<ListenerEntry>();
        private int dispatchDepth;
        private bool needCompact;

        public ListenerList(string channelName)
        {
            this.channelName = channelName;
        }

        public int Count { get { return entries.Count; } }

        public void Add(UnityAction action, UnityEngine.Object owner)
        {
            if (action == null)
            {
                Debug.LogWarning($"[PublicMono] {channelName} 注册了 null 委托，已忽略");
                return;
            }

            // 查重：同一方法重复注册会导致"每帧跑两遍"，这里直接忽略（幂等）
            for (int i = 0; i < entries.Count; i++)
            {
                if (!entries[i].removed && entries[i].action == action)
                {
                    Debug.LogWarning($"[PublicMono] {channelName} 重复注册同一个委托（{Describe(action)}），已忽略");
                    return;
                }
            }

            entries.Add(new ListenerEntry { action = action, owner = owner, hasOwner = !ReferenceEquals(owner, null) });
        }

        public bool Remove(UnityAction action)
        {
            if (action == null) return false;

            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].removed || entries[i].action != action) continue;

                if (dispatchDepth > 0)
                {
                    // 派发中不能直接 RemoveAt（会打乱下标）：先标记，派发结束再压缩
                    entries[i].removed = true;
                    needCompact = true;
                }
                else
                {
                    entries.RemoveAt(i);
                }
                return true;
            }
            return false;
        }

        public void Clear()
        {
            if (dispatchDepth > 0)
            {
                for (int i = 0; i < entries.Count; i++) entries[i].removed = true;
                needCompact = true;
            }
            else
            {
                entries.Clear();
            }
        }

        /// <summary>派发一帧</summary>
        public void Invoke()
        {
            if (entries.Count == 0) return;

            dispatchDepth++;
            try
            {
                // 用派发开始时的数量做上界：监听器里新注册的回调不会在本帧被立刻调用
                int count = entries.Count;
                for (int i = 0; i < count; i++)
                {
                    ListenerEntry entry = entries[i];
                    if (entry.removed) continue;

                    // owner（Unity 对象）已被销毁：自动注销，避免每帧 MissingReferenceException
                    if (entry.hasOwner && entry.owner == null)
                    {
                        entry.removed = true;
                        needCompact = true;
                        continue;
                    }

                    try
                    {
                        entry.action.Invoke();
                    }
                    catch (Exception e)
                    {
                        // 异常隔离：一条监听器抛异常不能连累同一帧的其它监听器
                        Debug.LogError($"[PublicMono] {channelName} 监听器抛异常（已隔离，其它监听器不受影响）\n"
                                       + $"目标：{Describe(entry.action)}\n{e}");
                    }
                }
            }
            finally
            {
                dispatchDepth--;
                if (needCompact && dispatchDepth == 0) Compact();
            }
        }

        private void Compact()
        {
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (entries[i].removed) entries.RemoveAt(i);
            }
            needCompact = false;
        }

        private static string Describe(UnityAction action)
        {
            if (action == null) return "null";
            if (action.Target == null) return $"{action.Method.DeclaringType?.Name}.{action.Method.Name}（静态）";
            return $"{action.Target.GetType().Name}.{action.Method.Name}";
        }
    }

    #endregion
}
