using System;
using System.Collections.Generic;
using System.IO;
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
        #endregion

        private void Reset()
        {
             SetComponent();
        }

        protected override void OnAwake()
        {
            // 基类 Awake 处理"重复实例"的方式是 Destroy(gameObject)，但之后仍然会调用 OnAwake()。
            // 若继续往下跑，InitAsync 的 await 链照样会跑完并再广播一次 GFLoadedEvent
            // （表现为：加载界面 / 首场景流程走两遍 + 一堆 MissingReference 日志）
            if (Instance != this) return;

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

            await PublicMono.Instance.RunCoroutine(debugerInit.Init());
            binaryDataMgr.Init();
            sourcesLoadMgr.AssetAESConfig = config.AssetAESConfig;

            if (config.AssetAESConfig != null && config.AssetAESConfig.enable)
            {
                PLogger.LogWarning("[UPGameRoot] 已开启资源清单 AES 解密：请确认打包端（AB 工具窗口）确实对清单加密，"
                                   + "否则清单会因解密失败而整体不可用（随包清单 assetData.assetref 当前是明文，应保持 enable=false）");
            }

            if (config.enableAssetUpdate)
            {
                sourceRef = await UpdateAssets();
            }

            await sourcesLoadMgr.Init(config.method, config.remoteURL, config.LoadAssetPath, sourceRef);
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

            string json = await StreamingAssetsLoader.LoadTextFileAsync(UPGameRootConfigFile.RelativePath);
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
            PLogger.Log("资源更新已启动");
            string remoteListPath = CombineUrl(config.remoteURL, "AssetBundles/", assetData);
            string tempFileSavePath = Path.Combine(Application.persistentDataPath, "AssetBundles/", tempAssetData);
            string localFilePath = Path.Combine(Application.persistentDataPath, "AssetBundles/", assetData);

            if (File.Exists(tempFileSavePath))
                File.Delete(tempFileSavePath);

            PLogger.Log($"资源配置下载路径：{remoteListPath}\n保存路径：{tempFileSavePath}");
            bool isDown = await downloader.DownloadAsync(remoteListPath, tempFileSavePath);
            if (!isDown)
            {
                PLogger.Log_red("资源校验目录下载失败，使用本地资源清单");
                return LoadLocalSourceRef(localFilePath);
            }

            PLogger.Log_green("校验数据下载成功");

            ABSourcesRelated tempSourceRef;
            try
            {
                byte[] tempSourceRefByte = File.ReadAllBytes(tempFileSavePath);
                tempSourceRef = sourcesLoadMgr.LoadABSourcesRelated(tempSourceRefByte);
            }
            catch (Exception e)
            {
                // 文件被外部删除 / 写坏 / 读取失败：不能直接抛出去，否则整个初始化失败，
                // 连下面已经写好的降级逻辑都走不到
                PLogger.LogError($"读取远端资源清单失败：{e.Message}，使用本地资源清单");
                return LoadLocalSourceRef(localFilePath);
            }

            if (tempSourceRef == null || tempSourceRef.mainBundleInfo == null)
            {
                PLogger.LogError("远端资源清单解析失败，使用本地资源清单");
                return LoadLocalSourceRef(localFilePath);
            }

            // 首次全量 / 增量更新
            bool allSuccess;
            if (File.Exists(localFilePath))
            {
                ABSourcesRelated localSourceRef = LoadLocalSourceRef(localFilePath);
                if (localSourceRef == null || localSourceRef.mainBundleInfo == null)
                {
                    PLogger.LogError("本地资源清单异常，按首次下载处理");
                    allSuccess = await DownloadFirstTime(tempSourceRef);
                }
                else
                {
                    allSuccess = await DownloadIncrement(tempSourceRef, localSourceRef);
                }
            }
            else
            {
                allSuccess = await DownloadFirstTime(tempSourceRef);
            }

            if (!allSuccess)
            {
                // 关键：下载没全部成功时**不能**提交远端清单。
                // 否则清单里"声称存在"的包其实没下载下来，运行时只会看到零散的"该资源不存在"，极难定位。
                PLogger.Log_red("本次热更未全部成功，不提交远端资源清单");

                if (File.Exists(tempFileSavePath))
                    File.Delete(tempFileSavePath);       // 不提交就清掉临时文件，避免下次误用

                if (File.Exists(localFilePath))
                {
                    PLogger.LogWarning("继续使用上一次成功热更的资源清单");
                    return LoadLocalSourceRef(localFilePath);
                }

                PLogger.LogWarning("本机没有可用的热更清单，本次改用随包清单（Assets/StreamingAssets/Data/assetData.assetref），下次启动会重试热更");
                return null;    // 传 null 时 AssetsLoader 会去读 StreamingAssets 里的随包清单
            }

            if (File.Exists(tempFileSavePath)
                && !FileUtility.SaveAsAndDeleteOriginal(tempFileSavePath, localFilePath, true))
            {
                // 清单落盘失败（被占用 / 磁盘满 / 权限不足）：新下载的包与旧清单不匹配，必须说清楚
                PLogger.LogError("资源清单落地失败，本次仍使用旧的本地清单，新下载的包可能与本清单不匹配");
                return LoadLocalSourceRef(localFilePath);
            }

            return LoadLocalSourceRef(localFilePath);
        }

        private ABSourcesRelated LoadLocalSourceRef(string localFilePath)
        {
            if (!File.Exists(localFilePath))
                return null;
            return sourcesLoadMgr.LoadABSourcesRelated(File.ReadAllBytes(localFilePath));
        }

        /// <summary>
        /// 首次下载：主包 + 所有"加载位置为 PersistentDataPath"的包
        /// </summary>
        private async Task<bool> DownloadFirstTime(ABSourcesRelated tempSourceRef)
        {
            List<DownloadItem> downloadList = new List<DownloadItem>();
            // 主包只有在"加载位置就是 PersistentDataPath"时才需要下载：
            // 若主包从 StreamingAssets / 远程加载，下载到本地不会被使用（下载地址需与加载地址一致）
            if (tempSourceRef.mainBundleInfo.loadPath == ABLoadPath.PersistentDataPath)
            {
                downloadList.Add(GetDownInfo(tempSourceRef.mainBundleInfo.bundleName, tempSourceRef.mainBundleInfo.size));
            }
            else
            {
                PLogger.LogWarning($"主包加载位置为 {tempSourceRef.mainBundleInfo.loadPath}，本次热更不会下载主包");
            }
            foreach (var item in tempSourceRef.bundleInfo.Values)
            {
                if (item.loadPath == ABLoadPath.PersistentDataPath)
                    downloadList.Add(GetDownInfo(item.bundleName, item.size));
            }
            PLogger.Log($"共有{downloadList.Count}个资源需要下载");
            return await DownloadBatchAndWait(downloadList);
        }

        /// <summary>
        /// 增量更新：按 MD5 对比下载变更资源，并清理远端已删除的本地资源
        /// </summary>
        private async Task<bool> DownloadIncrement(ABSourcesRelated tempSourceRef, ABSourcesRelated localSourceRef)
        {
            List<DownloadItem> downloadList = new List<DownloadItem>();
            if (tempSourceRef.mainBundleInfo.loadPath == ABLoadPath.PersistentDataPath
                && !string.Equals(tempSourceRef.mainBundleInfo.md5, localSourceRef.mainBundleInfo.md5))
            {
                downloadList.Add(GetDownInfo(tempSourceRef.mainBundleInfo.bundleName, tempSourceRef.mainBundleInfo.size));
            }

            foreach (var item in tempSourceRef.bundleInfo)
            {
                if (item.Value.loadPath != ABLoadPath.PersistentDataPath)
                    continue;
                if (localSourceRef.bundleInfo.TryGetValue(item.Key, out var localInfo))
                {
                    if (!string.Equals(item.Value.md5, localInfo.md5))
                        downloadList.Add(GetDownInfo(item.Key, item.Value.size));
                }
                else
                {
                    downloadList.Add(GetDownInfo(item.Key, item.Value.size));
                }
            }

            // 远端已删除、本地仍存在的资源做清理
            foreach (var item in localSourceRef.bundleInfo)
            {
                if (!tempSourceRef.bundleInfo.ContainsKey(item.Key))
                    DeleteLocalAsset(item.Key);
            }

            PLogger.Log($"共有{downloadList.Count}个资源需要更新");
            return await DownloadBatchAndWait(downloadList);
        }

        private void DeleteLocalAsset(string fileName)
        {
            string delPath = Path.Combine(Application.persistentDataPath, config.LoadAssetPath, fileName);
            try
            {
                if (File.Exists(delPath))
                {
                    File.Delete(delPath);
                    PLogger.Log($"{delPath} 已删除");
                }
            }
            catch (Exception e)
            {
                PLogger.LogWarning($"删除资源失败：{delPath}，{e.Message}");
            }
        }

        /// <summary>
        /// 批量下载并等待完成。
        /// <para>成功与否**按本批次自己的任务状态**判定：下载器的完成事件与失败列表都是全局的，
        /// 别的批次的任务同样会影响它们（本流程通常只有本批次，但不能假设）。</para>
        /// <para>带超时：下载器要求"无排队、无下载中、无暂停"才算批次结束，一旦有任务被暂停，
        /// 完成事件永远不会触发 —— 没有超时就会把整个启动链挂在 await 上且没有任何日志。</para>
        /// </summary>
        private async Task<bool> DownloadBatchAndWait(List<DownloadItem> downloadList)
        {
            if (downloadList == null || downloadList.Count == 0)
            {
                PLogger.Log("没有需要下载的资源");
                return true;
            }

            if (!downloader.IsIdle)
            {
                PLogger.LogWarning("[UPGameRoot] 下载器上已有其它任务在跑：批次完成事件与失败统计可能受其影响（成功判定仍按本批次任务状态）");
            }

            // RunContinuationsAsynchronously：避免后续流程在下载器的"完成事件分发"中途同步续跑
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            // 用带结果的事件：只要有一个任务重试耗尽，allSuccess 就是 false
            Action<bool> onFinished = (allSuccess) => tcs.TrySetResult(allSuccess);
            downloader.OnAllDownloadsFinished += onFinished;
            downloader.AddBatchDownloads(downloadList);
            try
            {
                bool? allSuccessByEvent = await WaitWithTimeout(tcs.Task, config.downloadBatchTimeout);
                if (allSuccessByEvent == null)
                {
                    PLogger.LogError($"[UPGameRoot] 等待下载完成超时（{config.downloadBatchTimeout} 秒）：可能有任务被暂停或下载器异常。"
                                     + "本次按失败处理，下载器仍可能在后台继续");
                    return false;
                }

                bool allSuccess = AllItemsCompleted(downloadList);
                if (allSuccess != allSuccessByEvent.Value)
                {
                    PLogger.LogWarning($"[UPGameRoot] 完成事件结果({allSuccessByEvent.Value})与本批次任务状态({allSuccess})不一致，按本批次状态处理");
                }

                if (!allSuccess)
                    LogDownloadFailures(downloadList);

                return allSuccess;
            }
            finally
            {
                downloader.OnAllDownloadsFinished -= onFinished;
            }
        }

        /// <summary>等待任务完成，带超时；返回 null 表示超时（timeoutSeconds &lt;= 0 表示不超时）</summary>
        private static async Task<bool?> WaitWithTimeout(Task<bool> task, float timeoutSeconds)
        {
            if (timeoutSeconds <= 0f)
                return await task;

            Task completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds)));
            return completed == task ? (bool?)await task : null;
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

        private DownloadItem GetDownInfo(string fileName, long size)
        {
            string assetsavePath = Path.Combine(Application.persistentDataPath, config.LoadAssetPath);
            return new DownloadItem()
            {
                url = CombineUrl(config.remoteURL, config.LoadAssetPath, fileName),
                savePath = Path.Combine(assetsavePath, fileName),
                fileName = fileName,
                fileSize = size
            };
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

        protected override void OnDestroy()
        {
            base.OnDestroy();
            PLogger.LogWarning("UPGameRoot Destory!!!");
        }
    }
}
