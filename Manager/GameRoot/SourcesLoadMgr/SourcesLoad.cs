using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;

namespace UPandaGF
{
    /// <summary>
    /// 资源加载封装
    /// </summary>
    public class SourcesLoad : BaseMonoSingleton<SourcesLoad>
    {
        private AssetLoaddingMethod method;
        private ABLoadMgr abLoadMgr;
        private EditorSourcesMgr editorSourcesMgr;
        private SceneMgr sceneMgr;
        private ABSourcesRelated sourceRef;
        public void Init(AssetLoaddingMethod arg0, ABSourcesRelated arg1, ABLoadMgr arg2)
        {
            method = arg0;
            sourceRef = arg1;
            abLoadMgr = arg2;
            editorSourcesMgr = EditorSourcesMgr.Instance;
            sceneMgr = SceneMgr.Instance;
        }
        /// <summary>
        /// 加载资源 泛型
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="path"></param>
        /// <param name="callback"></param>
        public void LoadAsync<T>(string path, UnityAction<T> callback) where T : Object
        {
            if (!sourceRef.sourcesDic.ContainsKey(path))
            {
                PLoger.LogError("该资源不存在：" + path + "\n请检查资源关联数据是否更新");
                return;
            }

            switch (method)
            {
                case AssetLoaddingMethod.Editor:
                    callback?.Invoke(editorSourcesMgr.Load<T>(path));
                    break;
                case AssetLoaddingMethod.Assetbundles:
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
            if (!sourceRef.sourcesDic.ContainsKey(path))
            {
                PLoger.LogError("该资源不存在：" + path);
                return;
            }

            switch (method)
            {
                case AssetLoaddingMethod.Editor:
                    callback?.Invoke(editorSourcesMgr.Load(path, type));
                    break;
                case AssetLoaddingMethod.Assetbundles:
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
            if (!sourceRef.sourcesDic.ContainsKey(path))
            {
                PLoger.LogError("该资源不存在：" + path);
                return;
            }
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
                    sceneMgr.LoadSceneAsyn(sceneName, callback);
                    break;
                case AssetLoaddingMethod.Assetbundles:
                    ABRelatedArg arg = sourceRef.sourcesDic[path];
                    abLoadMgr.LoadSceneResAsync(arg.packageName, arg.sourceName, () =>
                    {
                        sceneMgr.LoadSceneAsyn(sceneName, callback);
                    });
                    break;
            }
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


        /// <summary>
        /// 获取StreamingAssets的路径（适用于所有平台）
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>
        public static string GetStreamingAssetsPath(string fileName)
        {
            string path = "";

            // 判断平台类型，获取对应的路径
            if (Application.platform == RuntimePlatform.Android)
            {
                // Android平台上，StreamingAssets路径是通过jar包访问的
                path = "jar:file://" + Application.dataPath + "!/assets/" + fileName;
            }
            else if (Application.platform == RuntimePlatform.IPhonePlayer)
            {
                // iOS平台，StreamingAssets在应用包的根目录
                path = "file://" + Path.Combine(Application.dataPath, "Raw") + "/" + fileName;
            }
            else
            {
                // 其他平台（Windows, Mac, Linux, WebGL等），可以直接使用Application.streamingAssetsPath
                path = Application.streamingAssetsPath + "/" +fileName;
            }

#if UNITY_EDITOR
            path = Application.streamingAssetsPath + "/" + fileName;
#endif
            return path;
        }

        // 加载文件内容
        public static string LoadTextFile(string fileName)
        {
            string path = GetStreamingAssetsPath(fileName);

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

        private static string LoadTextFileFromWebRequest(string path)
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
            string path = GetStreamingAssetsPath(fileName);
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
    }
}

