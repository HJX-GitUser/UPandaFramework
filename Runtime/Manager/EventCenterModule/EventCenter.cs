using System;
using System.Collections.Generic;
using UnityEngine.Events;

namespace UPandaGF
{
    public abstract class EventArgBase { }

    /// <summary>单个监听者的调试信息（供「事件调试窗口」展示）</summary>
    public class EventListenerInfo
    {
        public string methodName;
        public string targetType;
        public int priority;
        public bool hasOwner;
    }

    /// <summary>某个事件类型的调试信息（供「事件调试窗口」展示）</summary>
    public class EventTypeDebugInfo
    {
        public string eventType;
        public int listenerCount;
        public long dispatchCount;
        public List<EventListenerInfo> listeners = new List<EventListenerInfo>();
    }

    /// <summary>
    /// 非泛型视图：让 EventCenter 能在不知道 T 的情况下统计监听者数量/取调试信息。
    /// （internal 类型只在本程序集内使用，不会污染对外 API）
    /// </summary>
    internal interface IPDEventInfo
    {
        int ActionCount { get; }
        List<EventListenerInfo> GetListenerInfos();
    }

    /// <summary>
    /// 单个事件类型的监听容器。
    ///
    /// 2026-09 重构要点：
    ///   1) 去重改为比较**委托本身**（Delegate 相等 = 同一方法 + 同一目标对象），不再用 GetHashCode：
    ///      原实现只存 int 哈希，两个不同委托哈希相同时会被误判为「重复加入」而静默丢弃合法监听；
    ///   2) 派发改为「快照 + 锁外调用」：慢监听者不再长时间占锁；派发过程中增删监听不会错位（不重复、不漏派发）；
    ///   3) 每个监听者单独 try/catch：一个监听者抛异常不再中断其余监听者；
    ///   4) 支持 owner（UnityEngine.Object）：owner 被销毁后自动失效并清理，避免「已销毁对象仍被回调」；
    ///   5) 支持优先级：priority 大的先收到事件，同优先级保持注册顺序（稳定）。
    /// </summary>
    internal class PD_EventInfo<T> : IPDEventInfo where T : EventArgBase
    {
        private class Listener
        {
            public UnityAction<T> action;
            public WeakReference<UnityEngine.Object> owner;   // null = 没有 owner，需要手动注销
            public int priority;
            public int seq;                                   // 注册序号：同优先级稳定排序用
        }

        private static readonly UnityAction<T>[] Empty = new UnityAction<T>[0];

        private readonly List<Listener> listeners = new List<Listener>();
        private readonly object lockObj = new object();
        private UnityAction<T>[] snapshot = Empty;             // 派发快照：只在变更后重建，派发过程零分配
        private bool snapshotDirty = true;
        private int seqSeed;

        public PD_EventInfo(UnityAction<T> action, UnityEngine.Object owner, int priority)
        {
            AddAction(action, owner, priority);
        }

        public int ActionCount
        {
            get { lock (lockObj) { PurgeDeadOwners(); return listeners.Count; } }
        }

        public void AddAction(UnityAction<T> action, UnityEngine.Object owner, int priority)
        {
            if (action == null) return;
            lock (lockObj)
            {
                PurgeDeadOwners();
                for (int i = 0; i < listeners.Count; i++)
                {
                    if (listeners[i].action == action)   // 委托相等比较（不用哈希）
                    {
                        PLogger.LogWarning($"事件 {typeof(T).Name} 的监听方法 {action.Method.Name} 被重复加入，已忽略");
                        return;
                    }
                }

                listeners.Add(new Listener
                {
                    action = action,
                    owner = owner != null ? new WeakReference<UnityEngine.Object>(owner) : null,
                    priority = priority,
                    seq = seqSeed++
                });
                snapshotDirty = true;
            }
        }

        public void RemoveAction(UnityAction<T> action)
        {
            if (action == null) return;
            lock (lockObj)
            {
                for (int i = listeners.Count - 1; i >= 0; i--)
                {
                    if (listeners[i].action != action) continue;
                    listeners.RemoveAt(i);
                    snapshotDirty = true;
                    return;   // 与原实现一致：一次只移除一个
                }
            }
        }

        /// <summary>派发：快照 + 锁外调用 + 单个监听者异常隔离</summary>
        public void Invoke(T arg)
        {
            UnityAction<T>[] callbacks;
            lock (lockObj)
            {
                PurgeDeadOwners();
                if (snapshotDirty) RebuildSnapshot();
                callbacks = snapshot;
            }

            if (callbacks == null || callbacks.Length == 0) return;

            for (int i = 0; i < callbacks.Length; i++)
            {
                try
                {
                    callbacks[i].Invoke(arg);
                }
                catch (Exception e)
                {
                    PLogger.LogError($"[EventCenter] 事件 {typeof(T).Name} 的监听者 {callbacks[i].Method.Name} 抛出异常（已隔离，其余监听者不受影响）：{e}");
                }
            }
        }

