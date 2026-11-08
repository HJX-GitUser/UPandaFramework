using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>
    /// 单次资源加载模式
    /// </summary>
    public enum AssetLoadMethod
    {
        Resources,
        AssetBundle
    }

    /// <summary>
    /// 应用级资源加载模式：编辑器直读、初始化 AssetBundle，或跳过 AssetBundle 初始化。
    /// <para>注意与 <see cref="AssetLoadMethod"/>（单次加载级别）区分。枚举名拼写保留以兼容旧代码。</para>
    /// </summary>
    public enum AssetLoaddingMethod
    {
        /// <summary>编辑器环境下直接加载资源（仅编辑器）</summary>
        Editor,
        /// <summary>初始化 AssetBundle（清单、主包、可选热更）</summary>
        AssetBundle,
        /// <summary>跳过 AssetBundle 初始化，只使用 Resources / 随包资源；不支持热更</summary>
        NoAssetBundle,
    }
}
