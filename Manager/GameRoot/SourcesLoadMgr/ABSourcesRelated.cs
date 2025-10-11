using System.Collections.Generic;

namespace UPandaGF
{
    ///// <summary>
    ///// AB资源关联信息
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
    /// AB关联数据
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
        public ABRelatedArg(string arg0, string arg1)
        {
            packageName = arg0;
            sourceName = arg1;
        }
    }
}

