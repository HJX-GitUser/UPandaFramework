using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>
    /// 资源加载方式
    /// </summary>
    public enum AssetLoadMethod
    {
        Resources,
        AssetBundle
    }

    /// <summary>
    /// 应用级资源加载方式：编辑器直读资源，或走 AssetBundle。
    /// <para>注意与 <see cref="AssetLoadMethod"/>（单次加载级别）区分。枚举名拼写保留以兼容旧代码。</para>
    /// </summary>
    public enum AssetLoaddingMethod
    {
        /// <summary>编辑器环境下直接加载资源</summary>
        Editor,
        /// <summary>使用 AssetBundle 加载资源</summary>
        Assetbundles,
    }
}
