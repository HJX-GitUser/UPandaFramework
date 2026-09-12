using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>
    /// 资源加载方式
    /// </summary>
    public enum AssetLoaddingMethod
    {
        Editor,//编辑器环境下加载资源
        Assetbundles,//使用AssetBundle加载资源
    }
    public class GFLoadedEvent : EventArgBase
    {

    }

    [AddComponentMenu("UPandaGF/GameRoot")]
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
        //[Header("资源加载方式")]
        public AssetLoaddingMethod method;
        public bool enableAssetUpdate;//启动资源更新
        //[Header("资源相对路径")]
        public string LoadAssetPath = "AssetBundles/StandaloneWindows/";

        /// <summary>
        /// 远程加载地址
        /// </summary>
        public string reomoteURL = "http://127.0.0.1:80/";

        public bool EnableDebugModel = false;

        public readonly string assetData = "assetData.assetref";
        public readonly string tempAssetData = "TempAssetData.assetref";
        #endregion
        public Reporter reporter;

        public AssetBundleClassificationWindowConfig AssetAESConfig;

        private ABSourcesRelated sourceRef = null;
        public ABSourcesRelated SourceRef { get => sourceRef; }

        private void Reset()
        {
             SetComponent();
        }

        protected override void OnAwake()
        {
            SetComponent();
            InitAsync();
        }

        private async void InitAsync()
        {
            try
            {
                await Init();
            }
            catch (Exception e)
            {
                Debug.LogError($"UPGameRoot 初始化失败：{e}");
            }
        }

        private async Task Init()
        {
            await PublicMono.Instance.RunCoroutine(debugerInit.Init());
            binaryDataMgr.Init();
            sourcesLoadMgr.AssetAESConfig = AssetAESConfig;

            if (enableAssetUpdate)
            {
                sourceRef = await UpdateAssets();
            }

            await sourcesLoadMgr.Init(method, reomoteURL, LoadAssetPath, sourceRef);
            _UIManager.SetResourcesLoader(sourcesLoadMgr, ResourcesLoader.Instance);
            OnInited();
        }

        /// <summary>
        /// 执行资源热更，返回更新后的资源清单（失败时降级到本地清单）
        /// </summary>
        private async Task<ABSourcesRelated> UpdateAssets()
        {
            PLogger.Log("资源更新已启动");
            string remoteListPath = CombineUrl(reomoteURL, "AssetBundles/", assetData);
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
            byte[] tempSourceRefByte = File.ReadAllBytes(tempFileSavePath);
            ABSourcesRelated tempSourceRef = sourcesLoadMgr.LoadABSourcesRelated(tempSourceRefByte);
            if (tempSourceRef == null || tempSourceRef.mainBundleInfo == null)
            {
                PLogger.LogError("远端资源清单解析失败，使用本地资源清单");
                return LoadLocalSourceRef(localFilePath);
            }

            if (File.Exists(localFilePath))
            {
                ABSourcesRelated localSourceRef = LoadLocalSourceRef(localFilePath);
                if (localSourceRef == null || localSourceRef.mainBundleInfo == null)
                {
                    PLogger.LogError("本地资源清单异常，按首次下载处理");
                    if (!await DownloadFirstTime(tempSourceRef))
                    {
                        PLogger.Log_red("首次资源下载未全部完成，部分资源可能缺失");
                    }
                }
                else
                {
                    if (!await DownloadIncrement(tempSourceRef, localSourceRef))
                    {
                        PLogger.Log_red("增量资源下载未全部完成，将使用已下载成功的部分");
                    }
                }
            }
            else
            {
                if (!await DownloadFirstTime(tempSourceRef))
                {
                    PLogger.Log_red("首次资源下载未全部完成，部分资源可能缺失");
                }
            }

            if (File.Exists(tempFileSavePath))
                FileUtility.SaveAsAndDeleteOriginal(tempFileSavePath, localFilePath, true);

            return LoadLocalSourceRef(localFilePath);
        }

        private ABSourcesRelated LoadLocalSourceRef(string localFilePath)
        {
            if (!File.Exists(localFilePath))
                return null;
            return sourcesLoadMgr.LoadABSourcesRelated(File.ReadAllBytes(localFilePath));
        }

        /// <summary>
        /// 首次下载：主包 + 所有持久化资源
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
            string delPath = Path.Combine(Application.persistentDataPath, LoadAssetPath, fileName);
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
        /// 批量下载并等待完成；空列表直接返回，避免死循环
        /// </summary>
        /// <summary>
        /// 批量下载并等待完成，返回是否全部成功；空列表直接返回成功，避免死循环
        /// </summary>
        private async Task<bool> DownloadBatchAndWait(List<DownloadItem> downloadList)
        {
            if (downloadList == null || downloadList.Count == 0)
            {
                PLogger.Log("没有需要下载的资源");
                return true;
            }

            // RunContinuationsAsynchronously：避免后续流程在下载器的"完成事件分发"中途同步续跑
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            // 用带结果的事件：只要有一个任务重试耗尽，allSuccess 就是 false
            Action<bool> onFinished = (allSuccess) => tcs.TrySetResult(allSuccess);
            downloader.OnAllDownloadsFinished += onFinished;
            downloader.AddBatchDownloads(downloadList);
            try
            {
                bool allSuccess = await tcs.Task;
                if (!allSuccess)
                    LogDownloadFailures(downloadList);

                return allSuccess;
            }
            finally
            {
                downloader.OnAllDownloadsFinished -= onFinished;
            }
        }

        /// <summary>
        /// 打印本批次下载失败的资源（最多列出 10 个），便于定位是哪个资源导致的失败
        /// </summary>
        private void LogDownloadFailures(List<DownloadItem> downloadList)
        {
            IReadOnlyList<DownloadItem> failed = downloader.FailedItems;
            PLogger.Log_red($"资源下载未全部成功：共 {downloadList.Count} 个任务，失败 {failed.Count} 个");

            int shown = 0;
            foreach (DownloadItem item in failed)
            {
                if (item == null)
                    continue;

                PLogger.LogError($"下载失败：{item.fileName}（{item.url}）：{item.error}");

                shown++;
                if (shown >= 10)
                    break;
            }
        }

        private DownloadItem GetDownInfo(string fileName, long size)
        {
            string assetsavePath = Path.Combine(Application.persistentDataPath, LoadAssetPath);
            return new DownloadItem()
            {
                url = CombineUrl(reomoteURL, LoadAssetPath, fileName),
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
            if (AssetAESConfig == null) AssetAESConfig = new AssetBundleClassificationWindowConfig();
            if (debugerInit == null) debugerInit = InitComponent<DebugerInit>();
            if (downloader == null) downloader = InitComponent<Downloader>();
            if (sourcesLoadMgr == null) sourcesLoadMgr = InitComponent<AssetsLoader>();
            if (binaryDataMgr == null) binaryDataMgr = InitComponent<BinaryDataMgrInit>();
            if (_UIManager == null) _UIManager = InitComponent<UIManager>();
            _UIManager.Init();
        }

        private T InitComponent<T>() where T : Component
        {
            T component = GetComponentInChildren<T>();
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
            //PLoger.Log_red($"GameRoot Initialization completed!");
            //PLoger.Log_green($"GameRoot Initialization completed!");
            //PLoger.Log_blue($"GameRoot Initialization completed!");
            //PLoger.Log_yellow($"GameRoot Initialization completed!");
            //PLoger.Log_cyan($"GameRoot Initialization completed!");
            //PLoger.LogFormat("<color=yellow>{0}</color>", "GameRoot Initialization completed!");
            PLogger.Log_white($"GameRoot Initialization completed!");
            EventCenter.Instance.EventTrigger(new GFLoadedEvent());
        }

        /// <summary>
        /// 获取资源加载接口
        /// </summary>
        /// <returns></returns>
        public IAssetsLoader GetAssetsLoader()
        {
            return sourcesLoadMgr;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            PLogger.LogWarning("UPGameRoot Destory!!!");
        }
    }
}
