using System;
using System.Collections.Generic;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>
    /// 事件监听器接口（订阅者需实现此接口）
    /// </summary>
    /// <typeparam name="T">消息类型，必须为 struct</typeparam>
    public interface IEventListener<in T> where T : struct
    {
        void OnEvent(T message);
    }

    /// <summary>
    /// 事件总线（可创建多个实例以实现上下文/场景隔离）
    /// </summary>
    public class EventBus
    {
        // 订阅列表包装（每个消息类型一个实例）
        private class SubscriptionList<T> where T : struct
        {
            // 使用 WeakReference 存储，自动弱引用，防止内存泄漏
            private readonly List<WeakReference<IEventListener<T>>> _list = new List<WeakReference<IEventListener<T>>>();

            /// <summary> 添加订阅 </summary>
            public void Add(IEventListener<T> listener)
            {
                if (listener == null) return;
                // 允许重复订阅（若需去重，可在此处检查，但会增加开销）
                _list.Add(new WeakReference<IEventListener<T>>(listener));
            }

            /// <summary> 取消订阅 </summary>
            public void Remove(IEventListener<T> listener)
            {
                if (listener == null) return;
                // 倒序查找并移除（防止多次遍历）
                for (int i = _list.Count - 1; i >= 0; i--)
                {
                    if (_list[i].TryGetTarget(out var target) && target.Equals(listener))
                    {
                        _list.RemoveAt(i);
                        return;
                    }
                }
            }

            /// <summary> 派发消息，同时自动清理已被回收的订阅者 </summary>
            public void Dispatch(T message)
            {
                // 倒序遍历：允许在 OnEvent 中修改订阅列表（添加/移除）而不影响遍历
                for (int i = _list.Count - 1; i >= 0; i--)
                {
                    if (_list[i].TryGetTarget(out var listener))
                    {
                        listener.OnEvent(message);
                    }
                    else
                    {
                        // 目标已被 GC，移除无效项
                        _list.RemoveAt(i);
                    }
                }
            }

            /// <summary> 当前订阅数量（调试用） </summary>
            public int Count => _list.Count;
        }

        // 存储所有消息类型的订阅列表（Key = 消息类型，Value = SubscriptionList<T>）
        private readonly Dictionary<Type, object> _subscriptions = new Dictionary<Type, object>();

        /// <summary> 订阅事件 </summary>
        public void Subscribe<T>(IEventListener<T> listener) where T : struct
        {
            if (listener == null)
            {
                Debug.LogError($"EventBus: 尝试订阅 null 监听器 (Type: {typeof(T)})");
                return;
            }

            var type = typeof(T);
            if (!_subscriptions.TryGetValue(type, out var obj))
            {
                var list = new SubscriptionList<T>();
                _subscriptions[type] = list;
                obj = list;
            }

            (obj as SubscriptionList<T>)?.Add(listener);
        }

        /// <summary> 取消订阅 </summary>
        public void Unsubscribe<T>(IEventListener<T> listener) where T : struct
        {
            if (listener == null) return;

            var type = typeof(T);
            if (_subscriptions.TryGetValue(type, out var obj))
            {
                (obj as SubscriptionList<T>)?.Remove(listener);
            }
        }

        /// <summary> 派发事件 </summary>
        public void Dispatch<T>(T message) where T : struct
        {
            var type = typeof(T);
            if (_subscriptions.TryGetValue(type, out var obj))
            {
                (obj as SubscriptionList<T>)?.Dispatch(message);
            }
        }

        /// <summary> 清空所有订阅（用于场景切换时释放） </summary>
        public void Clear()
        {
            _subscriptions.Clear();
        }

        /// <summary> 获取指定消息类型的订阅数量（调试/测试） </summary>
        public int GetSubscriptionCount<T>() where T : struct
        {
            var type = typeof(T);
            if (_subscriptions.TryGetValue(type, out var obj))
            {
                return (obj as SubscriptionList<T>)?.Count ?? 0;
            }
            return 0;
        }

        // ----- 静态默认实例（可选，方便全局使用） -----
        //private static EventBus _default;
        //public static EventBus Instance => _default != null ? _default : new EventBus();

    }
}