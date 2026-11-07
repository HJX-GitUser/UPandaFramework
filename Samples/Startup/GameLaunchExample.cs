using System;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UPandaGF;

/// <summary>
/// 游戏启动案例。
/// <para>职责：等框架初始化完成 → 打开加载界面 → （可选）加载热更程序集 → 加载首个场景 → 关闭加载界面。</para>
/// <para>两种进场景方式：<see cref="sceneIsAB"/> = true 走 AssetBundle（SceneService 会先准备场景所在包）；
/// false 表示场景直接打进包（Build Settings），走 SceneManager。</para>
/// </summary>
public class GameLaunchExample : MonoBehaviour
{
    [Header("加载热更程序集")]
    public bool loadHotUpdateScripts = false;//HybridCLR
    [Header("热更程序集资源列表")]
    public string[] HybridCLRScriptsList;
    [Header("场景是否是AB包加载")]
    public bool sceneIsAB = true;
    [Header("加载场景")]
    public string firstScene = "Assets/Scenes/InitScene.unity";

    // ── 进度分段：避免“下载 100% → 场景又从头开始”的倒退（配合 SetProgress 只增不减）
    //   0.0 ~ 0.2 初始化/AOT
    //   0.2 ~ 0.5 热更程序集
    //   0.5 ~ 0.8 场景资源准备（AB 下载/加载）
    //   0.8 ~ 1.0 场景加载/激活
    private const float HotUpdateEnd = 0.5f;
    private const float SceneAssetEnd = 0.8f;

    IAssetsLoader sourcesLoad;
    private UIManager uiManager;
    private SimpleLoadUI simpleLoadUI;
    private ISceneService scenes;
    /// <summary>全部热更程序集加载成功后的回调（HybridCLR 的泛型注册挂这里）。</summary>
    public UnityAction onAssemblyLoaded;

    /// <summary>启动流程只跑一次：事件万一重复广播，不会重复开界面/重复进场景。</summary>
    private bool launching;
    /// <summary>当前进度条值（只增不减，见 SetProgress）。</summary>
    private float progressValue;
    private void Awake()
    {
        EventCenter.Instance.AddEventListener<GFLoadedEvent>(OnGFLoadedEvent);//框架加载结束事件
        EventCenter.Instance.AddEventListener<GFLoadedFailedEvent>(OnGFLoadedFailed);//框架初始化失败事件
    }

    private void OnDestroy()
    {
        EventCenter.Instance.RemoveEventListener<GFLoadedEvent>(OnGFLoadedEvent);
        EventCenter.Instance.RemoveEventListener<GFLoadedFailedEvent>(OnGFLoadedFailed);
        // 场景加载中途被销毁时，AB 进度监听可能还挂着（正常路径已在 finally 里退订）
        EventCenter.Instance.RemoveEventListener<ABLoadProgressEvent>(AssetLoadProgressEvent);
    }

    /// <summary>
    /// 框架初始化失败：给出明确提示。
    /// <para>失败时 GFLoadedEvent 不会再来，若不处理会表现为“卡在加载界面 / 黑屏且没有任何反馈”。</para>
    /// </summary>
    private void OnGFLoadedFailed(GFLoadedFailedEvent arg0)
    {
        PLogger.LogError($"框架初始化失败，后续启动流程已中止：{arg0?.message}");
        if (simpleLoadUI != null)
            simpleLoadUI.SetMessage(1f, "框架初始化失败，请查看日志");
    }

