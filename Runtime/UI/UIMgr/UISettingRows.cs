using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace UPandaGF
{
    /// <summary>Toggle 行：双向绑定 <see cref="UISettingItem.BoolValue"/>。</summary>
    public class UISettingToggleRow : UISettingRow
    {
        [SerializeField] private Toggle toggle;

        protected override void ResolveReferences()
        {
            if (toggle == null) toggle = FindInChildren<Toggle>(transform);
            if (toggle == null) Debug.LogError("[UISettingToggleRow] 行预制体缺少 Toggle。");
        }

        protected override void BindValue()
        {
            if (toggle == null || Item.BoolValue == null) return;
            Scope.Add(UIBinder.BindToggle(toggle, Item.BoolValue));
            RefreshInteractable();
        }

        protected override void RefreshInteractable()
        {
            ApplyInteractable(toggle);
        }
    }

    /// <summary>
    /// Slider 行：双向绑定 <see cref="UISettingItem.FloatValue"/>，支持 Min/Max/Step 与数值文本。
    /// 拖动时按 Step 吸附；无 Step 时保持连续值。
    /// </summary>
    public class UISettingSliderRow : UISettingRow
    {
        [SerializeField] private Slider slider;
        [SerializeField] private Component valueLabel;

        protected override void ResolveReferences()
        {
            if (slider == null) slider = FindInChildren<Slider>(transform);
            if (valueLabel == null) valueLabel = FindText("Value", false);
            if (slider == null) Debug.LogError("[UISettingSliderRow] 行预制体缺少 Slider。");
        }

        protected override void BindValue()
        {
            if (slider == null || Item.FloatValue == null) return;

            slider.minValue = Item.Min;
            slider.maxValue = Mathf.Max(Item.Min, Item.Max);
            slider.wholeNumbers = false;

            float min = slider.minValue;
            float max = slider.maxValue;
            float step = Mathf.Max(0f, Item.Step);

            float Snap(float raw)
            {
                float clamped = Mathf.Clamp(raw, min, max);
                if (step <= 0f) return clamped;
                return Mathf.Clamp(min + Mathf.Round((clamped - min) / step) * step, min, max);
            }

            void ShowValue(float value)
            {
                if (valueLabel != null) SetText(valueLabel, Item.FormatValue(value));
            }

            slider.SetValueWithoutNotify(Snap(Item.FloatValue.Value));
            ShowValue(slider.value);

            UnityAction<float> onUIChanged = raw =>
            {
                float next = Snap(raw);
                if (!Mathf.Approximately(raw, next)) slider.SetValueWithoutNotify(next);
                if (!Mathf.Approximately(Item.FloatValue.Value, next)) Item.FloatValue.Value = next;
                ShowValue(next);
            };
            slider.onValueChanged.AddListener(onUIChanged);
            Scope.Add(new DisposableAction(() =>
            {
                if (slider != null) slider.onValueChanged.RemoveListener(onUIChanged);
            }));
            Scope.Add(Item.FloatValue.Subscribe(value =>
            {
                if (slider == null) return;
                float next = Snap(value);
                slider.SetValueWithoutNotify(next);
                ShowValue(next);
            }, true));

            RefreshInteractable();
        }

        protected override void RefreshInteractable()
        {
            ApplyInteractable(slider);
        }
    }

    /// <summary>Dropdown 行：双向绑定选中索引，选项来自 <see cref="UISettingItem.Options"/>。</summary>
    public class UISettingDropdownRow : UISettingRow
    {
        [SerializeField] private Dropdown dropdown;

        protected override void ResolveReferences()
        {
            if (dropdown == null) dropdown = FindInChildren<Dropdown>(transform);
            if (dropdown == null) Debug.LogError("[UISettingDropdownRow] 行预制体缺少 Dropdown。");
        }

        protected override void BindValue()
        {
            if (dropdown == null || Item.IntValue == null) return;

            List<string> options = new List<string>();
            if (Item.Options != null)
                for (int i = 0; i < Item.Options.Count; i++)
                    options.Add(Item.Options[i] ?? string.Empty);

            dropdown.ClearOptions();
            if (options.Count == 0)
            {
                Debug.LogWarning("[UISettingDropdownRow] 设置项 “" + Item.Key + "” 没有可选项。");
                return;
            }
            dropdown.AddOptions(options);

            int lastIndex = options.Count - 1;
            dropdown.SetValueWithoutNotify(Mathf.Clamp(Item.IntValue.Value, 0, lastIndex));

            UnityAction<int> onUIChanged = value =>
            {
                if (Item.IntValue.Value != value) Item.IntValue.Value = value;
            };
            dropdown.onValueChanged.AddListener(onUIChanged);
            Scope.Add(new DisposableAction(() =>
            {
                if (dropdown != null) dropdown.onValueChanged.RemoveListener(onUIChanged);
            }));
            Scope.Add(Item.IntValue.Subscribe(value =>
            {
                if (dropdown != null) dropdown.SetValueWithoutNotify(Mathf.Clamp(value, 0, lastIndex));
            }, true));

            RefreshInteractable();
        }

        protected override void RefreshInteractable()
        {
            ApplyInteractable(dropdown);
        }
    }

    /// <summary>
    /// 按钮/键位行：点击执行 <see cref="UISettingItem.Action"/>；有 StringValue 时显示在 "Value" 文本上。
    /// 键位捕获、危险操作二次确认由业务在 Action 内处理。
    /// </summary>
    public class UISettingButtonRow : UISettingRow
    {
        [SerializeField] private Button button;
        [SerializeField] private Component valueLabel;

        protected override void ResolveReferences()
        {
            if (button == null) button = FindInChildren<Button>(transform);
            if (valueLabel == null) valueLabel = FindText("Value", false);
            if (button == null) Debug.LogError("[UISettingButtonRow] 行预制体缺少 Button。");
        }

        protected override void BindValue()
        {
            if (button == null) return;

            Scope.Add(UIBinder.BindButton(button, () => Item.Action?.Invoke()));
            if (valueLabel != null && Item.StringValue != null)
                Scope.Add(Item.StringValue.Subscribe(value => SetText(valueLabel, value), true));

            RefreshInteractable();
        }

        protected override void RefreshInteractable()
        {
            ApplyInteractable(button);
        }
    }

    /// <summary>只读行：显示不可交互的文本（版本号、平台限制说明等）。</summary>
    public class UISettingReadOnlyRow : UISettingRow
    {
        [SerializeField] private Component valueLabel;

        protected override void ResolveReferences()
        {
            if (valueLabel == null) valueLabel = FindText("Value", false);
        }

        protected override void BindValue()
        {
            if (valueLabel == null || Item.StringValue == null) return;
            Scope.Add(Item.StringValue.Subscribe(value => SetText(valueLabel, value), true));
        }
    }
}
