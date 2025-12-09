using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UPandaGF;

/// <summary>
/// 场景切换模块,
/// </summary>
public class SceneMgr : LazySingletonBase<SceneMgr>
{
    /// <summary>
    /// 同步加载场景
    /// </summary>
    /// <param name="sceneName">场景名称</param>
    /// <param name="loadSceneMode">加载模式：Single(加载场景并替换当前场景) Additive(加载场景并叠加在当前场景上，不卸载当前场景)</param>
    /// <param name="Callback">回调</param>
    public void LoadScene(string sceneName, UnityAction Callback = null)
    {
        LoadScene(sceneName, LoadSceneMode.Single, Callback);
    }
    public void LoadScene(string sceneName, LoadSceneMode loadSceneMode, UnityAction Callback = null)
    {
        SceneManager.LoadScene(sceneName, loadSceneMode);
        Callback?.Invoke();
    }


    /// <summary>
    /// 异步加载场景
    /// </summary>
    /// <param name="sceneName">场景名称</param>
    /// <param name="loadSceneMode">加载模式：Single(加载场景并替换当前场景) Additive(加载场景并叠加在当前场景上，不卸载当前场景)</param>
    /// <param name="Callback">回调</param>
    public void LoadSceneAsyn(string sceneName, UnityAction Callback = null)
    {
        LoadSceneAsyn(sceneName, LoadSceneMode.Single, Callback);
    }
    public void LoadSceneAsyn(string sceneName, LoadSceneMode loadSceneMode, UnityAction Callback = null)
    {
        PublicMono.Instance.StartCoroutine(ReallyLoadSceneAsyn(sceneName, loadSceneMode, Callback));
    }
    private IEnumerator ReallyLoadSceneAsyn(string sceneName, LoadSceneMode loadSceneMode, UnityAction Callback = null)
    {
        AsyncOperation ao = SceneManager.LoadSceneAsync(sceneName, loadSceneMode);
        while (!ao.isDone)
        {
            // 事件中心 向外分发 进度情况  外面想用就用
            EventCenter.Instance.EventTrigger(new SceneMgr_SceneAsynLoadProgress(ao.progress));
            yield return 0;
        }
        EventCenter.Instance.EventTrigger(new SceneMgr_SceneAsynLoadProgress(ao.progress));
        Callback?.Invoke();
    }
}

/// <summary>
/// 异步加载场景进度通知事件
/// </summary>
public class SceneMgr_SceneAsynLoadProgress : EventArgBase
{
    public override int EventID => typeof(SceneMgr_SceneAsynLoadProgress).GetHashCode();
    /// <summary>
    /// 加载进度
    /// </summary>
    public float progress { get; private set; }

    public SceneMgr_SceneAsynLoadProgress(float progress)
    {
        this.progress = progress;
    }
}

