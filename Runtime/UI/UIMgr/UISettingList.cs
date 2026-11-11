using System;
using System.Collections.Generic;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>行预制体注册项：一种控件类型对应一个行预制体。</summary>
    [Serializable]
    public sealed class UISettingRowPrefab
    {
        public UISettingControl control;
        public UISettingRow prefab;
    }

    /// <summary>
    /// 描述表驱动的设置列表：按 <see cref="UISettingItem"/> 的控件类型取用对应的行预制体，
    /// 行实例由 <see cref="UIItemPool"/> 复用，新增设置项不需要改面板代码。
    /// <para>同一索引且控件类型未变时复用原行实例，只重新 <see cref="UISettingRow.Bind"/>；
    /// 控件类型变化时换行。行数减少时多余行回到池中。</para>
    /// <para>行预制体根节点必须挂 <see cref="UISettingRow"/> 子类；
    /// <see cref="UISettingControl.Key"/> 未单独注册时回退到 Button 行。</para>
    /// </summary>
    [AddComponentMenu("UPandaGF/UI/Setting List")]
    public sealed class UISettingList : MonoBehaviour
    {
        [SerializeField] private RectTransform content;
        [SerializeField] private List<UISettingRowPrefab> rowPrefabs = new List<UISettingRowPrefab>();
        [SerializeField] private int maxPooledRows = 8;

        private readonly Dictionary<UISettingControl, UISettingRowPrefab> prefabMap =
            new Dictionary<UISettingControl, UISettingRowPrefab>();
        private readonly Dictionary<UISettingControl, UIItemPool> pools =
            new Dictionary<UISettingControl, UIItemPool>();
        private readonly List<UISettingRow> rows = new List<UISettingRow>();
        private readonly List<UISettingControl> rowKinds = new List<UISettingControl>();
        private readonly List<UISettingItem> items = new List<UISettingItem>();
        private Transform poolRoot;
        private bool prefabMapDirty = true;

        /// <summary>当前行数。</summary>
        public int RowCount => rows.Count;
        /// <summary>当前设置项数。</summary>
        public int ItemCount => items.Count;
        /// <summary>行容器；未指定时使用本组件所在节点。</summary>
        public RectTransform Content
        {
            get { return content != null ? content : transform as RectTransform; }
            set { content = value; }
        }

        public int CreatedRows { get; private set; }
        public int ReusedRows { get; private set; }
        public int ReleasedRows { get; private set; }
        public int PooledRows => CountPooledRows();

        /// <summary>运行期注册/替换某类控件对应的行预制体；通常在 SetItems 之前调用。</summary>
        public void SetRowPrefab(UISettingControl control, UISettingRow prefab)
        {
            UISettingControl kind = ResolveKind(control);
            for (int i = rowPrefabs.Count - 1; i >= 0; i--)
            {
                if (rowPrefabs[i] != null && ResolveKind(rowPrefabs[i].control) == kind)
                    rowPrefabs.RemoveAt(i);
            }
            if (prefab != null)
                rowPrefabs.Add(new UISettingRowPrefab { control = control, prefab = prefab });
            prefabMapDirty = true;

            if (pools.TryGetValue(kind, out UIItemPool pool))
            {
                pool.Clear();
                pools.Remove(kind);
            }
        }

        /// <summary>
        /// 用新的描述表刷新列表：按索引与控件类型复用行实例，减少 Instantiate/Destroy。
        /// 传入 null 等价于 <see cref="Clear"/>。
        /// </summary>
        public void SetItems(IReadOnlyList<UISettingItem> value)
        {
            items.Clear();
            if (value != null)
            {
                for (int i = 0; i < value.Count; i++)
                {
                    UISettingItem item = value[i];
                    if (item == null) continue;
                    if (!item.IsValid(out string error))
                        Debug.LogError("[UISettingList] 设置项 “" + item.Key + "” 描述无效：" + error);
                    items.Add(item);
                }
            }

            int count = items.Count;
            for (int i = 0; i < count; i++)
            {
                UISettingItem item = items[i];
                UISettingControl kind = ResolveKind(item.Control);

                if (i < rows.Count && rowKinds[i] == kind && rows[i] != null)
                {
                    rows[i].Bind(item);
                    ReusedRows++;
                }
                else
                {
                    if (i < rows.Count) ReleaseRowAt(i);
                    UISettingRow row = AcquireRow(kind);
                    if (row == null) break;
                    rows.Insert(i, row);
                    rowKinds.Insert(i, kind);
                    row.Bind(item);
                }

                rows[i].transform.SetSiblingIndex(i);
            }

            while (rows.Count > count) ReleaseRowAt(rows.Count - 1);
        }

        /// <summary>不重建行，只刷新标题/说明/可用性；设备能力变化后调用。</summary>
        public void Refresh()
        {
            int count = Mathf.Min(rows.Count, items.Count);
            for (int i = 0; i < count; i++) rows[i].Refresh();
        }

        /// <summary>释放全部行与空闲池；可在关闭面板时调用。</summary>
        public void Clear()
        {
            while (rows.Count > 0) ReleaseRowAt(rows.Count - 1);
            foreach (UIItemPool pool in pools.Values) pool.Clear();
            pools.Clear();
            items.Clear();
        }

        /// <summary>取指定索引的行；越界返回 null。</summary>
        public UISettingRow GetRow(int index)
        {
            return index >= 0 && index < rows.Count ? rows[index] : null;
        }

        private UISettingRow AcquireRow(UISettingControl kind)
        {
            if (!pools.TryGetValue(kind, out UIItemPool pool))
            {
                UISettingRow prefab = GetPrefab(kind);
                if (prefab == null)
                {
                    Debug.LogError("[UISettingList] 未注册 " + kind + " 行预制体；请检查 Inspector 或 SetRowPrefab。");
                    return null;
                }
                pool = new UIItemPool(prefab.gameObject, GetPoolRoot(), maxPooledRows);
                pools.Add(kind, pool);
            }

            int createdBefore = pool.CreatedCount;
            GameObject instance = pool.Acquire(Content);
            if (instance == null) return null;
            if (pool.CreatedCount > createdBefore) CreatedRows++;
            else ReusedRows++;

            UISettingRow row = instance.GetComponent<UISettingRow>();
            if (row == null)
            {
                Debug.LogError("[UISettingList] 行预制体根节点缺少 UISettingRow 组件：" + instance.name);
                pool.Release(instance);
                return null;
            }
            return row;
        }

        private void ReleaseRowAt(int index)
        {
            UISettingRow row = rows[index];
            UISettingControl kind = rowKinds[index];
            rows.RemoveAt(index);
            rowKinds.RemoveAt(index);
            if (row == null) return;

            row.Unbind();
            ReleasedRows++;
            if (pools.TryGetValue(kind, out UIItemPool pool)) pool.Release(row.gameObject);
            else if (row != null) DestroyObject(row.gameObject);
        }

        private UISettingRow GetPrefab(UISettingControl kind)
        {
            if (prefabMapDirty) RebuildPrefabMap();
            return prefabMap.TryGetValue(kind, out UISettingRowPrefab entry) ? entry.prefab : null;
        }

        private void RebuildPrefabMap()
        {
            prefabMap.Clear();
            for (int i = 0; i < rowPrefabs.Count; i++)
            {
                UISettingRowPrefab entry = rowPrefabs[i];
                if (entry == null || entry.prefab == null) continue;
                prefabMap[ResolveKind(entry.control)] = entry;
            }
            prefabMapDirty = false;
        }

        private Transform GetPoolRoot()
        {
            if (poolRoot != null) return poolRoot;
            GameObject root = new GameObject("__UISettingRowPool", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            root.SetActive(false);
            poolRoot = root.transform;
            return poolRoot;
        }

        private int CountPooledRows()
        {
            int total = 0;
            foreach (UIItemPool pool in pools.Values) total += pool.IdleCount;
            return total;
        }

        private static UISettingControl ResolveKind(UISettingControl control)
        {
            // 键位行复用按钮行：显示当前按键 + 捕获动作。
            return control == UISettingControl.Key ? UISettingControl.Button : control;
        }

        private static void DestroyObject(GameObject target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }
    }
}
