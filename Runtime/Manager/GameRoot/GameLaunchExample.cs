using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using UPandaGF;

/// <summary>
/// 游戏启动案例
/// </summary>
public class GameLaunchExample : MonoBehaviour
{
    [Header("加载热更程序集")]
    public bool loadHotUpdateScripts = false;//HybridCLR
    [Header("热更程序集资源列表")]
    public string[] HybridCLRScriptsList;
    [Header("加载场景")]
    public string firstScene = "Assets/Scenes/InitScene.unity";
    IAssetsLoader sourcesLoad;
    private UIManager uiManager;
    private SimpleLoadUI simpleLoadUI;
    public UnityAction onAssemblyLoaded;
    private async void Awake()
    {
        EventCenter.Instance.AddEventListener<GFLoadedEvent>(OnGFLoadedEvent);//框架加载结束事件
        EventCenter.Instance.AddEventListener<SceneMgr_SceneAsynLoadProgress>(SceneAsynLoadProgress);//场景加载进度
        uiManager = UIManager.Instance;
        simpleLoadUI = await uiManager.ShowPanelAsync<SimpleLoadUI>("UI/LoadUI");
        simpleLoadUI.SetMessage(0.4f, "程序加载中...");
    }

    private void OnDestroy()
    {
        EventCenter.Instance.RemoveEventListener<GFLoadedEvent>(OnGFLoadedEvent);
        EventCenter.Instance.RemoveEventListener<SceneMgr_SceneAsynLoadProgress>(SceneAsynLoadProgress);
    }

    private void SceneAsynLoadProgress(SceneMgr_SceneAsynLoadProgress arg0)
    {
        PLogger.Log($"Scene load :{arg0.progress * 100}%");
        simpleLoadUI.SetMessage(arg0.progress, "场景资源加载中...");
    }

    private async void OnGFLoadedEvent(GFLoadedEvent arg0)
    {
        PLogger.Log_white("框架加载结束，进入游戏逻辑");
        simpleLoadUI.SetMessage(1f, "程序加载完成");
        await Task.Delay(1000);
        UPGameRoot gr = UPGameRoot.Instance;

        sourcesLoad = gr.GetAssetsLoader();

        if (loadHotUpdateScripts)
        {
            for (int i = 0; i < HybridCLRScriptsList.Length; i++)
            {
                simpleLoadUI.SetMessage(i / HybridCLRScriptsList.Length, "热更程序加载中...");
                await sourcesLoad.LoadAssemblyAsync(HybridCLRScriptsList[i]);
            }

            simpleLoadUI.SetMessage(1, "热更程序完成");
            await Task.Delay(1000);
            simpleLoadUI.SetMessage(1, "创建泛型注册");
            onAssemblyLoaded?.Invoke();
            await Task.Delay(1000);
        }

        if (gr.method == AssetLoaddingMethod.Editor)
        {
            string sceneName = firstScene.Substring(firstScene.LastIndexOf('/') + 1);
            sceneName = sceneName.Split('.')[0];
            //这是直接加载场景的方式
            SceneMgr.Instance.LoadSceneAsyn(sceneName, async () =>
            {
                PLogger.Log("<color=blue>进入场景</color>");
                simpleLoadUI.SetMessage(1, "场景资源加载完成");
                await Task.Delay(1000);
                uiManager.ClosePanel("UI/LoadUI");
            });
        }
        else if (gr.method == AssetLoaddingMethod.Assetbundles)
        {
            //这是场景作为AssetBundle加载的方式
            sourcesLoad.LoadSceneAsync(firstScene, async () =>
            {
                PLogger.Log("<color=blue>进入场景</color>");
                simpleLoadUI.SetMessage(1, "场景资源加载完成");
                await Task.Delay(1000);
                uiManager.ClosePanel("UI/LoadUI");
            });
        }
    }
}
