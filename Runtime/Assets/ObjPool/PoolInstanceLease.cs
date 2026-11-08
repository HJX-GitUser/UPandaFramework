using UnityEngine;
namespace UPandaGF
{
    [AddComponentMenu("")]
    internal sealed class PoolInstanceLease : MonoBehaviour
    {
        internal GameObjectPoolMgr owner;
        internal PoolData container;
        internal string path;
        internal AssetLoadMethod method;
        internal int generation;
        internal bool cached, transitioning;
        internal AssetLease<GameObject> lease;
        private Vector3 initialPosition;
        private Quaternion initialRotation;
        private Vector3 initialScale;

        /// <summary>记录实例首次交付时的局部变换，复用时恢复到该姿态（而不是硬编码 identity）。</summary>
        internal void CaptureInitialTransform()
        {
            initialPosition = transform.localPosition;
            initialRotation = transform.localRotation;
            initialScale = transform.localScale;
        }

        internal void ResetTransform()
        {
            transform.localPosition = initialPosition;
            transform.localRotation = initialRotation;
            transform.localScale = initialScale;
        }

        private void OnDestroy()
        {
            container?.Remove(gameObject);
            owner?.Forget(this);
            lease?.Dispose(); lease = null; owner = null; container = null;
        }
    }
}
