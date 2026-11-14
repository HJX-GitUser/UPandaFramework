using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UPandaGF;

namespace UPandaGF.Tests
{
    /// <summary>PlayMode：真实 Resources 表加载 + LocalizedText 语言切换刷新。</summary>
    public class LocalizationPlayModeTests
    {
        private string directory;
        private SettingsService settings;
        private LocalizationService localization;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "upandagf-localization-play", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            settings = new SettingsService(new SettingsStore(Path.Combine(directory, "settings.json")),
                new SettingsRegistry(SettingsPlatform.Desktop | SettingsPlatform.Editor),
                new ISettingsApplier[0], null, null);
            settings.LoadAsync().GetAwaiter().GetResult();
            settings.Set("language.ui", "zh-Hans");

            localization = new LocalizationService(new ResourceSource(), settings, new LocalizationSettings(), null);
            localization.LoadAsync().GetAwaiter().GetResult();
        }

        [TearDown]
        public void TearDown()
        {
            localization?.Dispose();
            try { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
            catch (Exception) { }
        }

        [Test]
        public void Resources_ContainBuiltInTables()
        {
            Assert.IsNotNull(Resources.Load<TextAsset>("Localization/catalog"));
            Assert.IsNotNull(Resources.Load<TextAsset>("Localization/zh-Hans"));
            Assert.IsNotNull(Resources.Load<TextAsset>("Localization/en"));
            Assert.AreEqual("zh-Hans", localization.CurrentLocale);
            Assert.AreEqual("系统设置", localization.Get("settings.panel.title"));
        }

        [Test]
        public void LocalizedText_RefreshesOnLocaleChange()
        {
            var go = new GameObject("LocalizedTextTest", typeof(RectTransform));
            try
            {
                Text text = go.AddComponent<Text>();
                LocalizedText localized = go.AddComponent<LocalizedText>();
                localized.Key = "settings.panel.title";
                localized.Bind(localization);
                Assert.AreEqual("系统设置", text.text);

                Assert.IsTrue(localization.SetLocale("en"));
                Assert.AreEqual("Settings", text.text);

                Assert.IsTrue(localization.SetLocale("zh-Hans"));
                Assert.AreEqual("系统设置", text.text);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        private sealed class ResourceSource : ILocalizationAssetSource
        {
            public Task<string> LoadTextAsync(string path, AssetLoadMethod method, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                TextAsset asset = Resources.Load<TextAsset>(path);
                return Task.FromResult(asset != null ? asset.text : null);
            }
        }
    }
}
