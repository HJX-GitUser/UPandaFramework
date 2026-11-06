using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
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
                FileUpdateTransaction.Recover(Path.Combine(Application.persistentDataPath, "AssetBundles", ".update-recovery"));
            if (config.method == AssetLoaddingMethod.Assetbundles && config.enableAssetUpdate)
            {
                sourceRef = await UpdateAssets();
            }

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
            string basePath = Path.Combine(Application.persistentDataPath, "AssetBundles");
            string recoveryPath = Path.Combine(basePath, ".update-recovery");
            // 上次在提交途中退出：先恢复完整旧版本，恢复失败则停止启动，避免加载混合版本。
            FileUpdateTransaction.Recover(recoveryPath);
            string localFilePath = Path.Combine(basePath, assetData);
            string stageRoot = Path.GetFullPath(Path.Combine(basePath, ".update-stage"));
            if (Directory.Exists(stageRoot))
                foreach (string oldStage in Directory.GetDirectories(stageRoot))
                    TryDeleteStage(oldStage, stageRoot);
            string stagePath = Path.Combine(stageRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagePath);
            string tempFileSavePath = Path.Combine(stagePath, assetData);
            try
            {
                if (!await downloader.DownloadAsync(CombineUrl(config.remoteURL, "AssetBundles", assetData), tempFileSavePath))
                    return LoadLocalSourceRef(localFilePath);
                ThrowIfDestroyed();
                var remote = sourcesLoadMgr.LoadABSourcesRelated(VersionedDataSerializer.ReadFile(tempFileSavePath));
                if (remote == null || remote.mainBundleInfo == null || remote.bundleInfo == null)
                    return LoadLocalSourceRef(localFilePath);

                var downloads = new List<DownloadItem>();
                var expected = new List<AssetBundleLoadInfo>();
                var staged = new List<string>();
                var targets = new List<string>();
                var bundles = new List<AssetBundleLoadInfo>(remote.bundleInfo.Values);
                bundles.Add(remote.mainBundleInfo);
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var info in bundles)
                {
                    if (info == null) throw new InvalidDataException("清单包含空包信息");
                    if (info.loadPath != ABLoadPath.PersistentDataPath) continue;
                    string target = SafeBundlePath(info.bundleName);
                    if (!seen.Add(target)) throw new InvalidDataException("清单包含重复包路径：" + info.bundleName);
                    // 直接校验文件，不只比较两份清单，修复缺失/损坏/加载位置变更遗漏下载的问题。
                    if (VerifyBundle(target, info)) continue;
                    string stage = Path.Combine(stagePath, downloads.Count + ".bundle");
                    downloads.Add(new DownloadItem {
                        url = CombineUrl(config.remoteURL, config.LoadAssetPath, info.bundleName),
                        savePath = stage, fileName = info.bundleName, fileSize = info.size, enableResume = false
                    });
                    expected.Add(info);
                    staged.Add(stage);
                    targets.Add(target);
                }
                if (!await DownloadBatchAndWait(downloads)) return LoadLocalSourceRef(localFilePath);
                ThrowIfDestroyed();
                for (int i = 0; i < downloads.Count; i++)
                    if (!VerifyBundle(staged[i], expected[i]))
                        throw new InvalidDataException("下载文件大小或 MD5 不匹配：" + expected[i].bundleName);
                staged.Add(tempFileSavePath);
                targets.Add(localFilePath);
                FileUpdateTransaction.Commit(staged, targets, recoveryPath);
                // 保留不再使用的旧包，避免删除仍由随包清单引用的文件；后续可单独实现版本垃圾回收。
                return remote;
            }
            catch (Exception e)
            {
                if (File.Exists(Path.Combine(recoveryPath, "journal"))) throw; // 回滚未完成，不能降级加载。
                PLogger.LogError("资源更新失败，保留旧版本：" + e.Message);
                return LoadLocalSourceRef(localFilePath);
            }
            finally { TryDeleteStage(stagePath, stageRoot); }
        }

        private static void TryDeleteStage(string path, string root)
        {
            string fullPath = Path.GetFullPath(path);
            string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return;
            try { if (Directory.Exists(fullPath)) Directory.Delete(fullPath, true); }
            catch (IOException) { } // 超时取消中的文件仍可能被请求占用，下次启动再清理。
            catch (UnauthorizedAccessException) { }
        }

        private string SafeBundlePath(string name)
        {
            string persistent = Path.GetFullPath(Application.persistentDataPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string root = Path.GetFullPath(Path.Combine(Application.persistentDataPath,
                config.LoadAssetPath.TrimStart('/', '\\'))).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string target = Path.GetFullPath(Path.Combine(root, name ?? string.Empty));
            if (!root.StartsWith(persistent, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(name) || Path.IsPathRooted(name)
                || !target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("非法资源包路径：" + name);
            return target;
        }

        private static bool VerifyBundle(string path, AssetBundleLoadInfo info)
        {
            if (!File.Exists(path)) return false;
            if (info.size > 0 && new FileInfo(path).Length != info.size) return false;
            if (string.IsNullOrEmpty(info.md5)) return false;
            using (var md5 = MD5.Create())
            using (var stream = File.OpenRead(path))
                return string.Equals(BitConverter.ToString(md5.ComputeHash(stream)).Replace("-", ""),
                    info.md5, StringComparison.OrdinalIgnoreCase);
        }

        private ABSourcesRelated LoadLocalSourceRef(string localFilePath)
        {
            try
            {
                if (!File.Exists(localFilePath)) return null;
                return sourcesLoadMgr.LoadABSourcesRelated(VersionedDataSerializer.ReadFile(localFilePath));
            }
            catch (Exception e)
            {
                PLogger.LogWarning("读取本地清单失败，尝试随包清单：" + e.Message);
                return null;
            }
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

            downloader.AddBatchDownloads(downloadList);
            float start = Time.realtimeSinceStartup;
            while (!AllItemsTerminal(downloadList))
            {
                if (destroyed || downloader == null || (config.downloadBatchTimeout > 0f
                    && Time.realtimeSinceStartup - start >= config.downloadBatchTimeout))
                {
                    if (downloader != null)
                        foreach (var item in downloadList) downloader.CancelDownload(item);
                    PLogger.LogError("资源下载批次已取消或超时，保留旧版本。");
                    return false;
                }
                await Task.Delay(50);
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
            string url = string.Empty;
            foreach (string segment in segments)
            {
                if (string.IsNullOrEmpty(segment))
                    continue;
                url = url.Length == 0
                    ? segment
                    : url.TrimEnd('/') + "/" + segment.TrimStart('/');
            }
            return url;
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
            IsInited = false;
            base.OnDestroyed();
            PLogger.LogWarning("UPGameRoot Destory!!!");
        }
    }
}
