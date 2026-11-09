using UnityEngine;

namespace UPandaGF.Gameplay
{
    /// <summary>
    /// 描边代理对象上的标记组件。描边不再改动原始 Renderer 的材质与网格，
    /// 而是在目标下挂一个隐藏的代理 Renderer；层级扫描时靠本组件把代理排除掉，
    /// 避免「给描边再描边」。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class OutlineProxy : MonoBehaviour
    {
        /// <summary>创建该代理的描边组件，便于排查时定位来源。</summary>
        public OutLine Owner { get; internal set; }
    }
}
