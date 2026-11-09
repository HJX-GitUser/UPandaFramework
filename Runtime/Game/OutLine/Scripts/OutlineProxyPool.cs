using System.Collections.Generic;
using UnityEngine;

namespace UPandaGF.Gameplay
{
    /// <summary>浠ｇ悊娓叉煋鍣ㄧ殑绉嶇被锛氭櫘閫氱綉鏍兼垨钂欑毊缃戞牸銆?/summary>
    internal enum OutlineProxyKind
    {
        Mesh,
        Skinned
    }

    /// <summary>
    /// 浠ｇ悊瀵硅薄姹犮€傞绻佸紑鍏抽珮浜椂澶嶇敤浠ｇ悊 Renderer锛岄伩鍏嶅弽澶嶅垱寤?閿€姣?GameObject 涓庣粍浠躲€?
    /// 褰掕繕鏃舵妸浠ｇ悊绉诲埌闅愯棌鐨勬睜鏍硅妭鐐广€佹竻绌虹綉鏍间笌楠ㄩ寮曠敤銆佸仠鐢ㄥ璞★紱
    /// 姹犳牴鑺傜偣甯?DontSave锛屼笉闅忓満鏅繚瀛橈紝涔熶笉闅忓満鏅嵏杞介攢姣併€?
    /// 鏁伴噺鎸夌绫诲皝椤讹紝瓒呭嚭涓婇檺鐩存帴閿€姣侊紝閬垮厤鏃犻檺澧為暱銆?
    /// </summary>
    internal static class OutlineProxyPool
    {
        private const int MaxPerKind = 64;

        private static readonly Stack<GameObject> MeshPool = new Stack<GameObject>();
        private static readonly Stack<GameObject> SkinnedPool = new Stack<GameObject>();
        private static Transform root;

        internal static int PooledCount => MeshPool.Count + SkinnedPool.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnLoad()
        {
            // 鍏抽棴鍩熼噸杞芥椂闈欐€佸瓧娈典細璺?Play 淇濈暀锛岃繖閲屾妸涓婁竴杞殑瀵硅薄娓呮帀銆?
            Clear();
        }

        /// <summary>鍙栦竴涓唬鐞嗗璞″苟鎸傚埌鐩爣涓嬶紱杩斿洖鐨勫璞′竴瀹氭槸鍚敤鐘舵€併€?/summary>
        internal static GameObject Rent(OutlineProxyKind kind, OutLine owner, Transform parent)
        {
            Stack<GameObject> pool = PoolOf(kind);
            GameObject instance = null;
            while (pool.Count > 0 && instance == null)
            {
                instance = pool.Pop();
            }
            if (instance == null)
            {
                instance = Create(kind);
            }

            instance.SetActive(true);
            instance.transform.SetParent(parent, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            OutlineProxy marker = instance.GetComponent<OutlineProxy>();
            if (marker == null)
            {
                marker = instance.AddComponent<OutlineProxy>();
            }
            marker.Owner = owner;
            return instance;
        }

        /// <summary>褰掕繕浠ｇ悊锛氭竻鎺夊缃戞牸/楠ㄩ/灞炴€х殑寮曠敤鍚庢斁杩涙睜閲屽鐢ㄣ€?/summary>
        internal static void Return(GameObject instance, OutlineProxyKind kind)
        {
            if (instance == null)
            {
                return;
            }

            OutlineProxy marker = instance.GetComponent<OutlineProxy>();
            if (marker != null)
            {
                marker.Owner = null;
            }

            MeshFilter filter = instance.GetComponent<MeshFilter>();
            if (filter != null)
            {
                filter.sharedMesh = null;
            }
            SkinnedMeshRenderer skin = instance.GetComponent<SkinnedMeshRenderer>();
            if (skin != null)
            {
                skin.sharedMesh = null;
                skin.bones = null;
                skin.rootBone = null;
            }
            Renderer renderer = instance.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.SetPropertyBlock(null);
                renderer.enabled = true;
            }

            instance.SetActive(false);
            // 褰掕繕鏃舵仮澶嶉殣钘忥細璋冭瘯寮€鍏虫墦寮€杩囩殑浠ｇ悊涓嶈兘鐣欏湪 Hierarchy 閲屻€?
            instance.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
            Transform poolRoot = Root;
            if (poolRoot != null)
            {
                instance.transform.SetParent(poolRoot, false);
            }

            Stack<GameObject> pool = PoolOf(kind);
            if (pool.Count >= MaxPerKind)
            {
                Object.Destroy(instance);
                return;
            }
            pool.Push(instance);
        }

        internal static void Clear()
        {
            DestroyAll(MeshPool);
            DestroyAll(SkinnedPool);
            if (root != null)
            {
                Object.Destroy(root.gameObject);
                root = null;
            }
        }

        private static void DestroyAll(Stack<GameObject> pool)
        {
            while (pool.Count > 0)
            {
                GameObject instance = pool.Pop();
                if (instance != null)
                {
                    Object.Destroy(instance);
                }
            }
        }

        private static Stack<GameObject> PoolOf(OutlineProxyKind kind)
        {
            return kind == OutlineProxyKind.Skinned ? SkinnedPool : MeshPool;
        }

        private static GameObject Create(OutlineProxyKind kind)
        {
            bool skinned = kind == OutlineProxyKind.Skinned;
            var instance = new GameObject(skinned ? "OutLine Proxy (Skinned)" : "OutLine Proxy");
            instance.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
            instance.AddComponent<OutlineProxy>();

            if (skinned)
            {
                instance.AddComponent<SkinnedMeshRenderer>();
            }
            else
            {
                instance.AddComponent<MeshFilter>();
                instance.AddComponent<MeshRenderer>();
            }

            instance.SetActive(false);
            Transform poolRoot = Root;
            if (poolRoot != null)
            {
                instance.transform.SetParent(poolRoot, false);
            }
            return instance;
        }

        private static Transform Root
        {
            get
            {
                if (root == null)
                {
                    var instance = new GameObject("[OutLine Proxy Pool]");
                    // DontSave锛氫笉鍐欏叆鍦烘櫙锛屼篃涓嶉殢鍦烘櫙鍒囨崲琚攢姣併€?
                    instance.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
                    root = instance.transform;
                }
                return root;
            }
        }
    }
}
