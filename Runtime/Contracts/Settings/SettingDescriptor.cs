using System;
using System.Collections.Generic;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>
    /// 单个设置项的元数据（描述表条目）：驱动校验、UI 生成、平台显隐与云同步分级。
    /// <para>新增设置项只需在 <c>SettingsRegistry</c> 追加一条描述，不要改面板/存储代码；
    /// 详细流程见 Runtime/Core/Settings/README.md。</para>
    /// <para>Get/Set 委托直接读写 <see cref="SettingsData"/> 的强类型字段，热路径不查字符串。</para>
    /// </summary>
    public sealed class SettingDescriptor
    {
        private readonly Func<SettingsData, object> getter;
        private readonly Action<SettingsData, object> setter;
        private readonly Func<object, object> normalizer;
        private readonly Func<SettingsData, string> availability;
        private readonly Action<SettingsData> afterSet;

        public string Key { get; }
        public SettingsCategory Category { get; }
        public SettingsValueType ValueType { get; }
        public object DefaultValue { get; }
        public SettingsApplyMode ApplyMode { get; }
        public SettingsPlatform Platforms { get; }
        public int Order { get; }
        public string LocKey { get; }
        public SettingsSyncScope SyncScope { get; }
        /// <summary>数值范围（仅 Float/Int 有效；Max 小于 Min 表示不限）。</summary>
        public float Min { get; }
        public float Max { get; }
        public float Step { get; }
        /// <summary>枚举项名称（顺序即 Dropdown 索引）；非枚举为 null。</summary>
        public IReadOnlyList<string> Options { get; }

        public SettingDescriptor(string key, SettingsCategory category, SettingsValueType valueType, object defaultValue,
            Func<SettingsData, object> getter, Action<SettingsData, object> setter,
            SettingsApplyMode applyMode = SettingsApplyMode.Immediate, SettingsPlatform platforms = SettingsPlatform.All,
            int order = 0, string locKey = null, SettingsSyncScope syncScope = SettingsSyncScope.Device,
            float min = 0f, float max = 0f, float step = 0f, IReadOnlyList<string> options = null,
            Func<object, object> normalizer = null, Func<SettingsData, string> availability = null,
            Action<SettingsData> afterSet = null)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("设置项 key 不能为空。", nameof(key));
            if (getter == null) throw new ArgumentNullException(nameof(getter));
            if (setter == null) throw new ArgumentNullException(nameof(setter));

            Key = key;
            Category = category;
            ValueType = valueType;
            DefaultValue = defaultValue;
            ApplyMode = applyMode;
            Platforms = platforms;
            Order = order;
            LocKey = string.IsNullOrEmpty(locKey) ? "settings." + key : locKey;
            SyncScope = syncScope;
            Min = min;
            Max = max;
            Step = step;
            Options = options;
            this.getter = getter;
            this.setter = setter;
            this.normalizer = normalizer;
            this.availability = availability;
            this.afterSet = afterSet;
        }

        /// <summary>读取当前值；异常（字段为空/类型不符）返回默认值，避免单个坏项拖垮全局。</summary>
        public object Get(SettingsData data)
        {
            if (data == null) return DefaultValue;
            try { return getter(data); }
            catch (Exception) { return DefaultValue; }
        }

        /// <summary>写入（调用方应先 <see cref="Normalize"/>）；afterSet 用于维护预设等派生字段。</summary>
        public void Set(SettingsData data, object value)
        {
            if (data == null) return;
            setter(data, value);
            if (afterSet != null) afterSet(data);
        }

        /// <summary>归一化：数值 clamp、NaN 回退默认、枚举越界回退默认；异常时回退默认。</summary>
        public object Normalize(object value)
        {
            if (normalizer == null) return value;
            try { return normalizer(value); }
            catch (Exception) { return DefaultValue; }
        }

        /// <summary>
        /// 当前平台/依赖是否可用。不可用时给出玩家可读原因（UI 置灰并显示）。
        /// </summary>
        public bool IsAvailable(SettingsPlatform platform, SettingsData data, out string reason)
        {
            if ((Platforms & platform) == 0)
            {
                reason = "当前平台不支持该设置";
                return false;
            }
            if (availability != null)
            {
                try
                {
                    reason = availability(data);
                    return string.IsNullOrEmpty(reason);
                }
                catch (Exception)
                {
                    reason = "可用性检查失败";
                    return false;
                }
            }
            reason = null;
            return true;
        }

        public override string ToString() { return Key; }
    }

    /// <summary>当前运行平台对应的 <see cref="SettingsPlatform"/> 组合（编辑器同时带 Editor + Desktop/Mobile）。</summary>
    public static class SettingsPlatformProfile
    {
        public static SettingsPlatform Current
        {
            get
            {
                SettingsPlatform platform = SettingsPlatform.Editor;
                if (Application.isMobilePlatform) platform |= SettingsPlatform.Mobile;
                else platform |= SettingsPlatform.Desktop;
                return platform;
            }
        }
    }

    /// <summary>设置值比较/拷贝工具：数组按元素比较，避免预览/回滚被引用相等误导。</summary>
    public static class SettingsValueUtility
    {
        public static bool ValuesEqual(object left, object right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null) return false;

            if (left is InputBinding leftBinding && right is InputBinding rightBinding)
            {
                return leftBinding.action == rightBinding.action
                    && leftBinding.context == rightBinding.context
                    && leftBinding.axis == rightBinding.axis
                    && leftBinding.positive == rightBinding.positive
                    && leftBinding.negative == rightBinding.negative;
            }

            if (left is Array leftArray && right is Array rightArray)
            {
                if (leftArray.Length != rightArray.Length) return false;
                for (int i = 0; i < leftArray.Length; i++)
                {
                    if (!ValuesEqual(leftArray.GetValue(i), rightArray.GetValue(i))) return false;
                }
                return true;
            }

            if (left is float leftFloat && right is float rightFloat)
                return leftFloat.Equals(rightFloat) || (float.IsNaN(leftFloat) && float.IsNaN(rightFloat));

            return left.Equals(right);
        }

        public static object CloneValue(object value)
        {
            if (value is InputBinding[] bindings)
            {
                var copy = new InputBinding[bindings.Length];
                for (int i = 0; i < bindings.Length; i++) copy[i] = bindings[i] == null ? null : bindings[i].Clone();
                return copy;
            }
            if (value is Array array) return array.Clone();
            return value;
        }
    }
}
