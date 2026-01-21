using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace UPandaGF
{
    /// <summary>
    /// 资源加载封装
    /// </summary>
    public class AssetsLoader : MonoBehaviour, IAssetsLoader
    {
        private AssetLoaddingMethod method;
        private ABLoadMgr abLoadMgr;
        private EditorSourcesMgr editorSourcesMgr;
        private SceneMgr sceneMgr;
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
        public async Task Init(AssetLoaddingMethod arg0, ABLoadMgr arg2)
        {
            method = arg0;
            abLoadMgr = arg2;
            editorSourcesMgr = EditorSourcesMgr.Instance;
            sceneMgr = SceneMgr.Instance;
            PLogger.Log("AssetsLoader init");
            //资源关联数据
            string assetRefpath = assetRefSavePath + assetRefName + assetRefextension;
            byte[] b = await StreamingAssetsLoader.LoadBinaryDataAsync(assetRefpath);
            sourceRef = LoadABSourcesRelated(b);
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
                    if (!sourceRef.sourcesDic.ContainsKey(path))
                    {
                        PLogger.LogError("该资源不存在：" + path);
                    }
                    ABRelatedArg arg = sourceRef.sourcesDic[path];
                    asset = await abLoadMgr.LoadResAsync<T>(arg.packageName, arg.sourceName, arg.loodPath);
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
                    if (!sourceRef.sourcesDic.ContainsKey(path))
                    {
                        PLogger.LogError("该资源不存在：" + path);
                    }
                    ABRelatedArg arg = sourceRef.sourcesDic[path];
                    asset = await abLoadMgr.LoadResAsync(arg.packageName, arg.sourceName, type, arg.loodPath);
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
                    if (!sourceRef.sourcesDic.ContainsKey(path))
                    {
                        PLogger.LogError("该资源不存在：" + path);
                        return;
                    }
                    ABRelatedArg arg = sourceRef.sourcesDic[path];
                    abLoadMgr.LoadResAsync(arg.packageName, arg.sourceName, arg.loodPath, callback);
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
                    if (!sourceRef.sourcesDic.ContainsKey(path))
                    {
                        PLogger.LogError("该资源不存在：" + path);
                        return;
                    }
                    ABRelatedArg arg = sourceRef.sourcesDic[path];
                    abLoadMgr.LoadResAsync(arg.packageName, arg.sourceName, type, arg.loodPath, callback);
                    break;
            }
        }


        public void LoadSceneAsync(string path, UnityAction assetLoadComplete = null, UnityAction sceneLoadComplete = null)
        {
            LoadSceneAsync(path, LoadSceneMode.Single, assetLoadComplete, sceneLoadComplete);
        }
        public void LoadSceneAsync(string path, LoadSceneMode loadSceneMode, UnityAction assetLoadComplete = null, UnityAction sceneLoadComplete = null)
        {

            if (path.Substring(path.LastIndexOf('/')).Length == 0)
            {
                PLogger.LogError($"路径异常：{path}");
                return;
            }
            string sceneName = path.Substring(path.LastIndexOf('/') + 1);
            sceneName = sceneName.Split('.')[0];
            switch (method)
            {
                case AssetLoaddingMethod.Editor:
                    assetLoadComplete?.Invoke();
                    sceneMgr.LoadSceneAsyn(sceneName, loadSceneMode, sceneLoadComplete);
                    break;
                case AssetLoaddingMethod.Assetbundles:
                    if (!sourceRef.sourcesDic.ContainsKey(path))
                    {
                        PLogger.LogError("该资源不存在：" + path);
                        assetLoadComplete?.Invoke();
                        return;
                    }
                    ABRelatedArg arg = sourceRef.sourcesDic[path];
                    abLoadMgr.GetAssetBundle(arg.packageName, arg.loodPath, (bundle) =>
                    {
                        assetLoadComplete?.Invoke();
                        sceneMgr.LoadSceneAsyn(sceneName, loadSceneMode, sceneLoadComplete);
                    });
                    break;
            }
        }

        public void LoadAssemblyAsync(string path, UnityAction<Assembly> callback)
        {
            Assembly hotUpdateAss = null;
#if !UNITY_EDITOR
            LoadAsync<TextAsset>(path, (dllAsset) =>
            {
                hotUpdateAss = Assembly.Load(dllAsset.bytes);
                callback?.Invoke(hotUpdateAss);
            });
#else
            // Editor下无需加载，直接查找获得HotUpdate程序集
            string AssemblyName = Path.GetFileName(path).Split('.')[0];
            hotUpdateAss = System.AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == AssemblyName);
            callback?.Invoke(hotUpdateAss);
#endif
        }

        public async Task<Assembly> LoadAssemblyAsync(string path)
        {
            Assembly hotUpdateAss = null;
#if !UNITY_EDITOR
            TextAsset dllAsset = await LoadAsync<TextAsset>(path);
            hotUpdateAss = Assembly.Load(dllAsset.bytes);
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

        public void ClearAB()
        {
            if (method == AssetLoaddingMethod.Assetbundles)
            {
                abLoadMgr.ClearAB();
            }
        }

        private ABSourcesRelated LoadABSourcesRelated(byte[] bytes)
        {
            ABSourcesRelated obj = null;
            string AESKEY = "111a222aaabbbccc";
            string AESIV = "111b222aaabbbccc";
            bytes = AESEncryption.AESDecrypt(bytes, AESKEY, AESIV);
            using (MemoryStream ms = new MemoryStream(bytes))
            {
                BinaryFormatter bf = new BinaryFormatter();
                obj = bf.Deserialize(ms) as ABSourcesRelated;
                ms.Close();
            }
            return obj;
        }

    }
}