    /// <summary>
    /// 框架初始化完成：执行完整启动流程。
    /// <para>async void 只用于事件回调；整体 try/catch，任何失败都会反映到加载界面，不会静默卡住。</para>
    /// <para>顺序：打开加载界面 → 程序集（AOT 占位 → 热更）→ 校验场景 → 进场景 → 收尾。</para>
    /// </summary>
    private async void OnGFLoadedEvent(GFLoadedEvent arg0)
    {
        // 启动是一次性的：事件重复广播时不重复开界面 / 重复进场景
        if (launching) return;
        launching = true;

        try
        {
            PLogger.Log_white("框架加载结束，进入游戏逻辑");

            uiManager = UIManager.Instance;
            if (uiManager == null) { Fail("UIManager 未初始化，无法启动加载流程"); return; }

            simpleLoadUI = await uiManager.ShowPanelAsync<SimpleLoadUI>();
            if (simpleLoadUI == null) { Fail("加载界面 SimpleLoadUI 打开失败"); return; }

            UPGameRoot gr = UPGameRoot.Instance;
            if (gr == null) { Fail("UPGameRoot 未初始化"); return; }

            sourcesLoad = gr.GetAssetsLoader();
            if (sourcesLoad == null) { Fail("资源加载器未初始化"); return; }
            scenes = gr.Services.Scenes;

            // ── 1. 程序集：AOT 部分是示例占位（实际项目在这里加载 AOT 补充元数据/程序集）
            SetProgress(0.2f, "AOT 程序加载完成");

            // ── 2. 热更程序集：失败必须中止
            //       带着缺失类型进场景，只会在后面报更难查的 NullReference/MissingMethod
            if (loadHotUpdateScripts && !await LoadHotUpdateAssembliesAsync())
                return;

            // ── 3. 场景
            if (string.IsNullOrEmpty(firstScene)) { Fail("启动失败：未配置启动场景"); return; }

            if (sceneIsAB) await LoadSceneFromAssetBundleAsync();
            else await LoadBuiltinSceneAsync();

            // ── 4. 收尾
            SetProgress(1f, "场景加载完成");
            uiManager.ClosePanel<SimpleLoadUI>();
            PLogger.Log("<color=blue>进入场景</color>");
        }
        catch (Exception e)
        {
            PLogger.LogError($"游戏启动流程异常：{e}");
            if (simpleLoadUI != null)
                simpleLoadUI.SetMessage(1f, $"启动失败：{e.Message}");
        }
    }

    /// <summary>
    /// 加载热更程序集（HybridCLR 把 dll 以 TextAsset 打进 AB）。
    /// <para>全部成功才返回 true；任何一个失败（含列表为空）都会 Fail 并中止启动。</para>
    /// <para>全部成功后才调用 <see cref="onAssemblyLoaded"/> —— HybridCLR 的泛型注册就挂在这里。</para>
    /// </summary>
    private async Task<bool> LoadHotUpdateAssembliesAsync()
    {
        if (HybridCLRScriptsList == null || HybridCLRScriptsList.Length == 0)
        {
            Fail("程序热更失败：HybridCLRScriptsList 为空");
            return false;
        }

        int loaded = 0;
        for (int i = 0; i < HybridCLRScriptsList.Length; i++)
        {
            string path = HybridCLRScriptsList[i];
            // 0.2 ~ 0.5 均分给各个程序集
            SetProgress(0.2f + 0.3f * (i / (float)HybridCLRScriptsList.Length),
                        $"热更新程序集加载中...({i + 1}/{HybridCLRScriptsList.Length})");

            Assembly assembly = null;
            try { assembly = await sourcesLoad.LoadAssemblyAsync(path); }
            catch (Exception e) { PLogger.LogError($"加载程序集 {path} 失败：{e}"); }

            if (assembly == null) PLogger.LogError($"热更程序集加载失败：{path}");
            else loaded++;
        }

        if (loaded != HybridCLRScriptsList.Length)
        {
            Fail($"程序热更失败：{loaded}/{HybridCLRScriptsList.Length} 个程序集加载成功");
            return false;
        }

        SetProgress(0.45f, "泛型注册...");
        onAssemblyLoaded?.Invoke();
        SetProgress(0.5f, "程序热更完成");
        return true;
    }

