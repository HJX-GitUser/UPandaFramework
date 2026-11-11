using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace UPandaGF
{
    /// <summary>
    /// 面板级事件绑定容器：UnityEvent、EventBus、EventCenter 和任意 IDisposable
    /// 都可登记到同一作用域，关闭面板时统一解绑，避免闭包/委托持有已关闭面板。
    /// </summary>
    public sealed class UIEventScope : IDisposable
    {
        private readonly List<IDisposable> items = new List<IDisposable>();
        private bool disposed;

        public bool IsDisposed => disposed;

        public void Add(IDisposable item)
        {
            if (item == null) return;
            if (disposed)
            {
                item.Dispose();
                return;
            }
            items.Add(item);
        }

        public void Listen(UnityEvent evt, UnityAction callback)
        {
            if (evt == null || callback == null) return;
            evt.AddListener(callback);
            Add(new DisposableAction(() =>
            {
                if (evt != null) evt.RemoveListener(callback);
            }));
        }

        public void Listen<T>(UnityEvent<T> evt, UnityAction<T> callback)
        {
            if (evt == null || callback == null) return;
            evt.AddListener(callback);
            Add(new DisposableAction(() =>
            {
                if (evt != null) evt.RemoveListener(callback);
            }));
        }

        public void Listen<T>(EventCenter center, UnityAction<T> callback) where T : EventArgBase
        {
            if (center == null || callback == null) return;
            center.AddEventListener(callback);
            Add(new DisposableAction(() => center.RemoveEventListener(callback)));
        }

        public IDisposable Subscribe<T>(EventBus bus, IEventListener<T> listener) where T : struct
        {
            if (bus == null || listener == null) return null;
            bus.Subscribe(listener);
            DisposableAction subscription = new DisposableAction(() => bus.Unsubscribe(listener));
            Add(subscription);
            return subscription;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            for (int i = items.Count - 1; i >= 0; i--)
            {
                try { items[i]?.Dispose(); }
                catch (Exception e) { Debug.LogException(e); }
            }
            items.Clear();
        }
    }
}
