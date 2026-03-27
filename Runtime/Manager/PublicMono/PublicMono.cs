using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Mono的管理
///1.利用帧更新或定时更新处理逻辑
///2.利用协同程序处理逻辑
///3.可以统一执行管理帧更新或定时更新相关逻辑
/// </summary>
public class PublicMono : EagerMonoSingletonBase<PublicMono>
{
    private event UnityAction updateEvent;
    private event UnityAction fixedUpdatteEvent;
    private event UnityAction lateUpdateEvent;
    

    // Update is called once per frame
    void Update()
    {
        updateEvent?.Invoke();
    }
    private void FixedUpdate()
    {
        fixedUpdatteEvent?.Invoke();
    }

    private void LateUpdate()
    {
        lateUpdateEvent?.Invoke();
    }

    /// <summary>
    /// 添加帧更新事件
    /// </summary>
    /// <param name="fun"></param>
    public void AddUpdateListener(UnityAction fun)
    {
        updateEvent += fun;
    }
    /// <summary>
    /// 移除帧更新事件
    /// </summary>
    /// <param name="fun"></param>
    public void RemoveUpdateListener(UnityAction fun)
    {
        updateEvent -= fun;
    }

    public void AddFixedUpdatteEventListener(UnityAction fun)
    {
        fixedUpdatteEvent += fun;
    }
    public void RemoveFixedUpdatteListener(UnityAction fun)
    {
        fixedUpdatteEvent -= fun;
    }

    public void AddLateUpdateEventListener(UnityAction fun)
    {
        lateUpdateEvent += fun;
    }
    public void RemoveLateUpdateEventListener(UnityAction fun)
    {
        lateUpdateEvent -= fun;
    }

    /// <summary>
    /// 启动协程，并返回一个可等待的 Task
    /// </summary>
    /// <param name="coroutine"></param>
    /// <returns></returns>
    public Task RunCoroutine(IEnumerator coroutine)
    {
        var tcs = new TaskCompletionSource<bool>();
        StartCoroutine(RunCoroutineInternal(coroutine, tcs));
        return tcs.Task;
    }
    private IEnumerator RunCoroutineInternal(IEnumerator coroutine, TaskCompletionSource<bool> tcs)
    {
        yield return coroutine;
        tcs.SetResult(true);
    }

}
