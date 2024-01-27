using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public interface IEventInfo { }
public class EventInfo<T> : IEventInfo
{
    public UnityAction<T> actions;
    public EventInfo(UnityAction<T> action)
    {
        actions += action;
    }
}
public class EventInfo : IEventInfo
{
    public UnityAction actions;
    public EventInfo(UnityAction action)
    {
        this.actions += action;
    }
}

public class EventCenter : BaseSingleton<EventCenter>
{
    private Dictionary<EEventDefine, IEventInfo> eventDic = new Dictionary<EEventDefine, IEventInfo>();
    /// <summary>
    /// 添加事件
    /// </summary>
    /// <typeparam name="T">事件的参数类型</typeparam>
    /// <param name="eventDefine">事件枚举</param>
    /// <param name="action">事件</param>
    public void AddEventListener<T>(EEventDefine eventDefine, UnityAction<T> action)
    {
        if (eventDic.ContainsKey(eventDefine))
        {
            (eventDic[eventDefine] as EventInfo<T>).actions += action;
        }
        else
        {
            eventDic.Add(eventDefine, new EventInfo<T>(action));
        }
    }
    /// <summary>
    /// 添加事件(无参数)
    /// </summary>
    /// <param name="eventDefine">事件枚举</param>
    /// <param name="action">事件</param>
    public void AddEventListener(EEventDefine eventDefine, UnityAction action)
    {
        if (eventDic.ContainsKey(eventDefine))
        {
            (eventDic[eventDefine] as EventInfo).actions += action;
        }
        else
        {
            eventDic.Add(eventDefine, new EventInfo(action));
        }
    }
    /// <summary>
    /// 移除对应的事件监听
    /// </summary>
    /// <typeparam name="T">事件参数的类型</typeparam>
    /// <param name="eventDefine">事件枚举</param>
    /// <param name="action">事件</param>
    public void RemoveEventListener<T>(EEventDefine eventDefine, UnityAction<T> action)
    {
        if (eventDic.ContainsKey(eventDefine))
        {
            (eventDic[eventDefine] as EventInfo<T>).actions -= action;
        }
    }
    /// <summary>
    /// 移除事件（无参数）
    /// </summary>
    /// <param name="eventDefine">事件枚举</param>
    /// <param name="action">事件</param>
    public void RemoveEventListener(EEventDefine eventDefine, UnityAction action)
    {
        if (eventDic.ContainsKey(eventDefine))
        {
            (eventDic[eventDefine] as EventInfo).actions -= action;
        }
    }
    /// <summary>
    /// 事件触发
    /// </summary>
    /// <typeparam name="T">事件参数的类型</typeparam>
    /// <param name="eventDefine">事件枚举</param>
    /// <param name="info">事件参数</param>
    public void EventTrigger<T>(EEventDefine eventDefine, T info)
    {
        if (eventDic.ContainsKey(eventDefine))
        {
            (eventDic[eventDefine] as EventInfo<T>).actions?.Invoke(info);
        }
    }
    /// <summary>
    /// 事件触发（不需要参数的）
    /// </summary>
    /// <param name="eventDefine">事件枚举</param>
    public void EventTrigger(EEventDefine eventDefine)
    {
        if (eventDic.ContainsKey(eventDefine))
        {
            (eventDic[eventDefine] as EventInfo).actions?.Invoke();
        }
    }
    /// <summary>
    /// 清空事件中心 
    /// </summary>
    public void Clear()
    {
        eventDic.Clear();
    }
    /// <summary>
    /// 清除指定的事件的监听
    /// </summary>
    /// <param name="eventDefine">事件枚举</param>
    /// <returns></returns>
    public bool ClearOneEvent(EEventDefine eventDefine)
    {
        if (eventDic.ContainsKey(eventDefine))
        {
            eventDic.Remove(eventDefine);
            return true;
        }
        else
        {
            return false;
        }

    }
}
