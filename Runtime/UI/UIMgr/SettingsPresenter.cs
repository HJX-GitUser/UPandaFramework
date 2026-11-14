using System;
using System.Collections.Generic;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>
    /// 设置面板 Presenter：把 <see cref="ISettingsService"/> 的描述表包装成 <see cref="UISettingItem"/>，
    /// 负责值双向绑定、分区列表、键位捕获与恢复默认。
    /// <para>面板只负责布局与生命周期；新增设置项时本类通常不需要改（由描述表驱动），
    /// 只有全新的控件类型才需要扩展 <see cref="UISettingItem"/> 与行预制体。</para>
    /// <para>无本地化系统时用 <see cref="SettingsText"/> 兜底；业务可传 localize 覆盖文案。</para>
    /// </summary>
    public sealed class SettingsPresenter : IDisposable
    {
        private readonly ISettingsService settings;
        private readonly IInputService input;
        private readonly ILocalizationService localization;
        private readonly Func<string, string> localize;
        private readonly Dictionary<string, Action<object>> refreshers =
            new Dictionary<string, Action<object>>(StringComparer.Ordinal);
        private readonly List<IDisposable> owned = new List<IDisposable>();
        private readonly List<SettingsCategory> categories = new List<SettingsCategory>();

        private string captureAction;
        private string captureContext;
        private IDisposable captureScope;
        private bool disposed;

        /// <summary>键位覆盖层变化（需要重建当前列表）。</summary>
        public event Action BindingsChanged;
        /// <summary>改键/取消/冲突的提示文本。</summary>
        public event Action<string> RebindMessage;

        public SettingsPresenter(ISettingsService settings, IInputService input,
            ILocalizationService localization = null, Func<string, string> localize = null)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.input = input;
            this.localization = localization;
            this.localize = localize;

            BuildCategories();
            settings.Changed += OnSettingsChanged;
        }

        /// <summary>有可用设置项的分区（移动端会自动隐藏只有桌面项的“显示”）。</summary>
        public IReadOnlyList<SettingsCategory> Categories { get { return categories; } }

        /// <summary>是否正在等待玩家按键。</summary>
        public bool IsCapturing { get { return !string.IsNullOrEmpty(captureAction); } }

        /// <summary>当前改键提示。</summary>
        public string CaptureHint { get; private set; }

        /// <summary>
        /// 生成某分区的设置行描述。每次调用会释放上一次生成的行订阅。
        /// </summary>
        public IReadOnlyList<UISettingItem> BuildItems(SettingsCategory category)
        {
            ClearItems();
            var items = new List<UISettingItem>();
            bool monitorRowAdded = false;
            if (category == SettingsCategory.Display)
            {
                UISettingItem monitor = BuildMonitor();
                if (monitor != null) { items.Add(monitor); monitorRowAdded = true; }
                UISettingItem resolution = BuildResolution();
                if (resolution != null) items.Add(resolution);
            }

            IReadOnlyList<SettingDescriptor> descriptors = settings.Describe(category);
            for (int i = 0; i < descriptors.Count; i++)
            {
                SettingDescriptor descriptor = descriptors[i];
                if (descriptor.Key == "input.bindings") continue; // 键位用逐条行展示
                if (descriptor.Key == "privacy.termsVersion" || descriptor.Key == "privacy.consentUtc")
                    continue; // 同意状态由 SettingsConsent 管理，不在普通设置页暴露
                if (descriptor.Key == "display.monitor" && monitorRowAdded) continue;
                if (descriptor.Key == "display.width" || descriptor.Key == "display.height"
                    || descriptor.Key == "display.refreshRate")
                    continue; // 合并为“分辨率”一行，避免三个独立滑杆产生中间态
                UISettingItem item = BuildDescriptorItem(descriptor);
                if (item != null) items.Add(item);
            }

            if (category == SettingsCategory.Input && input != null)
            {
                BuildBindingItems(items);
                items.Add(UISettingItem.CreateButton("input.bindings.reset",
                    L("settings.input.bindings.reset.label", "恢复默认键位"),
                    () => settings.Reset(SettingsScope.Item, "input.bindings"), null,
                    L("settings.input.bindings.reset.tooltip", "清除玩家覆盖层，回到默认绑定")));
            }
            if (category == SettingsCategory.Audio)
            {
                items.Add(UISettingItem.CreateButton("audio.preview",
                    L("settings.audio.preview.label", "试听音效"),
                    () => SettingsAudioPreview.Play(
                        settings.Get<float>("audio.sfx"),
                        settings.Get<float>("audio.master"),
                        settings.Get<bool>("audio.muteAll")),
                    null, L("settings.audio.preview.tooltip", "按当前主音量/音效音量播放短音")));
            }
            return items;
        }

        /// <summary>开始捕获新键；期间压入 Rebind 上下文，屏蔽普通操作。</summary>
        public void BeginRebind(string action, string context)
        {
            if (disposed || input == null) return;
            CancelRebind(null);
            captureAction = action;
            captureContext = context;
            captureScope = input.PushContext(InputContexts.Rebind);
            CaptureHint = L("settings.rebind.hint", "请按下新按键（Esc 取消）");
            RebindMessage?.Invoke(CaptureHint);
        }

        /// <summary>每帧调用：捕获到按键后校验并写回覆盖层。</summary>
        public void PollRebind()
        {
            if (!IsCapturing || input == null) return;
            if (!input.TryCaptureKey(out KeyCode key)) return;

            if (key == KeyCode.Escape)
            {
                CancelRebind(L("settings.rebind.canceled", "已取消改键"));
                return;
            }

            InputBinding current = FindBinding(captureAction, captureContext);
            if (current == null)
            {
                CancelRebind(L("settings.rebind.missing", "绑定不存在"));
                return;
            }

            InputBinding candidate = current.Clone();
            candidate.positive = key;
            candidate.negative = KeyCode.None;
            candidate.axis = null;

            InputRebindResult result = input.TryRebind(candidate);
            if (result == InputRebindResult.Success)
            {
                settings.Set("input.bindings", input.ExportOverrides());
                string keyText = KeyText(candidate);
                string message = localization != null
                    ? localization.Format("settings.rebind.success", keyText)
                    : null;
                if (string.IsNullOrEmpty(message) || message == "settings.rebind.success" || message == keyText)
                    message = L("settings.rebind.success", "已绑定：") + keyText;
                FinishRebind(message);
            }
            else
            {
                CaptureHint = ResultText(result) + L("settings.rebind.retryHint", "，请按其它键（Esc 取消）");
                RebindMessage?.Invoke(CaptureHint);
            }
        }

        /// <summary>取消当前捕获（不会改键）。</summary>
        public void CancelRebind(string message)
        {
            DisposeCaptureScope();
            captureAction = null;
            captureContext = null;
            CaptureHint = null;
            if (!string.IsNullOrEmpty(message)) RebindMessage?.Invoke(message);
        }

        private void FinishRebind(string message)
        {
            DisposeCaptureScope();
            captureAction = null;
            captureContext = null;
            CaptureHint = null;
            RebindMessage?.Invoke(message);
        }

        private void DisposeCaptureScope()
        {
            IDisposable scope = captureScope;
            captureScope = null;
            scope?.Dispose();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            settings.Changed -= OnSettingsChanged;
            CancelRebind(null);
            ClearItems();
        }

        #region 行生成

        private UISettingItem BuildDescriptorItem(SettingDescriptor descriptor)
        {
            bool available = settings.IsAvailable(descriptor.Key, out string reason);
            string label = Label(descriptor);
            string description = available ? null : reason;

            switch (descriptor.ValueType)
            {
                case SettingsValueType.Bool:
                    return BuildBool(descriptor, label, description);
                case SettingsValueType.Float:
                    return BuildFloat(descriptor, label, description);
                case SettingsValueType.Int:
                    return BuildInt(descriptor, label, description);
                case SettingsValueType.Enum:
                    return BuildEnum(descriptor, label, description);
                case SettingsValueType.String:
                    if (descriptor.Key == "language.ui") return BuildLanguage(descriptor, label, description);
                    return BuildString(descriptor, label, description);
                default:
                    return null;
            }
        }

        private UISettingItem BuildBool(SettingDescriptor descriptor, string label, string description)
        {
            object raw = descriptor.Get(settings.Current);
            var observable = new BoolObservable(raw is bool value && value);
            UISettingItem item = UISettingItem.CreateToggle(descriptor.Key, label, observable, description);
            item.CanInteract = () => settings.IsAvailable(descriptor.Key, out _);

            IDisposable subscription = observable.Subscribe(next => settings.Set(descriptor.Key, next));
            if (subscription != null) owned.Add(subscription);
            refreshers[descriptor.Key] = next => observable.Value = next is bool boolean && boolean;
            return item;
        }

        private UISettingItem BuildFloat(SettingDescriptor descriptor, string label, string description)
        {
            object raw = descriptor.Get(settings.Current);
            var observable = new FloatObservable(raw is float value ? value : 0f);
            bool percent = descriptor.Min <= 0.001f && descriptor.Max >= 0.999f && descriptor.Max <= 1.001f;
            Func<float, string> format = percent
                ? (Func<float, string>)(v => Mathf.RoundToInt(Mathf.Clamp01(v) * 100f) + "%")
                : v => v.ToString("0.##");

            UISettingItem item = UISettingItem.CreateSlider(descriptor.Key, label, observable,
                descriptor.Min, descriptor.Max, descriptor.Step, format, description);
            item.CanInteract = () => settings.IsAvailable(descriptor.Key, out _);

            IDisposable subscription = observable.Subscribe(next => settings.Set(descriptor.Key, next));
            if (subscription != null) owned.Add(subscription);
            refreshers[descriptor.Key] = next => observable.Value = next is float number ? number : 0f;
            return item;
        }

        private UISettingItem BuildInt(SettingDescriptor descriptor, string label, string description)
        {
            object raw = descriptor.Get(settings.Current);
            int value = raw is int integer ? integer : 0;
            var observable = new FloatObservable(value);
            Func<float, string> format = number => Mathf.RoundToInt(number).ToString();

            UISettingItem item = UISettingItem.CreateSlider(descriptor.Key, label, observable,
                descriptor.Min, descriptor.Max, descriptor.Step > 0f ? descriptor.Step : 1f, format, description);
            item.CanInteract = () => settings.IsAvailable(descriptor.Key, out _);

            IDisposable subscription = observable.Subscribe(next => settings.Set(descriptor.Key, Mathf.RoundToInt(next)));
            if (subscription != null) owned.Add(subscription);
            refreshers[descriptor.Key] = next => observable.Value = next is int integerValue ? integerValue : 0;
            return item;
        }

        private UISettingItem BuildEnum(SettingDescriptor descriptor, string label, string description)
        {
            Type enumType = descriptor.DefaultValue.GetType();
            Array values = Enum.GetValues(enumType);
            object currentValue = descriptor.Get(settings.Current);
            int index = Mathf.Max(0, Array.IndexOf(values, currentValue));
            var options = new List<string>(values.Length);
            for (int i = 0; i < values.Length; i++) options.Add(OptionText(descriptor, values.GetValue(i)));

            var selected = new IntObservable(index);
            IDisposable subscription = selected.Subscribe(value =>
            {
                if (value < 0 || value >= values.Length) return;
                if (!settings.IsAvailable(descriptor.Key, out _)) return;
                settings.Set<object>(descriptor.Key, values.GetValue(value));
            });
            if (subscription != null) owned.Add(subscription);

            UISettingItem item = UISettingItem.CreateDropdown(descriptor.Key, label, selected, options,
                string.IsNullOrEmpty(description)
                    ? L("settings.description.select", "点击选择")
                    : description);
            item.CanInteract = () => settings.IsAvailable(descriptor.Key, out _);
            refreshers[descriptor.Key] = next =>
            {
                int value = Mathf.Clamp(Array.IndexOf(values, next), 0, Mathf.Max(0, values.Length - 1));
                if (selected.Value != value) selected.Value = value;
            };
            return item;
        }

        private UISettingItem BuildString(SettingDescriptor descriptor, string label, string description)
        {
            object raw = descriptor.Get(settings.Current);
            var text = new StringObservable(raw as string ?? string.Empty);
            UISettingItem item = UISettingItem.CreateReadOnly(descriptor.Key, label, text,
                string.IsNullOrEmpty(description)
                    ? L("settings.description.readonly", "由本地化系统/平台提供")
                    : description);
            refreshers[descriptor.Key] = next => text.Value = next as string ?? string.Empty;
            return item;
        }

        /// <summary>
        /// 分辨率一行：把 Screen.resolutions 去重排序后循环选择，同时写入宽/高/刷新率。
        /// 三项描述仍然保留在数据层，只是面板不再显示三个独立滑杆。
        /// </summary>
        private UISettingItem BuildMonitor()
        {
            if (Application.isMobilePlatform) return null;
            List<DisplayInfo> displays = CollectDisplays();
            if (displays.Count == 0) return null;

            var options = new List<string>(displays.Count);
            for (int i = 0; i < displays.Count; i++)
            {
                string name = string.IsNullOrWhiteSpace(displays[i].name) ? "Display " + (i + 1) : displays[i].name;
                options.Add(name + " (" + displays[i].width + "×" + displays[i].height + ")");
            }

            int current = Mathf.Clamp(settings.Get<int>("display.monitor"), 0, displays.Count - 1);
            var selected = new IntObservable(current);
            IDisposable subscription = selected.Subscribe(value =>
            {
                if (value >= 0 && value < displays.Count) settings.Set("display.monitor", value);
            });
            if (subscription != null) owned.Add(subscription);

            UISettingItem item = UISettingItem.CreateDropdown("display.monitor",
                L("settings.display.monitor.label", "显示器"), selected, options,
                L("settings.display.monitor.tooltip", "选择输出显示器"));
            item.CanInteract = () => settings.IsAvailable("display.monitor", out _);
            refreshers["display.monitor"] = next =>
            {
                int value = next is int index ? Mathf.Clamp(index, 0, displays.Count - 1) : 0;
                if (selected.Value != value) selected.Value = value;
            };
            return item;
        }

        private static List<DisplayInfo> CollectDisplays()
        {
            var displays = new List<DisplayInfo>();
            try { Screen.GetDisplayLayout(displays); }
            catch (Exception) { displays.Clear(); }
            return displays;
        }

        private UISettingItem BuildResolution()
        {
            if (Application.isMobilePlatform) return null;
            List<Resolution> choices = CollectResolutions();
            if (choices.Count == 0) return null;
            if (!settings.IsAvailable("display.width", out _)) return null;

            var options = new List<string>(choices.Count + 1);
            options.Add(L("settings.display.resolution.auto", "自动"));
            for (int i = 0; i < choices.Count; i++) options.Add(FormatResolution(choices[i]));

            int current = FindResolutionIndex(choices);
            var selected = new IntObservable(current < 0 ? 0 : current + 1);
            bool applying = false;
            IDisposable subscription = selected.Subscribe(value =>
            {
                if (applying || value < 0 || value >= options.Count) return;
                applying = true;
                if (value == 0)
                {
                    settings.Set("display.refreshRate", 0);
                    settings.Set("display.height", 0);
                    settings.Set("display.width", 0);
                }
                else
                {
                    Resolution target = choices[value - 1];
                    // 先刷新率，再高/宽；最后一次 Set 触发最终 Apply，避免中间态。
                    settings.Set("display.refreshRate", target.refreshRate);
                    settings.Set("display.height", target.height);
                    settings.Set("display.width", target.width);
                }
                applying = false;
            });
            if (subscription != null) owned.Add(subscription);

            UISettingItem item = UISettingItem.CreateDropdown("display.resolution",
                L("settings.display.resolution.label", "分辨率"), selected, options,
                L("settings.display.resolution.tooltip", "从平台支持的分辨率中选择"));
            item.CanInteract = () => settings.IsAvailable("display.width", out _);
            refreshers["display.width"] = _ =>
            {
                if (applying) return;
                int found = FindResolutionIndex(choices);
                int targetIndex = found < 0 ? 0 : found + 1;
                if (selected.Value != targetIndex) selected.Value = targetIndex;
            };
            return item;
        }

        private static string FormatResolution(Resolution resolution)
        {
            return resolution.width + "×" + resolution.height + "@" + resolution.refreshRate;
        }

        private List<Resolution> CollectResolutions()
        {
            var result = new List<Resolution>();
            Resolution[] available = Screen.resolutions;
            for (int i = 0; i < available.Length; i++)
            {
                Resolution candidate = available[i];
                if (candidate.width <= 0 || candidate.height <= 0) continue;
                bool exists = false;
                for (int j = 0; j < result.Count; j++)
                {
                    if (result[j].width == candidate.width
                        && result[j].height == candidate.height
                        && result[j].refreshRate == candidate.refreshRate)
                    {
                        exists = true;
                        break;
                    }
                }
                if (!exists) result.Add(candidate);
            }
            result.Sort((left, right) =>
            {
                int compare = left.width.CompareTo(right.width);
                if (compare != 0) return compare;
                compare = left.height.CompareTo(right.height);
                return compare != 0 ? compare : left.refreshRate.CompareTo(right.refreshRate);
            });
            if (result.Count == 0)
            {
                Resolution current = Screen.currentResolution;
                if (current.width > 0 && current.height > 0) result.Add(current);
            }
            return result;
        }

        private int FindResolutionIndex(List<Resolution> choices)
        {
            int width = settings.Get<int>("display.width");
            int height = settings.Get<int>("display.height");
            int refresh = settings.Get<int>("display.refreshRate");
            for (int i = 0; i < choices.Count; i++)
            {
                if (choices[i].width == width && choices[i].height == height
                    && (refresh <= 0 || choices[i].refreshRate == refresh))
                    return i;
            }
            return -1;
        }

        /// <summary>语言行：跟随系统 + 当前 catalog 中已加载的语言；点击循环切换。</summary>
        private UISettingItem BuildLanguage(SettingDescriptor descriptor, string label, string description)
        {
            if (localization == null || localization.Locales.Count == 0)
                return BuildString(descriptor, label, description);

            var codes = new List<string> { string.Empty };
            var options = new List<string> { L("settings.language.system", "跟随系统") };
            for (int i = 0; i < localization.Locales.Count; i++)
            {
                LocaleInfo locale = localization.Locales[i];
                if (locale == null || string.IsNullOrEmpty(locale.code)) continue;
                codes.Add(locale.code);
                options.Add(string.IsNullOrEmpty(locale.displayName) ? locale.code : locale.displayName);
            }

            string current = settings.Get<string>(descriptor.Key);
            int index = codes.IndexOf(current);
            if (index < 0) index = 0;
            var selected = new IntObservable(index);
            IDisposable subscription = selected.Subscribe(value =>
            {
                if (value >= 0 && value < codes.Count) settings.Set(descriptor.Key, codes[value]);
            });
            if (subscription != null) owned.Add(subscription);

            UISettingItem item = UISettingItem.CreateDropdown(descriptor.Key, label, selected, options,
                string.IsNullOrEmpty(description) ? L("settings.description.select", "点击选择") : description);
            item.CanInteract = () => localization != null && localization.Locales.Count > 0;
            refreshers[descriptor.Key] = next =>
            {
                int value = codes.IndexOf(next as string ?? string.Empty);
                if (value < 0) value = 0;
                if (selected.Value != value) selected.Value = value;
            };
            return item;
        }

        private void BuildBindingItems(List<UISettingItem> items)
        {
            IReadOnlyList<InputBinding> bindings = input.GetBindings();
            for (int i = 0; i < bindings.Count; i++)
            {
                InputBinding binding = bindings[i];
                if (binding == null || string.IsNullOrEmpty(binding.action)) continue;

                string key = "input.bind." + binding.context + "." + binding.action;
                string label = L("settings.input.action." + binding.action, binding.action);
                var text = new StringObservable(KeyText(binding));
                bool rebindable = string.IsNullOrEmpty(binding.axis);
                string description = rebindable
                    ? L("settings.rebind.hint", "点击后按下新按键（Esc 取消）")
                    : L("settings.rebind.axisLocked", "轴绑定不可重绑") + "：" + binding.axis;

                UISettingItem item;
                if (rebindable)
                {
                    string action = binding.action;
                    string context = binding.context;
                    item = UISettingItem.CreateKey(key, label, text, () => BeginRebind(action, context), description);
                }
                else
                {
                    item = UISettingItem.CreateReadOnly(key, label, text, description);
                }
                item.CanInteract = () => rebindable;
                items.Add(item);
            }
        }

        #endregion

        #region 刷新与工具

        private void OnSettingsChanged(SettingsChange change)
        {
            if (change.Key == "input.bindings")
            {
                BindingsChanged?.Invoke();
                return;
            }
            if (refreshers.TryGetValue(change.Key, out Action<object> refresh))
                refresh(change.NewValue);
        }

        private void ClearItems()
        {
            for (int i = 0; i < owned.Count; i++)
            {
                try { owned[i]?.Dispose(); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
            owned.Clear();
            refreshers.Clear();
        }

        private void BuildCategories()
        {
            categories.Clear();
            Array values = Enum.GetValues(typeof(SettingsCategory));
            for (int i = 0; i < values.Length; i++)
            {
                SettingsCategory category = (SettingsCategory)values.GetValue(i);
                if (category == SettingsCategory.System) continue; // 系统内部状态不暴露给玩家
                IReadOnlyList<SettingDescriptor> descriptors = settings.Describe(category);
                bool any = false;
                for (int j = 0; j < descriptors.Count; j++)
                {
                    if (settings.IsAvailable(descriptors[j].Key, out _)) { any = true; break; }
                }
                if (any) categories.Add(category);
            }
        }

        private string Label(SettingDescriptor descriptor)
        {
            string key = descriptor.LocKey + ".label";
            if (localize != null)
            {
                string localized = localize(key);
                if (!string.IsNullOrEmpty(localized) && localized != key) return localized;
            }
            if (localization != null)
            {
                string localized = localization.Get(key);
                if (!string.IsNullOrEmpty(localized) && localized != key) return localized;
            }
            return SettingsText.Get(descriptor.Key);
        }

        /// <summary>分区名称（设置面板分类按钮用）。</summary>
        public string CategoryLabel(SettingsCategory category)
        {
            string key = "settings.category." + category.ToString().ToLowerInvariant();
            if (localization != null)
            {
                string localized = localization.Get(key);
                if (!string.IsNullOrEmpty(localized) && localized != key) return localized;
            }
            return SettingsText.Category(category);
        }

        private string OptionText(SettingDescriptor descriptor, object value)
        {
            string name = value == null ? string.Empty : value.ToString();
            string key = descriptor.LocKey + ".option." + name;
            if (localization != null)
            {
                string localized = localization.Get(key);
                if (!string.IsNullOrEmpty(localized) && localized != key) return localized;
            }
            return SettingsText.Option(descriptor.Key, name);
        }

        /// <summary>通用本地化取文案：先服务、再代码兜底。</summary>
        private string L(string key, string fallback)
        {
            if (localization != null)
            {
                string localized = localization.Get(key);
                if (!string.IsNullOrEmpty(localized) && localized != key) return localized;
            }
            string text = SettingsText.Get(key);
            return text == key ? fallback : text;
        }

        private InputBinding FindBinding(string action, string context)
        {
            IReadOnlyList<InputBinding> bindings = input.GetBindings();
            for (int i = 0; i < bindings.Count; i++)
            {
                InputBinding binding = bindings[i];
                if (binding != null && binding.action == action && binding.context == context) return binding;
            }
            return null;
        }

        /// <summary>键位显示文本：轴 / 双向键 / 单键。</summary>
        private string KeyText(InputBinding binding)
        {
            if (binding == null) return string.Empty;
            if (!string.IsNullOrEmpty(binding.axis)) return L("settings.input.axis", "轴") + " " + binding.axis;
            if (binding.positive != KeyCode.None && binding.negative != KeyCode.None)
                return binding.positive + " / " + binding.negative;
            if (binding.positive != KeyCode.None) return binding.positive.ToString();
            return binding.negative != KeyCode.None ? binding.negative.ToString() : L("settings.input.unbound", "未绑定");
        }

        private string ResultText(InputRebindResult result)
        {
            switch (result)
            {
                case InputRebindResult.Conflict: return L("settings.rebind.conflict", "该按键已被其它动作使用");
                case InputRebindResult.ReservedKey: return L("settings.rebind.reserved", "该按键为系统保留键");
                case InputRebindResult.InvalidBinding: return L("settings.rebind.invalid", "按键无效");
                default: return L("settings.rebind.failed", "改键失败");
            }
        }

        #endregion
    }
}