        public List<EventListenerInfo> GetListenerInfos()
        {
            lock (lockObj)
            {
                PurgeDeadOwners();
                List<EventListenerInfo> result = new List<EventListenerInfo>(listeners.Count);
                for (int i = 0; i < listeners.Count; i++)
                {
                    Listener item = listeners[i];
                    result.Add(new EventListenerInfo
                    {
                        methodName = item.action != null ? item.action.Method.Name : "<null>",
                        targetType = item.action != null && item.action.Target != null ? item.action.Target.GetType().Name : "(静态方法)",
                        priority = item.priority,
                        hasOwner = item.owner != null
                    });
                }
                return result;
            }
        }

        /// <summary>重建派发快照（需在 lock 内调用）</summary>
        private void RebuildSnapshot()
        {
            snapshotDirty = false;
            if (listeners.Count == 0)
            {
                snapshot = Empty;
                return;
            }

            List<Listener> ordered = new List<Listener>(listeners);
            ordered.Sort((a, b) => a.priority != b.priority ? b.priority.CompareTo(a.priority) : a.seq.CompareTo(b.seq));

            UnityAction<T>[] result = new UnityAction<T>[ordered.Count];
            for (int i = 0; i < ordered.Count; i++) result[i] = ordered[i].action;
            snapshot = result;
        }

        /// <summary>清理 owner 已被销毁的监听（需在 lock 内调用）</summary>
        private void PurgeDeadOwners()
        {
            for (int i = listeners.Count - 1; i >= 0; i--)
            {
                WeakReference<UnityEngine.Object> owner = listeners[i].owner;
                if (owner == null) continue;
                if (owner.TryGetTarget(out UnityEngine.Object target) && target != null) continue;
                listeners.RemoveAt(i);
                snapshotDirty = true;
            }
        }
    }

    /// <summary>
    /// 事件中心（委托式全局事件，单例）。
    ///
    /// 用法：
    ///   EventCenter.Instance.AddEventListener&lt;MyEvent&gt;(OnMyEvent);              // 普通注册
    ///   EventCenter.Instance.AddEventListener&lt;MyEvent&gt;(this, OnMyEvent);        // 带 owner：this 被销毁后自动失效
    ///   EventCenter.Instance.AddEventListener&lt;MyEvent&gt;(OnMyEvent, 10);          // 优先级：数字大的先收到
    ///   EventCenter.Instance.AddEventListenerOnce&lt;MyEvent&gt;(OnMyEvent);          // 只收一次
    ///   EventCenter.Instance.EventTrigger(new MyEvent(...));
    ///   EventCenter.Instance.RemoveEventListener&lt;MyEvent&gt;(OnMyEvent);
    ///
    /// 注意：事件参数是 class（EventArgBase），每次派发都会 new 一次，会产生少量 GC；
    /// 帧内高频、且在意 GC 的场景建议改用 EventBus（struct 消息，零分配）。
    ///
    /// 2026-09 重构：事件类型改用 Type 做键（原先用 typeof(T).GetHashCode()，碰撞会静默失效）、
    /// 去重改用委托相等、补上 RemoveEventListener 的锁、派发锁外执行并隔离异常。
    /// </summary>
    public class EventCenter : LazySingletonBase<EventCenter>
    {
        private readonly Dictionary<Type, object> eventDic = new Dictionary<Type, object>();
        private readonly Dictionary<Type, long> dispatchCounts = new Dictionary<Type, long>();
        private readonly object lockObj = new object();   // 只保护容器本身，用户回调一律在锁外执行

        /// <summary>派发时若该事件没有任何监听者，是否打印提示（排查「事件发了没人收」，默认关闭）</summary>
        public bool warnOnMissingListener = false;

        /// <summary>当前有监听者的事件类型数量（调试用）</summary>
        public int ListenerTypeCount
        {
            get { lock (lockObj) { return eventDic.Count; } }
        }

        #region 注册 / 注销

        /// <summary>添加事件监听</summary>
        public void AddEventListener<T>(UnityAction<T> action) where T : EventArgBase
        {
            AddEventListener(action, 0, null);
        }

        /// <summary>添加事件监听（指定优先级，数字越大越先收到事件）</summary>
        public void AddEventListener<T>(UnityAction<T> action, int priority) where T : EventArgBase
        {
            AddEventListener(action, priority, null);
        }

