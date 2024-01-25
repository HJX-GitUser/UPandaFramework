using System.Collections;
using System.Collections.Generic;
using UnityEngine;
/// <summary>
/// 使用方式
/// </summary>
public class HowUseMonoMgr
{
    /// <summary>
    /// 调用该方法就可以让MyUpdate()方法在每帧调用
    /// </summary>
    public void EnableMyUpdate()
    {
        MonoMgr.Instance.AddUpdateListener(MyUpdate);
    }
    public void DisableMyUpdate()
    {
        MonoMgr.Instance.RemoveUpdateListener(MyUpdate);
    }

    private void MyUpdate()
    {
        Debug.Log("更新");
    }
    /// <summary>
    /// 开启协程的方式
    /// </summary>
    public void StartMyIEnumerator()
    {
        MonoMgr.Instance.StartCoroutine(SelfIEnumerator());
    }

    private IEnumerator SelfIEnumerator()
    {
        Debug.Log("开启协程");
        yield return new WaitForSeconds(3f);
        Debug.Log("协程结束");
    }
}
