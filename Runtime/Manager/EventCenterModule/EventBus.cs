using System;
using System.Collections.Generic;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>
    /// 事件监听器接口（订阅者需实现此接口）。
    ///
    /// 注意：实现者必须是**引用类型**（class）。若用 struct 实现会被装箱，
    /// 而总线内部以弱引用保存监听者，装箱对象随时可能被 GC 回收导致订阅静默失效；
    /// EventBus.Subscribe 会检测到这种写法并直接报错。
    /// </summary>
    /// <typeparam name="T">消息类型，必须为 struct（值类型消息，零 GC）</typeparam>
    public interface IEventListener<in T> where T : struct
    {
        void OnEvent(T message);
    }

    /// <summary>EventBus 某个消息类型的调试信息（供「事件调试窗口」展示）</summary>
    public class EventBusSubscriptionInfo
    {
        public string messageType;
        public int count;
        public List<string> subscribers = new List<string>();
    }

    /// <summary>
    /// 事件总线（可创建多个实例，实现上下文 / 场景隔离）。
    ///
    /// 2026-09 重构要点：
    ///   1) 派发改用「弱引用快照 + 锁外调用」：派发过程中订阅 / 退订不会造成重复派发或漏派发，用户回调也不再占锁；
    ///   2) 每个监听者单独 try/catch：一个监听者抛异常不影响其余订阅者；
    ///   3) 退订比较改用 ReferenceEquals：避免订阅者重写 Equals 后误退订别的对象；
    ///   4) Count / GetSubscriptionCount 先清理已被 GC 回收的订阅项再返回；
    ///   5) Subscribe 拒绝值类型（struct）监听器 —— 装箱后弱引用立刻失效；
    ///   6) 派发顺序为「订阅顺序」（原实现是倒序，那只是为了规避遍历中修改列表的问题，现在由快照解决）。
    /// </summary>
    public class EventBus
    {
        /// <summary>订阅列表的非泛型视图：让总线在不知道 T 的情况下统计/导出/清空调试信息</summary>
        private interface ISubscriptionList
        {
            int Count { get; }
            void CollectNames(List<string> result);
            void Clear();
        }

        /// <summary>每个消息类型一个订阅列表</summary>
        private class SubscriptionList<T> : ISubscriptionList where T : struct
        {
            private readonly List<WeakReference<IEventListener<T>>> _list = new List<WeakReference<IEventListener<T>>>();
            private readonly object _lock = new object();

            // 派发快照：只存弱引用，因此不会阻止监听者被 GC（若快照里存强引用，等于把弱引用机制废掉）
            private WeakReference<IEventListener<T>>[] _snapshot;
            private bool _snapshotDirty = true;

            /// <summary>添加订阅（允许重复订阅）</summary>
            public void Add(IEventListener<T> listener)
            {
                if (listener == null) return;
                lock (_lock)
                {
                    PurgeDead();
                    _list.Add(new WeakReference<IEventListener<T>>(listener));
                    _snapshotDirty = true;
                }
            }

            /// <summary>取消订阅（按引用比较，一次只移除一个）</summary>
            public void Remove(IEventListener<T> listener)
            {
                if (listener == null) return;
                lock (_lock)
                {
                    for (int i = _list.Count - 1; i >= 0; i--)
                    {
                        if (_list[i].TryGetTarget(out IEventListener<T> target) && ReferenceEquals(target, listener))
                        {
                            _list.RemoveAt(i);
                            _snapshotDirty = true;
                            return;
                        }
                    }
                }
            }

            /// <summary>派发消息：遍历快照，因此派发中增删订阅既不会重复派发也不会漏派发</summary>
            public void Dispatch(T message)
            {
                WeakReference<IEventListener<T>>[] items;
                lock (_lock)
                {
                    if (_snapshotDirty)
                    {
                        _snapshot = _list.ToArray();
                        _snapshotDirty = false;
                    }
                    items = _snapshot;
                }

                if (items == null) return;

                for (int i = 0; i < items.Length; i++)
                {
                    if (!items[i].TryGetTarget(out IEventListener<T> listener) || listener == null)
                        continue;   // 已被 GC：留着下次增删订阅时统一清理

                    try
                    {
                        listener.OnEvent(message);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[EventBus] 消息 {typeof(T).Name} 的监听者 {listener.GetType().Name} 抛出异常（已隔离，其余订阅者不受影响）：{e}");
                    }
                }
            }

            /// <summary>有效订阅数量（先清理已回收项）</summary>
            public int Count
            {
                get { lock (_lock) { PurgeDead(); return _list.Count; } }
            }

            public void CollectNames(List<string> result)
            {
                if (result == null) return;
                lock (_lock)
                {
                    PurgeDead();
                    for (int i = 0; i < _list.Count; i++)
                    {
                        if (_list[i].TryGetTarget(out IEventListener<T> target) && target != null)
                            result.Add(target.GetType().Name);
                    }
                }
            }

            public void Clear()
            {
                lock (_lock)
                {
                    _list.Clear();
                    _snapshot = null;
                    _snapshotDirty = true;
                }
            }

            /// <summary>清理已被 GC 回收的订阅项（需在 lock 内调用）</summary>
            private void PurgeDead()
            {
                for (int i = _list.Count - 1; i >= 0; i--)
                {
                    if (_list[i].TryGetTarget(out IEventListener<T> target) && target != null) continue;
                    _list.RemoveAt(i);
                    _snapshotDirty = true;
                }
            }
        }

        // ----- 实例登记：只服务于调试窗口，弱引用不会阻止实例被回收 -----
        private static readonly List<WeakReference<EventBus>> _instances = new List<WeakReference<EventBus>>();

        readonly Dictionary<Type, object> _subscriptions = new Dictionary<Type, object>();
        readonly object _dictLock = new object();   // 保护字典本身；用户回调一律在锁外执行

        public EventBus()
        {
            lock (_instances)
            {
                PurgeDeadInstances();
                _instances.Add(new WeakReference<EventBus>(this));
            }
        }

        /// <summary>当前存活的事件总线实例（调试用，业务侧一般不需要）</summary>
        public static List<EventBus> GetAliveInstances()
        {
            List<EventBus> result = new List<EventBus>();
            lock (_instances)
            {
                PurgeDeadInstances();
                for (int i = 0; i < _instances.Count; i++)
                {
                    if (_instances[i].TryGetTarget(out EventBus bus) && bus != null) result.Add(bus);
                }
            }
            return result;
        }

        private static void PurgeDeadInstances()
        {
            for (int i = _instances.Count - 1; i >= 0; i--)
            {
                if (_instances[i].TryGetTarget(out EventBus bus) && bus != null) continue;
                _instances.RemoveAt(i);
            }
        }

        /// <summary>订阅事件</summary>
        public void Subscribe<T>(IEventListener<T> listener) where T : struct
        {
            if (listener == null)
            {
                Debug.LogError($"EventBus: 尝试订阅 null 监听器 (Type: {typeof(T)})");
                return;
            }

            if (listener.GetType().IsValueType)
            {
                Debug.LogError($"EventBus: 监听器 {listener.GetType().Name} 是值类型（struct），装箱后会被 GC 回收导致订阅静默失效，请改用 class 实现 IEventListener<{typeof(T).Name}>");
                return;
            }

            Type type = typeof(T);
            lock (_dictLock)
            {
                if (!_subscriptions.TryGetValue(type, out object obj) || !(obj is SubscriptionList<T> list))
                {
                    list = new SubscriptionList<T>();
                    _subscriptions[type] = list;
                }
                list.Add(listener);
            }
        }

        /// <summary>取消订阅</summary>
        public void Unsubscribe<T>(IEventListener<T> listener) where T : struct
        {
            if (listener == null) return;
            SubscriptionList<T> list = Find<T>();
            if (list != null) list.Remove(listener);
        }

        /// <summary>派发事件</summary>
        public void Dispatch<T>(T message) where T : struct
        {
            SubscriptionList<T> list = Find<T>();
            if (list != null) list.Dispatch(message);
        }

        /// <summary>清空所有订阅（用于场景切换时释放）</summary>
        public void Clear()
        {
            lock (_dictLock)
            {
                foreach (KeyValuePair<Type, object> kv in _subscriptions)
                {
                    if (kv.Value is ISubscriptionList list) list.Clear();
                }
                _subscriptions.Clear();
            }
        }

        /// <summary>获取指定消息类型的有效订阅数量（调试 / 测试）</summary>
        public int GetSubscriptionCount<T>() where T : struct
        {
            SubscriptionList<T> list = Find<T>();
            return list != null ? list.Count : 0;
        }

        /// <summary>指定消息类型是否还有订阅者（调试 / 测试）</summary>
        public bool HasSubscriber<T>() where T : struct
        {
            return GetSubscriptionCount<T>() > 0;
        }

        /// <summary>某个消息类型的订阅者名称列表（调试 / 测试）</summary>
        public List<string> GetSubscriberNames<T>() where T : struct
        {
            List<string> result = new List<string>();
            SubscriptionList<T> list = Find<T>();
            if (list != null) list.CollectNames(result);
            return result;
        }

        /// <summary>本实例的订阅调试快照（消息类型 → 订阅者数量 / 名称）</summary>
        public List<EventBusSubscriptionInfo> GetDebugSnapshot()
        {
            List<EventBusSubscriptionInfo> result = new List<EventBusSubscriptionInfo>();
            lock (_dictLock)
            {
                foreach (KeyValuePair<Type, object> kv in _subscriptions)
                {
                    if (!(kv.Value is ISubscriptionList list)) continue;
                    EventBusSubscriptionInfo info = new EventBusSubscriptionInfo
                    {
                        messageType = kv.Key.Name,
                        count = list.Count
                    };
                    list.CollectNames(info.subscribers);
                    result.Add(info);
                }
            }
            result.Sort((a, b) => b.count.CompareTo(a.count));
            return result;
        }

        /// <summary>取某个消息类型的订阅列表（不存在返回 null）</summary>
        private SubscriptionList<T> Find<T>() where T : struct
        {
            lock (_dictLock)
            {
                if (_subscriptions.TryGetValue(typeof(T), out object obj) && obj is SubscriptionList<T> list)
                    return list;
                return null;
            }
        }

        // ----- 静态默认实例（可选，方便全局使用）-----
        //private static EventBus _default;
        //public static EventBus Instance => _default != null ? _default : new EventBus();
    }
}
