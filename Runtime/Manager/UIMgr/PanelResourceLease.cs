using UnityEngine;
using System;

namespace UPandaGF
{
    /// <summary>将 Resources 预制体引用的归还绑定到面板实例，而非某一种关闭入口。</summary>
    [AddComponentMenu("")]
    internal sealed class PanelResourceLease : MonoBehaviour
    {
        private IDisposable lease;
        public void Initialize(IDisposable value) { lease = value; }
        private ResourcesLoader loader;
        private string path;
        public void Initialize(ResourcesLoader resourceLoader, string resourcePath)
        {
            loader = resourceLoader;
            path = resourcePath;
        }
        private void OnDestroy()
        {
            lease?.Dispose(); lease = null;
            if (loader != null) loader.UnLoadAsset<GameObject>(path, true);
            loader = null;
        }
    }
}
