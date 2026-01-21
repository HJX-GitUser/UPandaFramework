using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;

namespace UPandaGF
{
    /// <summary>
    /// AB包加载管理器
    /// </summary>
    public class ABLoadMgr : LazyMonoSingletonBase<ABLoadMgr>
    {
        private bool isInit = false;
        /// <summary>
        /// 是否从StreamingAssets路径下加载
        /// </summary>
        // [HideInInspector]
        // public bool IsLoadedOnStreammingAssets = true;
        //主包
        private AssetBundle mainAB = null;
        //主包依赖获取配置文件
        private AssetBundleManifest manifest = null;

        //存储 AB包对象(AB包不能够重复加载 否则会报错)
        private Dictionary<string, AssetBundle> abDic = new Dictionary<string, AssetBundle>();

        /// <summary>
        /// 获取AB包加载路径（相对路径）
        /// </summary>
        [HideInInspector]
        public string PathUrl;

        /// <summary>
        /// 主包名 根据平台不同 包名不同
        /// </summary>
        [HideInInspector]
        public string MainName;

        /// <summary>
        /// 主包加载路径
        /// </summary>
        public ABLoadPath MainPackageLoadPath = ABLoadPath.StreamingAssetsPath;

        /// <summary>
        /// 远程加载地址
        /// </summary>
        public string remoteURL = "http://127.0.0.1:8090/";

        private EventCenter eventCenter => EventCenter.Instance;

        #region 初始化
        /// <summary>
        /// 初始化
        /// </summary>
        /// <param name="pathUrl">相对路径</param>
        /// <param name="mainName">主包名</param>
        /// <param name="mainLoadPath">主包加载路径</param>
        /// <returns></returns>
        public async Task Init(string pathUrl, string mainName, ABLoadPath mainLoadPath)
        {
            isInit = true;
            PathUrl = pathUrl;
            MainName = mainName;
            MainPackageLoadPath = mainLoadPath;
            try
            {
                // 加载主包 和 配置文件
                // 加载所有包是 通过它才能得到依赖信息
                mainAB = await LoadAssetBundle(MainName, MainPackageLoadPath);
                manifest = mainAB.LoadAsset<AssetBundleManifest>("AssetBundleManifest");
                Debug.Log($"AB包加载路径：{PathUrl}\n资源包主包：{MainName}");
            }
            catch
            {
                PLogger.LogError($"AssetBundleManifest 主包加载失败");
            }
        }
        #endregion

