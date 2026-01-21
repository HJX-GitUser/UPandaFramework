using System.Collections.Generic;

namespace UPandaGF
{
    /// <summary>
    /// AB包加载路径
    /// </summary>
    public enum ABLoadPath
    {
        StreamingAssetsPath,
        PersistentDataPath,
        RemotePath
    }
    ///// <summary>
    ///// AB资源引用信息
    /// </summary>
    [System.Serializable]
    public class ABSourcesRelated
    {
        /// <summary>
        /// key是编辑器里的路径
        /// </summary>
        public Dictionary<string, ABRelatedArg> sourcesDic = new Dictionary<string, ABRelatedArg>();
    }

    /// <summary>
    /// AB引用数据
    /// </summary>
    [System.Serializable]
    public class ABRelatedArg
    {
        /// <summary>
        /// AB包名
        /// </summary>
        public string packageName;
        /// <summary>
        /// 资源名
        /// </summary>
        public string sourceName;
        /// <summary>
        /// 资源加载根路径
        /// </summary>
        public ABLoadPath loodPath;
       
        public ABRelatedArg(string arg0, string arg1, ABLoadPath arg2)
        {
            packageName = arg0;
            sourceName = arg1;
            loodPath = arg2;
        }
    }
}

