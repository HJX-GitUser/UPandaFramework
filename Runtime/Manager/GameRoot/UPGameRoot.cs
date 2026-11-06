using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UPandaGF
{
    /// <summary>
    /// 资源加载方式（整个应用级别：编辑器直读资源 / 走 AssetBundle）
    /// <para>注意与 <see cref="AssetLoadMethod"/>（单次加载级别：Resources / AssetBundle）区分开。</para>
    /// </summary>
    public enum AssetLoaddingMethod
    {
        Editor,//编辑器环境下加载资源
        Assetbundles,//使用AssetBundle加载资源
    }

    /// <summary>
    /// 框架初始化完成事件（<see cref="UPGameRoot.OnInited"/> 广播）。
    /// 订阅方应在自己的 Awake 里订阅，收到后再去取资源加载器 / 加载场景。
    /// </summary>
    public class GFLoadedEvent : EventArgBase
    {

    }

    /// <summary>
    /// 框架初始化失败事件（失败时 <see cref="GFLoadedEvent"/> 不会再来）。
    /// 带上失败原因，便于上层给出用户可见提示或做重试，而不是"卡在加载界面且没有日志"。
    /// </summary>
    public class GFLoadedFailedEvent : EventArgBase
    {
        /// <summary>失败原因（异常信息，含堆栈）</summary>
        public string message;

        public GFLoadedFailedEvent(string message)
        {
            this.message = message;
        }
    }

    [AddComponentMenu("UPandaGF/GameRoot")]
    // 让根节点比业务脚本更早 Awake：业务脚本通常在 Awake 里订阅 GFLoadedEvent，
    // 这里只保证"根节点先启动"，真正的完成时机由异步初始化决定（至少跨一帧）
    [DefaultExecutionOrder(-100)]
    public class UPGameRoot : EagerMonoSingletonBase<UPGameRoot>
    {
        #region Component
        private DebugerInit debugerInit;//日志系统
        private Downloader downloader;//下载器
        private AssetsLoader sourcesLoadMgr;//资源加载组件
        private BinaryDataMgrInit binaryDataMgr;//数据管理
        private UIManager _UIManager;//UI

        public Downloader Downloader => downloader;
        #endregion

        #region Config
        /// <summary>
        /// 面板 / Json 配置（唯一真源）。
        /// <para>启动时 <see cref="InitializeAsync"/> 的第一步先读 StreamingAssets/Data/GameRootConfig.json：
        /// 读到就以它为准，读不到 / 解析失败才用面板上的值；面板改完点 Inspector 的「保存到 Json」写回。</para>
        /// </summary>
        [SerializeField] private UPGameRootConfig config = new UPGameRootConfig();

        /// <summary>当前配置（外部读用；要改就改这个对象上的字段）</summary>
        private UPGameRootConfig runtimeConfig;
        private GameServices services;
        public UPGameRootConfig Config => runtimeConfig != null ? UPGameRootConfigFile.Snapshot(runtimeConfig) : config;
        public IGameServices Services => IsInited && State == StartupState.Ready ? services : throw new InvalidOperationException("请先等待框架就绪。");

        /// <summary>整体替换配置（读 Json / 工具回填用），并做空值兜底</summary>
        public void SetConfig(UPGameRootConfig newConfig)
        {
            if (runtimeConfig != null) throw new InvalidOperationException("启动配置已冻结；请重建根节点后修改。");
            config = newConfig ?? new UPGameRootConfig();
            EnsureConfigValid();
        }

        /// <summary>配置项兜底：Json 缺字段 / 手改 Json 漏项 / 旧数据升级时用</summary>
        private void EnsureConfigValid()
        {
            if (config == null) config = new UPGameRootConfig();
            if (config.hotUpdate == null) config.hotUpdate = new HotUpdateSettings();
            if (config.AssetAESConfig == null)
                config.AssetAESConfig = new AssetBundleClassificationWindowConfig { enable = false };
            if (string.IsNullOrEmpty(config.LoadAssetPath))
                config.LoadAssetPath = "AssetBundles/StandaloneWindows/";
        }

        public const string assetData = "assetData.assetref";
        public const string tempAssetData = "TempAssetData.assetref";
        #endregion

        public Reporter reporter;

        private ABSourcesRelated sourceRef = null;
        public ABSourcesRelated SourceRef { get => sourceRef; }

        #region 状态
        /// <summary>框架初始化是否已完成（等价于"GFLoadedEvent 是否已广播"）</summary>
        public bool IsInited { get; private set; }

        /// <summary>初始化是否失败（失败后 IsInited 始终为 false，并会广播 GFLoadedFailedEvent）</summary>
        public bool IsInitFailed { get; private set; }

        private bool destroyed;
        private StartupLifecycle lifecycle;
        private ContentUpdateService contentUpdate;
        public StartupState State => lifecycle?.State ?? StartupState.Created;
        public Exception InitializationError => lifecycle?.Error;
        internal StartupLifecycle Lifecycle => lifecycle;
        internal string CurrentStartupStep => lifecycle?.CurrentStep;
        internal GameServices RuntimeServices => services;
        internal AssetsLoader AssetLoader => sourcesLoadMgr;
        internal UIManager RootUIManager => _UIManager;

        /// <summary>最近一次热更的文件阶段统计（下载等待时间另计）。</summary>
        public HotUpdateStatistics LastHotUpdateStatistics => contentUpdate?.LastHotUpdateStatistics;

        /// <summary>各启动步骤耗时；启动失败时也保留已执行步骤，便于定位慢在哪一步。</summary>
        public IReadOnlyList<StartupStepMetric> StartupMetrics => lifecycle?.Metrics;

        /// <summary>
        /// 外部模块自注册入口：在根节点创建 StartupLifecycle 后、开始初始化前回调。
        /// 用法：在 [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)] 里订阅，
        /// 回调中 lifecycle.Add(new MyStartupModule())。模块失败会走统一的回滚与 GFLoadedFailedEvent。
        /// </summary>
        public static event Action<StartupLifecycle> ConfigureLifecycle;
        #endregion

        private void Reset()
        {
             SetComponent();
        }

        protected override void OnAwake()
        {
            // 重复实例不会走到这里：基类 Awake 已改为对重复实例 Destroy + return，不再执行 OnAwake。
            // （原补丁 `if (Instance != this) return;` 于 2026-09-24 移除，历史故障见基类 Awake 注释。）
            SetComponent();
            InitAsync();
        }

        private async void InitAsync()
        {
            try { await InitializeAsync(); }
            catch (OperationCanceledException) { }
            catch (Exception e) { if (!destroyed) PLogger.LogError($"UPGameRoot 初始化失败：{e}"); }
        }

        /// <summary>同一根节点只启动一次。token 只取消本调用者等待；关闭整套服务请调用 ShutdownAsync。</summary>
        public Task InitializeAsync(CancellationToken token = default)
        {
            if (destroyed) return Task.FromCanceled(new CancellationToken(true));
            if (State == StartupState.Stopping || State == StartupState.Stopped)
                return Task.FromException(new InvalidOperationException("根节点已关闭，请创建新根节点后再启动。"));
            if (lifecycle == null)
            {
                lifecycle = new StartupLifecycle();
                lifecycle.Add("Configuration", async ct => {
                    await LoadConfigAsync(); ct.ThrowIfCancellationRequested();
                    string target = Application.isEditor ? null : BundleTargetFor(Application.platform);
                    runtimeConfig = UPGameRootConfigFile.SnapshotAndValidate(config, Application.isEditor, target);
                });
                lifecycle.Add("Logging", async ct => {
                    await PublicMono.Instance.RunCoroutine(debugerInit.Init(), cancellationToken: ct);
                });
                lifecycle.Add("Data", ct => { binaryDataMgr.Init(); return Task.CompletedTask; });
                lifecycle.Add("ContentUpdate", async ct => {
                    contentUpdate = new ContentUpdateService(runtimeConfig, downloader);
                    if (runtimeConfig.method == AssetLoaddingMethod.Assetbundles)
                        sourceRef = await contentUpdate.ExecuteAsync();
                    ct.ThrowIfCancellationRequested();
                }, async () => { if (contentUpdate != null) await contentUpdate.ShutdownAsync(); sourceRef = null; });
                lifecycle.Add("Assets", async ct => {
                    sourcesLoadMgr.AssetAESConfig = runtimeConfig.AssetAESConfig;
                    await sourcesLoadMgr.Init(runtimeConfig.method, runtimeConfig.remoteURL, runtimeConfig.LoadAssetPath, sourceRef);
                    ct.ThrowIfCancellationRequested();
                    if (!sourcesLoadMgr.IsInitialized) throw new InvalidOperationException("资源系统初始化失败，框架未就绪。");
                }, () => { sourcesLoadMgr?.Shutdown(); return Task.CompletedTask; });
                lifecycle.Add("Services", ct => {
                    services = new GameServices(ResourcesLoader.Instance, sourcesLoadMgr, runtimeConfig, Path.Combine(Application.persistentDataPath, "Saves"));
#pragma warning disable CS0618 // SceneMgr 是旧业务兼容层，框架内部仍需完成适配
                    SceneMgr.Instance.Configure(services.SceneService);
#pragma warning restore CS0618
                    sourcesLoadMgr.ConfigureScenes(services.Scenes);
                    return Task.CompletedTask;
                }, async () => {
                    if (services != null) await services.ShutdownAsync();
                });
                lifecycle.Add("UI", ct => {
                    _UIManager.Configure(services.Assets);
                    _UIManager.Init();
                    _UIManager.SetResourcesLoader(sourcesLoadMgr, ResourcesLoader.Instance);
                    return Task.CompletedTask;
                }, () => { _UIManager?.Shutdown(); return Task.CompletedTask; });
                try { ConfigureLifecycle?.Invoke(lifecycle); }
                catch { lifecycle = null; throw; }
            }
            return StartupLifecycle.WaitAsync(InitializeAndNotifyAsync(), token);
        }
        private Task initializationNotification;
        private Task InitializeAndNotifyAsync() => initializationNotification ?? (initializationNotification = InitializeAndNotifyCoreAsync());
        private async Task InitializeAndNotifyCoreAsync()
        {
            try
            {
                await lifecycle.InitializeAsync();
                if (State == StartupState.Ready && !destroyed) OnInited();
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                if (!destroyed && State == StartupState.Failed)
                {
                    IsInitFailed = true;
                    EventCenter.Instance.EventTrigger(new GFLoadedFailedEvent(e.ToString()));
                }
                throw;
            }
        }
        public Task WaitUntilReadyAsync(CancellationToken token = default) => InitializeAsync(token);

        /// <summary>终止启动，逆序释放根节点管理的服务。关闭后重新创建根节点才能重启。</summary>
        public Task ShutdownAsync()
        {
            IsInited = false;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            contentUpdate?.Cancel();
            return lifecycle?.ShutdownAsync() ?? Task.CompletedTask;
        }

        /// <summary>
        /// 读配置文件（StreamingAssets/Data/GameRootConfig.json）并覆盖当前配置。
        /// <para>用 StreamingAssetsLoader 的异步接口：Android / WebGL 上 StreamingAssets 在包内，
        /// 不能用 File 直读；桌面平台它内部走 Task.Run，不会卡主线程。</para>
        /// <para>任何异常都只降级（用面板 / 代码里的值），不能影响启动链。</para>
        /// </summary>
        private async Task LoadConfigAsync()
        {
            EnsureConfigValid();

            // 桌面 / 编辑器：文件不存在就直接跳过，避免 StreamingAssetsLoader 打一条"加载失败"的错误日志
            bool canCheckFile = Application.platform != RuntimePlatform.Android
                                && Application.platform != RuntimePlatform.WebGLPlayer;
            if (canCheckFile && !File.Exists(UPGameRootConfigFile.AbsolutePath))
            {
                PLogger.Log($"[UPGameRoot] 未找到配置文件 {UPGameRootConfigFile.AssetPath}，使用面板 / 代码里的配置"
                            + "（可在 UPGameRoot 的 Inspector 上点「保存到 Json」生成）");
                return;
            }

            string json;
            try { json = await StreamingAssetsLoader.LoadTextFileAsync(UPGameRootConfigFile.RelativePath); }
            catch (Exception e)
            {
                PLogger.LogWarning("读取配置失败，使用面板配置：" + e.Message);
                return;
            }
            if (string.IsNullOrEmpty(json))
            {
                PLogger.LogWarning($"[UPGameRoot] 配置文件读取失败或内容为空：{UPGameRootConfigFile.AssetPath}，使用面板 / 代码里的配置");
                return;
            }

            UPGameRootConfig loaded;
            try
            {
                loaded = UPGameRootConfigFile.FromJson(json);
            }
            catch (Exception e)
            {
                PLogger.LogError($"[UPGameRoot] 配置解析失败，使用面板 / 代码里的配置：{UPGameRootConfigFile.AssetPath}\n{e.Message}");
                return;
            }

            if (loaded == null)
            {
                PLogger.LogError($"[UPGameRoot] 配置解析结果为空，使用面板 / 代码里的配置：{UPGameRootConfigFile.AssetPath}");
                return;
            }

            SetConfig(loaded);
            PLogger.Log($"[UPGameRoot] 配置已加载：{UPGameRootConfigFile.AssetPath}（加载方式={config.method}，热更={config.enableAssetUpdate}，URL={config.remoteURL}）");
        }

        /// <summary>
        /// 装配根节点拥有的组件，实际初始化由 StartupLifecycle 排序。
        /// </summary>
        private void SetComponent()
        {
            // 配置项兜底（AES 默认关闭：随包清单 assetData.assetref 是明文，误开会让清单解密失败、AB 模式整体不可用；
            // 需要加密时必须"打包端加密 + 这里开启"成对配置）
            EnsureConfigValid();

            if (debugerInit == null) debugerInit = InitComponent<DebugerInit>();
            if (downloader == null) downloader = InitComponent<Downloader>();
            if (sourcesLoadMgr == null) sourcesLoadMgr = InitComponent<AssetsLoader>();
            if (binaryDataMgr == null) binaryDataMgr = InitComponent<BinaryDataMgrInit>();
            if (_UIManager == null) _UIManager = InitComponent<UIManager>();
        }

        private T InitComponent<T>() where T : Component
        {
            // includeInactive: true —— 子节点被禁用时也要找得到，否则会重复创建一个同名组件
            T component = GetComponentInChildren<T>(true);
            if (component == null)
            {
                GameObject obj = new GameObject(typeof(T).Name);
                obj.transform.parent = transform;
                component = obj.AddComponent<T>();
            }
            return component;
        }

        private void OnInited()
        {
            ThrowIfDestroyed();
            IsInitFailed = false;
            IsInited = true;
            SceneManager.sceneLoaded += OnSceneLoaded;
            for (int i = 0; i < SceneManager.sceneCount; i++) BindScene(SceneManager.GetSceneAt(i));
            long startupMs = 0;
            if (lifecycle?.Metrics != null)
                foreach (var metric in lifecycle.Metrics) startupMs += metric.Milliseconds;
            PLogger.Log_white($"GameRoot Initialization completed in {startupMs} ms!");
            EventCenter.Instance.EventTrigger(new GFLoadedEvent());
        }

        /// <summary>
        /// 获取资源加载接口。
        /// <para>⚠ <see cref="IsInited"/> 为 false 时它还没被真正初始化（加载方式仍是默认值、
        /// 清单还是 null），访问会抛出异常；请先等待 WaitUntilReadyAsync。</para>
        /// </summary>
        public IAssetsLoader GetAssetsLoader()
        {
            if (!IsInited || State != StartupState.Ready)
                throw new InvalidOperationException("资源系统尚未就绪，请先等待 WaitUntilReadyAsync。");
            return sourcesLoadMgr;
        }

        private static string BundleTargetFor(RuntimePlatform platform)
        {
            switch (platform)
            {
                case RuntimePlatform.WindowsPlayer: return "StandaloneWindows64";
                case RuntimePlatform.Android: return "Android";
                case RuntimePlatform.IPhonePlayer: return "iOS";
                case RuntimePlatform.WebGLPlayer: return "WebGL";
                case RuntimePlatform.OSXPlayer: return "StandaloneOSX";
                case RuntimePlatform.LinuxPlayer: return "StandaloneLinux64";
                default: return platform.ToString();
            }
        }
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) { if (IsInited) BindScene(scene); }
        private void BindScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return;
            services.SceneService.GetScope(scene);
            foreach (var root in scene.GetRootGameObjects())
                foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                    if (behaviour is IGameServicesConsumer consumer)
                        try { consumer.Configure(services); } catch (Exception e) { Debug.LogException(e); }
        }
        private void OnApplicationPause(bool paused)
        {
            if (!IsInited || services == null) return;
            services.AppLifecycleService.NotifyPause(paused);
        }
        private void OnApplicationFocus(bool focused)
        {
            if (!IsInited || services == null) return;
            services.AppLifecycleService.NotifyFocus(focused);
        }
        private void OnApplicationLowMemory()
        {
            if (!IsInited || services == null) return;
            services.AppLifecycleService.NotifyLowMemory();
        }
        private void OnApplicationQuit()
        {
            services?.AppLifecycleService.NotifyQuitting();
        }

        private static async void ObserveShutdown(Task task)
        {
            try { await task; } catch (Exception e) { Debug.LogException(e); }
        }

        private void ThrowIfDestroyed()
        {
            if (destroyed || this == null) throw new OperationCanceledException("UPGameRoot 已销毁");
        }

        protected override void OnDestroyed()
        {
            destroyed = true;
            ObserveShutdown(ShutdownAsync());
            IsInited = false;
            base.OnDestroyed();
            PLogger.LogWarning("UPGameRoot Destory!!!");
        }
    }
}
