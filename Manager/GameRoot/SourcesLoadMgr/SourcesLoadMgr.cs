using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace UPandaGF
{
    public class SourcesLoadMgr : BaseMonoSingleton<SourcesLoadMgr>
    {
        private AssetLoaddingMethod method;
        private ABLoadMgr abLoadMgr;
        private EditorSourcesMgr editorSourcesMgr;
        private ABSourcesRef sourceRef;
        public void Init(AssetLoaddingMethod arg0, ABSourcesRef arg1, ABLoadMgr arg2, EditorSourcesMgr arg3)
        {
            method = arg0;
            sourceRef = arg1;
            abLoadMgr = arg2;
            editorSourcesMgr = arg3;
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
                PLoger.LogError("该资源不存在：" + path);
                return;
            }

            switch (method)
            {
                case AssetLoaddingMethod.Editor:
                    callback?.Invoke(editorSourcesMgr.Load<T>(path));
                    break;
                case AssetLoaddingMethod.Assetbundles:
                    ABArg arg = sourceRef.sourcesDic[path];
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
                    ABArg arg = sourceRef.sourcesDic[path];
                    abLoadMgr.LoadResAsync(arg.packageName, arg.sourceName, type, callback);
                    break;
            }
        }

        public void LoadScene(string path)
        {
            if (!sourceRef.sourcesDic.ContainsKey(path))
            {
                PLoger.LogError("该资源不存在：" + path);
                return;
            }
            string sceneName = path.Substring(path.LastIndexOf('/') + 1);
            sceneName = sceneName.Split('.')[0];
            switch (method)
            {
                case AssetLoaddingMethod.Editor:
                    SceneMgr.Instance.LoadScene(sceneName);
                    break;
                case AssetLoaddingMethod.Assetbundles:
                    ABArg arg = sourceRef.sourcesDic[path];
                    abLoadMgr.LoadSceneResAsync(arg.packageName, arg.sourceName, () =>
                    {
                        SceneMgr.Instance.LoadScene(sceneName);
                    });
                    break;
            }
        }

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
                ABArg arg = sourceRef.sourcesDic[path];
                isUnload = abLoadMgr.UnLoadAB(arg.sourceName);
            }
            else
            {
                isUnload = true;
            }
            return isUnload;
        }



    }
    /// <summary>
    /// AB资源信息
    /// </summary>
    [System.Serializable]
    public class ABSourcesRef
    {
        /// <summary>
        /// key是编辑器里的路径
        /// </summary>
        public Dictionary<string, ABArg> sourcesDic = new Dictionary<string, ABArg>();
    }

    /// <summary>
    /// AB数据
    /// </summary>
    [System.Serializable]
    public class ABArg
    {
        /// <summary>
        /// AB包名
        /// </summary>
        public string packageName;
        /// <summary>
        /// 资源名
        /// </summary>
        public string sourceName;
        public ABArg(string arg0, string arg1)
        {
            packageName = arg0;
            sourceName = arg1;
        }
    }
}