        #region 加载AssetBundle
        private string GetFullPath(ABLoadPath loodPath)
        {
            string fullPath = "";
            switch (loodPath)
            {
                case ABLoadPath.StreamingAssetsPath:
                    fullPath = StreamingAssetsLoader.CombinePath(PathUrl);
                    break;
                case ABLoadPath.PersistentDataPath:
                    //fullPath = Application.persistentDataPath + "/" + PathUrl;
                    fullPath = Path.Combine(Application.persistentDataPath, PathUrl); ;
                    break;
                case ABLoadPath.RemotePath:
                    //fullPath = remoteURL + "/" + PathUrl;
                    fullPath = Path.Combine(remoteURL, PathUrl); ; ;
                    break;
            }
            return fullPath;
        }
        /// <summary>
        /// 加载AssetBundle
        /// </summary>
        /// <param name="abName">包名</param>
        /// <param name="loodPath">加载方式</param>
        /// <param name="progressEvent">加载进度</param>
        /// <returns></returns>
        private async Task<AssetBundle> LoadAssetBundle(string abName, ABLoadPath loodPath)
        {
            //string fullPath = Path.Combine(PathUrl, abName);
            string fullPath = Path.Combine(GetFullPath(loodPath), abName);
            AssetBundle ab = null;
            ABLoadProgressEvent aBLoadProgessEvent = new ABLoadProgressEvent(abName, loodPath, 0, false);
            try
            {
                switch (loodPath)
                {
                    case ABLoadPath.RemotePath:
                    case ABLoadPath.StreamingAssetsPath:
                        // 对于StreamingAssets路径（特别是WebGL和Android），使用UnityWebRequest
                        using (UnityWebRequest webRequest = UnityWebRequestAssetBundle.GetAssetBundle(fullPath))
                        {
                            webRequest.SendWebRequest();
                            
                            while (!webRequest.isDone)
                            {
                                aBLoadProgessEvent.progress = webRequest.downloadProgress;
                                //PLogger.Log_yellow($"{aBLoadProgessEvent.abName}:下载进度：{aBLoadProgessEvent.progress}");
                                eventCenter.EventTrigger(aBLoadProgessEvent);
                                await System.Threading.Tasks.Task.Yield();
                            }
                            //检查请求结果
                            if (webRequest.result != UnityWebRequest.Result.Success)
                            {
                                Debug.LogError($"AssetBundle加载失败\n{fullPath}\n{webRequest.error}");
                                return null;
                            }
                            ab = DownloadHandlerAssetBundle.GetContent(webRequest);
                            if (ab == null)
                            {
                                Debug.LogError($"AssetBundle加载失败\n{fullPath}");
                            }
                        }
                        break;
                    case ABLoadPath.PersistentDataPath:
                        // 对于本地文件路径，使用LoadFromFileAsync
                        AssetBundleCreateRequest createRequest = AssetBundle.LoadFromFileAsync(fullPath);
                        if (createRequest == null)
                        {
                            Debug.LogError($"AssetBundle加载失败\n{fullPath}");
                            return null;
                        }
                        while (!createRequest.isDone)
                        {
                            aBLoadProgessEvent.progress = createRequest.progress;
                            //PLogger.Log_yellow($"{aBLoadProgessEvent.abName}:加载进度：{aBLoadProgessEvent.progress}");
                            eventCenter.EventTrigger(aBLoadProgessEvent);
                            await System.Threading.Tasks.Task.Yield();
                        }
                        ab = createRequest.assetBundle;
                        if (ab == null)
                        {
                            Debug.LogError($"AssetBundle加载失败\n{fullPath}");
                        }
                        break;
                }
                aBLoadProgessEvent.progress = 1;
                aBLoadProgessEvent.isDown = true;
                eventCenter.EventTrigger(aBLoadProgessEvent);
                return ab;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"AssetBundle加载失败 特殊异常 '{abName}'\n{e.Message}");
                return null;
            }
        }

        private IEnumerator LoadAssetBundle(string abName, ABLoadPath loodPath, UnityAction<AssetBundle> callback)
        {
            // string fullPath = Path.Combine(PathUrl, abName);
            string fullPath = Path.Combine(GetFullPath(loodPath), abName);
            AssetBundle ab = null;
            ABLoadProgressEvent aBLoadProgessEvent = new ABLoadProgressEvent(abName, loodPath, 0, false);
            switch (loodPath)
            {
                case ABLoadPath.RemotePath:
                case ABLoadPath.StreamingAssetsPath:
                    // 对于StreamingAssets路径（特别是WebGL和Android），使用UnityWebRequest
                    using (UnityWebRequest webRequest = UnityWebRequestAssetBundle.GetAssetBundle(fullPath))
                    {
                        webRequest.SendWebRequest();
                        while (!webRequest.isDone)
                        {
                            aBLoadProgessEvent.progress = webRequest.downloadProgress;
                            // PLogger.Log_yellow($"{aBLoadProgessEvent.abName}:下载进度：{aBLoadProgessEvent.progress}");
                            eventCenter.EventTrigger(aBLoadProgessEvent);
                            yield return null;
                        }
                        //检查请求结果
                        if (webRequest.result != UnityWebRequest.Result.Success)
                        {
                            Debug.LogError($"AssetBundle加载失败\n{fullPath}\n{webRequest.error}");
                            callback.Invoke(null);
                            yield break;
                        }
                        ab = DownloadHandlerAssetBundle.GetContent(webRequest);
                        if (ab == null)
                        {
                            Debug.LogError($"AssetBundle加载失败\n{fullPath}");
                            callback.Invoke(null);
                            yield break;
                        }
                    }
                    break;
                case ABLoadPath.PersistentDataPath:
                    // 对于本地文件路径，使用LoadFromFileAsync
                    AssetBundleCreateRequest createRequest = AssetBundle.LoadFromFileAsync(fullPath);
                    if (createRequest == null)
                    {
                        Debug.LogError($"AssetBundle加载失败\n{fullPath}");
                        callback.Invoke(null);
                        yield break;
                    }
                    while (!createRequest.isDone)
                    {
                        aBLoadProgessEvent.progress = createRequest.progress;
                        //PLogger.Log_yellow($"{aBLoadProgessEvent.abName}:加载进度：{aBLoadProgessEvent.progress}");
                        eventCenter.EventTrigger(aBLoadProgessEvent);
                        yield return null;
                    }
                    ab = createRequest.assetBundle;
                    if (ab == null)
                    {
                        Debug.LogError($"AssetBundle加载失败\n{fullPath}");
                        callback.Invoke(null);
                        yield break;
                    }
                    break;
            }
            aBLoadProgessEvent.progress = 1;
            aBLoadProgessEvent.isDown = true;
            //PLogger.Log_yellow($"{aBLoadProgessEvent.abName}：{aBLoadProgessEvent.progress}");
            eventCenter.EventTrigger(aBLoadProgessEvent);
            callback(ab);
        }
        #endregion

