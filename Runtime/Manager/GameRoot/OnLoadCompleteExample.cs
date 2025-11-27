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
