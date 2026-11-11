using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UPandaGF
{
    /// <summary>
    /// 面板/页签级焦点导航：打开时选中默认项、切页/恢复时找回上次焦点、
    /// 手柄方向键移动、确认、返回与滑块/下拉微调。
    /// <para>不直接读输入设备：由面板把 <c>IInputService</c> 的动作映射到
    /// <see cref="Navigate"/>、<see cref="Submit"/>、<see cref="Cancel"/>、<see cref="Adjust"/>，
    /// 从而同时适配旧 Input Manager 与 Input System。</para>
    /// <para>只接管本节点子树内的焦点，不会抢走其它面板的选中项；关闭后由 <see cref="RestoreFocus"/> 复位。</para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("UPandaGF/UI/Focus Navigator")]
    public sealed class UIFocusNavigator : MonoBehaviour
    {
        [SerializeField] private Selectable defaultFocus;
        [SerializeField] private bool focusOnEnable = true;
        [SerializeField] private bool restoreLastFocus = true;
        [SerializeField] private EventSystem eventSystem;

        private Selectable lastFocused;
        private Coroutine pendingFocus;

        /// <summary>返回键/取消键被请求；有订阅者时 <see cref="Cancel"/> 返回 true。</summary>
        public event Action BackRequested;

        /// <summary>打开时优先选中的控件；为空时选子树中第一个可交互控件。</summary>
        public Selectable DefaultFocus
        {
            get { return defaultFocus; }
            set { defaultFocus = value; }
        }

        /// <summary>当前选中项；选中项不在本节点子树内时返回 null。</summary>
        public Selectable Current
        {
            get
            {
                EventSystem system = ResolveEventSystem();
                if (system == null) return null;
                GameObject selected = system.currentSelectedGameObject;
                if (selected == null) return null;
                Selectable selectable = selected.GetComponent<Selectable>();
                return IsOwned(selectable) ? selectable : null;
            }
        }

        public bool HasFocus => Current != null;

        private void OnEnable()
        {
            if (!focusOnEnable) return;
            if (pendingFocus != null) StopCoroutine(pendingFocus);
            pendingFocus = StartCoroutine(FocusOnNextFrame());
        }

        private void OnDisable()
        {
            if (pendingFocus != null)
            {
                StopCoroutine(pendingFocus);
                pendingFocus = null;
            }
            if (restoreLastFocus) lastFocused = Current;
        }

        private IEnumerator FocusOnNextFrame()
        {
            // 等一帧：面板 OnOpen 里常会重建布局/选项，立即选中可能落到被替换的控件上。
            yield return null;
            pendingFocus = null;
            if (!isActiveAndEnabled) yield break;
            if (restoreLastFocus && lastFocused != null) RestoreFocus();
            else FocusFirst();
        }

        /// <summary>设置默认焦点；focusNow 为 true 时立即选中。</summary>
        public void SetDefault(Selectable target, bool focusNow = false)
        {
            defaultFocus = target;
            if (focusNow) Focus(target);
        }

        /// <summary>选中指定控件；控件不在本子树、未激活或不可交互时返回 false。</summary>
        public bool Focus(Selectable target)
        {
            if (!IsUsable(target)) return false;
            EventSystem system = ResolveEventSystem();
            if (system == null) return false;

            system.SetSelectedGameObject(target.gameObject);
            lastFocused = target;
            return true;
        }

        /// <summary>选中默认项；没有默认项时选子树中第一个可导航控件。</summary>
        public bool FocusFirst()
        {
            if (IsUsable(defaultFocus)) return Focus(defaultFocus);

            List<Selectable> selectables = CollectNavigable();
            for (int i = 0; i < selectables.Count; i++)
            {
                if (selectables[i] != null && selectables[i].navigation.mode != Navigation.Mode.None)
                    return Focus(selectables[i]);
            }
            return false;
        }

        /// <summary>按方向移动焦点；从当前项出发，没有当前项时先聚焦默认项。</summary>
        public bool Navigate(Vector2 direction)
        {
            Selectable from = Current;
            if (from == null)
            {
                if (!FocusFirst()) return false;
                from = Current;
                if (from == null) return false;
            }

            Selectable next;
            if (direction.y > 0.5f) next = from.FindSelectableOnUp();
            else if (direction.y < -0.5f) next = from.FindSelectableOnDown();
            else if (direction.x > 0.5f) next = from.FindSelectableOnRight();
            else if (direction.x < -0.5f) next = from.FindSelectableOnLeft();
            else return false;

            return next != null && Focus(next);
        }

        /// <summary>
        /// 方向输入优先用于调整当前滑块/下拉：横向输入时微调，否则移动焦点。
        /// <paramref name="stepScale"/> 对滑块是范围比例，对下拉是选项数。
        /// </summary>
        public bool NavigateOrAdjust(Vector2 direction, float stepScale = 0.1f)
        {
            bool horizontal = Mathf.Abs(direction.x) > Mathf.Abs(direction.y);
            if (horizontal && Current is Slider) return Adjust(direction.x, stepScale);
            if (horizontal && Current is Dropdown) return Adjust(direction.x, 1f);
            return Navigate(direction);
        }

        /// <summary>Tab/肩键切换：在可导航控件之间循环，不要求空间相邻。</summary>
        public bool FocusNext(bool forward = true)
        {
            List<Selectable> selectables = CollectNavigable();
            if (selectables.Count == 0) return false;

            int current = selectables.IndexOf(Current);
            int next = current < 0 ? 0 : (current + (forward ? 1 : -1) + selectables.Count) % selectables.Count;
            return Focus(selectables[next]);
        }

        /// <summary>
        /// 微调当前控件：Slider 按 amount（为 0 时取范围的 10%）增减；Dropdown 切换选项。
        /// </summary>
        public bool Adjust(float direction, float amount = 0f)
        {
            if (Mathf.Approximately(direction, 0f)) return false;
            Selectable current = Current;
            if (current == null) return false;

            if (current is Slider slider)
            {
                float step = amount > 0f ? amount : (slider.maxValue - slider.minValue) * 0.1f;
                slider.value = Mathf.Clamp(slider.value + direction * step, slider.minValue, slider.maxValue);
                return true;
            }

            if (current is Dropdown dropdown)
            {
                int delta = Mathf.Max(1, Mathf.RoundToInt(amount));
                int last = Mathf.Max(0, dropdown.options.Count - 1);
                dropdown.value = Mathf.Clamp(dropdown.value + (direction > 0f ? delta : -delta), 0, last);
                return true;
            }
            return false;
        }

        /// <summary>确认键：按钮触发点击，开关切换；其它控件返回 false。</summary>
        public bool Submit()
        {
            Selectable current = Current;
            if (current == null) return false;

            if (current is Button button)
            {
                button.onClick.Invoke();
                return true;
            }
            if (current is Toggle toggle)
            {
                toggle.isOn = !toggle.isOn;
                return true;
            }
            return false;
        }

        /// <summary>返回/取消键；有 BackRequested 订阅者时视为已处理。</summary>
        public bool Cancel()
        {
            Action handler = BackRequested;
            if (handler == null) return false;
            handler.Invoke();
            return true;
        }

        /// <summary>记录当前焦点，切页签/被遮挡前调用。</summary>
        public void RememberFocus()
        {
            lastFocused = Current;
        }

        /// <summary>恢复上次焦点；上次控件已失效时回退到第一个可导航控件。</summary>
        public bool RestoreFocus()
        {
            if (IsUsable(lastFocused)) return Focus(lastFocused);
            return FocusFirst();
        }

        /// <summary>清空本子树内的选中项；不影响其它面板。</summary>
        public void ClearFocus()
        {
            if (Current == null) return;
            EventSystem system = ResolveEventSystem();
            if (system != null) system.SetSelectedGameObject(null);
            lastFocused = null;
        }

        private List<Selectable> CollectNavigable()
        {
            Selectable[] all = GetComponentsInChildren<Selectable>(false);
            List<Selectable> result = new List<Selectable>(all.Length);
            for (int i = 0; i < all.Length; i++)
                if (IsUsable(all[i])) result.Add(all[i]);
            return result;
        }

        private bool IsUsable(Selectable target)
        {
            return IsOwned(target) && target.isActiveAndEnabled && target.IsInteractable();
        }

        private bool IsOwned(Selectable target)
        {
            if (target == null) return false;
            Transform targetTransform = target.transform;
            return targetTransform == transform || targetTransform.IsChildOf(transform);
        }

        private EventSystem ResolveEventSystem()
        {
            if (eventSystem != null) return eventSystem;
            eventSystem = EventSystem.current;
            if (eventSystem == null) eventSystem = FindObjectOfType<EventSystem>();
            return eventSystem;
        }
    }
}