        #region 通过AssetBundle 获取资源
        /// <summary>
        /// 通过AssetBundle 获取资源
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="bundle"></param>
        /// <param name="assetName"></param>
        /// <returns></returns>
        private async Task<T> LoadAsset<T>(AssetBundle bundle, string assetName) where T : UnityEngine.Object
        {
            AssetBundleRequest abq = bundle.LoadAssetAsync<T>(assetName);
            while (!abq.isDone)
            {
                await Task.Yield();
            }
            return abq.asset as T;
        }

        private async Task<UnityEngine.Object> LoadAsset(AssetBundle bundle, string assetName, System.Type type)
        {
            AssetBundleRequest abq = bundle.LoadAssetAsync(assetName, type);
            while (!abq.isDone)
            {
                await Task.Yield();
            }
            return abq.asset;
        }

        private IEnumerator LoadAsset<T>(AssetBundle bundle, string assetName, UnityAction<T> callback) where T : UnityEngine.Object
        {
            AssetBundleRequest abq = bundle.LoadAssetAsync<T>(assetName);
            yield return abq;
            callback(abq.asset as T);
        }

        private IEnumerator LoadAsset(AssetBundle bundle, string assetName, System.Type type, UnityAction<UnityEngine.Object> callback)
        {
            AssetBundleRequest abq = bundle.LoadAssetAsync(assetName, type);
            yield return abq;
            callback(abq.asset);
        }
        #endregion

        #region 加载指定包以及依赖包
        /// <summary>
        /// 加载指定包以及依赖包
        /// </summary>
        /// <param name="abName"></param>
        private async Task<AssetBundle> LoadDependenciesAndAimBundle(string abName, ABLoadPath loodPath)
        {
            if (mainAB == null || manifest == null)
            {
                Debug.LogError("主包未加载");
                return null;
            };
            //获取依赖包
            string[] strs = manifest.GetAllDependencies(abName);
            for (int i = 0; i < strs.Length; i++)
            {
                if (!abDic.ContainsKey(strs[i]))
                {
                    AssetBundle ab = await LoadAssetBundle(strs[i], loodPath);
                    abDic.Add(strs[i], ab);
                }
            }
            //加载目标包
            if (!abDic.ContainsKey(abName))
            {
                AssetBundle ab = await LoadAssetBundle(abName, loodPath);
                abDic.Add(abName, ab);
            }

            if (abDic[abName] == null)
            {
                abDic.Remove(abName);
                return null;
            }
            return abDic[abName];
        }
        private IEnumerator LoadDependenciesAndAimBundle(string abName, ABLoadPath loodPath, UnityAction<AssetBundle> callback)
        {
            if (mainAB == null || manifest == null)
            {
                Debug.LogError("主包未加载");
                callback(null);
                yield break;
            };
            //获取依赖包
            string[] strs = manifest.GetAllDependencies(abName);
            for (int i = 0; i < strs.Length; i++)
            {
                if (!abDic.ContainsKey(strs[i]))
                {
                    yield return StartCoroutine(LoadAssetBundle(strs[i], loodPath, (arg) =>
                     {
                         abDic.Add(strs[i], arg);
                     }));
                }
            }
            //加载目标包
            if (!abDic.ContainsKey(abName))
            {
                yield return StartCoroutine(LoadAssetBundle(abName, loodPath, (arg) =>
                 {
                     abDic.Add(abName, arg);
                 }));
            }

            if (abDic[abName] == null)
            {
                abDic.Remove(abName);
                callback(null);
                yield break;
            }
            callback(abDic[abName]);
        }

        public async Task<AssetBundle> GetAssetBundle(string abName, ABLoadPath loodPath)
        {
            return await LoadDependenciesAndAimBundle(abName, loodPath);
        }

