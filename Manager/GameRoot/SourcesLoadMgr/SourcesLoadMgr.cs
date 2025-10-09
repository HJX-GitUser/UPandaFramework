using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace UPandaGF
{
    public class SourcesLoadMgr : BaseMonoSingleton<SourcesLoadMgr>
    {
        private AssetLoaddingMethod method;
        private ABLoadMgr abLoadMgr;
        private ABSourcesRef sourceRef;
        public void Init(AssetLoaddingMethod arg0, ABSourcesRef arg1)
        {
            method = arg0;
            sourceRef = arg1;
            abLoadMgr = ABLoadMgr.Instance;
        }
        /// <summary>
        /// 加载资源 泛型
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="path"></param>
        /// <param name="callback"></param>
        public void Load<T>(string path, UnityAction<T> callback) where T : Object
        {
            if (!sourceRef.sourcesDic.ContainsKey(path))
            {
                PLoger.LogError("该资源不存在：" + path);
                return;
            }

            switch (method)
            {
                case AssetLoaddingMethod.Editor:
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
        public void Load(string path, System.Type type, UnityAction<Object> callback)
        {
            if (!sourceRef.sourcesDic.ContainsKey(path))
            {
                PLoger.LogError("该资源不存在：" + path);
                return;
            }

            switch (method)
            {
                case AssetLoaddingMethod.Editor:
                    break;
                case AssetLoaddingMethod.Assetbundles:
                    ABArg arg = sourceRef.sourcesDic[path];
                    abLoadMgr.LoadResAsync(arg.packageName, arg.sourceName, type, callback);
                    break;
            }
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

