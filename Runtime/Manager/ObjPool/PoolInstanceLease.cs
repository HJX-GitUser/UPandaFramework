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
        private void OnDestroy()
        {
            container?.Remove(gameObject);
            owner?.Forget(this);
            lease?.Dispose(); lease = null; owner = null; container = null;
        }
    }
}
