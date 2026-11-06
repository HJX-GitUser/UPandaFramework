using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

#pragma warning disable CS0618 // SceneMgr 是旧业务兼容层，本类仍承担旧路径适配

namespace UPandaGF
{
    /// <summary>
    /// 资源加载封装
    /// </summary>
    public class AssetsLoader : MonoBehaviour, IAssetsLoader, ISceneAssetProvider
    {
        private int initializationGeneration;
        private bool initialized;
        public bool IsInitialized => initialized && (method != AssetLoaddingMethod.Assetbundles || (abLoadMgr != null && abLoadMgr.IsInitialized));
        private AssetLoaddingMethod method;
        private ABLoadMgr abLoadMgr;
        internal ABLoadMgr BundleLoader => abLoadMgr;
        private EditorSourcesMgr editorSourcesMgr;
        private SceneMgr sceneMgr;
        private ISceneService sceneService;
        public void ConfigureScenes(ISceneService service) { sceneService = service; }
        private ABSourcesRelated sourceRef;

        /// <summary>
        /// 资源关联数据存储的位置
        /// </summary>
        private string assetRefSavePath = "/Data/";
        /// <summary>
        /// 资源关联数据文件名
        /// </summary>
        private string assetRefName = "assetData";
        /// <summary>
        /// 资源关联存储文件后缀
        /// </summary>
        private string assetRefextension = ".assetref";

        [HideInInspector]
        public AssetBundleClassificationWindowConfig AssetAESConfig;

        public async Task Init(AssetLoaddingMethod arg0, string remoteURL, string LoadAssetPath, ABSourcesRelated sourceRefArgBytes)
        {
            int epoch = ++initializationGeneration;
            PLogger.Log("AssetsLoader Init");
            initialized = false;
            method = arg0;
            sourceRef = null;
            editorSourcesMgr = EditorSourcesMgr.Instance;
            sceneMgr = SceneMgr.Instance;
            if (method == AssetLoaddingMethod.Editor)
            {
#if !UNITY_EDITOR
                throw new System.InvalidOperationException("Player 中不能使用 Editor 资源加载模式。");
#else
                initialized = true;
                return;
#endif
            }
            if (sourceRefArgBytes != null)
            {
                sourceRef = sourceRefArgBytes;
            }
            else
            {
                //资源关联数据
                string assetRefpath = assetRefSavePath + assetRefName + assetRefextension;
                byte[] b = await StreamingAssetsLoader.LoadBinaryDataAsync(assetRefpath);
                if (epoch != initializationGeneration || this == null) return;
                if (b != null) sourceRef = LoadABSourcesRelated(b);
            }


            if (method == AssetLoaddingMethod.Assetbundles)
            {
                if (sourceRef == null)
                {
                    PLogger.LogError("资源关联数据 assetData.assetref 缺失或反序列化失败，无法以 AssetBundle 方式加载资源");
                    return;
                }
                if (sourceRef.mainBundleInfo == null)
                {
                    PLogger.LogError("资源关联数据缺少主包信息 mainBundleInfo");
                    return;
                }
                abLoadMgr = ABLoadMgr.Instance;
                abLoadMgr.remoteURL = remoteURL;
                PLogger.Log($"主包：{sourceRef.mainBundleInfo.bundleName}，加载方式：{sourceRef.mainBundleInfo.loadPath}");
                await abLoadMgr.Init(LoadAssetPath, sourceRef.mainBundleInfo.bundleName, sourceRef.mainBundleInfo.loadPath);
                if (epoch != initializationGeneration || this == null || !abLoadMgr.IsInitialized) return;
                abLoadMgr.SetABSourcesRelated(sourceRef);
            }
            initialized = true;
            PLogger.Log("AssetsLoader Inited !!!");
        }

