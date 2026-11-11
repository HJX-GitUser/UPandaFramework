using UnityEngine;
using System;

namespace UPandaGF
{
    /// <summary>把面板预制体的资源持有凭据绑定到面板实例销毁，并在实例被外部销毁时通知管理器。</summary>
    [AddComponentMenu("")]
    internal sealed class PanelResourceLease : MonoBehaviour
    {
        private IDisposable lease;
        private Action destroyed;

        public void Initialize(IDisposable value, Action onDestroyed = null)
        {
            lease = value;
            destroyed = onDestroyed;
        }

        private void OnDestroy()
        {
            IDisposable value = lease;
            lease = null;
            try { value?.Dispose(); }
            finally
            {
                Action callback = destroyed;
                destroyed = null;
                try { callback?.Invoke(); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }
    }
}
