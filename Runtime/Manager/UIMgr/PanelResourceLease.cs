using UnityEngine;
using System;

namespace UPandaGF
{
    /// <summary>把面板预制体的资源持有凭据绑定到面板实例销毁，而非某一种关闭入口。</summary>
    [AddComponentMenu("")]
    internal sealed class PanelResourceLease : MonoBehaviour
    {
        private IDisposable lease;
        public void Initialize(IDisposable value) { lease = value; }
        private void OnDestroy()
        {
            lease?.Dispose(); lease = null;
        }
    }
}
