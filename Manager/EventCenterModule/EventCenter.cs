using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public interface IEventInfo
{

}
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
    private Dictionary<string, IEventInfo> eventDic = new Dictionary<string, IEventInfo>();
    /// <summary>
    /// 添加事件
    /// </summary>
    /// <typeparam name="T">事件的参数类型</typeparam>
    /// <param name="name">事件名</param>
    /// <param name="action">事件</param>
    public void AddEventListener<T>(string name, UnityAction<T> action)
    {
        if (eventDic.ContainsKey(name))
        {
            (eventDic[name] as EventInfo<T>).actions += action;
        }
        else
        {
            eventDic.Add(name, new EventInfo<T>(action));
        }
    }
    /// <summary>
    /// 添加事件(无参数)
    /// </summary>
    /// <param name="name">事件名</param>
    /// <param name="action">事件</param>
    public void AddEventListener(string name, UnityAction action)
    {
        if (eventDic.ContainsKey(name))
        {
            (eventDic[name] as EventInfo).actions += action;
        }
        else
        {
            eventDic.Add(name, new EventInfo(action));
        }
    }
    /// <summary>
    /// 移除对应的事件监听
    /// </summary>
    /// <typeparam name="T">事件参数的类型</typeparam>
    /// <param name="name">事件名</param>
    /// <param name="action">事件</param>
    public void RemoveEventListener<T>(string name, UnityAction<T> action)
    {
        if (eventDic.ContainsKey(name))
        {
            (eventDic[name] as EventInfo<T>).actions -= action;
        }
    }
    /// <summary>
    /// 移除事件（无参数）
    /// </summary>
    /// <param name="name">事件名</param>
    /// <param name="action">事件</param>
    public void RemoveEventListener(string name, UnityAction action)
    {
        if (eventDic.ContainsKey(name))
        {
            (eventDic[name] as EventInfo).actions -= action;
        }
    }
    /// <summary>
    /// 事件触发
    /// </summary>
    /// <typeparam name="T">事件参数的类型</typeparam>
    /// <param name="name">事件名字</param>
    /// <param name="info">事件参数</param>
    public void EventTrigger<T>(string name, T info)
    {
        if (eventDic.ContainsKey(name))
        {
            (eventDic[name] as EventInfo<T>).actions?.Invoke(info);
        }
    }
    /// <summary>
    /// 事件触发（不需要参数的）
    /// </summary>
    /// <param name="name">事件名</param>
    public void EventTrigger(string name)
    {
        if (eventDic.ContainsKey(name))
        {
            (eventDic[name] as EventInfo).actions?.Invoke();
        }
    }
    /// <summary>
    /// 清空事件中心 主要用在 场景切换时
    /// </summary>
    public void Clear()
    {
        eventDic.Clear();
    }
}
