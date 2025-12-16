using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Policy;
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
        [HideInInspector]
        public bool IsLoadedOnStreammingAssets = true;
        //主包
        private AssetBundle mainAB = null;
        //主包依赖获取配置文件
        private AssetBundleManifest manifest = null;

        //存储 AB包对象(AB包不能够重复加载 否则会报错)
        private Dictionary<string, AssetBundle> abDic = new Dictionary<string, AssetBundle>();

        /// <summary>
        /// 获取AB包加载路径
        /// </summary>
        [HideInInspector]
        public string PathUrl;

        /// <summary>
        /// 主包名 根据平台不同 包名不同
        /// </summary>
        [HideInInspector]
        public string MainName;

        #region 初始化

        public async Task Init(bool isLoadedOnStreammingAssets, string pathUrl, string mainName)
        {
            isInit = true;
            IsLoadedOnStreammingAssets = isLoadedOnStreammingAssets;
            PathUrl = pathUrl;
            MainName = mainName;
            mainAB = await LoadAssetBundle(MainName);
            manifest = mainAB.LoadAsset<AssetBundleManifest>("AssetBundleManifest");
        }

        private void Start()
        {
            if (!isInit)
            {
                isInit = true;
                if (PathUrl == null || PathUrl == "")
                {
                    PathUrl = Application.streamingAssetsPath + "/AB/PC/";
                    IsLoadedOnStreammingAssets = true;
                }
                if (MainName == null || MainName == "")
                {
                    MainName = "PC";
                }
                LoadMainAssetBundleManifest();
            }
        }

        /// <summary>
        /// 加载主包 和 配置文件
        /// 加载所有包是 通过它才能得到依赖信息
        /// </summary>
        private async void LoadMainAssetBundleManifest()
        {
            mainAB = await LoadAssetBundle(MainName);
            manifest = mainAB.LoadAsset<AssetBundleManifest>("AssetBundleManifest");
            Debug.Log($"AB包加载路径：{PathUrl}\n资源包主包：{MainName}");
        }

        #endregion

        #region 加载AssetBundle
        /// <summary>
        /// 加载AssetBundle
        /// </summary>
        /// <param name="abName"></param>
        /// <returns></returns>
        private async Task<AssetBundle> LoadAssetBundle(string abName)
        {
            string fullPath = Path.Combine(PathUrl, abName);
            AssetBundle ab = null;
            try
            {
                if (IsLoadedOnStreammingAssets)
                {
                    // 对于StreamingAssets路径（特别是WebGL和Android），使用UnityWebRequest
                    using (UnityWebRequest webRequest = UnityWebRequestAssetBundle.GetAssetBundle(fullPath))
                    {
                        webRequest.SendWebRequest();
                        while (!webRequest.isDone)
                        {
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
                }
                else
                {
                    // 对于本地文件路径，使用LoadFromFileAsync
                    AssetBundleCreateRequest createRequest = AssetBundle.LoadFromFileAsync(fullPath);
                    if (createRequest == null)
                    {
                        Debug.LogError($"AssetBundle加载失败\n{fullPath}");
                        return null;
                    }
                    while (!createRequest.isDone)
                    {
                        await System.Threading.Tasks.Task.Yield();
                    }
                    ab = createRequest.assetBundle;
                    if (ab == null)
                    {
                        Debug.LogError($"AssetBundle加载失败\n{fullPath}");
                    }
                }
                return ab;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"AssetBundle加载失败 特殊异常 '{abName}'\n{e.Message}");
                return null;
            }
        }

        private IEnumerator LoadAssetBundle(string abName, UnityAction<AssetBundle> callback)
        {
            string fullPath = Path.Combine(PathUrl, abName);
            AssetBundle ab = null;
            if (IsLoadedOnStreammingAssets)
            {
                // 对于StreamingAssets路径（特别是WebGL和Android），使用UnityWebRequest
                using (UnityWebRequest webRequest = UnityWebRequestAssetBundle.GetAssetBundle(fullPath))
                {
                    yield return webRequest.SendWebRequest();
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
            }
            else
            {
                // 对于本地文件路径，使用LoadFromFileAsync
                AssetBundleCreateRequest createRequest = AssetBundle.LoadFromFileAsync(fullPath);
                if (createRequest == null)
                {
                    Debug.LogError($"AssetBundle加载失败\n{fullPath}");
                    callback.Invoke(null);
                    yield break;
                }
                yield return createRequest;
                ab = createRequest.assetBundle;
                if (ab == null)
                {
                    Debug.LogError($"AssetBundle加载失败\n{fullPath}");
                    callback.Invoke(null);
                    yield break;
                }
            }
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
        private async Task<AssetBundle> LoadDependenciesAndAimBundle(string abName)
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
                    AssetBundle ab = await LoadAssetBundle(strs[i]);
                    abDic.Add(strs[i], ab);
                }
            }
            //加载目标包
            if (!abDic.ContainsKey(abName))
            {
                AssetBundle ab = await LoadAssetBundle(abName);
                abDic.Add(abName, ab);
            }

            if (abDic[abName] == null)
            {
                abDic.Remove(abName);
                return null;
            }
            return abDic[abName];
        }
        private IEnumerator LoadDependenciesAndAimBundle(string abName, UnityAction<AssetBundle> callback)
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
                    yield return StartCoroutine(LoadAssetBundle(strs[i], (arg) =>
                    {
                        abDic.Add(strs[i], arg);
                    }));
                }
            }
            //加载目标包
            if (!abDic.ContainsKey(abName))
            {
                yield return StartCoroutine(LoadAssetBundle(abName, (arg) =>
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

        public async Task<AssetBundle> GetAssetBundle(string abName)
        {
            return await LoadDependenciesAndAimBundle(abName);
        }

        public void GetAssetBundle(string abName, UnityAction<AssetBundle> callback)
        {
            StartCoroutine(LoadDependenciesAndAimBundle(abName, (arg) =>
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
        public async Task<T> LoadResAsync<T>(string abName, string resName) where T : UnityEngine.Object
        {
            await LoadDependenciesAndAimBundle(abName);
            return await LoadAsset<T>(abDic[abName], resName);
        }

        public async Task<Object> LoadResAsync(string abName, string resName, System.Type type)
        {
            await LoadDependenciesAndAimBundle(abName);
            return await LoadAsset(abDic[abName], resName, type);
        }

        public void LoadResAsync<T>(string abName, string resName, UnityAction<T> callBack) where T : Object
        {
            StartCoroutine(ReallyLoadResAsync(abName, resName, callBack));
        }
        private IEnumerator ReallyLoadResAsync<T>(string abName, string resName, UnityAction<T> callBack) where T : Object
        {
            yield return StartCoroutine(LoadDependenciesAndAimBundle(abName, (arg) =>
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

        public void LoadResAsync(string abName, string resName, System.Type type, UnityAction<Object> callBack)
        {
            StartCoroutine(ReallyLoadResAsync(abName, resName, type, callBack));
        }
        public void LoadResAsync(string abName, string resName, UnityAction<Object> callBack)
        {
            StartCoroutine(ReallyLoadResAsync(abName, resName, typeof(Object), callBack));
        }
        private IEnumerator ReallyLoadResAsync(string abName, string resName, System.Type type, UnityAction<Object> callBack)
        {
            yield return StartCoroutine(LoadDependenciesAndAimBundle(abName, (arg) =>
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
    }
}