        public async Task<AssetLease<T>> AcquireAsync<T>(string path) where T : UnityEngine.Object
        {
            if (!IsInitialized) throw new System.InvalidOperationException("资源服务尚未就绪。");
            if (method == AssetLoaddingMethod.Editor)
            {
                var asset = editorSourcesMgr.Load<T>(path);
                // AssetDatabase owns editor assets; the lease still has explicit lifetime.
                return asset != null ? new AssetLease<T>(asset, null) : null;
            }
            AssetRelatedArg arg;
            if (!TryGetAssetArg(path, out arg)) return null;
            return await abLoadMgr.AcquireAsync<T>(arg.bundleName, arg.sourceName, sourceRef.GetABLoadPath(arg));
        }
        public async Task<IDisposable> PrepareSceneAsync(string path, System.Threading.CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!IsInitialized) throw new System.InvalidOperationException("资源服务尚未就绪。");
            if (method == AssetLoaddingMethod.Editor) return null;
            AssetRelatedArg arg;
            if (!sourceRef.sourcesDic.ContainsKey(path))
            {
                var matches = sourceRef.sourcesDic.Keys.Where(key => key.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)
                    && Path.GetFileNameWithoutExtension(key) == path).ToArray();
                if (matches.Length != 1) throw new ArgumentException("场景名称未登记或存在重名，请使用完整路径：" + path);
                path = matches[0];
            }
            if (!TryGetAssetArg(path, out arg)) throw new System.ArgumentException("场景资源未登记：" + path);
            var lease = await AssetService.WaitForLeaseAsync(abLoadMgr.AcquireBundleAsync(arg.bundleName, sourceRef.GetABLoadPath(arg)), token);
            if (lease == null) throw new System.IO.IOException("场景资源包加载失败：" + path);
            return lease;
        }

        public async Task<T> LoadAsync<T>(string path) where T : UnityEngine.Object
        {
            T asset = null;
            switch (method)
            {
                case AssetLoaddingMethod.Editor:
                    asset = editorSourcesMgr.Load<T>(path);
                    break;
                case AssetLoaddingMethod.Assetbundles:
                    AssetRelatedArg arg;
                    if (!TryGetAssetArg(path, out arg))
                        break;
                    asset = await abLoadMgr.LoadResAsync<T>(arg.bundleName, arg.sourceName, sourceRef.GetABLoadPath(arg));
                    break;
            }
            return asset;
        }

        public async Task<UnityEngine.Object> LoadAsync(string path, System.Type type)
        {
            UnityEngine.Object asset = null;
            switch (method)
            {
                case AssetLoaddingMethod.Editor:
                    asset = editorSourcesMgr.Load(path, type);
                    break;
                case AssetLoaddingMethod.Assetbundles:
                    AssetRelatedArg arg;
                    if (!TryGetAssetArg(path, out arg))
                        break;
                    asset = await abLoadMgr.LoadResAsync(arg.bundleName, arg.sourceName, type, sourceRef.GetABLoadPath(arg));
                    break;
            }
            return asset;
        }

        public void LoadAsync<T>(string path, UnityAction<T> callback) where T : UnityEngine.Object
        {
            switch (method)
            {
                case AssetLoaddingMethod.Editor:
                    callback?.Invoke(editorSourcesMgr.Load<T>(path));
                    break;
                case AssetLoaddingMethod.Assetbundles:
                    AssetRelatedArg arg;
                    if (!TryGetAssetArg(path, out arg))
                    {
                        callback?.Invoke(null);
                        return;
                    }
                    abLoadMgr.LoadResAsync(arg.bundleName, arg.sourceName, sourceRef.GetABLoadPath(arg), callback);
                    break;
            }
        }

        public void LoadAsync(string path, System.Type type, UnityAction<UnityEngine.Object> callback)
        {
            switch (method)
            {
                case AssetLoaddingMethod.Editor:
                    callback?.Invoke(editorSourcesMgr.Load(path, type));
                    break;
                case AssetLoaddingMethod.Assetbundles:
                    AssetRelatedArg arg;
                    if (!TryGetAssetArg(path, out arg))
                    {
                        callback?.Invoke(null);
                        return;
                    }
                    abLoadMgr.LoadResAsync(arg.bundleName, arg.sourceName, type, sourceRef.GetABLoadPath(arg), callback);
                    break;
            }
        }


        public void LoadSceneAsync(string path, UnityAction assetLoadComplete = null, UnityAction sceneLoadComplete = null)
        {
            LoadSceneAsync(path, LoadSceneMode.Single, assetLoadComplete, sceneLoadComplete);
        }
        public void LoadSceneAsync(string path, LoadSceneMode loadSceneMode, UnityAction assetLoadComplete = null, UnityAction sceneLoadComplete = null)
        {
            if (sceneService != null)
            {
                _ = LoadManagedSceneAsync(path, loadSceneMode, assetLoadComplete, sceneLoadComplete);
                return;
            }
            if (string.IsNullOrEmpty(path))
            {
                PLogger.LogError($"场景路径为空：{path}");
                return;
            }
            string sceneName = Path.GetFileNameWithoutExtension(path);
            switch (method)
            {
                case AssetLoaddingMethod.Editor:
                    assetLoadComplete?.Invoke();
                    sceneMgr.LoadSceneAsyn(sceneName, loadSceneMode, sceneLoadComplete);
                    break;
                case AssetLoaddingMethod.Assetbundles:
                    AssetRelatedArg arg;
                    if (!TryGetAssetArg(path, out arg))
                    {
                        assetLoadComplete?.Invoke();
                        return;
                    }
                    LoadSceneAssetBundle(arg, path, sceneName, loadSceneMode, assetLoadComplete, sceneLoadComplete);
                    break;
            }
        }

        private static void InvokeSceneCallback(UnityAction callback)
        {
            if (callback == null) return;
            foreach (UnityAction action in callback.GetInvocationList())
                try { action(); } catch (Exception e) { Debug.LogException(e); }
        }
        private async Task LoadManagedSceneAsync(string path, LoadSceneMode mode, UnityAction prepared, UnityAction completed)
        {
            bool notified = false;
            try
            {
                await sceneService.LoadAsync(path, mode, progress: progress => {
                    if (!notified && progress.Phase == SceneTransitionPhase.Loading)
                    { notified = true; InvokeSceneCallback(prepared); }
                });
                InvokeSceneCallback(completed);
            }
            catch (System.OperationCanceledException) { }
            catch (System.Exception e) { Debug.LogException(e); }
        }

        /// <summary>
        /// 加载场景所在 AssetBundle，就绪后异步加载场景
        /// </summary>
        private void LoadSceneAssetBundle(AssetRelatedArg arg, string scenePath, string sceneName, LoadSceneMode loadSceneMode, UnityAction assetLoadComplete, UnityAction sceneLoadComplete)
        {
            abLoadMgr.GetAssetBundle(arg.bundleName, sourceRef.GetABLoadPath(arg), (bundle) =>
            {
                if (bundle == null)
                {
                    PLogger.LogError("场景所在 AssetBundle 加载失败：" + scenePath);
                    assetLoadComplete?.Invoke();
                    return;
                }
                assetLoadComplete?.Invoke();
                sceneMgr.LoadSceneAsyn(sceneName, loadSceneMode, sceneLoadComplete);
            });
        }

        public void LoadAssemblyAsync(string path, UnityAction<Assembly> callback)
        {
            Assembly hotUpdateAss = null;
            if (string.IsNullOrEmpty(path))
            {
                PLogger.LogError("程序集路径为空");
                callback?.Invoke(null);
                return;
            }
#if !UNITY_EDITOR
            LoadAsync<TextAsset>(path, (dllAsset) =>
            {
                if (dllAsset == null)
                {
                    PLogger.LogError("程序集文件加载失败：" + path);
                    callback?.Invoke(null);
                    return;
                }
                try
                {
                    hotUpdateAss = Assembly.Load(dllAsset.bytes);
                }
                catch (System.Exception e)
                {
                    PLogger.LogError($"加载程序集 {path} 失败：{e}");
                }
                callback?.Invoke(hotUpdateAss);
            });
#else
            // Editor下无需加载，直接查找获得HotUpdate程序集
            string AssemblyName = Path.GetFileName(path).Split('.')[0];
            try
            {
                hotUpdateAss = System.AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == AssemblyName);
            }
            catch (System.Exception e)
            {
                PLogger.LogError($"{AssemblyName}\n{e}");
            }
            callback?.Invoke(hotUpdateAss);
