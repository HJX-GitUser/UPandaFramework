using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace UPandaGF
{
    public sealed class DisposableAction : IDisposable
    {
        private Action action;
        public DisposableAction(Action action) { this.action = action; }
        public void Dispose() { var value = action; action = null; value?.Invoke(); }
    }
    /// <summary>主线程作用域。先取消任务，再逆序释放持有物；迟到的持有物立即归还。</summary>
    public sealed class LifetimeScope : IDisposable, ILifetimeScope
    {
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly CancellationToken token;
        private readonly List<IDisposable> owned = new List<IDisposable>();
        public string Name { get; }
        public CancellationToken Token => token;
        public bool IsDisposed { get; private set; }
        public LifetimeScope(string name) { Name = name; token = lifetime.Token; }
        public T Own<T>(T value) where T : IDisposable
        {
            if (IsDisposed) { value?.Dispose(); throw new ObjectDisposedException(Name); }
            if (token.IsCancellationRequested) { value?.Dispose(); token.ThrowIfCancellationRequested(); }
            if (value != null) owned.Add(value);
            return value;
        }
        public IDisposable Subscribe<T>(EventBus bus, IEventListener<T> listener) where T : struct
        {
            if (IsDisposed) throw new ObjectDisposedException(Name);
            return ScopeSubscriptions.Subscribe(this, bus, listener);
        }
        public void Cancel()
        {
            if (!IsDisposed && !token.IsCancellationRequested) lifetime.Cancel();
        }
        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            var errors = new List<Exception>();
            try { lifetime.Cancel(); } catch (Exception e) { errors.Add(e); }
            for (int i = owned.Count - 1; i >= 0; i--)
                try { owned[i].Dispose(); } catch (Exception e) { errors.Add(e); }
            owned.Clear(); lifetime.Dispose();
            if (errors.Count > 0) throw new AggregateException("作用域释放失败：" + Name, errors);
        }
    }
}
