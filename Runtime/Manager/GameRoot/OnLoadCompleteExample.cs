using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UPandaGF;

public class OnLoadCompleteExample : MonoBehaviour
{
    public string firstScene = "Assets/Scenes/InitScene.unity";
    ISourcesLoad sourcesLoad;
    private void Awake()
    {
        EventCenter.Instance.AddEventListener<GFLoadedEvent>(OnGFLoadedEvent);
        EventCenter.Instance.AddEventListener<SceneMgr_SceneAsynLoadProgress>(SceneAsynLoadProgress);
    }

    private void SceneAsynLoadProgress(SceneMgr_SceneAsynLoadProgress arg0)
    {
        Debug.Log($"Scene load :{arg0.progress * 100}%");
    }

    private void OnGFLoadedEvent(GFLoadedEvent arg0)
    {
        PLoger.Log_white("框架加载结束，进入游戏逻辑");
        sourcesLoad = UPGameRoot.Instance.GetSourcesLoadComponent();
        sourcesLoad.LoadSceneAsync(firstScene, () =>
        {
            PLoger.LogError("<color=blue>进入场景</color>");
        });
    }
}
