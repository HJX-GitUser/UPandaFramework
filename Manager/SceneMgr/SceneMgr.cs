using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

/// <summary>
/// 场景切换模块,
/// </summary>
public class SceneMgr : BaseSingleton<SceneMgr>
{
    /// <summary>
    /// 同步
    /// </summary>
    /// <param name="sceneName"></param>
    /// <param name="Callback"></param>
    public void LoadScene(string sceneName, UnityAction Callback = null)
    {
        SceneManager.LoadScene(sceneName);
        Callback?.Invoke();
    }

    /// <summary>
    /// 异步
    /// </summary>
    /// <param name="sceneName"></param>
    /// <param name="Callback"></param>
    public void LoadSceneAsyn(string sceneName, UnityAction Callback = null)
    {
        MonoMgr.Instance.StartCoroutine(ReallyLoadSceneAsyn(sceneName, Callback));
    }
    private IEnumerator ReallyLoadSceneAsyn(string sceneName, UnityAction Callback = null)
    {
        AsyncOperation ao = SceneManager.LoadSceneAsync(sceneName);
        while(!ao.isDone)
        {
            // 事件中心 向外分发 进度情况  外面想用就用
            EventCenter.Instance.EventTrigger("SceneAsynLoadProgress", ao.progress);
            yield return ao.progress;
        }
        Callback?.Invoke();
    }
}
