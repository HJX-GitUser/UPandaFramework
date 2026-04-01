using System;
using System.Collections;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine;
using UnityEngine.Events;
using System.Threading.Tasks;
using System.Text;
using System.Collections.Generic;

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
        //资源远端更新配置
        //public ABUpdataMgrArg assetUpdataConfig;

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
        protected override async void OnAwake()
        {
            SetComponent();
            //StartCoroutine();
            await Init();
        }

        private async Task Init()
        {
            await PublicMono.Instance.RunCoroutine(debugerInit.Init());
            binaryDataMgr.Init();
            sourcesLoadMgr.AssetAESConfig = AssetAESConfig;
            if (enableAssetUpdate)
            {
                PLogger.Log("资源更新已启动");
                string removeDownPath = Path.Combine(reomoteURL, "AssetBundles/", assetData);//远端资源数据
                string tempFileSavePath = Path.Combine(Application.persistentDataPath, "AssetBundles/", tempAssetData);//临时资源数据
                if (File.Exists(tempFileSavePath)) File.Delete(tempFileSavePath);//如果缓存对比文件存在就直接删掉
                string localFilePath = Path.Combine(Application.persistentDataPath, "AssetBundles/", assetData);//本地资源数据
                PLogger.Log($"资源配置下载路径：{removeDownPath}\n保存路径：{tempFileSavePath}");
                bool isDown = await downloader.DownloadAsync(removeDownPath, tempFileSavePath);
                if (isDown)
                {
                    PLogger.Log_green("校验数据下载成功");
                    byte[] tempSourceRefByte = File.ReadAllBytes(tempFileSavePath);
                    ABSourcesRelated tempSourceRef = sourcesLoadMgr.LoadABSourcesRelated(tempSourceRefByte);
                    if (File.Exists(localFilePath))
                    {
                        ABSourcesRelated localSourceRef = sourcesLoadMgr.LoadABSourcesRelated(File.ReadAllBytes(localFilePath));
                        //校验对比资源并更新
                        List<DownloadItem> downloadList = new List<DownloadItem>();
                        if (tempSourceRef.mainBundleInfo.loadPath == ABLoadPath.PersistentDataPath && !tempSourceRef.mainBundleInfo.md5.Equals(localSourceRef.mainBundleInfo.md5))
                        {
                            downloadList.Add(GetDownInfo(tempSourceRef.mainBundleInfo.bundleName, tempSourceRef.mainBundleInfo.size));
                        }
                        foreach (var item in tempSourceRef.bundleInfo)
                        {
                            if (item.Value.loadPath != ABLoadPath.PersistentDataPath) continue;
                            if (localSourceRef.bundleInfo.ContainsKey(item.Key))
                            {
                                if (!item.Value.md5.Equals(localSourceRef.bundleInfo[item.Key].md5))
                                {
                                    downloadList.Add(GetDownInfo(item.Key, item.Value.size));
                                }
                            }
                            else
                            {
                                downloadList.Add(GetDownInfo(item.Key, item.Value.size));
                            }
                        }
                        //检查需要删除的目录（远程目录没有，但是本地存在的就删掉）
                        foreach (var item in localSourceRef.bundleInfo)
                        {
                            if (!tempSourceRef.bundleInfo.ContainsKey(item.Key))
                            {
                                string delPath = Path.Combine(Application.persistentDataPath, LoadAssetPath, item.Key);
                                Debug.Log($"{delPath} 已删除");
                                File.Delete(delPath);
                            }
                        }
                        Debug.Log($"共有{downloadList.Count}个资源需要更新");
                        bool isDownComplete = false;
                        downloader.OnAllDownloadsComplete += () =>
                        {
                            Debug.Log("所有资源下载结束");
                            isDownComplete = true;
                        };
                        downloader.AddBatchDownloads(downloadList);
                        while (!isDownComplete)
                        {
                            await Task.Yield();
                        }
                    }
                    else // 文件不存在说明是第一次下载
                    {
                        List<DownloadItem> downloadList = new List<DownloadItem>();
                        downloadList.Add(GetDownInfo(tempSourceRef.mainBundleInfo.bundleName, tempSourceRef.mainBundleInfo.size));
                        foreach (var item in tempSourceRef.bundleInfo.Values)
                        {
                            if (item.loadPath == ABLoadPath.PersistentDataPath)
                                downloadList.Add(GetDownInfo(item.bundleName, item.size));
                        }
                        Debug.Log($"共有{downloadList.Count}个资源需要下载");
                        bool isDownComplete = false;
                        downloader.AddBatchDownloads(downloadList);
                        downloader.OnAllDownloadsComplete += () =>
                        {
                            Debug.Log("所有资源下载结束");
                            isDownComplete = true;
                        };
                        while (!isDownComplete)
                        {
                            await Task.Yield();
                        }
                    }
                    if (File.Exists(tempFileSavePath))
                        FileUtility.SaveAsAndDeleteOriginal(tempFileSavePath, localFilePath, true);
                }
                else
                {
                    PLogger.Log_red("资源校验目录下载失败");
                }
                if (File.Exists(localFilePath))
                {
                    byte[] Temparg = File.ReadAllBytes(localFilePath);
                    sourceRef = sourcesLoadMgr.LoadABSourcesRelated(Temparg);
                }
            }
            await sourcesLoadMgr.Init(method, reomoteURL, LoadAssetPath, sourceRef);
            _UIManager.SetResourcesLoader(sourcesLoadMgr, ResourcesLoader.Instance);
            OnInited();
        }


        private DownloadItem GetDownInfo(string fileName, long size)
        {
            string assetRemotePath = Path.Combine(reomoteURL, LoadAssetPath);
            string assetsavePath = Path.Combine(Application.persistentDataPath, LoadAssetPath);
            return new DownloadItem()
            {
                url = Path.Combine(assetRemotePath, fileName),
                savePath = Path.Combine(assetsavePath, fileName),
                fileName = fileName,
                fileSize = size
            };
        }


        private void SetComponent()
        {
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




