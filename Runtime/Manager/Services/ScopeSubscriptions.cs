/* ==========================================================================================
 * ScopeSubscriptions —— 把“事件订阅”纳入作用域管理
 *
 * 【解决的问题】
 *   事件总线（EventBus / EventCenter）本身只给 IDisposable 退订句柄；如果调用方忘了退订，
 *   就会出现“对象已销毁但回调还在被调用”。这里把退订句柄挂进 LifetimeScope：
 *   作用域释放 → 自动退订。
 *
 * 【为什么要包一层 proxy】
 *   EventBus 的订阅是“按监听者对象”区分的。若直接把业务监听者塞进去，同一个监听者被两个
 *   作用域订阅时，任一个作用域的退订都会把另一个也退掉。用一层独立 proxy 包住，
 *   每个作用域退订的是自己的 proxy，互不影响。
 * ========================================================================================== */

using System;
using UnityEngine.Events;

namespace UPandaGF
{
    /// <summary>作用域内事件订阅的静态入口（真正的实现挂在 LifetimeScope.Own 上）。</summary>
    public static class ScopeSubscriptions
    {
        /// <summary>透传代理：让每次订阅在总线上都是一个独立对象，从而能独立退订。</summary>
        private sealed class Listener<T> : IEventListener<T> where T : struct
        {
            private readonly IEventListener<T> target;
            public Listener(IEventListener<T> target) { this.target = target; }
            public void OnEvent(T message) { target.OnEvent(message); }
        }
        /// <summary>每个作用域拥有独立订阅，不会退订外部恰好使用同一监听者的注册。</summary>
        public static IDisposable Subscribe<T>(this ILifetimeScope scope, EventBus bus, IEventListener<T> listener) where T : struct
        {
            // 作用域已关闭/已取消：直接拒绝，避免“订阅完就泄漏”
            if (scope.IsDisposed) throw new ObjectDisposedException(nameof(scope));
            scope.Token.ThrowIfCancellationRequested();
            if (bus == null || listener == null) throw new ArgumentNullException();
            var proxy = new Listener<T>(listener); bus.Subscribe(proxy);
            // 退订句柄本身也是持有物：作用域 Dispose 时自动退订
            return scope.Own(new DisposableAction(() => bus.Unsubscribe(proxy)));
        }
        /// <summary>
        /// 旧版 EventCenter（UnityEvent 风格）的同样处理。
        /// <para>注意：这里包装的是 UnityAction 委托，退订必须用同一个委托实例，所以先存进 proxy 再 Add/Remove。</para>
        /// </summary>
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
