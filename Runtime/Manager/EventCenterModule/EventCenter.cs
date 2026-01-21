using System.Collections.Generic;
using UnityEngine.Events;

namespace UPandaGF
{
    public abstract class EventArgBase { }
    /// <summary>
    /// 事件参数
    /// </summary>
    internal class PD_EventInfo<T> where T : EventArgBase
    {
        private UnityAction<T> actions;
        private HashSet<int> actionIDs; // 使用 HashSet 更高效地避免重复
        private readonly object lockObj = new object(); // 用于同步的锁对象
        public PD_EventInfo(UnityAction<T> action)
        {
            actionIDs = new HashSet<int>();
            AddAction(action);
        }

        public void AddAction(UnityAction<T> action)
        {
            int id = action.GetHashCode();
            lock (lockObj) // 保证线程安全
            {
                if (!actionIDs.Contains(id))
                {
                    actions += action;
                    actionIDs.Add(id);
                }
                else
                {
                    PLogger.LogWarning($"id：{id} 的方法试图重复加入监听!");
                }
            }

        }

        public void RemoveAction(UnityAction<T> action)
        {
            int id = action.GetHashCode();
            lock (lockObj) // 保证线程安全
            {
                if (actionIDs.Contains(id))
                {
                    actions -= action;
                    actionIDs.Remove(id);
                }
            }
        }

        public int GetActionCount => actionIDs.Count;

        public void Invoke(T arg)
        {
            lock (lockObj) // 保证线程安全
            {
                actions?.Invoke(arg);
            }
        }
    }

    /// <summary>
    /// 事件中心
    /// </summary>
    public class EventCenter : LazySingletonBase<EventCenter>
    {
        private Dictionary<int, object> eventDic = new Dictionary<int, object>();
        private readonly object lockObj = new object(); // 用于同步的锁对象

        /// <summary>
        /// 添加事件
        /// </summary>
        /// <typeparam name="T">事件参数的类型</typeparam>
        /// <param name="action">事件回调方法</param>
        public void AddEventListener<T>(UnityAction<T> action) where T : EventArgBase
        {
            int id = typeof(T).GetHashCode();
            lock (lockObj) // 保证线程安全
            {
                if (eventDic.ContainsKey(id))
                {
                    (eventDic[id] as PD_EventInfo<T>)?.AddAction(action);
                }
                else
                {
                    eventDic.Add(id, new PD_EventInfo<T>(action));
                }
            }
        }

        /// <summary>
        /// 移除对应的事件监听
        /// </summary>
        /// <typeparam name="T">事件参数的类型</typeparam>
        /// <param name="action">事件回调方法</param>
        public void RemoveEventListener<T>(UnityAction<T> action) where T : EventArgBase
        {
            int id = typeof(T).GetHashCode();
            if (eventDic.ContainsKey(id))
            {
                PD_EventInfo<T> temp = (eventDic[id] as PD_EventInfo<T>);
                temp?.RemoveAction(action);
                // 如果没有任何监听者，移除事件
                //if (temp.GetActionCount == 0)
                //{
                //    eventDic.Remove(id);
                //}
            }
        }

        /// <summary>
        /// 事件触发
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="arg"></param>
        public void EventTrigger<T>(T arg) where T : EventArgBase
        {
            lock (lockObj) // 保证线程安全
            {
                int id = typeof(T).GetHashCode();
                if (eventDic.ContainsKey(id))
                {
                    (eventDic[id] as PD_EventInfo<T>)?.Invoke(arg);
                }
            }
        }

        /// <summary>
        /// 清空事件中心 
        /// </summary>
        public void Clear()
        {
            lock (lockObj) // 保证线程安全
            {
                eventDic.Clear();
            }
        }
    }
}