        /// <summary>
        /// 添加事件监听（带 owner，推荐在 MonoBehaviour 中使用）：
        /// owner 被 Destroy 后该监听自动失效并从列表清理，避免忘记注销导致的泄漏与「对已销毁对象回调」。
        /// </summary>
        public void AddEventListener<T>(UnityEngine.Object owner, UnityAction<T> action, int priority = 0) where T : EventArgBase
        {
            AddEventListener(action, priority, owner);
        }

        /// <summary>只监听一次：收到事件后自动注销</summary>
        public void AddEventListenerOnce<T>(UnityAction<T> action) where T : EventArgBase
        {
            if (action == null) return;
            UnityAction<T> wrapper = null;
            wrapper = arg =>
            {
                RemoveEventListener<T>(wrapper);   // 先注销再回调，保证「只收一次」
                action(arg);
            };
            AddEventListener(wrapper, 0, null);
        }

        private void AddEventListener<T>(UnityAction<T> action, int priority, UnityEngine.Object owner) where T : EventArgBase
        {
            if (action == null) return;
            Type id = typeof(T);
            lock (lockObj)
            {
                if (eventDic.TryGetValue(id, out object obj) && obj is PD_EventInfo<T> exist)
                {
                    exist.AddAction(action, owner, priority);
                }
                else
                {
                    eventDic[id] = new PD_EventInfo<T>(action, owner, priority);
                }
            }
        }

        /// <summary>移除事件监听（同一个「方法 + 目标对象」只移除一次）</summary>
        public void RemoveEventListener<T>(UnityAction<T> action) where T : EventArgBase
        {
            if (action == null) return;
            Type id = typeof(T);
            lock (lockObj)   // 修复：原先这里漏了加锁，与其它方法线程安全承诺不一致
            {
                if (!eventDic.TryGetValue(id, out object obj)) return;
                if (!(obj is PD_EventInfo<T> info)) return;
                info.RemoveAction(action);
                if (info.ActionCount == 0) eventDic.Remove(id);   // 没有监听者时不再保留空壳
            }
        }

        #endregion

        #region 派发

        /// <summary>事件触发（锁外派发 + 监听者异常隔离）</summary>
        public void EventTrigger<T>(T arg) where T : EventArgBase
        {
            Type id = typeof(T);
            PD_EventInfo<T> info;
            lock (lockObj)
            {
                dispatchCounts.TryGetValue(id, out long count);
                dispatchCounts[id] = count + 1;
                info = eventDic.TryGetValue(id, out object obj) ? obj as PD_EventInfo<T> : null;
            }

            if (info == null)
            {
                if (warnOnMissingListener) PLogger.LogWarning($"[EventCenter] 事件 {id.Name} 没有任何监听者");
                return;
            }

            info.Invoke(arg);   // 内部只在取快照时加锁，用户回调在锁外执行
        }

        #endregion

        #region 调试 / 统计

        /// <summary>指定事件类型当前的监听者数量</summary>
        public int GetListenerCount<T>() where T : EventArgBase
        {
            lock (lockObj)
            {
                return eventDic.TryGetValue(typeof(T), out object obj) && obj is IPDEventInfo info ? info.ActionCount : 0;
            }
        }

        /// <summary>指定事件类型是否有监听者</summary>
        public bool HasListener<T>() where T : EventArgBase
        {
            return GetListenerCount<T>() > 0;
        }

        /// <summary>指定事件类型的累计派发次数</summary>
        public long GetDispatchCount<T>() where T : EventArgBase
        {
            lock (lockObj)
            {
                return dispatchCounts.TryGetValue(typeof(T), out long count) ? count : 0L;
            }
        }

        /// <summary>取所有已注册事件类型的调试快照（供「事件调试窗口」使用）</summary>
        public List<EventTypeDebugInfo> GetDebugSnapshot()
        {
            List<EventTypeDebugInfo> result = new List<EventTypeDebugInfo>();
            lock (lockObj)
            {
                foreach (KeyValuePair<Type, object> kv in eventDic)
                {
                    if (!(kv.Value is IPDEventInfo info)) continue;
                    result.Add(new EventTypeDebugInfo
                    {
                        eventType = kv.Key.Name,
                        listenerCount = info.ActionCount,
                        dispatchCount = dispatchCounts.TryGetValue(kv.Key, out long count) ? count : 0L,
                        listeners = info.GetListenerInfos()
                    });
                }
            }
            result.Sort((a, b) => b.listenerCount.CompareTo(a.listenerCount));
            return result;
        }

        /// <summary>清空派发次数统计</summary>
        public void ResetDispatchCounts()
        {
            lock (lockObj) { dispatchCounts.Clear(); }
        }

        #endregion

        /// <summary>清空事件中心</summary>
        public void Clear()
        {
            lock (lockObj)
            {
                eventDic.Clear();
                dispatchCounts.Clear();
            }
        }
    }
}
