using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UPandaGF
{
    /// <summary>
    /// 面板基类
    /// 通过一次子树遍历快速找到所有 UI 控件，节约找控件的工作量。
    /// 在子类中处理逻辑；必须加上 [UILoadInfo] 特性。
    /// </summary>
    public abstract class BasePanel : MonoBehaviour
    {
        /// <summary>创建该面板的 UIManager（由 UIManager 在实例化后注入）。</summary>
        private UIManager owner;
        private IGameServices services;
        private readonly UIEventScope events = new UIEventScope();
        private readonly UIEventScope openEvents = new UIEventScope();

        /// <summary>
        /// 所属 UI 管理器。在 OnOpen 及之后可用；Awake/OnEnable 阶段尚未注入，请勿依赖。
        /// </summary>
        protected UIManager uiManager => owner;
        /// <summary>UI 服务视图；在 OnOpen 及之后可用。</summary>
        protected IUIService UI => owner;
        /// <summary>面板可用的完整服务集合；未通过 GameServices 装配时为 null。</summary>
        public IGameServices Services => services;
        /// <summary>面板生命周期事件作用域；关闭/销毁时由 UIManager 统一释放。</summary>
        protected UIEventScope Events => events;
        /// <summary>每次打开的事件作用域；关闭时释放。当前与 Events 同生命周期，预留给池化。</summary>
        protected UIEventScope OpenEvents => openEvents;

        internal void AttachManager(UIManager manager) { owner = manager; }
        internal void AttachServices(IGameServices value) { services = value; }

        /// <summary>
        /// UI组件容器。key 是 GameObject 名称；同名控件按类型查找。
        /// </summary>
        protected Dictionary<string, List<UIBehaviour>> controlDic = new Dictionary<string, List<UIBehaviour>>();

        private static readonly HashSet<string> defaultNameList = new HashSet<string>
        {
            "Image",
            "Text (TMP)",
            "RawImage",
            "Background",
            "Checkmark",
            "Label",
            "Text (Legacy)",
            "Arrow",
            "Placeholder",
            "Fill",
            "Handle",
            "Viewport",
            "Scrollbar Horizontal",
            "Scrollbar Vertical"
        };

        private void Awake()
        {
            // 一次遍历完成控件缓存；每种控件不再各扫一遍整棵子树。
            UIBehaviour[] controls = GetComponentsInChildren<UIBehaviour>(true);
            for (int i = 0; i < controls.Length; i++)
            {
                UIBehaviour control = controls[i];
                if (control == null) continue;
                BasePanel ownerPanel = control.GetComponentInParent<BasePanel>();
                if (ownerPanel != null && ownerPanel != this) continue;
                RegisterControl(control);
            }
            OnAwake();
        }

        protected virtual void OnAwake() { }

        /// <summary>
        /// 在子类重写显示逻辑
        /// </summary>
        public abstract void OnOpen(object panelArg = null);
        /// <summary>
        /// 隐藏
        /// </summary>
        public abstract void OnClose();
        /// <summary>
        /// 面板已打开时再次请求打开：默认只置顶，不重复执行 OnOpen。
        /// 需要根据新参数刷新内容时重写此方法。
        /// </summary>
        public virtual void OnRefresh(object panelArg = null) { }
        /// <summary>面板被上层模态面板遮挡时调用；不要在这里销毁对象。</summary>
        public virtual void OnPause() { }
        /// <summary>遮挡它的上层模态面板关闭后调用。</summary>
        public virtual void OnResume() { }

        /// <summary>
        /// 按钮监听事件，重写时自己根据传入的按钮名字处理对应按钮的逻辑
        /// </summary>
        protected virtual void Button_OnClick(string btnName) { }
        /// <summary>Toggle 监听事件。</summary>
        protected virtual void Toggle_OnValueChanged(string toggleName, bool value) { }
        /// <summary>Slider 监听事件。</summary>
        protected virtual void Slider_OnValueChanged(string sliderName, float value) { }
        /// <summary>Scrollbar 监听事件。</summary>
        protected virtual void Scrollbar_OnValueChanged(string scrollbarName, float value) { }
        /// <summary>Dropdown 监听事件。</summary>
        protected virtual void Dropdown_OnValueChanged(string dropdownName, int value) { }
        /// <summary>TMP_Dropdown 监听事件。</summary>
        protected virtual void TMP_Dropdown_OnValueChanged(string dropdownName, int value) { }
        /// <summary>InputField 值改变。</summary>
        protected virtual void InputField_OnValueChanged(string inputFieldName, string value) { }
        /// <summary>InputField 提交。</summary>
        protected virtual void InputField_onSubmit(string inputFieldName, string value) { }
        /// <summary>InputField 结束编辑。</summary>
        protected virtual void InputField_onEndEdit(string inputFieldName, string value) { }
        /// <summary>TMP_InputField 值改变。</summary>
        protected virtual void TMP_InputField_OnValueChanged(string inputFieldName, string value) { }
        /// <summary>TMP_InputField 提交。</summary>
        protected virtual void TMP_InputField_onSubmit(string inputFieldName, string value) { }
        /// <summary>TMP_InputField 结束编辑。</summary>
        protected virtual void TMP_InputField_onEndEdit(string inputFieldName, string value) { }

        private void RegisterControl(UIBehaviour control)
        {
            string objName = control.gameObject.name;
            if (controlDic.TryGetValue(objName, out List<UIBehaviour> list))
            {
                if (!defaultNameList.Contains(objName)) list.Add(control);
            }
            else
            {
                controlDic.Add(objName, new List<UIBehaviour> { control });
            }

            if (control is Button button)
                Events.Listen(button.onClick, () => Button_OnClick(objName));
            else if (control is Toggle toggle)
                Events.Listen(toggle.onValueChanged, value => Toggle_OnValueChanged(objName, value));
            else if (control is Slider slider)
                Events.Listen(slider.onValueChanged, value => Slider_OnValueChanged(objName, value));
            else if (control is Scrollbar scrollbar)
                Events.Listen(scrollbar.onValueChanged, value => Scrollbar_OnValueChanged(objName, value));
            else if (control is TMP_Dropdown tmpDropdown)
                Events.Listen(tmpDropdown.onValueChanged, value => TMP_Dropdown_OnValueChanged(objName, value));
            else if (control is Dropdown dropdown)
                Events.Listen(dropdown.onValueChanged, value => Dropdown_OnValueChanged(objName, value));
            else if (control is TMP_InputField tmpInput)
            {
                Events.Listen(tmpInput.onValueChanged, value => TMP_InputField_OnValueChanged(objName, value));
                Events.Listen(tmpInput.onSubmit, value => TMP_InputField_onSubmit(objName, value));
                Events.Listen(tmpInput.onEndEdit, value => TMP_InputField_onEndEdit(objName, value));
            }
            else if (control is InputField input)
            {
                Events.Listen(input.onValueChanged, value => InputField_OnValueChanged(objName, value));
                Events.Listen(input.onSubmit, value => InputField_onSubmit(objName, value));
                Events.Listen(input.onEndEdit, value => InputField_onEndEdit(objName, value));
            }
        }

        /// <summary>
        /// 根据组件挂载对象的名字获得对应UI组件；找不到时返回 null 并记录错误。
        /// </summary>
        protected T GetControl<T>(string controlName) where T : UIBehaviour
        {
            if (TryGetControl(controlName, out T control)) return control;
            Debug.LogError($"“{controlName}”不存在!");
            return null;
        }

        /// <summary>尝试查找控件，不输出错误日志。</summary>
        protected bool TryGetControl<T>(string controlName, out T control) where T : UIBehaviour
        {
            control = null;
            if (!controlDic.TryGetValue(controlName, out List<UIBehaviour> list)) return false;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] is T typed)
                {
                    control = typed;
                    return true;
                }
            }
            return false;
        }

        protected void AddCustomEventListener(UIBehaviour control, EventTriggerType type, UnityAction<BaseEventData> callBack)
        {
            if (control == null || callBack == null) return;
            EventTrigger trigger = control.gameObject.GetOrAddComponent<EventTrigger>();
            EventTrigger.Entry entry = new EventTrigger.Entry();
            entry.eventID = type;
            entry.callback.AddListener(callBack);
            trigger.triggers.Add(entry);
            Events.Add(new DisposableAction(() =>
            {
                if (trigger != null) trigger.triggers.Remove(entry);
            }));
        }

        /// <summary>关闭时释放本次打开登记的事件，保留控件级绑定以便面板池复用。</summary>
        public void DisposeOpenEvents()
        {
            openEvents.Dispose();
        }

        /// <summary>销毁/归还池失败时释放全部事件，幂等。</summary>
        public void DisposeEventScopes()
        {
            openEvents.Dispose();
            events.Dispose();
        }
    }
}
