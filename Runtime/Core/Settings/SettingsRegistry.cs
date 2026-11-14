using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>
    /// 设置描述表：唯一扩展入口。新增设置项在这里追加一条描述（必要时同步加数据字段与 Applier 映射），
    /// UI、存储、校验、平台显隐、云同步分级都会自动跟随。
    /// <para>约束：key 全局唯一且以分区前缀开头；默认值必须通过归一化且在范围内；
    /// 每个描述都可用 <see cref="CreateDefaults"/> 读回默认值。EditMode 测试会强制校验这些规则。</para>
    /// </summary>
    public sealed class SettingsRegistry
    {
        private readonly Dictionary<string, SettingDescriptor> byKey =
            new Dictionary<string, SettingDescriptor>(StringComparer.Ordinal);
        private readonly List<SettingDescriptor> descriptors = new List<SettingDescriptor>();
        private readonly SettingsPlatform platform;

        /// <summary>当前平台（默认取 <see cref="SettingsPlatformProfile.Current"/>；测试可覆盖）。</summary>
        public SettingsPlatform Platform { get { return platform; } }

        /// <summary>全部描述（注册顺序）。</summary>
        public IReadOnlyList<SettingDescriptor> Descriptors { get { return descriptors; } }

        public SettingsRegistry(SettingsPlatform? platformOverride = null)
        {
            platform = platformOverride ?? SettingsPlatformProfile.Current;
            Build();
        }

        /// <summary>按 key 查描述；不存在返回 null。</summary>
        public SettingDescriptor Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            return byKey.TryGetValue(key, out SettingDescriptor descriptor) ? descriptor : null;
        }

        /// <summary>按分区/顺序返回描述快照。</summary>
        public IReadOnlyList<SettingDescriptor> Describe(SettingsCategory? category = null)
        {
            var result = new List<SettingDescriptor>();
            for (int i = 0; i < descriptors.Count; i++)
            {
                if (category.HasValue && descriptors[i].Category != category.Value) continue;
                result.Add(descriptors[i]);
            }
            result.Sort(CompareDescriptors);
            return result;
        }

        /// <summary>生成全默认数据（默认值来自描述表，保证与校验/UI 一致）。</summary>
        public SettingsData CreateDefaults()
        {
            var data = new SettingsData();
            data.EnsureDefaults();
            for (int i = 0; i < descriptors.Count; i++) descriptors[i].Set(data, descriptors[i].DefaultValue);
            // 子项 afterSet 会把预设标成 Custom；最后恢复预设默认值。
            SettingDescriptor preset = Get("graphics.preset");
            if (preset != null) preset.Set(data, preset.DefaultValue);
            return data;
        }

        /// <summary>逐项归一化并统计被修正的数量（损坏/手改/旧版本的越界值）。</summary>
        public int NormalizeAll(SettingsData data)
        {
            if (data == null) return 0;
            data.EnsureDefaults();
            int changed = 0;
            for (int i = 0; i < descriptors.Count; i++)
            {
                SettingDescriptor descriptor = descriptors[i];
                object currentValue = descriptor.Get(data);
                object normalized = descriptor.Normalize(currentValue);
                if (SettingsValueUtility.ValuesEqual(currentValue, normalized)) continue;
                descriptor.Set(data, normalized);
                changed++;
            }
            // 旧数据/手改可能出现 preset=Low 但子项仍是 High：非 Custom 时按预设展开，保证面板与实际一致。
            if (data.graphics.preset != QualityPreset.Custom) ApplyGraphicsPreset(data);
            return changed;
        }

        /// <summary>描述表自检：返回问题列表；空列表表示通过。CI/Editor 可直接调用。</summary>
        public IReadOnlyList<string> Validate()
        {
            var issues = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            SettingsData defaults = CreateDefaults();

            for (int i = 0; i < descriptors.Count; i++)
            {
                SettingDescriptor descriptor = descriptors[i];
                if (!seen.Add(descriptor.Key)) issues.Add("key 重复：" + descriptor.Key);
                if (!descriptor.Key.StartsWith(Prefix(descriptor.Category) + ".", StringComparison.Ordinal))
                    issues.Add("key 前缀与分区不一致：" + descriptor.Key + " / " + descriptor.Category);
                if (descriptor.Platforms == SettingsPlatform.None)
                    issues.Add("平台集合为空：" + descriptor.Key);
                if (!MatchesValueType(descriptor.ValueType, descriptor.DefaultValue))
                    issues.Add("默认值类型不符：" + descriptor.Key);
                if ((descriptor.ValueType == SettingsValueType.Float || descriptor.ValueType == SettingsValueType.Int)
                    && descriptor.Max > descriptor.Min)
                {
                    double defaultValue = Convert.ToDouble(descriptor.DefaultValue);
                    if (defaultValue < descriptor.Min || defaultValue > descriptor.Max)
                        issues.Add("默认值超出范围：" + descriptor.Key);
                }
                if (descriptor.ValueType == SettingsValueType.Enum
                    && (descriptor.Options == null || descriptor.Options.Count == 0 || !ContainsOption(descriptor)))
                    issues.Add("枚举默认值不在选项内：" + descriptor.Key);

                object normalized = descriptor.Normalize(descriptor.DefaultValue);
                if (!SettingsValueUtility.ValuesEqual(descriptor.DefaultValue, normalized))
                    issues.Add("默认值未通过归一化：" + descriptor.Key);
                object fromDefaults = descriptor.Get(defaults);
                if (!SettingsValueUtility.ValuesEqual(fromDefaults, descriptor.DefaultValue))
                    issues.Add("默认数据与描述不一致：" + descriptor.Key);
            }
            return issues;
        }

        #region 描述注册

        private void AddBool(string key, SettingsCategory category, bool defaultValue,
            Func<SettingsData, bool> getter, Action<SettingsData, bool> setter, int order,
            SettingsApplyMode mode = SettingsApplyMode.Immediate, SettingsPlatform platforms = SettingsPlatform.All,
            SettingsSyncScope syncScope = SettingsSyncScope.Device, Func<SettingsData, string> availability = null,
            Action<SettingsData> afterSet = null)
        {
            Add(new SettingDescriptor(key, category, SettingsValueType.Bool, defaultValue,
                data => getter(data), (data, value) => setter(data, (bool)value),
                mode, platforms, order, null, syncScope,
                normalizer: value => value is bool boolValue ? boolValue : defaultValue,
                availability: availability, afterSet: afterSet));
        }

        private void AddFloat(string key, SettingsCategory category, float defaultValue, float min, float max, float step,
            Func<SettingsData, float> getter, Action<SettingsData, float> setter, int order,
            SettingsApplyMode mode = SettingsApplyMode.Immediate, SettingsPlatform platforms = SettingsPlatform.All,
            SettingsSyncScope syncScope = SettingsSyncScope.Device, Func<SettingsData, string> availability = null,
            Action<SettingsData> afterSet = null)
        {
            Add(new SettingDescriptor(key, category, SettingsValueType.Float, defaultValue,
                data => getter(data), (data, value) => setter(data, (float)value),
                mode, platforms, order, null, syncScope, min, max, step,
                normalizer: value => NormalizeFloat(value, defaultValue, min, max),
                availability: availability, afterSet: afterSet));
        }

        private void AddInt(string key, SettingsCategory category, int defaultValue, int min, int max,
            Func<SettingsData, int> getter, Action<SettingsData, int> setter, int order,
            SettingsApplyMode mode = SettingsApplyMode.Immediate, SettingsPlatform platforms = SettingsPlatform.All,
            SettingsSyncScope syncScope = SettingsSyncScope.Device, Func<SettingsData, string> availability = null,
            Action<SettingsData> afterSet = null)
        {
            Add(new SettingDescriptor(key, category, SettingsValueType.Int, defaultValue,
                data => getter(data), (data, value) => setter(data, (int)value),
                mode, platforms, order, null, syncScope, min, max,
                normalizer: value => value is int intValue ? Mathf.Clamp(intValue, min, max) : defaultValue,
                availability: availability, afterSet: afterSet));
        }

        private void AddEnum<T>(string key, SettingsCategory category, T defaultValue,
            Func<SettingsData, T> getter, Action<SettingsData, T> setter, int order,
            SettingsApplyMode mode = SettingsApplyMode.Immediate, SettingsPlatform platforms = SettingsPlatform.All,
            SettingsSyncScope syncScope = SettingsSyncScope.Device, Func<SettingsData, string> availability = null,
            Action<SettingsData> afterSet = null) where T : struct, Enum
        {
            Add(new SettingDescriptor(key, category, SettingsValueType.Enum, defaultValue,
                data => getter(data), (data, value) => setter(data, (T)value),
                mode, platforms, order, null, syncScope,
                options: Enum.GetNames(typeof(T)),
                normalizer: value => value is T enumValue && Enum.IsDefined(typeof(T), enumValue) ? enumValue : defaultValue,
                availability: availability, afterSet: afterSet));
        }

        private void AddString(string key, SettingsCategory category, string defaultValue,
            Func<SettingsData, string> getter, Action<SettingsData, string> setter, int order,
            SettingsApplyMode mode = SettingsApplyMode.Immediate, SettingsPlatform platforms = SettingsPlatform.All,
            SettingsSyncScope syncScope = SettingsSyncScope.Device, Func<SettingsData, string> availability = null)
        {
            Add(new SettingDescriptor(key, category, SettingsValueType.String, defaultValue,
                data => getter(data), (data, value) => setter(data, value as string ?? defaultValue),
                mode, platforms, order, null, syncScope,
                normalizer: value => value as string ?? defaultValue,
                availability: availability));
        }

        private void AddBindings(string key, SettingsCategory category, int order)
        {
            Add(new SettingDescriptor(key, category, SettingsValueType.Object, Array.Empty<InputBinding>(),
                data => data.input.bindings, (data, value) => data.input.bindings = CloneBindings(value as InputBinding[]),
                SettingsApplyMode.Immediate, SettingsPlatform.All, order, null, SettingsSyncScope.Account,
                normalizer: value => CloneBindings(value as InputBinding[]),
                availability: null));
        }

        private void Add(SettingDescriptor descriptor)
        {
            if (byKey.ContainsKey(descriptor.Key))
                throw new InvalidOperationException("设置项 key 重复：" + descriptor.Key);
            byKey.Add(descriptor.Key, descriptor);
            descriptors.Add(descriptor);
        }

        #endregion

        #region 描述定义（P0 基线；新增项按分区/Order 追加）

        private void Build()
        {
            BuildAudio();
            BuildGraphics();
            BuildDisplay();
            BuildInput();
            BuildLanguage();
            BuildAccessibility();
            BuildGameplay();
            BuildPrivacy();
            BuildSystem();
        }

        private void BuildAudio()
        {
            AddFloat("audio.master", SettingsCategory.Audio, 0.8f, 0f, 1f, 0.01f,
                data => data.audio.master, (data, value) => data.audio.master = value, 0);
            AddFloat("audio.music", SettingsCategory.Audio, 0.7f, 0f, 1f, 0.01f,
                data => data.audio.music, (data, value) => data.audio.music = value, 10);
            AddFloat("audio.sfx", SettingsCategory.Audio, 0.8f, 0f, 1f, 0.01f,
                data => data.audio.sfx, (data, value) => data.audio.sfx = value, 20);
            AddFloat("audio.ui", SettingsCategory.Audio, 1f, 0f, 1f, 0.01f,
                data => data.audio.ui, (data, value) => data.audio.ui = value, 15);
            AddFloat("audio.voice", SettingsCategory.Audio, 1f, 0f, 1f, 0.01f,
                data => data.audio.voice, (data, value) => data.audio.voice = value, 25);
            AddBool("audio.muteAll", SettingsCategory.Audio, false,
                data => data.audio.muteAll, (data, value) => data.audio.muteAll = value, 30);
            AddBool("audio.muteWhenUnfocused", SettingsCategory.Audio, false,
                data => data.audio.muteWhenUnfocused, (data, value) => data.audio.muteWhenUnfocused = value, 35);
        }

        private void BuildGraphics()
        {
            AddEnum("graphics.preset", SettingsCategory.Graphics, QualityPreset.High,
                data => data.graphics.preset, (data, value) => data.graphics.preset = value, 0,
                mode: SettingsApplyMode.PreviewConfirm, afterSet: ApplyGraphicsPreset);
            AddEnum("graphics.antiAliasing", SettingsCategory.Graphics, AntiAliasingMode.x4,
                data => data.graphics.antiAliasing, (data, value) => data.graphics.antiAliasing = value, 10,
                mode: SettingsApplyMode.PreviewConfirm, afterSet: MarkGraphicsCustom);
            AddEnum("graphics.shadowQuality", SettingsCategory.Graphics, ShadowQualityLevel.High,
                data => data.graphics.shadowQuality, (data, value) => data.graphics.shadowQuality = value, 20,
                mode: SettingsApplyMode.PreviewConfirm, afterSet: MarkGraphicsCustom);
            AddEnum("graphics.textureQuality", SettingsCategory.Graphics, TextureQualityLevel.High,
                data => data.graphics.textureQuality, (data, value) => data.graphics.textureQuality = value, 30,
                mode: SettingsApplyMode.PreviewConfirm, afterSet: MarkGraphicsCustom);
            AddFloat("graphics.renderScale", SettingsCategory.Graphics, 1f, 0.5f, 1f, 0.05f,
                data => data.graphics.renderScale, (data, value) => data.graphics.renderScale = value, 40,
                afterSet: MarkGraphicsCustom);
            AddFloat("graphics.brightness", SettingsCategory.Graphics, 1f, 0.5f, 1.5f, 0.01f,
                data => data.graphics.brightness, (data, value) => data.graphics.brightness = value, 50);
            AddFloat("graphics.fieldOfView", SettingsCategory.Graphics, 60f, 50f, 90f, 1f,
                data => data.graphics.fieldOfView, (data, value) => data.graphics.fieldOfView = value, 60);
        }

        private void BuildDisplay()
        {
            SettingsPlatform desktop = SettingsPlatform.Desktop | SettingsPlatform.Editor;
            AddInt("display.monitor", SettingsCategory.Display, 0, 0, 7,
                data => data.display.monitor, (data, value) => data.display.monitor = value, -5,
                SettingsApplyMode.PreviewConfirm, desktop);
            AddEnum("display.mode", SettingsCategory.Display, DisplayModeSetting.Borderless,
                data => data.display.mode, (data, value) => data.display.mode = value, 0,
                SettingsApplyMode.PreviewConfirm, desktop);
            AddInt("display.width", SettingsCategory.Display, 0, 0, 7680,
                data => data.display.width, (data, value) => data.display.width = value, 10,
                SettingsApplyMode.PreviewConfirm, desktop);
            AddInt("display.height", SettingsCategory.Display, 0, 0, 4320,
                data => data.display.height, (data, value) => data.display.height = value, 20,
                SettingsApplyMode.PreviewConfirm, desktop);
            AddInt("display.refreshRate", SettingsCategory.Display, 0, 0, 360,
                data => data.display.refreshRate, (data, value) => data.display.refreshRate = value, 30,
                SettingsApplyMode.PreviewConfirm, desktop);
            AddBool("display.vsync", SettingsCategory.Display, true,
                data => data.display.vsync, (data, value) => data.display.vsync = value, 40,
                SettingsApplyMode.PreviewConfirm, desktop);
            AddEnum("display.frameRateLimit", SettingsCategory.Display, FrameRateLimit.Fps60,
                data => data.display.frameRateLimit, (data, value) => data.display.frameRateLimit = value, 50);
        }

        private void BuildInput()
        {
            AddFloat("input.mouseSensitivity", SettingsCategory.Input, 1f, 0.1f, 5f, 0.05f,
                data => data.input.mouseSensitivity, (data, value) => data.input.mouseSensitivity = value, 0,
                syncScope: SettingsSyncScope.Account);
            AddFloat("input.gamepadSensitivity", SettingsCategory.Input, 1f, 0.1f, 5f, 0.05f,
                data => data.input.gamepadSensitivity, (data, value) => data.input.gamepadSensitivity = value, 10,
                syncScope: SettingsSyncScope.Account);
            AddBool("input.invertY", SettingsCategory.Input, false,
                data => data.input.invertY, (data, value) => data.input.invertY = value, 20,
                syncScope: SettingsSyncScope.Account);
            AddFloat("input.gamepadDeadzone", SettingsCategory.Input, 0.2f, 0.1f, 0.4f, 0.01f,
                data => data.input.gamepadDeadzone, (data, value) => data.input.gamepadDeadzone = value, 30,
                syncScope: SettingsSyncScope.Account);
            AddBool("input.vibration", SettingsCategory.Input, true,
                data => data.input.vibration, (data, value) => data.input.vibration = value, 40,
                syncScope: SettingsSyncScope.Account);
            AddEnum("input.aimMode", SettingsCategory.Input, AimMode.Hold,
                data => data.input.aimMode, (data, value) => data.input.aimMode = value, 50,
                syncScope: SettingsSyncScope.Account);
            AddBindings("input.bindings", SettingsCategory.Input, 60);
        }

        private void BuildLanguage()
        {
            AddString("language.ui", SettingsCategory.Language, string.Empty,
                data => data.language.ui, (data, value) => data.language.ui = value, 0,
                syncScope: SettingsSyncScope.Account);
            AddBool("language.subtitles", SettingsCategory.Language, true,
                data => data.language.subtitles, (data, value) => data.language.subtitles = value, 10,
                syncScope: SettingsSyncScope.Account);
            AddEnum("language.subtitleSize", SettingsCategory.Language, SubtitleSize.Medium,
                data => data.language.subtitleSize, (data, value) => data.language.subtitleSize = value, 20,
                syncScope: SettingsSyncScope.Account);
        }

        private void BuildAccessibility()
        {
            AddFloat("accessibility.uiScale", SettingsCategory.Accessibility, 1f, 0.8f, 1.3f, 0.05f,
                data => data.accessibility.uiScale, (data, value) => data.accessibility.uiScale = value, 0,
                syncScope: SettingsSyncScope.Account);
            AddFloat("accessibility.screenShake", SettingsCategory.Accessibility, 1f, 0f, 1f, 0.05f,
                data => data.accessibility.screenShake, (data, value) => data.accessibility.screenShake = value, 10,
                syncScope: SettingsSyncScope.Account);
            AddFloat("accessibility.flashIntensity", SettingsCategory.Accessibility, 1f, 0f, 1f, 0.05f,
                data => data.accessibility.flashIntensity, (data, value) => data.accessibility.flashIntensity = value, 20,
                syncScope: SettingsSyncScope.Account);
            AddEnum("accessibility.colorblindMode", SettingsCategory.Accessibility, ColorBlindMode.Off,
                data => data.accessibility.colorblindMode, (data, value) => data.accessibility.colorblindMode = value, 30,
                syncScope: SettingsSyncScope.Account);
            AddBool("accessibility.reduceMotion", SettingsCategory.Accessibility, false,
                data => data.accessibility.reduceMotion, (data, value) => data.accessibility.reduceMotion = value, 40,
                syncScope: SettingsSyncScope.Account);
        }

        private void BuildGameplay()
        {
            AddEnum("gameplay.difficulty", SettingsCategory.Gameplay, DifficultyLevel.Normal,
                data => data.gameplay.difficulty, (data, value) => data.gameplay.difficulty = value, 0,
                SettingsApplyMode.NextLaunch, SettingsPlatform.All, SettingsSyncScope.Account);
            AddBool("gameplay.autosave", SettingsCategory.Gameplay, true,
                data => data.gameplay.autosave, (data, value) => data.gameplay.autosave = value, 10,
                syncScope: SettingsSyncScope.Account);
        }

        private void BuildPrivacy()
        {
            AddBool("privacy.telemetry", SettingsCategory.Privacy, false,
                data => data.privacy.telemetry, (data, value) => data.privacy.telemetry = value, 0,
                syncScope: SettingsSyncScope.Never);
            AddBool("privacy.crashReport", SettingsCategory.Privacy, false,
                data => data.privacy.crashReport, (data, value) => data.privacy.crashReport = value, 10,
                syncScope: SettingsSyncScope.Never);
            AddBool("privacy.personalizedAds", SettingsCategory.Privacy, false,
                data => data.privacy.personalizedAds, (data, value) => data.privacy.personalizedAds = value, 20,
                syncScope: SettingsSyncScope.Never);
            // 供 SettingsConsent 使用；面板跳过，不在普通设置页暴露。
            AddInt("privacy.termsVersion", SettingsCategory.Privacy, 0, -1, 1000000,
                data => data.privacy.termsVersion, (data, value) => data.privacy.termsVersion = value, 40,
                syncScope: SettingsSyncScope.Never);
            AddString("privacy.consentUtc", SettingsCategory.Privacy, string.Empty,
                data => data.privacy.consentUtc, (data, value) => data.privacy.consentUtc = value, 50,
                syncScope: SettingsSyncScope.Never);
        }

        private void BuildSystem()
        {
            AddBool("system.firstRunCompleted", SettingsCategory.System, false,
                data => data.system.firstRunCompleted, (data, value) => data.system.firstRunCompleted = value, 0,
                syncScope: SettingsSyncScope.Never);
        }

        #endregion

        #region 工具

        private static void MarkGraphicsCustom(SettingsData data)
        {
            if (data.graphics.preset != QualityPreset.Custom) data.graphics.preset = QualityPreset.Custom;
        }

        /// <summary>预设展开为具体子项：保证面板显示、预览和实际应用使用同一组值。</summary>
        private static void ApplyGraphicsPreset(SettingsData data)
        {
            switch (data.graphics.preset)
            {
                case QualityPreset.Low:
                    data.graphics.antiAliasing = AntiAliasingMode.Off;
                    data.graphics.shadowQuality = ShadowQualityLevel.Off;
                    data.graphics.textureQuality = TextureQualityLevel.Low;
                    data.graphics.renderScale = 0.75f;
                    break;
                case QualityPreset.Medium:
                    data.graphics.antiAliasing = AntiAliasingMode.x2;
                    data.graphics.shadowQuality = ShadowQualityLevel.Medium;
                    data.graphics.textureQuality = TextureQualityLevel.Medium;
                    data.graphics.renderScale = 0.9f;
                    break;
                case QualityPreset.High:
                    data.graphics.antiAliasing = AntiAliasingMode.x4;
                    data.graphics.shadowQuality = ShadowQualityLevel.High;
                    data.graphics.textureQuality = TextureQualityLevel.High;
                    data.graphics.renderScale = 1f;
                    break;
                case QualityPreset.Ultra:
                    data.graphics.antiAliasing = AntiAliasingMode.x8;
                    data.graphics.shadowQuality = ShadowQualityLevel.High;
                    data.graphics.textureQuality = TextureQualityLevel.High;
                    data.graphics.renderScale = 1f;
                    break;
            }
        }

        private static InputBinding[] CloneBindings(InputBinding[] source)
        {
            if (source == null || source.Length == 0) return Array.Empty<InputBinding>();
            var copy = new InputBinding[source.Length];
            for (int i = 0; i < source.Length; i++) copy[i] = source[i] == null ? null : source[i].Clone();
            return copy;
        }

        private static float NormalizeFloat(object value, float defaultValue, float min, float max)
        {
            if (!(value is float floatValue) || float.IsNaN(floatValue) || float.IsInfinity(floatValue)) return defaultValue;
            return Mathf.Clamp(floatValue, min, max);
        }

        private static bool MatchesValueType(SettingsValueType valueType, object value)
        {
            if (value == null) return false;
            switch (valueType)
            {
                case SettingsValueType.Bool: return value is bool;
                case SettingsValueType.Int: return value is int;
                case SettingsValueType.Float: return value is float;
                case SettingsValueType.Enum: return value.GetType().IsEnum;
                case SettingsValueType.String: return value is string;
                case SettingsValueType.Object: return true;
                default: return false;
            }
        }

        private static bool ContainsOption(SettingDescriptor descriptor)
        {
            if (descriptor.Options == null || descriptor.Options.Count == 0) return false;
            string name = descriptor.DefaultValue == null ? null : descriptor.DefaultValue.ToString();
            if (string.IsNullOrEmpty(name)) return false;
            for (int i = 0; i < descriptor.Options.Count; i++)
            {
                if (string.Equals(descriptor.Options[i], name, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static string Prefix(SettingsCategory category)
        {
            switch (category)
            {
                case SettingsCategory.Audio: return "audio";
                case SettingsCategory.Graphics: return "graphics";
                case SettingsCategory.Display: return "display";
                case SettingsCategory.Input: return "input";
                case SettingsCategory.Language: return "language";
                case SettingsCategory.Accessibility: return "accessibility";
                case SettingsCategory.Gameplay: return "gameplay";
                case SettingsCategory.Privacy: return "privacy";
                case SettingsCategory.System: return "system";
                default: return category.ToString().ToLowerInvariant();
            }
        }

        private static int CompareDescriptors(SettingDescriptor left, SettingDescriptor right)
        {
            int byCategory = ((int)left.Category).CompareTo((int)right.Category);
            if (byCategory != 0) return byCategory;
            int byOrder = left.Order.CompareTo(right.Order);
            if (byOrder != 0) return byOrder;
            return string.CompareOrdinal(left.Key, right.Key);
        }

        /// <summary>诊断用：把描述表导出为文本（key/类型/默认/平台/生效方式）。</summary>
        public string ExportTable()
        {
            var builder = new StringBuilder();
            IReadOnlyList<SettingDescriptor> list = Describe(null);
            for (int i = 0; i < list.Count; i++)
            {
                SettingDescriptor descriptor = list[i];
                builder.Append(descriptor.Key).Append(" | ")
                    .Append(descriptor.Category).Append(" | ")
                    .Append(descriptor.ValueType).Append(" | ")
                    .Append(descriptor.DefaultValue).Append(" | ")
                    .Append(descriptor.ApplyMode).Append(" | ")
                    .Append(descriptor.Platforms).AppendLine();
            }
            return builder.ToString();
        }

        #endregion
    }
}
