using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UPandaGF;

/// <summary>
/// 游戏启动案例
/// </summary>
public class GameLaunchExample : MonoBehaviour
{
    [Header("加载热更程序集")]
    public bool loadHotUpdateScripts = false;//HybridCLR
    [Header("热更程序集资源路径")]
    public string HybridCLRScriptsPath = "Assets/HotUpdate/MainScripts.dll.bytes";
    [Header("加载场景")]
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
        PLogger.Log($"Scene load :{arg0.progress * 100}%");
    }

    private async void OnGFLoadedEvent(GFLoadedEvent arg0)
    {

        PLogger.Log_white("框架加载结束，进入游戏逻辑");
        UPGameRoot gr = UPGameRoot.Instance;

        sourcesLoad = gr.GetAssetsLoader();

        if (loadHotUpdateScripts)
        {
            await sourcesLoad.LoadAssemblyAsync(HybridCLRScriptsPath);
        }

        if (gr.method == AssetLoaddingMethod.Editor)
        {
            string sceneName = firstScene.Substring(firstScene.LastIndexOf('/') + 1);
            sceneName = sceneName.Split('.')[0];
            //这是直接加载场景的方式
            SceneMgr.Instance.LoadSceneAsyn(sceneName, () =>
            {
                PLogger.Log("<color=blue>进入场景</color>");
            });
        }
        else if (gr.method == AssetLoaddingMethod.Assetbundles)
        {
            //这是场景作为AssetBundle加载的方式
            sourcesLoad.LoadSceneAsync(firstScene, () =>
            {
                PLogger.Log("<color=blue>进入场景</color>");
            });
        }
    }
}
