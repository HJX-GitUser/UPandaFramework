using System.Collections.Generic;
using UnityEngine;

namespace UPandaGF.Gameplay
{
    /// <summary>代理渲染器的种类：普通网格或蒙皮网格。</summary>
    internal enum OutlineProxyKind
    {
        Mesh,
        Skinned
    }

    /// <summary>
    /// 代理对象池。频繁开关高亮时复用代理 Renderer，避免反复创建/销毁 GameObject 与组件。
    /// 归还时把代理移到隐藏的池根节点、清空网格与骨骼引用、停用对象；
    /// 池根节点带 DontSave，不随场景保存，也不随场景卸载销毁。
    /// 数量按种类封顶，超出上限直接销毁，避免无限增长。
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
            // 关闭域重载时静态字段会跨 Play 保留，这里把上一轮的对象清掉。
            Clear();
        }

        /// <summary>取一个代理对象并挂到目标下；返回的对象一定是启用状态。</summary>
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

        /// <summary>归还代理：清掉对网格/骨骼/属性的引用后放进池里复用。</summary>
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
                    // DontSave：不写入场景，也不随场景切换被销毁。
                    instance.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
                    root = instance.transform;
                }
                return root;
            }
        }
    }
}