    /// <summary>
    /// sceneIsAB = true：交给 ISceneService 加载（串行化 + 先准备场景所在 AB + 进度 + 场景资源作用域）。
    /// <para>用 await 而不是旧的 fire-and-forget 回调：失败会抛到这里，能反馈给玩家，而不是永远卡在进度条上。</para>
    /// </summary>
    private async Task LoadSceneFromAssetBundleAsync()
    {
        // 事件是全局的：只在本次场景加载期间监听，避免把其它资源的加载/下载也算进进度
        EventCenter.Instance.AddEventListener<ABLoadProgressEvent>(AssetLoadProgressEvent);
        try
        {
            await scenes.LoadAsync(firstScene, LoadSceneMode.Single, progress: SceneLoadProgress);
        }
        finally
        {
            EventCenter.Instance.RemoveEventListener<ABLoadProgressEvent>(AssetLoadProgressEvent);
        }
    }

    /// <summary>
    /// sceneIsAB = false：场景随包打进 Build Settings，直接用 SceneManager 加载。
    /// <para>不走 SceneService —— 它会在加载前按资源清单准备 AB，而随包场景不在清单里。</para>
    /// </summary>
    private async Task LoadBuiltinSceneAsync()
    {
        if (!Application.CanStreamedLevelBeLoaded(firstScene))
            throw new InvalidOperationException($"场景不在 Build Settings 中：{firstScene}（sceneIsAB=false 时必须是随包场景）");

        AsyncOperation operation = SceneManager.LoadSceneAsync(firstScene, LoadSceneMode.Single);
        if (operation == null)
            throw new InvalidOperationException($"Unity 未能创建场景加载操作：{firstScene}");

        // progress 到 0.9 就停（剩下 0.1 是激活阶段），归一化后映射到 0.5~1
        while (!operation.isDone)
        {
            SetProgress(HotUpdateEnd + (1f - HotUpdateEnd) * Mathf.Clamp01(operation.progress / 0.9f), "场景加载中...");
            await Task.Yield();
        }
    }

    /// <summary>
    /// 场景加载进度：Preparing（资源准备）由 AB 逐包进度事件驱动；Loading 才是 Unity 的场景激活进度。
    /// <para>两者映射到不同区间，配合 SetProgress 的“只增不减”避免进度条来回跳。</para>
    /// </summary>
    private void SceneLoadProgress(SceneTransitionProgress progress)
    {
        switch (progress.Phase)
        {
            case SceneTransitionPhase.Loading:
                SetProgress(SceneAssetEnd + (1f - SceneAssetEnd) * progress.Progress, "场景加载中...");
                break;
            case SceneTransitionPhase.Completed:
                SetProgress(1f, "场景加载完成");
                break;
            case SceneTransitionPhase.Failed:
                SetProgress(progressValue, "场景加载失败");
                break;
            case SceneTransitionPhase.Canceled:
                SetProgress(progressValue, "场景加载已取消");
                break;
            default:
                SetProgress(progressValue, "场景资源准备中...");
                break;
        }
    }

    /// <summary>AB 准备阶段的逐包进度（仅在本次场景加载期间订阅，见 LoadSceneFromAssetBundleAsync）。</summary>
    private void AssetLoadProgressEvent(ABLoadProgressEvent arg0)
    {
        if (arg0 == null) return;
        string action = arg0.loadPath == ABLoadPath.RemotePath ? "下载" : "加载";
        SetProgress(HotUpdateEnd + (SceneAssetEnd - HotUpdateEnd) * arg0.progress, $"【{arg0.abName}】正在{action}");
    }

    /// <summary>
    /// 统一更新进度条：只增不减（异步阶段/多个来源的进度并存时不会倒退），并顺带更新文案。
    /// </summary>
    private void SetProgress(float value, string message)
    {
        progressValue = Mathf.Max(progressValue, Mathf.Clamp01(value));
        if (simpleLoadUI != null)
            simpleLoadUI.SetMessage(progressValue, message);
    }

    /// <summary>统一失败出口：记日志 + 在加载界面上给出可读原因（界面保留，方便看到错误）。</summary>
    private void Fail(string message)
    {
        PLogger.LogError(message);
        if (simpleLoadUI != null)
            simpleLoadUI.SetMessage(1f, message);
    }
}
