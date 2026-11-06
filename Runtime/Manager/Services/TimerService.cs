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
        private sealed class Entry
        {
            public float remaining, interval;
            public bool scaled, repeat, completed, canceled;
            public Action callback;
            public TaskCompletionSource<bool> completion;
            public CancellationTokenRegistration registration;
            public bool hasRegistration;
        }
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

        private readonly List<Entry> timers = new List<Entry>();
        private readonly List<Entry> pending = new List<Entry>();
        private bool ticking, disposed;

        public ITimerHandle Delay(float seconds, Action callback, bool scaled = true)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            return new Handle(this, Schedule(Math.Max(0f, seconds), 0f, false, scaled, callback, null));
        }
        public ITimerHandle Every(float interval, Action callback, bool scaled = true)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            if (float.IsNaN(interval) || float.IsInfinity(interval) || interval <= 0f)
                throw new ArgumentOutOfRangeException(nameof(interval));
            return new Handle(this, Schedule(interval, interval, true, scaled, callback, null));
        }
        public Task DelayAsync(float seconds, CancellationToken token = default, bool scaled = true)
        {
            if (disposed) return Task.FromCanceled(new CancellationToken(true));
            if (token.IsCancellationRequested) return Task.FromCanceled(token);
            var completion = new TaskCompletionSource<bool>();
            var entry = Schedule(Math.Max(0f, seconds), 0f, false, scaled, null, completion);
            if (token.CanBeCanceled)
            {
                entry.registration = token.Register(() => Cancel(entry));
                entry.hasRegistration = true;
            }
            return completion.Task;
        }

        private Entry Schedule(float remaining, float interval, bool repeat, bool scaled, Action callback, TaskCompletionSource<bool> completion)
        {
            if (disposed) throw new ObjectDisposedException(nameof(TimerService));
            var entry = new Entry { remaining = remaining, interval = interval, repeat = repeat, scaled = scaled, callback = callback, completion = completion };
            if (ticking) pending.Add(entry); else timers.Add(entry);
            return entry;
        }
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
            if (scaledDelta < 0f) scaledDelta = 0f;
            if (unscaledDelta < 0f) unscaledDelta = 0f;
            ticking = true;
            try
            {
                for (int i = timers.Count - 1; i >= 0; i--)
                {
                    var entry = timers[i];
                    if (entry.canceled || entry.completed) { timers.RemoveAt(i); Release(entry); continue; }
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
                if (pending.Count > 0) { timers.AddRange(pending); pending.Clear(); }
            }
        }
        private static void Fire(Entry entry)
        {
            try { entry.callback?.Invoke(); entry.completion?.TrySetResult(true); }
            catch (Exception e)
            {
                entry.completed = true;
                PLogger.LogError("[TimerService] 计时器回调异常：" + e);
            }
        }
        private static void Release(Entry entry)
        {
            if (!entry.hasRegistration) return;
            entry.registration.Dispose();
            entry.hasRegistration = false;
        }
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
