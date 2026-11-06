using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

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
        /// <para>启动时 <see cref="Init"/> 的第一步先读 StreamingAssets/Data/GameRootConfig.json：
        /// 读到就以它为准，读不到 / 解析失败才用面板上的值；面板改完点 Inspector 的「保存到 Json」写回。</para>
        /// </summary>
        [SerializeField] private UPGameRootConfig config = new UPGameRootConfig();

        /// <summary>当前配置（外部读用；要改就改这个对象上的字段）</summary>
        public UPGameRootConfig Config { get { return config; } }

        /// <summary>整体替换配置（读 Json / 工具回填用），并做空值兜底</summary>
        public void SetConfig(UPGameRootConfig newConfig)
        {
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

        private bool warnedNotInited;
        private bool destroyed;
        private readonly CancellationTokenSource hotUpdateLifetime = new CancellationTokenSource();
        private readonly List<DownloadItem> hotUpdateDownloads = new List<DownloadItem>();
        /// <summary>最近一次热更的文件阶段统计（下载等待时间另计）。</summary>
        public HotUpdateStatistics LastHotUpdateStatistics { get; private set; }
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
            try
            {
                await Init();
                IsInitFailed = false;
            }
            catch (Exception e)
            {
                if (destroyed) return;
                IsInited = false;
                IsInitFailed = true;
                PLogger.LogError($"UPGameRoot 初始化失败：{e}");

                // 失败必须让上层知道：否则 GFLoadedEvent 永远不来，GameLaunchExample 的整套启动流程
                // 永不执行，外部表现就是"卡在加载界面 / 黑屏，且没有提示"
                EventCenter.Instance.EventTrigger(new GFLoadedFailedEvent(e.ToString()));
            }
        }

        private async Task Init()
        {
            // ① 配置优先：读 StreamingAssets/Data/GameRootConfig.json（读到就以它为准）
            await LoadConfigAsync();
            ThrowIfDestroyed();

            await PublicMono.Instance.RunCoroutine(debugerInit.Init());
            binaryDataMgr.Init();
            sourcesLoadMgr.AssetAESConfig = config.AssetAESConfig;

            if (config.AssetAESConfig != null && config.AssetAESConfig.enable)
            {
                PLogger.LogWarning("[UPGameRoot] 已开启资源清单 AES 解密：请确认打包端（AB 工具窗口）确实对清单加密，"
                                   + "否则清单会因解密失败而整体不可用（随包清单 assetData.assetref 当前是明文，应保持 enable=false）");
            }

            if (config.method == AssetLoaddingMethod.Assetbundles)
                sourceRef = await UpdateAssets();

            ThrowIfDestroyed();
            await sourcesLoadMgr.Init(config.method, config.remoteURL, config.LoadAssetPath, sourceRef);
            ThrowIfDestroyed();
            if (!sourcesLoadMgr.IsInitialized)
                throw new InvalidOperationException("资源系统初始化失败，框架未就绪。请检查清单与主包加载日志。");
            _UIManager.SetResourcesLoader(sourcesLoadMgr, ResourcesLoader.Instance);
            OnInited();
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
        /// 执行资源热更，返回本次要使用的资源清单。
        /// <para>失败降级顺序：远端清单不可用 → 本地（persistentDataPath）清单 → 返回 null 交由
        /// <see cref="AssetsLoader"/> 读随包清单（StreamingAssets/Data/assetData.assetref）。</para>
        /// </summary>
        private async Task<ABSourcesRelated> UpdateAssets()
        {
            var totalClock = System.Diagnostics.Stopwatch.StartNew();
            ThrowIfDestroyed();
            // Unity 路径、配置和原生 AB 状态只在主线程读取；工作线程只接收快照。
            string persistent = Application.persistentDataPath, relativeRoot = config.LoadAssetPath, remoteUrl = config.remoteURL;
            bool updateEnabled = config.enableAssetUpdate;
            if (!updateEnabled && !await HotUpdateStorage.RecoveryRequiredAsync(persistent)) return null;
            ThrowIfDestroyed();
            var maintenance = HotUpdateFileGate.TryMaintenance();
            if (maintenance == null) throw new InvalidOperationException("已有 AB 加载或其他热更维护，不能覆盖正在使用的文件");
            if (AssetBundle.GetAllLoadedAssetBundles().Any())
            { maintenance.Dispose(); throw new InvalidOperationException("启动热更前已有已加载 AB；请在资源系统初始化前完成更新"); }
            var aes = new AssetBundleClassificationWindowConfig { enable = config.AssetAESConfig.enable,
                AESKEY = config.AssetAESConfig.AESKEY, AESIV = config.AssetAESConfig.AESIV };
            // 打开/恢复失败不得降级：可能仍有未恢复的混合版本。
            var store = await HotUpdateStorage.OpenAsync(persistent, relativeRoot, config.hotUpdate,
                hotUpdateLifetime.Token, maintenanceScope: maintenance);
            try
            {
                ThrowIfDestroyed();
                if (!updateEnabled) return null;
                var bundled = await LoadBundledSourceRef(aes);
                ThrowIfDestroyed();
                await store.BeginStageAsync();
                var manifest = new DownloadItem { url = CombineUrl(remoteUrl, "AssetBundles", assetData),
                    savePath = store.StagedManifestPath, fileName = assetData, enableResume = false, maximumBytes = store.ManifestDownloadLimit };
                var downloadClock = System.Diagnostics.Stopwatch.StartNew();
                bool manifestReady = await DownloadBatchAndWait(new List<DownloadItem> { manifest });
                store.Statistics.manifestDownloadMilliseconds = downloadClock.ElapsedMilliseconds;
                if (!manifestReady) return await LoadLocalSourceRef(store, aes);
                ThrowIfDestroyed();
                var plan = await store.PrepareAsync(aes, bundled);
                ThrowIfDestroyed();
                var downloads = plan.Downloads.Select(item => new DownloadItem {
                    url = CombineUrl(remoteUrl, relativeRoot, item.Name), savePath = item.StagedPath, fileName = item.Name,
                    fileSize = item.Size, maximumBytes = item.Size, enableResume = false }).ToList();
                downloadClock.Restart();
                bool bundlesReady = await DownloadBatchAndWait(downloads);
                store.Statistics.bundleDownloadMilliseconds = downloadClock.ElapsedMilliseconds;
                if (!bundlesReady) return await LoadLocalSourceRef(store, aes);
                ThrowIfDestroyed();
                await store.CommitAsync(plan);
                if (store.RecoveryPending) throw new IOException("缓存维护恢复未完成，停止资源初始化");
                ThrowIfDestroyed();
                return plan.Manifest;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                if (store.RecoveryPending || store.Statistics.committed) throw;
                PLogger.LogError("资源更新失败，保留旧版本：" + e.Message);
                return await LoadLocalSourceRef(store, aes);
            }
            finally
            {
                CancelHotUpdateDownloads();
                await store.DisposeAsync();
                store.Statistics.totalMilliseconds = totalClock.ElapsedMilliseconds;
                LastHotUpdateStatistics = store.Statistics;
                foreach (string warning in store.Warnings) if (!destroyed) PLogger.LogWarning(warning);
                if (!destroyed) PLogger.Log($"热更文件处理：校验 {store.Statistics.verifyMilliseconds}ms，提交 {store.Statistics.commitMilliseconds}ms，"
                    + $"回收 {store.Statistics.reclaimedBytes} 字节，更新 {store.Statistics.downloadedBundles} 个包，提交={store.Statistics.committed}");
            }
        }

        private async Task<ABSourcesRelated> LoadBundledSourceRef(AssetBundleClassificationWindowConfig aes)
        {
            try
            {
                if (Application.platform != RuntimePlatform.Android && Application.platform != RuntimePlatform.WebGLPlayer
                    && !File.Exists(Path.Combine(Application.streamingAssetsPath, "Data", assetData))) return null;
                var bytes = await StreamingAssetsLoader.LoadBinaryDataAsync("/Data/" + assetData);
                return bytes == null ? null : await HotUpdateStorage.DecodeManifestAsync(bytes, aes);
            }
            catch (Exception e) { PLogger.LogWarning("随包清单不可用，本轮跳过旧包回收：" + e.Message); return null; }
        }
        private async Task<ABSourcesRelated> LoadLocalSourceRef(HotUpdateStorage store, AssetBundleClassificationWindowConfig aes)
        {
            ThrowIfDestroyed();
            try { return await store.ReadLocalAsync(aes, verifyFiles: true); }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) { PLogger.LogWarning("本地清单或文件不可用，尝试随包清单：" + e.Message); return null; }
        }
        private void CancelHotUpdateDownloads()
        {
            if (downloader != null)
                foreach (var item in hotUpdateDownloads)
                    if (item.state != DownloadState.Completed && item.state != DownloadState.Failed && item.state != DownloadState.Canceled)
                        downloader.CancelDownload(item);
            hotUpdateDownloads.Clear();
        }

        /// <summary>
        /// 批量下载并等待完成。
        /// <para>成功与否**按本批次自己的任务状态**判定：下载器的完成事件与失败列表都是全局的，
        /// 别的批次的任务同样会影响它们（本流程通常只有本批次，但不能假设）。</para>
        /// <para>仅轮询本批次任务的终态；超时取消本批次，不依赖全局完成事件。</para>
        /// </summary>
        private async Task<bool> DownloadBatchAndWait(List<DownloadItem> downloadList)
        {
            if (downloadList == null || downloadList.Count == 0)
            {
                PLogger.Log("没有需要下载的资源");
                return true;
            }

            hotUpdateDownloads.AddRange(downloadList);
            if (downloader == null || !downloader.isActiveAndEnabled) return false;
            downloader.AddBatchDownloads(downloadList);
            float batchTimeout = config.downloadBatchTimeout;
            float start = Time.realtimeSinceStartup;
            while (!AllItemsTerminal(downloadList))
            {
                if (destroyed || downloader == null || (batchTimeout > 0f
                    && Time.realtimeSinceStartup - start >= batchTimeout))
                {
                    if (downloader != null)
                        foreach (var item in downloadList) downloader.CancelDownload(item);
                    PLogger.LogError("资源下载批次已取消或超时，保留旧版本。");
                    return false;
                }
                await PlatformTasks.Delay(TimeSpan.FromMilliseconds(50));
            }
            bool success = AllItemsCompleted(downloadList);
            if (!success) LogDownloadFailures(downloadList);
            return success;
        }

        private static bool AllItemsTerminal(List<DownloadItem> items)
        {
            foreach (var item in items)
                if (item != null && item.state != DownloadState.Completed
                    && item.state != DownloadState.Failed && item.state != DownloadState.Canceled) return false;
            return true;
        }

        /// <summary>本批次的任务是否全部成功（下载器完成后会把 state 置为 Completed）</summary>
        private static bool AllItemsCompleted(List<DownloadItem> downloadList)
        {
            for (int i = 0; i < downloadList.Count; i++)
            {
                DownloadItem item = downloadList[i];
                if (item == null)
                    continue;

                if (item.state != DownloadState.Completed)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// 打印本批次未完成的资源（最多列出 10 个），便于定位是哪个资源导致的失败。
        /// 只统计本批次自己的任务，避免被其它批次的失败记录干扰。
        /// </summary>
        private static void LogDownloadFailures(List<DownloadItem> downloadList)
        {
            int failedCount = 0;
            int shown = 0;

            for (int i = 0; i < downloadList.Count; i++)
            {
                DownloadItem item = downloadList[i];
                if (item == null || item.state == DownloadState.Completed)
                    continue;

                failedCount++;
                if (shown < 10)
                {
                    PLogger.LogError($"下载未完成：{item.fileName}（{item.url}）状态：{item.state}，错误：{item.error}");
                    shown++;
                }
            }

            PLogger.Log_red($"资源下载未全部成功：共 {downloadList.Count} 个任务，未完成 {failedCount} 个");
        }

        /// <summary>
        /// 拼接远程 URL，避免 Path.Combine 在跨平台下产生错误的路径分隔符
        /// </summary>
        private static string CombineUrl(params string[] segments)
        {
            return ABLoadMgr.CombineUrl(segments);
        }

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
            _UIManager.Init();
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
            PLogger.Log_white($"GameRoot Initialization completed!");
            EventCenter.Instance.EventTrigger(new GFLoadedEvent());
        }

        /// <summary>
        /// 获取资源加载接口。
        /// <para>⚠ <see cref="IsInited"/> 为 false 时它还没被真正初始化（加载方式仍是默认值、
        /// 清单还是 null），请在收到 <see cref="GFLoadedEvent"/> 之后再使用。</para>
        /// </summary>
        public IAssetsLoader GetAssetsLoader()
        {
            if (!IsInited && !warnedNotInited)
            {
                warnedNotInited = true;
                PLogger.LogWarning("[UPGameRoot] 资源系统尚未初始化完成（IsInited=false）：此时取到的 IAssetsLoader 仍处于默认加载方式，"
                                   + "AssetBundle 相关接口不可用。请在 GFLoadedEvent 之后再取。");
            }
            return sourcesLoadMgr;
        }

        private void ThrowIfDestroyed()
        {
            if (destroyed || this == null) throw new OperationCanceledException("UPGameRoot 已销毁");
        }

        protected override void OnDestroyed()
        {
            destroyed = true;
            CancelHotUpdateDownloads();
            hotUpdateLifetime.Cancel(); // 后台文件阶段自行恢复并关闭，不依赖销毁后的 Unity 续体。
            hotUpdateLifetime.Dispose();
            IsInited = false;
            base.OnDestroyed();
            PLogger.LogWarning("UPGameRoot Destory!!!");
        }
    }
}
