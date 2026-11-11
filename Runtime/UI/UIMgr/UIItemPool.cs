using System.Collections.Generic;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>
    /// 通用 UI 行/项池：按预制体复用实例，避免滚动列表和设置面板反复 Instantiate/Destroy。
    /// <para>只负责实例的取用、回收、空闲上限与统计，不依赖任何面板类型；
    /// <see cref="UISettingList"/> 与业务列表都可以直接使用。</para>
    /// <para>池内对象在 <see cref="Clear"/> 或上限为 0 时销毁；被外部销毁的对象在下次取用时自动跳过。</para>
    /// </summary>
    public sealed class UIItemPool
    {
        private readonly GameObject prefab;
        private readonly Transform poolRoot;
        private readonly int maxIdle;
        private readonly Stack<GameObject> idle = new Stack<GameObject>();
        private readonly HashSet<GameObject> idleSet = new HashSet<GameObject>();

        /// <param name="prefab">行预制体（可来自场景对象）；为 null 时不创建，取用时返回 null。</param>
        /// <param name="poolRoot">空闲实例的父节点；为 null 时回收即销毁。</param>
        /// <param name="maxIdle">空闲实例上限；0 表示不缓存。</param>
        public UIItemPool(GameObject prefab, Transform poolRoot = null, int maxIdle = 8)
        {
            this.prefab = prefab;
            this.poolRoot = poolRoot;
            this.maxIdle = Mathf.Max(0, maxIdle);
        }

        public GameObject Prefab => prefab;
        public int MaxIdle => maxIdle;
        public int IdleCount => idle.Count;

        /// <summary>累计创建数（Instantiate 次数）。</summary>
        public int CreatedCount { get; private set; }
        /// <summary>累计复用数（从空闲池取出次数）。</summary>
        public int ReusedCount { get; private set; }
        /// <summary>累计归还数（Release 被调用次数）。</summary>
        public int ReleasedCount { get; private set; }
        /// <summary>累计销毁数（超过上限或池根为空）。</summary>
        public int DestroyedCount { get; private set; }

        /// <summary>
        /// 取一个实例：优先复用空闲对象，否则实例化；返回对象已激活、父节点为 <paramref name="parent"/>。
        /// </summary>
        public GameObject Acquire(Transform parent)
        {
            if (prefab == null)
            {
                Debug.LogError("[UIItemPool] 预制体已被销毁或未设置，无法取用行实例。");
                return null;
            }

            GameObject item = null;
            while (idle.Count > 0)
            {
                GameObject candidate = idle.Pop();
                if (candidate == null) continue;
                idleSet.Remove(candidate);
                item = candidate;
                ReusedCount++;
                break;
            }

            if (item == null)
            {
                item = Object.Instantiate(prefab);
                item.name = prefab.name;
                CreatedCount++;
            }

            if (parent != null) item.transform.SetParent(parent, false);
            item.SetActive(true);
            item.transform.SetAsLastSibling();
            return item;
        }

        /// <summary>
        /// 归还实例：隐藏并移到空闲池；超过上限或没有池根时销毁。
        /// 重复归还会被忽略，避免同一实例在池中出现两次。
        /// </summary>
        public void Release(GameObject item)
        {
            if (item == null) return;
            if (idleSet.Contains(item)) return; // 重复归还：忽略，避免同一实例在池中出现两次
            ReleasedCount++;

            if (poolRoot == null || maxIdle <= 0 || idle.Count >= maxIdle)
            {
                DestroyObject(item);
                DestroyedCount++;
                return;
            }

            item.SetActive(false);
            item.transform.SetParent(poolRoot, false);
            idle.Push(item);
            idleSet.Add(item);
        }

        /// <summary>销毁全部空闲实例；正在使用中的实例不受影响。</summary>
        public void Clear()
        {
            while (idle.Count > 0)
            {
                GameObject item = idle.Pop();
                if (item == null) continue;
                DestroyObject(item);
                DestroyedCount++;
            }
            idleSet.Clear();
        }

        private static void DestroyObject(GameObject item)
        {
            if (item == null) return;
            if (Application.isPlaying) Object.Destroy(item);
            else Object.DestroyImmediate(item);
        }
    }
}
