using System;
using System.Collections.Generic;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>只读可观察值；UI 绑定只依赖该接口。</summary>
    public interface IReadOnlyObservable<T>
    {
        T Value { get; }
        IDisposable Subscribe(Action<T> listener, bool invokeCurrent = false);
    }

    /// <summary>
    /// 轻量主线程可观察值。值变化时只推送订阅者，不轮询；
    /// 订阅返回的 IDisposable 可直接加入 BasePanel.OpenEvents。
    /// </summary>
    public class Observable<T> : IReadOnlyObservable<T>, IDisposable
    {
        private T value;
        private readonly List<Action<T>> listeners = new List<Action<T>>();
        private bool disposed;

        public Observable() { }
        public Observable(T initial) { value = initial; }

        public T Value
        {
            get { return value; }
            set
            {
                if (disposed) return;
                if (EqualityComparer<T>.Default.Equals(this.value, value)) return;
                this.value = value;
                Notify(value);
            }
        }

        public void Set(T next) { Value = next; }

        public IDisposable Subscribe(Action<T> listener, bool invokeCurrent = false)
        {
            if (listener == null) return null;
            if (disposed)
            {
                if (invokeCurrent) listener(value);
                return new DisposableAction(() => { });
            }

            listeners.Add(listener);
            if (invokeCurrent) listener(value);
            return new DisposableAction(() => listeners.Remove(listener));
        }

        private void Notify(T next)
        {
            if (listeners.Count == 0) return;
            Action<T>[] snapshot = listeners.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                try { snapshot[i]?.Invoke(next); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        public void Dispose()
        {
            disposed = true;
            listeners.Clear();
        }
    }

    // 常用值类型提供具体类，热更新代码可直接使用，避免 AOT 泛型实例化限制。
    public sealed class BoolObservable : Observable<bool>
    {
        public BoolObservable(bool initial = false) : base(initial) { }
    }

    public sealed class FloatObservable : Observable<float>
    {
        public FloatObservable(float initial = 0f) : base(initial) { }
    }

    public sealed class IntObservable : Observable<int>
    {
        public IntObservable(int initial = 0) : base(initial) { }
    }

    public sealed class StringObservable : Observable<string>
    {
        public StringObservable(string initial = "") : base(initial) { }
    }

    public sealed class ColorObservable : Observable<Color>
    {
        public ColorObservable(Color initial) : base(initial) { }
    }

    /// <summary>按登记顺序逆序释放的一组持有物；重复 Dispose 安全。</summary>
    public sealed class CompositeDisposable : IDisposable
    {
        private readonly List<IDisposable> items = new List<IDisposable>();
        private bool disposed;

        public bool IsDisposed => disposed;

        public T Add<T>(T item) where T : IDisposable
        {
            if (item == null) return item;
            if (disposed)
            {
                item.Dispose();
                return item;
            }
            items.Add(item);
            return item;
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
