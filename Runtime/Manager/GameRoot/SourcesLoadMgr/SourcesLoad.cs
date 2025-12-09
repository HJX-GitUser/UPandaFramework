using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace UPandaGF
{
    /// <summary>
    /// 资源加载封装
    /// </summary>
    public class SourcesLoad : MonoBehaviour, ISourcesLoad
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
            string assetRefpath = Application.streamingAssetsPath + assetRefSavePath + assetRefName + assetRefextension;
            sourceRef = LoadABSourcesRelated(assetRefpath);
        }


        /// <summary>
        /// 加载资源 泛型
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="path"></param>
        /// <param name="callback"></param>
        public void LoadAsync<T>(string path, UnityAction<T> callback) where T : Object
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
        public void LoadAsync(string path, System.Type type, UnityAction<Object> callback)
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
        /// 获取StreamingAssets的路径（适用于所有平台）
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>

        /// <summary>
        /// 加载文件内容
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>
        public string LoadTextFileFromStreamingAssets(string fileName)
        {
            string path = UPGameRoot.GetStreamingAssetsPath() + "/" + fileName;

            // 判断平台是否是Android
            if (Application.platform == RuntimePlatform.Android || Application.platform == RuntimePlatform.WebGLPlayer)
            {
                return LoadTextFileFromWebRequest(path);
            }
            else
            {
                // 其他平台，直接读取文件
                return File.ReadAllText(path);
            }
        }

        private string LoadTextFileFromWebRequest(string path)
        {
            using (var request = UnityWebRequest.Get(path))
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

        // 加载文件内容 异步
        public IEnumerator LoadTextFileAsync(string fileName, UnityAction<string> callback)
        {
            string path = UPGameRoot.GetStreamingAssetsPath() + "/" + fileName;
            yield return StartCoroutine(LoadTextFileFromAsync(fileName, callback));
        }
        //加载文本文件
        private IEnumerator LoadTextFileFromAsync(string path, UnityAction<string> callback)
        {
            using (UnityWebRequest request = UnityWebRequest.Get(path))
            {
                request.SendWebRequest();

                yield return request;

                if (request.result == UnityWebRequest.Result.Success)
                {
                    callback?.Invoke(request.downloadHandler.text);
                }
                else
                {
                    Debug.LogError("Failed to load file from WebGL StreamingAssets: " + request.error);
                    callback?.Invoke(null);
                }
            }
        }
        #endregion

        private ABSourcesRelated LoadABSourcesRelated(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return null;
            }
            ABSourcesRelated obj = null;


            byte[] bytes = File.ReadAllBytes(filePath);
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

