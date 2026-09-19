using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
#if UNITY_EDITOR
using UnityEditor;
#endif
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
    private void Awake()
    {
        EventCenter.Instance.AddEventListener<GFLoadedEvent>(OnGFLoadedEvent);//框架加载结束事件
        EventCenter.Instance.AddEventListener<GFLoadedFailedEvent>(OnGFLoadedFailed);//框架初始化失败事件
        EventCenter.Instance.AddEventListener<SceneMgr_SceneAsynLoadProgress>(SceneAsynLoadProgress);//场景加载进度
    }

    private void OnDestroy()
    {
        EventCenter.Instance.RemoveEventListener<GFLoadedEvent>(OnGFLoadedEvent);
        EventCenter.Instance.RemoveEventListener<GFLoadedFailedEvent>(OnGFLoadedFailed);
        EventCenter.Instance.RemoveEventListener<SceneMgr_SceneAsynLoadProgress>(SceneAsynLoadProgress);
    }

    /// <summary>
    /// 框架初始化失败：给出明确提示。
    /// 失败时 GFLoadedEvent 不会再来，若不处理会表现为"卡在加载界面 / 黑屏且没有任何反馈"。
    /// </summary>
    private void OnGFLoadedFailed(GFLoadedFailedEvent arg0)
    {
        PLogger.LogError($"框架初始化失败，后续启动流程已中止：{arg0?.message}");
        if (simpleLoadUI != null)
            simpleLoadUI.SetMessage(1f, "框架初始化失败，请查看日志");
    }

    private void SceneAsynLoadProgress(SceneMgr_SceneAsynLoadProgress arg0)
    {
        if (arg0 == null || simpleLoadUI == null)
            return;
        PLogger.Log($"Scene load :{arg0.progress * 100}%");
        simpleLoadUI.SetMessage(arg0.progress, "场景加载中...");
    }

    private async void OnGFLoadedEvent(GFLoadedEvent arg0)
    {
        try
        {
            PLogger.Log_white("框架加载结束，进入游戏逻辑");
            uiManager = UIManager.Instance;
            if (uiManager == null)
            {
                PLogger.LogError("UIManager 未初始化，无法启动加载流程");
                return;
            }

            simpleLoadUI = await uiManager.ShowPanelAsync<SimpleLoadUI>();
            if (simpleLoadUI == null)
            {
                PLogger.LogError("加载界面 SimpleLoadUI 打开失败");
                return;
            }

            simpleLoadUI.SetMessage(0.4f, "程序加载中...");
            simpleLoadUI.SetMessage(1f, "AOT程序加载完成");

            UPGameRoot gr = UPGameRoot.Instance;
            if (gr == null)
            {
                PLogger.LogError("UPGameRoot 未初始化");
                return;
            }

            sourcesLoad = gr.GetAssetsLoader();
            if (sourcesLoad == null)
            {
                PLogger.LogError("资源加载器未初始化");
                return;
            }

            if (loadHotUpdateScripts)
            {
                if (HybridCLRScriptsList == null || HybridCLRScriptsList.Length == 0)
                {
                    PLogger.LogError("启用了热更加载，但 HybridCLRScriptsList 为空");
                    simpleLoadUI.SetMessage(1f, "程序热更失败！！！");
                }
                else
                {
                    int count = 0;
                    for (int i = 0; i < HybridCLRScriptsList.Length; i++)
                    {
                        simpleLoadUI.SetMessage((float)(i + 1) / HybridCLRScriptsList.Length, "热更新程序集加载中...");
                        Assembly assembly = null;
                        try
                        {
                            assembly = await sourcesLoad.LoadAssemblyAsync(HybridCLRScriptsList[i]);
                        }
                        catch (Exception e)
                        {
                            PLogger.LogError($"加载程序集 {HybridCLRScriptsList[i]} 失败：{e}");
                        }
                        if (assembly != null)
                        {
                            count++;
                        }
                    }
                    if (count == HybridCLRScriptsList.Length)
                        simpleLoadUI.SetMessage(1f, "程序热更完成");
                    else
                        simpleLoadUI.SetMessage(1f, "程序热更失败！！！");
                    simpleLoadUI.SetMessage(0.5f, "泛型注册...");
                    onAssemblyLoaded?.Invoke();
                    simpleLoadUI.SetMessage(1f, "泛型注册完成");
                }
            }

            if (string.IsNullOrEmpty(firstScene))
            {
                PLogger.LogError("启动场景 firstScene 未配置");
                simpleLoadUI.SetMessage(1f, "启动失败：未配置启动场景");
                return;
            }

            simpleLoadUI.SetMessage(0f, "开始获取场景资源");
            EventCenter.Instance.AddEventListener<ABLoadProgressEvent>(AssetLoadProgressEvent);
            //这是场景作为AssetBundle加载的方式
            sourcesLoad.LoadSceneAsync(firstScene, () =>
            {
                EventCenter.Instance.RemoveEventListener<ABLoadProgressEvent>(AssetLoadProgressEvent);
            },
            () =>
            {
                PLogger.Log("<color=blue>进入场景</color>");
                simpleLoadUI.SetMessage(1f, "场景加载完成");
                uiManager.ClosePanel<SimpleLoadUI>();//SimpleLoadUI.CloseUI;
            });
        }
        catch (Exception e)
        {
            PLogger.LogError($"游戏启动流程异常：{e}");
            if (simpleLoadUI != null)
                simpleLoadUI.SetMessage(0f, $"启动失败：{e.Message}");
        }
    }

    private void AssetLoadProgressEvent(ABLoadProgressEvent arg0)
    {
        if (arg0 == null || simpleLoadUI == null)
            return;
        string messageInfo = arg0.loadPath == ABLoadPath.RemotePath ? "下载" : "加载";
        simpleLoadUI.SetMessage(arg0.progress, $"【{arg0.abName}】 正在{messageInfo}");
    }
}
