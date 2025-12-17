using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UPandaGF;

/// <summary>
/// 游戏启动案例
/// </summary>
public class GameLaunchExample : MonoBehaviour
{
    public string firstScene = "Assets/Scenes/InitScene.unity";
    IAssetsLoader sourcesLoad;
    private void Awake()
    {
        EventCenter.Instance.AddEventListener<GFLoadedEvent>(OnGFLoadedEvent);//框架加载结束事件
        EventCenter.Instance.AddEventListener<SceneMgr_SceneAsynLoadProgress>(SceneAsynLoadProgress);//场景加载进度
    }

    private void OnDestroy()
    {
        EventCenter.Instance.RemoveEventListener<GFLoadedEvent>(OnGFLoadedEvent);
        EventCenter.Instance.RemoveEventListener<SceneMgr_SceneAsynLoadProgress>(SceneAsynLoadProgress);
    }

    private void SceneAsynLoadProgress(SceneMgr_SceneAsynLoadProgress arg0)
    {
        PLoger.Log($"Scene load :{arg0.progress * 100}%");
    }

    private void OnGFLoadedEvent(GFLoadedEvent arg0)
    {
        PLoger.Log_white("框架加载结束，进入游戏逻辑");
        sourcesLoad = UPGameRoot.Instance.GetAssetsLoader();
        sourcesLoad.LoadSceneAsync(firstScene, () =>
        {
            PLoger.Log("<color=blue>进入场景</color>");
        });
        //SceneMgr.Instance.LoadSceneAsyn("InitScene", () =>
        //{
        //    PLoger.Log("<color=blue>进入场景</color>");
        //});
    }
}