#endif
        }

        public async Task<Assembly> LoadAssemblyAsync(string path)
        {
            Assembly hotUpdateAss = null;
            if (string.IsNullOrEmpty(path))
            {
                PLogger.LogError("程序集路径为空");
                return null;
            }
#if !UNITY_EDITOR
            TextAsset dllAsset = await LoadAsync<TextAsset>(path);
            if (dllAsset == null)
            {
                PLogger.LogError("程序集文件加载失败：" + path);
                return null;
            }
            try
            {
                hotUpdateAss = Assembly.Load(dllAsset.bytes);
            }
            catch (System.Exception e)
            {
                PLogger.LogError($"加载程序集 {path} 失败：{e}");
            }
#else
            // Editor下无需加载，直接查找获得HotUpdate程序集
            string AssemblyName = Path.GetFileName(path).Split('.')[0];
            try
            {
                hotUpdateAss = System.AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == AssemblyName);
            }
            catch (System.Exception e)
            {
                PLogger.LogError($"{AssemblyName}\n{e}");
            }
#endif
            return hotUpdateAss;
        }

        public bool UnLoadAB(string abName)
        {
            bool isUnload = false;
            if (method == AssetLoaddingMethod.Assetbundles)
            {
                isUnload = abLoadMgr.UnLoadAB(abName);
            }
            else
            {
                isUnload = true;
            }
            return isUnload;
        }

        public void Shutdown()
        {
            initializationGeneration++;
            initialized = false;
            sceneService = null;
            if (abLoadMgr != null) abLoadMgr.ClearAB();
            abLoadMgr = null;
            sourceRef = null;
        }
        private void OnDestroy() { Shutdown(); }

        public void ClearAB()
        {
            initializationGeneration++;
            if (method == AssetLoaddingMethod.Assetbundles) initialized = false;
            if (method == AssetLoaddingMethod.Assetbundles)
            {
                if (abLoadMgr != null) abLoadMgr.ClearAB();
            }
        }

        /// <summary>
        /// 根据资源路径查询资源加载参数；资源不存在时记录错误并返回 false
        /// </summary>
        private bool TryGetAssetArg(string path, out AssetRelatedArg arg)
        {
            arg = null;
            if (IsInitialized && !string.IsNullOrEmpty(path) && sourceRef != null
                && sourceRef.sourcesDic != null && sourceRef.sourcesDic.TryGetValue(path, out arg)
                && arg != null && sourceRef.bundleInfo != null && sourceRef.bundleInfo.ContainsKey(arg.bundleName))
                return true;
            PLogger.LogError("该资源不存在：" + path);
            return false;
        }

        public ABSourcesRelated LoadABSourcesRelated(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                PLogger.LogError("资源关联数据为空，无法反序列化");
                return null;
            }
            try
            {
                return AssetReferenceSerializer.Decode(bytes, AssetAESConfig);
            }
            catch (System.Exception e)
            {
                PLogger.LogError($"资源关联数据反序列化失败：{e}");
                return null;
            }
        }

    }
}

#pragma warning restore CS0618

