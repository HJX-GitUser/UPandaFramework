using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UPandaGF;

namespace UPandaGF.Tests
{
    /// <summary>本地化服务的 EditMode 测试：catalog/表加载、回退、项目覆盖、设置联动、缺失 key。</summary>
    public class LocalizationServiceTests
    {
        private string directory;
        private SettingsService settings;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "upandagf-localization-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            settings = new SettingsService(new SettingsStore(Path.Combine(directory, "settings.json")),
                new SettingsRegistry(SettingsPlatform.Desktop | SettingsPlatform.Editor),
                new ISettingsApplier[0], null, null);
            settings.LoadAsync().GetAwaiter().GetResult();
        }

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
            catch (Exception) { }
        }

        [Test]
        public void Load_UsesCatalogAndTable()
        {
            settings.Set("language.ui", "zh-Hans");
            var source = new FakeSource();
            source.Set("Localization/catalog", CatalogJson("zh-Hans", "en",
                Locale("zh-Hans", "简体中文", "Localization/zh-Hans"),
                Locale("en", "English", "Localization/en")));
            source.Set("Localization/zh-Hans", TableJson("zh-Hans", ("hello", "你好")));
            source.Set("Localization/en", TableJson("en", ("hello", "Hello")));

            var service = Create(source);
            service.LoadAsync().GetAwaiter().GetResult();

            Assert.IsTrue(service.IsLoaded);
            Assert.AreEqual("zh-Hans", service.CurrentLocale);
            Assert.AreEqual("你好", service.Get("hello"));
            Assert.AreEqual(2, service.Locales.Count);
        }

        [Test]
        public void SetLocale_SwitchesAndRaisesEvent()
        {
            settings.Set("language.ui", "zh-Hans");
            var source = SourceWithBoth();
            var service = Create(source);
            service.LoadAsync().GetAwaiter().GetResult();

            LocalizationChange change = default;
            service.LocaleChanged += value => change = value;

            Assert.IsTrue(service.SetLocale("en"));
            Assert.AreEqual("en", service.CurrentLocale);
            Assert.AreEqual("Hello", service.Get("hello"));
            Assert.AreEqual("zh-Hans", change.PreviousLocale);
            Assert.AreEqual("en", change.CurrentLocale);
            Assert.IsFalse(service.SetLocale("ja"));
            Assert.AreEqual("en", service.CurrentLocale);
        }

        [Test]
        public void MissingKey_ReturnsKeyAndTryGetFalse()
        {
            settings.Set("language.ui", "zh-Hans");
            var service = Create(SourceWithBoth());
            service.LoadAsync().GetAwaiter().GetResult();

            Assert.AreEqual("missing.key", service.Get("missing.key"));
            string value;
            Assert.IsFalse(service.TryGet("missing.key", out value));
            Assert.IsFalse(service.HasKey("missing.key"));
            Assert.IsTrue(service.HasKey("hello"));
        }

        [Test]
        public void Format_UsesCurrentCultureAndPlaceholders()
        {
            settings.Set("language.ui", "zh-Hans");
            var source = SourceWithBoth();
            source.Set("Localization/zh-Hans", TableJson("zh-Hans", ("gold", "金币：{0}")));
            source.Set("Localization/en", TableJson("en", ("gold", "Gold: {0}")));
            var service = Create(source);
            service.LoadAsync().GetAwaiter().GetResult();

            Assert.AreEqual("金币：12", service.Format("gold", 12));
            Assert.AreEqual("金币：1200", service.Format("gold", 1200));
            service.SetLocale("en");
            Assert.AreEqual("Gold: 12", service.Format("gold", 12));
        }

        [Test]
        public void ProjectCatalog_OverridesAndAddsLocale()
        {
            settings.Set("language.ui", "zh-Hans");
            var source = SourceWithBoth();
            source.Set("Project/catalog", CatalogJson("zh-Hans", "en",
                new LocaleInfo { code = "zh-Hans", displayName = "简体中文", overrideTable = "Project/zh-Hans" },
                new LocaleInfo { code = "ja", displayName = "日本語", table = "Project/ja" }));
            source.Set("Project/zh-Hans", TableJson("zh-Hans", ("hello", "你好（覆盖）")));
            source.Set("Project/ja", TableJson("ja", ("hello", "こんにちは")));

            var options = new LocalizationSettings { projectCatalogPath = "Project/catalog" };
            var service = new LocalizationService(source, settings, options, null);
            service.LoadAsync().GetAwaiter().GetResult();

            Assert.AreEqual("你好（覆盖）", service.Get("hello"));
            Assert.IsTrue(service.SetLocale("ja"));
            Assert.AreEqual("こんにちは", service.Get("hello"));
            Assert.AreEqual(3, service.Locales.Count);
        }

        [Test]
        public void SettingsChange_SwitchesLocale()
        {
            settings.Set("language.ui", "zh-Hans");
            var service = Create(SourceWithBoth());
            service.LoadAsync().GetAwaiter().GetResult();

            LocalizationChange change = default;
            service.LocaleChanged += value => change = value;

            settings.Set("language.ui", "en");
            Assert.AreEqual("en", service.CurrentLocale);
            Assert.AreEqual("en", change.CurrentLocale);
        }

        [Test]
        public void BrokenTable_KeepsOtherLocales()
        {
            settings.Set("language.ui", "zh-Hans");
            var source = new FakeSource();
            source.Set("Localization/catalog", CatalogJson("zh-Hans", "en",
                Locale("zh-Hans", "简体中文", "Localization/zh-Hans"),
                Locale("en", "English", "Localization/en")));
            source.Set("Localization/zh-Hans", TableJson("zh-Hans", ("hello", "你好")));
            source.Set("Localization/en", "{ broken json");

            var service = Create(source);
            service.LoadAsync().GetAwaiter().GetResult();

            Assert.AreEqual(1, service.Locales.Count);
            Assert.IsFalse(service.SetLocale("en"));
            Assert.AreEqual("你好", service.Get("hello"));
        }

        private LocalizationService Create(FakeSource source)
        {
            return new LocalizationService(source, settings, new LocalizationSettings(), null);
        }

        private static FakeSource SourceWithBoth()
        {
            var source = new FakeSource();
            source.Set("Localization/catalog", CatalogJson("zh-Hans", "en",
                Locale("zh-Hans", "简体中文", "Localization/zh-Hans"),
                Locale("en", "English", "Localization/en")));
            source.Set("Localization/zh-Hans", TableJson("zh-Hans", ("hello", "你好")));
            source.Set("Localization/en", TableJson("en", ("hello", "Hello")));
            return source;
        }

        private static LocaleInfo Locale(string code, string displayName, string table)
        {
            return new LocaleInfo { code = code, displayName = displayName, table = table };
        }

        private static string CatalogJson(string defaultLocale, string fallbackLocale, params LocaleInfo[] locales)
        {
            return LocalizationCatalog.ToJson(new LocalizationCatalog
            {
                defaultLocale = defaultLocale,
                fallbackLocale = fallbackLocale,
                locales = locales
            });
        }

        private static string TableJson(string locale, params (string key, string value)[] entries)
        {
            var rows = new LocalizationEntry[entries.Length];
            for (int i = 0; i < entries.Length; i++)
                rows[i] = new LocalizationEntry(entries[i].key, entries[i].value);
            return LocalizationTableFile.ToJson(new LocalizationTableFile { locale = locale, entries = rows });
        }

        private sealed class FakeSource : ILocalizationAssetSource
        {
            private readonly Dictionary<string, string> files = new Dictionary<string, string>(StringComparer.Ordinal);

            public void Set(string path, string text) { files[path] = text; }

            public Task<string> LoadTextAsync(string path, AssetLoadMethod method, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(files.TryGetValue(path, out string value) ? value : null);
            }
        }
    }
}
