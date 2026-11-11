using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UPandaGF
{
    /// <summary>
    /// 设置行视图基类：负责标题/说明/可用性等公共部分，具体控件由子类绑定。
    /// <para>行由 <see cref="UISettingList"/> 从池中取用；<see cref="Bind"/> 会先释放上一次绑定，
    /// 因此重复打开/滚动复用不会叠加事件。</para>
    /// <para>标签与副文本字段类型是 <see cref="Component"/>，uGUI Text 与 TMP_Text 都可用；
    /// 未在 Inspector 赋值时按子节点名称 "Label"/"Value"/"Description" 自动查找。</para>
    /// </summary>
    public abstract class UISettingRow : MonoBehaviour
    {
        [SerializeField] private Component label;
        [SerializeField] private Component description;

        private CompositeDisposable bindings;

        /// <summary>当前绑定的描述；未绑定时为 null。</summary>
        public UISettingItem Item { get; private set; }
        public bool IsBound => Item != null;

        /// <summary>绑定一行；会先释放旧绑定。item 为 null 时等价于 <see cref="Unbind"/>。</summary>
        public void Bind(UISettingItem item)
        {
            Unbind();
            if (item == null) return;

            Item = item;
            ResolveReferences();
            ApplyCommon();
            BindValue();
        }

        /// <summary>释放本次绑定的事件与订阅；重复调用安全。</summary>
        public void Unbind()
        {
            bindings?.Dispose();
            bindings = null;
            Item = null;
        }

        /// <summary>不重建绑定，只刷新标题/说明/可用性；用于设备能力变化或行内状态更新。</summary>
        public void Refresh()
        {
            if (Item == null) return;
            ApplyCommon();
            RefreshInteractable();
        }

        /// <summary>本次绑定持有的订阅集合；子类在 BindValue 中登记。</summary>
        protected CompositeDisposable Scope => bindings ?? (bindings = new CompositeDisposable());

        /// <summary>查找并校验子控件；每行在绑定前调用。</summary>
        protected virtual void ResolveReferences() { }

        /// <summary>把描述中的值绑定到实际控件。</summary>
        protected abstract void BindValue();

        /// <summary>重新计算控件可用性。</summary>
        protected virtual void RefreshInteractable() { }

        protected bool IsInteractable =>
            Item == null || Item.CanInteract == null || Item.CanInteract();

        protected void ApplyInteractable(Selectable target)
        {
            if (target != null) target.interactable = IsInteractable;
        }

        protected virtual void ApplyCommon()
        {
            SetText(label, Item.Label);
            bool hasDescription = !string.IsNullOrEmpty(Item.Description);
            SetText(description, hasDescription ? Item.Description : null);
            SetActive(description, hasDescription);
        }

        protected Component FindText(string name, bool firstIfMissing)
        {
            Text[] uguiTexts = GetComponentsInChildren<Text>(true);
            for (int i = 0; i < uguiTexts.Length; i++)
                if (uguiTexts[i] != null && uguiTexts[i].gameObject.name == name) return uguiTexts[i];

            TMP_Text[] tmpTexts = GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < tmpTexts.Length; i++)
                if (tmpTexts[i] != null && tmpTexts[i].gameObject.name == name) return tmpTexts[i];

            if (!firstIfMissing) return null;
            if (uguiTexts.Length > 0) return uguiTexts[0];
            return tmpTexts.Length > 0 ? tmpTexts[0] : null;
        }

        protected static T FindInChildren<T>(Transform root) where T : Component
        {
            return root != null ? root.GetComponentInChildren<T>(true) : null;
        }

        protected static void SetText(Component target, string value)
        {
            if (target is Text ugui)
            {
                if (ugui.text != value) ugui.text = value ?? string.Empty;
            }
            else if (target is TMP_Text tmp)
            {
                if (tmp.text != value) tmp.text = value ?? string.Empty;
            }
        }

        private static void SetActive(Component target, bool active)
        {
            if (target == null) return;
            if (target is Text ugui) { if (ugui.gameObject.activeSelf != active) ugui.gameObject.SetActive(active); }
            else if (target is TMP_Text tmp) { if (tmp.gameObject.activeSelf != active) tmp.gameObject.SetActive(active); }
        }

        private void OnDestroy()
        {
            // 池清空/场景卸载时停止观察 Observable，避免已销毁的行继续收到回调。
            Unbind();
        }
    }
}
