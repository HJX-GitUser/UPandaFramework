using System;
using UnityEngine.Events;

namespace UPandaGF
{
    public static class ScopeSubscriptions
    {
        private sealed class Listener<T> : IEventListener<T> where T : struct
        {
            private readonly IEventListener<T> target;
            public Listener(IEventListener<T> target) { this.target = target; }
            public void OnEvent(T message) { target.OnEvent(message); }
        }
        /// <summary>每个作用域拥有独立订阅，不会退订外部恰好使用同一监听者的注册。</summary>
        public static IDisposable Subscribe<T>(this ILifetimeScope scope, EventBus bus, IEventListener<T> listener) where T : struct
        {
            if (scope.IsDisposed) throw new ObjectDisposedException(nameof(scope));
            scope.Token.ThrowIfCancellationRequested();
            if (bus == null || listener == null) throw new ArgumentNullException();
            var proxy = new Listener<T>(listener); bus.Subscribe(proxy);
            return scope.Own(new DisposableAction(() => bus.Unsubscribe(proxy)));
        }
        public static IDisposable Subscribe<T>(this ILifetimeScope scope, EventCenter bus, UnityAction<T> callback) where T : EventArgBase
        {
            if (scope.IsDisposed) throw new ObjectDisposedException(nameof(scope));
            scope.Token.ThrowIfCancellationRequested();
            if (bus == null || callback == null) throw new ArgumentNullException();
            UnityAction<T> proxy = value => callback(value);
            bus.AddEventListener(proxy);
            return scope.Own(new DisposableAction(() => bus.RemoveEventListener(proxy)));
        }
    }
}
