/* ==========================================================================================
 * TimerService —— 统一计时器（Delay / Every / DelayAsync）
 *
 * 【特点】
 *   1) 自己不跑帧循环：由 GameServices 挂到 PublicMono.Update；测试可直接手动 Tick。
 *   2) scaled / unscaled 两套时间：scaled 受 timeScale 与暂停影响（暂停时自动停），unscaled 是真实时间。
 *   3) 回调重入安全：Tick 遍历期间新增的计时器先进 pending，本轮结束后再并入，避免“遍历中修改集合”。
 *   4) 回调异常隔离：某个计时器回调抛异常只记录日志并结束它自己，不影响其他计时器。
 *   5) 大 delta 只补触发一次：卡顿/断点后恢复，不会在单帧里疯狂回调。
 *
 * 【注意】
 *   - Delay(0) 不会同步立刻执行，而是等下一次 Tick（避免在调用栈里重入）。
 *   - 回调在主线程执行；不要在回调里做长时间阻塞操作。
 * ========================================================================================== */

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UPandaGF
{
    /// <summary>
    /// 主线程计时服务。GameServices 把它挂到 PublicMono.Update；
    /// 纯逻辑测试可不依赖 Unity 帧循环，直接调用 Tick 推进时间。
    /// </summary>
    public sealed class TimerService : ITimerService, IDisposable
    {
        /// <summary>一个计时器的运行时状态（不是公开 API）。</summary>
        private sealed class Entry
        {
            public float remaining, interval;          // 剩余时间 / 周期（Every 用）
            public bool scaled, repeat, completed, canceled;
            public Action callback;                    // Delay/Every 的回调（DelayAsync 时为 null）
            public TaskCompletionSource<bool> completion; // DelayAsync 的完成源
            public CancellationTokenRegistration registration; // 取消注册，Dispose 时要释放
            public bool hasRegistration;
        }
        /// <summary>返回给调用方的句柄：可以 Cancel/Dispose，也能查状态；句柄释放后不再持有 Entry。</summary>
        private sealed class Handle : ITimerHandle
        {
            private TimerService owner;
            private Entry entry;
            public Handle(TimerService owner, Entry entry) { this.owner = owner; this.entry = entry; }
            public bool IsCompleted => entry == null || entry.completed;
            public bool IsCanceled => entry == null || entry.canceled;
            public void Cancel() { var o = owner; var e = entry; owner = null; entry = null; o?.Cancel(e); }
            public void Dispose() => Cancel();
        }

        /// <summary>当前在跑的计时器。Tick 从后往前遍历，删除元素时下标不会错位。</summary>
        private readonly List<Entry> timers = new List<Entry>();
        /// <summary>Tick 遍历期间新增的计时器（下一轮再并入 timers）。</summary>
        private readonly List<Entry> pending = new List<Entry>();
        private bool ticking, disposed;

        /// <summary>延时回调。seconds &lt;= 0 时也会推迟到下一次 Tick；scaled=false 用真实时间（暂停也会走）。</summary>
        public ITimerHandle Delay(float seconds, Action callback, bool scaled = true)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            return new Handle(this, Schedule(Math.Max(0f, seconds), 0f, false, scaled, callback, null));
        }
        /// <summary>周期回调。interval 必须 &gt; 0；返回句柄，Cancel/Dispose 即停。</summary>
        public ITimerHandle Every(float interval, Action callback, bool scaled = true)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            if (float.IsNaN(interval) || float.IsInfinity(interval) || interval <= 0f)
                throw new ArgumentOutOfRangeException(nameof(interval));
            return new Handle(this, Schedule(interval, interval, true, scaled, callback, null));
        }
        /// <summary>
        /// 可 await 的延时。取消时返回 Canceled 的 Task（Task.WhenAll 之类的组合用法不会卡住）。
        /// <para>与 Delay 的区别：没有句柄，只能靠 token/Dispose 取消；适合“等一会再继续”的线性流程。</para>
        /// </summary>
        public Task DelayAsync(float seconds, CancellationToken token = default, bool scaled = true)
        {
            if (disposed) return Task.FromCanceled(new CancellationToken(true));
            if (token.IsCancellationRequested) return Task.FromCanceled(token);
            var completion = new TaskCompletionSource<bool>();
            var entry = Schedule(Math.Max(0f, seconds), 0f, false, scaled, null, completion);
            if (token.CanBeCanceled)
            {
                // 取消时把 entry 标记为 canceled 并结束 Task；registration 由 Release 统一 Dispose
                entry.registration = token.Register(() => Cancel(entry));
                entry.hasRegistration = true;
            }
            return completion.Task;
        }

        /// <summary>创建并登记计时器：Tick 进行中则进 pending，等本轮结束再并入（避免修改正在遍历的集合）。</summary>
        private Entry Schedule(float remaining, float interval, bool repeat, bool scaled, Action callback, TaskCompletionSource<bool> completion)
        {
            if (disposed) throw new ObjectDisposedException(nameof(TimerService));
            var entry = new Entry { remaining = remaining, interval = interval, repeat = repeat, scaled = scaled, callback = callback, completion = completion };
            if (ticking) pending.Add(entry); else timers.Add(entry);
            return entry;
        }
        /// <summary>标记取消并结束 await；Entry 会在下次 Tick 时被清理。</summary>
        private void Cancel(Entry entry)
        {
            if (entry == null || entry.canceled || entry.completed) return;
            entry.canceled = true;
            entry.completion?.TrySetCanceled();
        }

        /// <summary>推进所有计时器；scaledDelta 传暂停感知的时间（暂停时为 0），unscaledDelta 传真实帧时间。</summary>
        public void Tick(float scaledDelta, float unscaledDelta)
        {
            if (disposed) return;
            // 防御负数 delta（时间回拨/编辑器异常）
            if (scaledDelta < 0f) scaledDelta = 0f;
            if (unscaledDelta < 0f) unscaledDelta = 0f;
            ticking = true;
            try
            {
                // 从后往前遍历：删除当前元素不会影响后面的下标
                for (int i = timers.Count - 1; i >= 0; i--)
                {
                    var entry = timers[i];
                    if (entry.canceled || entry.completed) { timers.RemoveAt(i); Release(entry); continue; }
                    // scaled 计时器在暂停时拿到 0，所以自然停住；unscaled 计时器照常走
                    entry.remaining -= entry.scaled ? scaledDelta : unscaledDelta;
                    if (entry.remaining > 0f) continue;
                    Fire(entry);
                    if (entry.repeat && !entry.canceled)
                    {
                        // 大 delta 只补触发一次，避免卡顿后单帧内回调风暴
                        entry.remaining += entry.interval;
                        if (entry.remaining <= 0f) entry.remaining = entry.interval;
                    }
                    else { entry.completed = true; timers.RemoveAt(i); Release(entry); }
                }
            }
            finally
            {
                ticking = false;
                // 把遍历期间新增的计时器并入（下一轮才会计时，避免刚创建就被大 delta 立即触发）
                if (pending.Count > 0) { timers.AddRange(pending); pending.Clear(); }
            }
        }
        /// <summary>触发回调。回调抛异常时：标记该计时器完成并记错误日志，不影响其他计时器。</summary>
        private static void Fire(Entry entry)
        {
            try { entry.callback?.Invoke(); entry.completion?.TrySetResult(true); }
            catch (Exception e)
            {
                entry.completed = true;
                PLogger.LogError("[TimerService] 计时器回调异常：" + e);
            }
        }
        /// <summary>释放取消注册句柄（不释放会一直把 Entry 挂在 CancellationTokenSource 上）。</summary>
        private static void Release(Entry entry)
        {
            if (!entry.hasRegistration) return;
            entry.registration.Dispose();
            entry.hasRegistration = false;
        }
        /// <summary>关闭：取消所有计时器并结束仍挂着的 await；之后 Schedule 会抛 ObjectDisposedException。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var entry in timers) { entry.canceled = true; entry.completion?.TrySetCanceled(); Release(entry); }
            foreach (var entry in pending) { entry.canceled = true; entry.completion?.TrySetCanceled(); Release(entry); }
            timers.Clear(); pending.Clear();
        }
    }
}
