using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
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
        public void Init(AssetLoaddingMethod arg0, ABLoadMgr arg2)
        {
            method = arg0;
            abLoadMgr = arg2;
            editorSourcesMgr = EditorSourcesMgr.Instance;
            sceneMgr = SceneMgr.Instance;

            //资源关联数据
            string assetRefpath = assetRefSavePath + assetRefName + assetRefextension;
            //yield return StartCoroutine(LoadTextFileFromStreamingAssets_Async(assetRefpath, (arg) => { }));
            byte[] b = LoadBinaryDataFromStreamingAssets(assetRefpath);
            sourceRef = LoadABSourcesRelated(b);
        }


        /// <summary>
        /// 加载资源 泛型
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="path"></param>
        /// <param name="callback"></param>
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
                        PLoger.LogError("该资源不存在：" + path);
                        return;
                    }
                    ABRelatedArg arg = sourceRef.sourcesDic[path];
                    abLoadMgr.LoadResAsync(arg.packageName, arg.sourceName, callback);
                    break;
            }
        }

        /// <summary>
        /// 加载资源
        /// </summary>
        /// <param name="path"></param>
        /// <param name="type"></param>
        /// <param name="callback"></param>
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
                        PLoger.LogError("该资源不存在：" + path);
                        return;
                    }
                    ABRelatedArg arg = sourceRef.sourcesDic[path];
                    abLoadMgr.LoadResAsync(arg.packageName, arg.sourceName, type, callback);
                    break;
            }
        }

        /// <summary>
        /// 场景加载
        /// </summary>
        /// <param name="path"></param>
        /// <param name="callback"></param>
        public void LoadSceneAsync(string path, UnityAction callback = null)
        {
            LoadSceneAsync(path, LoadSceneMode.Single, callback);
        }
        public void LoadSceneAsync(string path, LoadSceneMode loadSceneMode, UnityAction callback = null)
        {

            if (path.Substring(path.LastIndexOf('/')).Length == 0)
            {
                PLoger.LogError($"路径异常：{path}");
                return;
            }
            string sceneName = path.Substring(path.LastIndexOf('/') + 1);
            sceneName = sceneName.Split('.')[0];
            switch (method)
            {
                case AssetLoaddingMethod.Editor:
                    sceneMgr.LoadSceneAsyn(sceneName, loadSceneMode, callback);
                    break;
                case AssetLoaddingMethod.Assetbundles:
                    if (!sourceRef.sourcesDic.ContainsKey(path))
                    {
                        PLoger.LogError("该资源不存在：" + path);
                        callback?.Invoke();
                        return;
                    }
                    ABRelatedArg arg = sourceRef.sourcesDic[path];
                    abLoadMgr.LoadSceneResAsync(arg.packageName, arg.sourceName, () =>
                    {
                        sceneMgr.LoadSceneAsyn(sceneName, loadSceneMode, callback);
                    });
                    break;
            }
        }

        /// <summary>
        /// 加载程序集 （HybridCLR）
        /// </summary>
        /// <param name="path"></param>
        /// <param name="callback"></param>
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

        /// <summary>
        /// 资源卸载
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public bool UnLoadAB(string path)
        {
            if (!sourceRef.sourcesDic.ContainsKey(path))
            {
                PLoger.LogError("卸载失败,该资源不存在：" + path);
                return false;
            }
            bool isUnload = false;
            if (method == AssetLoaddingMethod.Assetbundles)
            {
                ABRelatedArg arg = sourceRef.sourcesDic[path];
                isUnload = abLoadMgr.UnLoadAB(arg.sourceName);
            }
            else
            {
                isUnload = true;
            }
            return isUnload;
        }

        /// <summary>
        /// 清空AB资源
        /// </summary>
        /// <returns></returns>
        public void ClearAB()
        {
            if (method == AssetLoaddingMethod.Assetbundles)
            {
                abLoadMgr.ClearAB();
            }
        }


        #region StreamingAssets资源获取
        /// <summary>
        /// 加载文本文件
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        private string LoadTextFileFromStreamingAssets(string filePath)
        {
            string fullPath = Application.streamingAssetsPath + filePath;
            if (Application.platform == RuntimePlatform.Android || Application.platform == RuntimePlatform.WebGLPlayer)
            {
                using (var request = UnityWebRequest.Get(fullPath))
                {
                    request.SendWebRequest();

                    while (!request.isDone)
                    {
                        // 等待文件加载完成
                    }

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        return request.downloadHandler.text;
                    }
                    else
                    {
                        Debug.LogError("Failed to load file from Android StreamingAssets: " + request.error);
                        return null;
                    }
                }
            }
            else
            {
                return File.ReadAllText(fullPath);
            }

        }

        private IEnumerator LoadTextFileFromStreamingAssets_Async(string filePath, UnityAction<string> callback)
        {
            string fullPath = Application.streamingAssetsPath + filePath;
            string text = null;
            if (Application.platform == RuntimePlatform.Android ||
                Application.platform == RuntimePlatform.WebGLPlayer)
            {
                using (UnityWebRequest request = UnityWebRequest.Get(fullPath))
                {
                    request.SendWebRequest();
                    yield return request;
                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        text = request.downloadHandler.text;
                    }
                    else
                    {
                        PLoger.LogError("Failed to load file from WebGL StreamingAssets: " + request.error);
                    }
                }
            }
            else
            {
                try
                {
                    text = File.ReadAllText(fullPath);
                }
                catch (Exception e)
                {
                    PLoger.LogError($"Failed to read binary data at {fullPath}: {e.Message}");
                }
            }
            callback?.Invoke(text);

        }

        /// <summary>
        /// 加载二进制文件
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        private byte[] LoadBinaryDataFromStreamingAssets(string filePath)
        {
            string fullPath = Application.streamingAssetsPath + filePath;
            if (Application.platform == RuntimePlatform.Android || Application.platform == RuntimePlatform.WebGLPlayer)
            {
                using (var request = UnityWebRequest.Get(fullPath))
                {
                    request.SendWebRequest();

                    while (!request.isDone)
                    {
                        // 等待文件加载完成
                    }

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        return request.downloadHandler.data;
                    }
                    else
                    {
                        Debug.LogError("Failed to load file from Android StreamingAssets: " + request.error);
                        return null;
                    }
                }
            }
            else
            {
                try
                {
                    return File.ReadAllBytes(fullPath);
                }
                catch (Exception e)
                {
                    PLoger.LogError($"Failed to read binary data at {fullPath}: {e.Message}");
                    return null;
                }
            }

        }

        public IEnumerator LoadBinaryDataFromStreamingAssets_Async(string filePath, UnityAction<byte[]> callback)
        {
            string fullPath = Application.streamingAssetsPath + filePath;
            byte[] bytes = null;
            if (Application.platform == RuntimePlatform.Android ||
                Application.platform == RuntimePlatform.WebGLPlayer)
            {
                using (UnityWebRequest www = UnityWebRequest.Get(fullPath))
                {
                    www.downloadHandler = new DownloadHandlerBuffer();
                    var operation = www.SendWebRequest();
                    yield return operation;

                    if (www.result != UnityWebRequest.Result.Success)
                    {
                        PLoger.Log($"Failed to load binary data at {fullPath}: {www.error}");
                    }
                    else
                    {
                        bytes = www.downloadHandler.data;
                    }
                }
            }
            else
            {
                try
                {
                    bytes = File.ReadAllBytes(filePath);
                }
                catch (Exception e)
                {
                    PLoger.LogError($"Failed to read binary data at {fullPath}: {e.Message}");
                }
            }
            callback?.Invoke(bytes);
        }

        #endregion
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

