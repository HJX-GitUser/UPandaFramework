using System;
using System.Collections.Generic;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>设置行的控件类型；<see cref="Key"/> 复用按钮行，额外显示当前按键。</summary>
    public enum UISettingControl
    {
        Toggle,
        Slider,
        Dropdown,
        Button,
        Key,
        ReadOnly
    }

    /// <summary>
    /// 设置行的视图描述（不是设置服务的数据模型）。
    /// <para>业务 Presenter 把 <c>ISettingsService</c> 的值包装成 Observable，再生成描述表；
    /// UI 框架只负责按描述创建/复用一个行视图，不反向依赖任何设置服务。</para>
    /// <para>新增一个设置项只需新增一条描述并指定控件类型，不需要改面板代码；
    /// 新增一类控件则注册新的行预制体（见 <see cref="UISettingList"/>）。</para>
    /// </summary>
    public sealed class UISettingItem
    {
        /// <summary>稳定键（建议 settings.&lt;category&gt;.&lt;item&gt;），用于诊断与定位。</summary>
        public string Key { get; set; }
        /// <summary>行标题。</summary>
        public string Label { get; set; }
        /// <summary>行说明；为空时说明控件隐藏。</summary>
        public string Description { get; set; }
        /// <summary>控件类型。</summary>
        public UISettingControl Control { get; set; }

        /// <summary>Toggle 的值。</summary>
        public BoolObservable BoolValue { get; set; }
        /// <summary>Slider 的值。</summary>
        public FloatObservable FloatValue { get; set; }
        /// <summary>Dropdown 的选中索引。</summary>
        public IntObservable IntValue { get; set; }
        /// <summary>只读文本/按钮副文本/按键名的值。</summary>
        public StringObservable StringValue { get; set; }

        /// <summary>Dropdown 选项文本。</summary>
        public IReadOnlyList<string> Options { get; set; }
        /// <summary>Slider 最小值。</summary>
        public float Min { get; set; }
        /// <summary>Slider 最大值。</summary>
        public float Max { get; set; } = 1f;
        /// <summary>Slider 步进；0 表示连续。</summary>
        public float Step { get; set; }
        /// <summary>Slider 数值文本格式化；为空时 0–1 显示百分比，其余显示两位小数。</summary>
        public Func<float, string> Format { get; set; }
        /// <summary>按钮/键位行的点击动作。</summary>
        public Action Action { get; set; }
        /// <summary>运行期可用性判定（设备不支持、受依赖项限制等）；为空表示可用。</summary>
        public Func<bool> CanInteract { get; set; }

        /// <summary>把数值格式化成显示文本；供自定义行或 Presenter 复用。</summary>
        public string FormatValue(float value)
        {
            if (Format != null) return Format(value);
            if (Mathf.Approximately(Min, 0f) && Mathf.Approximately(Max, 1f))
                return Mathf.RoundToInt(Mathf.Clamp01(value) * 100f) + "%";
            return value.ToString("0.##");
        }

        /// <summary>检查描述是否足以创建行；错误信息直接进日志/校验器。</summary>
        public bool IsValid(out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(Key)) error = "设置项 Key 不能为空。";
            else if (Control == UISettingControl.Toggle && BoolValue == null) error = "Toggle 行缺少 BoolValue。";
            else if (Control == UISettingControl.Slider && FloatValue == null) error = "Slider 行缺少 FloatValue。";
            else if (Control == UISettingControl.Slider && Max < Min) error = "Slider 的 Max 小于 Min。";
            else if (Control == UISettingControl.Dropdown && IntValue == null) error = "Dropdown 行缺少 IntValue。";
            else if (Control == UISettingControl.Dropdown && (Options == null || Options.Count == 0)) error = "Dropdown 行缺少 Options。";
            else if ((Control == UISettingControl.Button || Control == UISettingControl.Key) && Action == null) error = "按钮/键位行缺少 Action。";
            else if (Control == UISettingControl.ReadOnly && StringValue == null) error = "只读行缺少 StringValue。";
            else if (Control == UISettingControl.Key && StringValue == null) error = "键位行缺少按键文本。";
            return error == null;
        }

        public static UISettingItem CreateToggle(string key, string label, BoolObservable value, string description = null)
        {
            return new UISettingItem
            {
                Key = key, Label = label, Description = description,
                Control = UISettingControl.Toggle, BoolValue = value
            };
        }

        public static UISettingItem CreateSlider(string key, string label, FloatObservable value,
            float min = 0f, float max = 1f, float step = 0f, Func<float, string> format = null, string description = null)
        {
            return new UISettingItem
            {
                Key = key, Label = label, Description = description,
                Control = UISettingControl.Slider, FloatValue = value,
                Min = min, Max = max, Step = step, Format = format
            };
        }

        public static UISettingItem CreateDropdown(string key, string label, IntObservable value,
            IReadOnlyList<string> options, string description = null)
        {
            return new UISettingItem
            {
                Key = key, Label = label, Description = description,
                Control = UISettingControl.Dropdown, IntValue = value, Options = options
            };
        }

        public static UISettingItem CreateButton(string key, string label, Action action,
            StringObservable valueText = null, string description = null)
        {
            return new UISettingItem
            {
                Key = key, Label = label, Description = description,
                Control = UISettingControl.Button, Action = action, StringValue = valueText
            };
        }

        public static UISettingItem CreateKey(string key, string label, StringObservable keyText, Action rebind,
            string description = null)
        {
            return new UISettingItem
            {
                Key = key, Label = label, Description = description,
                Control = UISettingControl.Key, StringValue = keyText, Action = rebind
            };
        }

        public static UISettingItem CreateReadOnly(string key, string label, StringObservable text, string description = null)
        {
            return new UISettingItem
            {
                Key = key, Label = label, Description = description,
                Control = UISettingControl.ReadOnly, StringValue = text
            };
        }
    }
}
