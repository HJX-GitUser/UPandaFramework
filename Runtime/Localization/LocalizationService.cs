using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>
    /// 本地多语言服务实现：加载内置/项目 catalog 与语言表，合并覆盖，按设置项 language.ui 切换。
    /// <para>P0 预加载全部语言表；加载失败时回退到 key/代码兜底，不阻断游戏。</para>
    /// <para>所有事件在主线程触发；表只保留 key/value 字典，TextAsset 租约读取后立即归还。</para>
    /// </summary>
    public sealed class LocalizationService : ILocalizationService, IDisposable
    {
        private sealed class LocaleRuntime
        {
            public string Code;
            public string DisplayName;
            public string BaseTable;
            public string OverrideTable;
            public AssetLoadMethod BaseMethod;
            public AssetLoadMethod OverrideMethod;
        }

        private readonly ILocalizationAssetSource source;
        private readonly ISettingsService settings;
        private readonly LocalizationSettings options;
        private readonly ILogService log;

        private readonly Dictionary<string, Dictionary<string, string>> tables =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        private readonly List<LocaleInfo> locales = new List<LocaleInfo>();
        private readonly HashSet<string> missingLogged = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private string defaultLocale = "zh-Hans";
        private string fallbackLocale = "en";
        private string currentLocale = "zh-Hans";
        private CultureInfo culture = CultureInfo.InvariantCulture;
        private bool loaded;
        private bool disposed;

        public LocalizationService(IAssetService assets, ISettingsService settings,
            LocalizationSettings options = null, ILogService log = null)
            : this(new LocalizationAssetSource(assets, log), settings, options, log)
        {
        }

        public LocalizationService(ILocalizationAssetSource source, ISettingsService settings,
            LocalizationSettings options = null, ILogService log = null)
        {
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            this.settings = settings;
            this.options = options ?? new LocalizationSettings();
            this.log = log;
        }

        public bool IsLoaded { get { return loaded; } }
        public string CurrentLocale { get { return currentLocale; } }
        public CultureInfo Culture { get { return culture; } }
        public IReadOnlyList<LocaleInfo> Locales { get { return locales; } }

        public event Action<LocalizationChange> LocaleChanged;
        public event Action Loaded;
        public event Action<string> LoadFailed;

        public async Task LoadAsync(CancellationToken token = default)
        {
            if (loaded || disposed) return;
            token.ThrowIfCancellationRequested();

            try
            {
                LocalizationCatalog builtIn = null;
                if (!string.IsNullOrWhiteSpace(options.builtInCatalogPath))
                {
                    string text = await source.LoadTextAsync(options.builtInCatalogPath, AssetLoadMethod.Resources, token);
                    token.ThrowIfCancellationRequested();
                    builtIn = LocalizationCatalog.FromJson(text);
                    if (builtIn == null && log != null)
                        log.Warning("[Localization] 内置 catalog 缺失或损坏：" + options.builtInCatalogPath);
                }
                builtIn = builtIn ?? new LocalizationCatalog();
                builtIn.EnsureDefaults();

                var runtime = new Dictionary<string, LocaleRuntime>(StringComparer.OrdinalIgnoreCase);
                defaultLocale = builtIn.defaultLocale;
                fallbackLocale = builtIn.fallbackLocale;
                MergeCatalog(builtIn, runtime, false);

                if (!string.IsNullOrWhiteSpace(options.projectCatalogPath))
                {
                    string projectText = await source.LoadTextAsync(
                        options.projectCatalogPath, options.projectLoadMethod, token);
                    token.ThrowIfCancellationRequested();
                    LocalizationCatalog project = LocalizationCatalog.FromJson(projectText);
                    if (project == null)
                    {
                        if (log != null) log.Warning("[Localization] 项目 catalog 缺失或损坏：" + options.projectCatalogPath);
                    }
                    else
                    {
                        if (!string.IsNullOrWhiteSpace(project.defaultLocale)) defaultLocale = project.defaultLocale;
                        if (!string.IsNullOrWhiteSpace(project.fallbackLocale)) fallbackLocale = project.fallbackLocale;
                        MergeCatalog(project, runtime, true);
                    }
                }

                await LoadTablesAsync(runtime, token);
                if (locales.Count == 0 && log != null)
                    log.Warning("[Localization] 没有加载到任何语言表，将使用 key / 代码兜底。");

                ResolveInitialLocale();
                loaded = true;
                if (settings != null) settings.Changed += OnSettingsChanged;
                Loaded?.Invoke();
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                loaded = true;
                if (log != null) log.Error("[Localization] 初始化失败，使用兜底：" + exception.Message);
                LoadFailed?.Invoke(exception.Message);
                Loaded?.Invoke();
            }
        }

        private void MergeCatalog(LocalizationCatalog catalog, Dictionary<string, LocaleRuntime> runtime, bool project)
        {
            if (catalog == null || catalog.locales == null) return;
            for (int i = 0; i < catalog.locales.Length; i++)
            {
                LocaleInfo entry = catalog.locales[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.code)) continue;
                string code = entry.code.Trim();
                if (!runtime.TryGetValue(code, out LocaleRuntime locale))
                {
                    locale = new LocaleRuntime
                    {
                        Code = code,
                        DisplayName = string.IsNullOrWhiteSpace(entry.displayName) ? code : entry.displayName,
                        BaseTable = entry.table,
                        OverrideTable = entry.overrideTable,
                        BaseMethod = project ? options.projectLoadMethod : AssetLoadMethod.Resources,
                        OverrideMethod = options.projectLoadMethod
                    };
                    runtime[code] = locale;
                    RegisterAlias(code);
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(entry.displayName)) locale.DisplayName = entry.displayName;
                if (project)
                {
                    string overridePath = FirstNonEmpty(entry.overrideTable, entry.table);
                    if (!string.IsNullOrWhiteSpace(overridePath))
                    {
                        locale.OverrideTable = overridePath;
                        locale.OverrideMethod = options.projectLoadMethod;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(entry.overrideTable))
                {
                    locale.OverrideTable = entry.overrideTable;
                    locale.OverrideMethod = options.projectLoadMethod;
                }
            }
        }

        private async Task LoadTablesAsync(Dictionary<string, LocaleRuntime> runtime, CancellationToken token)
        {
            var codes = new List<string>(runtime.Keys);
            codes.Sort(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < codes.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                LocaleRuntime locale = runtime[codes[i]];
                var table = new Dictionary<string, string>(StringComparer.Ordinal);

                if (!string.IsNullOrWhiteSpace(locale.BaseTable))
                {
                    string text = await source.LoadTextAsync(locale.BaseTable, locale.BaseMethod, token);
                    ReadTable(text, locale.Code, table);
                }
                if (!string.IsNullOrWhiteSpace(locale.OverrideTable))
                {
                    string text = await source.LoadTextAsync(locale.OverrideTable, locale.OverrideMethod, token);
                    ReadTable(text, locale.Code, table);
                }

                if (table.Count == 0) continue;
                tables[locale.Code] = table;
                locales.Add(new LocaleInfo(locale.Code, locale.DisplayName, locale.BaseTable, locale.OverrideTable));
            }
        }

        private void ReadTable(string json, string expectedLocale, Dictionary<string, string> target)
        {
            if (string.IsNullOrWhiteSpace(json)) return;
            LocalizationTableFile table = LocalizationTableFile.FromJson(json);
            if (table == null)
            {
                if (log != null) log.Warning("[Localization] 语言表无法解析：" + expectedLocale);
                return;
            }
            if (!string.IsNullOrWhiteSpace(table.locale)
                && !string.Equals(table.locale, expectedLocale, StringComparison.OrdinalIgnoreCase)
                && log != null)
                log.Warning("[Localization] 表 locale 与 catalog 不一致：" + expectedLocale + " / " + table.locale);

            for (int i = 0; i < table.entries.Length; i++)
            {
                LocalizationEntry entry = table.entries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.key)) continue;
                target[entry.key] = entry.value ?? string.Empty;
            }
        }

        private void ResolveInitialLocale()
        {
            string requested = ReadSettingLanguage();
            string resolved = ResolveLocale(requested);
            currentLocale = resolved;
            culture = ResolveCulture(resolved);
        }

        private string ReadSettingLanguage()
        {
            if (settings == null) return string.Empty;
            try
            {
                string value;
                return settings.TryGet("language.ui", out value) ? value : string.Empty;
            }
            catch (Exception) { return string.Empty; }
        }

        private string ResolveLocale(string requested)
        {
            string normalized = SystemLocaleMapper.Normalize(requested);
            if (string.IsNullOrEmpty(normalized))
            {
                string system = SystemLocaleMapper.Map(Application.systemLanguage);
                normalized = system ?? string.Empty;
            }

            if (!string.IsNullOrEmpty(normalized))
            {
                string supported = FindSupported(normalized);
                if (supported != null) return supported;
                if (log != null && !string.IsNullOrEmpty(requested))
                    log.Warning("[Localization] 不支持的语言，回退默认：" + requested);
            }

            string fallback = FindSupported(fallbackLocale);
            if (fallback != null) return fallback;
            string def = FindSupported(defaultLocale);
            if (def != null) return def;
            if (locales.Count > 0) return locales[0].code;
            return string.IsNullOrWhiteSpace(defaultLocale) ? "zh-Hans" : defaultLocale;
        }

        private string FindSupported(string locale)
        {
            if (string.IsNullOrWhiteSpace(locale)) return null;
            string normalized = locale.Trim();
            if (tables.ContainsKey(normalized)) return normalized;
            if (aliases.TryGetValue(normalized, out string alias) && tables.ContainsKey(alias)) return alias;

            int dash = normalized.IndexOf('-');
            string prefix = dash > 0 ? normalized.Substring(0, dash) : normalized;
            foreach (string code in tables.Keys)
            {
                if (code.StartsWith(prefix + "-", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(code, prefix, StringComparison.OrdinalIgnoreCase))
                    return code;
            }
            return null;
        }

        private void RegisterAlias(string code)
        {
            aliases[code] = code;
            int dash = code.IndexOf('-');
            if (dash <= 0) return;
            string prefix = code.Substring(0, dash);
            if (!aliases.ContainsKey(prefix)) aliases[prefix] = code;
        }

        private static CultureInfo ResolveCulture(string locale)
        {
            if (string.IsNullOrWhiteSpace(locale)) return CultureInfo.InvariantCulture;
            try { return CultureInfo.GetCultureInfo(locale); }
            catch (Exception)
            {
                int dash = locale.IndexOf('-');
                if (dash > 0)
                {
                    try { return CultureInfo.GetCultureInfo(locale.Substring(0, dash)); }
                    catch (Exception) { }
                }
                return CultureInfo.InvariantCulture;
            }
        }

        public bool SetLocale(string locale)
        {
            if (disposed) return false;
            string requested = SystemLocaleMapper.Normalize(locale);
            string resolved;
            if (string.IsNullOrEmpty(requested))
            {
                resolved = ResolveLocale(string.Empty);
            }
            else
            {
                resolved = FindSupported(requested);
                if (resolved == null) return false;
            }
            if (resolved == null || !tables.ContainsKey(resolved)) return false;
            if (string.Equals(resolved, currentLocale, StringComparison.Ordinal)) return true;

            string previous = currentLocale;
            currentLocale = resolved;
            culture = ResolveCulture(resolved);
            LocaleChanged?.Invoke(new LocalizationChange(previous, resolved));
            return true;
        }

        private void OnSettingsChanged(SettingsChange change)
        {
            if (change.Key != "language.ui") return;
            string requested = change.NewValue as string ?? string.Empty;
            if (!SetLocale(requested) && log != null)
                log.Warning("[Localization] language.ui 切换失败：" + requested);
        }

        public string Get(string key)
        {
            string value;
            if (TryGetInternal(key, out value)) return value;
            return Missing(key);
        }

        public string Get(string key, params object[] args) { return Format(key, args); }

        public bool TryGet(string key, out string value)
        {
            return TryGetInternal(key, out value);
        }

        public bool HasKey(string key)
        {
            string ignored;
            return TryGetInternal(key, out ignored);
        }

        public string Format(string key, params object[] args)
        {
            string value;
            if (!TryGetInternal(key, out value)) return Missing(key);
            if (args == null || args.Length == 0) return value;
            try { return string.Format(culture, value, args); }
            catch (FormatException exception)
            {
                if (log != null) log.Warning("[Localization] 格式化失败：" + key + "：" + exception.Message);
                return value;
            }
        }

        private bool TryGetInternal(string key, out string value)
        {
            value = null;
            if (string.IsNullOrEmpty(key)) return false;
            if (TryLookup(currentLocale, key, out value)) return true;
            if (TryLookup(fallbackLocale, key, out value)) return true;
            if (TryLookup(defaultLocale, key, out value)) return true;
            return false;
        }

        private bool TryLookup(string locale, string key, out string value)
        {
            value = null;
            if (string.IsNullOrEmpty(locale)) return false;
            if (tables.TryGetValue(locale, out Dictionary<string, string> table)
                && table.TryGetValue(key, out value))
                return true;
            if (aliases.TryGetValue(locale, out string alias)
                && tables.TryGetValue(alias, out table)
                && table.TryGetValue(key, out value))
                return true;
            return false;
        }

        private string Missing(string key)
        {
            if (options.logMissingKeys && !string.IsNullOrEmpty(key) && missingLogged.Add(key) && log != null)
                log.Warning("[Localization] 缺少 key：" + key + "（locale=" + currentLocale + "）");

            switch (options.missingPolicy)
            {
                case LocalizationMissingPolicy.Empty: return string.Empty;
                case LocalizationMissingPolicy.Fallback:
                    if (TryLookup(fallbackLocale, key, out string fallback)) return fallback;
                    if (TryLookup(defaultLocale, key, out string def)) return def;
                    return key;
                default: return key;
            }
        }

        private static string FirstNonEmpty(string left, string right)
        {
            return !string.IsNullOrWhiteSpace(left) ? left : right;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (settings != null) settings.Changed -= OnSettingsChanged;
            LocaleChanged = null;
            Loaded = null;
            LoadFailed = null;
            tables.Clear();
            locales.Clear();
        }
    }
}
