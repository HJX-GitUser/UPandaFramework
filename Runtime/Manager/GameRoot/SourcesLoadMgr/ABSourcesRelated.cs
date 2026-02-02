using System.Collections.Generic;

namespace UPandaGF
{
    /// <summary>
    /// AB包加载路径
    /// </summary>
    [System.Serializable]
    public enum ABLoadPath
    {
        StreamingAssetsPath,
        PersistentDataPath,
        RemotePath
    }
    ///// <summary>
    ///// 资源信息
    /// </summary>
    [System.Serializable]
    public class ABSourcesRelated
    {
        /// <summary>
        /// AssetBunde数据 key是包名
        /// </summary>
        public Dictionary<string, AssetBundleLoadInfo> bundleInfo = new Dictionary<string, AssetBundleLoadInfo>();
        /// <summary>
        /// 资源加载信息 key是编辑器里的路径
        /// </summary>
        public Dictionary<string, AssetRelatedArg> sourcesDic = new Dictionary<string, AssetRelatedArg>();

        public ABLoadPath GetABLoadPath(AssetRelatedArg arg)
        {
            return bundleInfo[arg.bundleName].loadPath;
        }
    }

    /// <summary>
    /// 资源信息 记录每个资源对应的加载数据
    /// </summary>
    [System.Serializable]
    public class AssetRelatedArg
    {
        /// <summary>
        /// AB包名
        /// </summary>
        public string bundleName;
        /// <summary>
        /// 资源名
        /// </summary>
        public string sourceName;

        public AssetRelatedArg(string arg0, string arg1)
        {
            bundleName = arg0;
            sourceName = arg1;
        }
    }

    /// <summary>
    /// AssetBundle信息
    /// </summary>
    [System.Serializable]
    public class AssetBundleLoadInfo
    {
        public string bundleName;//包名
        public long size;//大小
        public string md5;//MD5码
        public ABLoadPath loadPath;//加载路径
    }
}

