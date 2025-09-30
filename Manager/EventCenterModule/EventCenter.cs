using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Events;
using UPandaGF;

/// <summary>
/// 事件参数
/// </summary>
public class PD_EventInfo
{
    private UnityAction<EventArgBase> actions;
    private List<int> actionID;
    public PD_EventInfo(UnityAction<EventArgBase> action)
    {
        actionID = new List<int>();
        actions += action;
        actionID.Add(actions.GetHashCode());
    }

    public void AddAction(UnityAction<EventArgBase> action)
    {
        int id = action.GetHashCode();
        if (!actionID.Contains(id))
        {
            actions += action;
            actionID.Add(id);
        }
        else
        {
            PLoger.LogWarning($"{id}方法试图重复加入监听!");
        }

    }

    public void RemoveAction(UnityAction<EventArgBase> action)
    {
        if (actionID.Contains(action.GetHashCode()))
        {
            actions -= action;
            actionID.Remove(action.GetHashCode());
        }
    }

    public void Invoke(EventArgBase arg)
    {
        actions?.Invoke(arg);
    }
}

/// <summary>
/// 事件中心
/// </summary>
public class EventCenter : BaseSingleton<EventCenter>
{
    private Dictionary<int, PD_EventInfo> eventDic = new Dictionary<int, PD_EventInfo>();

    /// <summary>
    /// 添加事件
    /// </summary>
    /// <typeparam name="T">事件的参数类型</typeparam>
    /// <param name="eventDefine">事件枚举</param>
    /// <param name="action">事件</param>
    public void AddEventListener<T>(UnityAction<EventArgBase> action) where T : EventArgBase
    {
        int id = typeof(T).GetHashCode();
        if (eventDic.ContainsKey(id))
        {
            eventDic[id].AddAction(action);
        }
        else
        {
            eventDic.Add(id, new PD_EventInfo(action));
        }
    }

    /// <summary>
    /// 移除对应的事件监听
    /// </summary>
    /// <typeparam name="T">事件参数的类型</typeparam>
    /// <param name="eventDefine">事件枚举</param>
    /// <param name="action">事件</param>
    public void RemoveEventListener<T>(UnityAction<EventArgBase> action) where T : EventArgBase
    {
        int id = typeof(T).GetHashCode();
        if (eventDic.ContainsKey(id))
        {
            eventDic[id].RemoveAction(action);
        }
    }

    /// <summary>
    /// 事件触发
    /// </summary>
    /// <typeparam name="T">事件参数的类型</typeparam>
    /// <param name="eventDefine">事件枚举</param>
    /// <param name="info">事件参数</param>
    public void EventTrigger(EventArgBase arg)
    {
        if (eventDic.ContainsKey(arg.EventID))
        {
            eventDic[arg.EventID].Invoke(arg);
        }
    }

    /// <summary>
    /// 清空事件中心 
    /// </summary>
    public void Clear()
    {
        eventDic.Clear();
    }
}