        public void GetAssetBundle(string abName, ABLoadPath loodPath, UnityAction<AssetBundle> callback)
        {
            Debug.Log($"加载资源{abName}：{loodPath}");
            StartCoroutine(LoadDependenciesAndAimBundle(abName, loodPath, (arg) =>
             {
                 callback(arg);
             }));
        }
        #endregion

        #region 异步加载Assets资源
        /// <summary>
        /// 异步加载资源
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="abName">AB包名</param>
        /// <param name="resName">资源名</param>
        public async Task<T> LoadResAsync<T>(string abName, string resName, ABLoadPath loodPath) where T : UnityEngine.Object
        {
            await LoadDependenciesAndAimBundle(abName, loodPath);
            return await LoadAsset<T>(abDic[abName], resName);
        }

        public async Task<Object> LoadResAsync(string abName, string resName, System.Type type, ABLoadPath loodPath)
        {
            await LoadDependenciesAndAimBundle(abName, loodPath);
            return await LoadAsset(abDic[abName], resName, type);
        }

        public void LoadResAsync<T>(string abName, string resName, ABLoadPath loodPath, UnityAction<T> callBack) where T : Object
        {
            StartCoroutine(ReallyLoadResAsync(abName, resName, loodPath, callBack));
        }
        private IEnumerator ReallyLoadResAsync<T>(string abName, string resName, ABLoadPath loodPath, UnityAction<T> callBack) where T : Object
        {
            yield return StartCoroutine(LoadDependenciesAndAimBundle(abName, loodPath, (arg) =>
             {
                 //目标包和依赖包加载结束，arg是目标包， abDic[abName]也能得到目标包
             }));
            if (abDic[abName] == null)
            {
                callBack(null);
                yield break;
            }
            StartCoroutine(LoadAsset(abDic[abName], resName, callBack));
        }

        public void LoadResAsync(string abName, string resName, System.Type type, ABLoadPath loodPath, UnityAction<Object> callBack)
        {
            StartCoroutine(ReallyLoadResAsync(abName, resName, type, loodPath, callBack));
        }
        public void LoadResAsync(string abName, string resName, ABLoadPath loodPath, UnityAction<Object> callBack)
        {
            StartCoroutine(ReallyLoadResAsync(abName, resName, typeof(Object), loodPath, callBack));
        }
        private IEnumerator ReallyLoadResAsync(string abName, string resName, System.Type type, ABLoadPath loodPath, UnityAction<Object> callBack)
        {
            yield return StartCoroutine(LoadDependenciesAndAimBundle(abName, loodPath, (arg) =>
             {
                 //目标包和依赖包加载结束，arg是目标包， abDic[abName]也能得到目标包
             }));
            if (abDic[abName] == null)
            {
                callBack(null);
                yield break;
            }
            StartCoroutine(LoadAsset(abDic[abName], resName, type, callBack));
        }
        #endregion

        #region 卸载AB包的方法
        /// <summary>
        /// 卸载AB包
        /// </summary>
        /// <param name="name"></param>
        public bool UnLoadAB(string name)
        {
            if (abDic.ContainsKey(name))
            {
                if (abDic[name] == null)
                {
                    Debug.Log("该资源正处于异步加载中，无法卸载！");
                    return false;
                }
                abDic[name].Unload(false);
                abDic.Remove(name);
            }
            return true;
        }
        /// <summary>
        /// 清空AB包的方法
        /// </summary>
        public void ClearAB()
        {
            StopAllCoroutines();
            AssetBundle.UnloadAllAssetBundles(false);
            abDic.Clear();
            //卸载主包
            mainAB = null;
        }
        #endregion

        /// <summary>
        /// 根据字节数得到大小
        /// </summary>
        /// <param name="bytes"></param>
        /// <returns></returns>
        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size = size / 1024;
            }
            return $"{size:0.##} {sizes[order]}";
        }
        
    }
    /// <summary>
    /// AB包加载进度事件
    /// </summary>
    public class ABLoadProgressEvent : EventArgBase
    {
        public string abName;
        public ABLoadPath loadPath;
        public float progress;
        public bool isDown;

        public ABLoadProgressEvent(string abName, ABLoadPath loadPath, float progress, bool isDown)
        {
            this.abName = abName;
            this.loadPath = loadPath;
            this.progress = progress;
            this.isDown = isDown;
        }
    }
}

