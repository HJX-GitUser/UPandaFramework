using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UPandaGF;

namespace UPandaGF.Tests
{
    /// <summary>设置迁移、安全模式与分辨率选择测试。</summary>
    public class SettingsMigrationAndSafeModeTests
    {
        private string directory;
        private string settingsPath;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "upandagf-settings-migration", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            settingsPath = Path.Combine(directory, "settings.json");
        }

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
            catch (Exception) { }
        }

        [Test]
        public void MissingSchemaVersion_IsMigratedFromZero()
        {
            File.WriteAllText(settingsPath, "{\"audio\":{\"master\":0.3}}");
            var service = CreateService();
            service.LoadAsync().GetAwaiter().GetResult();

            Assert.AreEqual(0.3f, service.Current.audio.master, 0.0001f);
            Assert.AreEqual(SettingsData.CurrentSchemaVersion, service.Current.schemaVersion);
        }

        [Test]
        public void CustomMigration_IsApplied()
        {
            File.WriteAllText(settingsPath, "{\"schemaVersion\":0,\"audio\":{\"master\":0.2}}");
            var chain = new SettingsMigrationChain(false);
            chain.Add(new MasterToHalfMigration());
            var service = CreateService(chain);
            service.LoadAsync().GetAwaiter().GetResult();

            Assert.AreEqual(0.5f, service.Current.audio.master, 0.0001f);
            Assert.AreEqual(SettingsData.CurrentSchemaVersion, service.Current.schemaVersion);
        }

        [Test]
        public void FutureSchemaVersion_FallsBackToDefaults()
        {
            File.WriteAllText(settingsPath, "{\"schemaVersion\":99,\"audio\":{\"master\":0.1}}");
            var service = CreateService();
            bool failed = false;
            service.LoadFailed += message => failed = true;
            service.LoadAsync().GetAwaiter().GetResult();

            Assert.IsTrue(failed);
            Assert.AreEqual(0.8f, service.Current.audio.master, 0.0001f);
        }

        [Test]
        public void SafeMode_ForcesWindowedLowQuality_AndSuccessClearsIt()
        {
            File.WriteAllText(settingsPath,
                "{\"schemaVersion\":1,\"display\":{\"mode\":0,\"width\":3840,\"height\":2160,\"vsync\":false}," +
                "\"graphics\":{\"preset\":2,\"antiAliasing\":4,\"shadowQuality\":3,\"textureQuality\":2,\"renderScale\":1.0}," +
                "\"accessibility\":{\"uiScale\":1.5}}");

            var safeMode = new SettingsSafeMode(Path.Combine(directory, "safe-mode.json"));
            safeMode.RecordFailure();
            Assert.IsFalse(safeMode.IsActive);
            safeMode.RecordFailure();
            Assert.IsTrue(safeMode.IsActive);

            var service = CreateService(null, safeMode);
            service.LoadAsync().GetAwaiter().GetResult();

            Assert.IsTrue(service.SafeModeActive);
            Assert.AreEqual(DisplayModeSetting.Windowed, service.Current.display.mode);
            Assert.AreEqual(1280, service.Current.display.width);
            Assert.AreEqual(720, service.Current.display.height);
            Assert.AreEqual(QualityPreset.Low, service.Current.graphics.preset);
            Assert.AreEqual(AntiAliasingMode.Off, service.Current.graphics.antiAliasing);
            Assert.AreEqual(1f, service.Current.accessibility.uiScale, 0.0001f);

            safeMode.MarkSuccess();
            Assert.IsFalse(safeMode.IsActive);
        }

        [Test]
        public void Save_PreservesUnknownFields()
        {
            File.WriteAllText(settingsPath,
                "{\"schemaVersion\":1,\"futureTop\":42,\"audio\":{\"master\":0.3,\"futureAudio\":\"x\"}}");
            var service = CreateService();
            service.LoadAsync().GetAwaiter().GetResult();
            service.Set("audio.music", 0.4f);
            service.SaveAsync(SettingsSaveReason.Explicit).GetAwaiter().GetResult();

            string saved = File.ReadAllText(settingsPath);
            StringAssert.Contains("futureTop", saved);
            StringAssert.Contains("futureAudio", saved);
            SettingsData reloaded = JsonUtility.FromJson<SettingsData>(saved);
            Assert.AreEqual(0.4f, reloaded.audio.music, 0.0001f);
            Assert.AreEqual(0.3f, reloaded.audio.master, 0.0001f);
        }

        [Test]
        public void JsonMerge_ObjectArrayMergesByKey()
        {
            string merged = SettingsJsonMerge.Merge(
                "{\"items\":[{\"key\":\"a\",\"future\":1},{\"key\":\"b\",\"future\":2}]}",
                "{\"items\":[{\"key\":\"a\",\"value\":3}]}");
            StringAssert.Contains("\"future\":1", merged);
            StringAssert.Contains("\"value\":3", merged);
            StringAssert.DoesNotContain("\"b\"", merged);
        }

        [Test]
        public void Consent_AcceptAndDecline()
        {
            var service = CreateService();
            service.LoadAsync().GetAwaiter().GetResult();

            Assert.IsTrue(service.NeedsConsent());
            Assert.IsTrue(service.Accept(true, true, false));
            Assert.IsFalse(service.NeedsConsent());
            Assert.IsTrue(service.IsAccepted());
            Assert.IsTrue(service.Get<bool>("privacy.telemetry"));
            Assert.IsTrue(service.Get<bool>("system.firstRunCompleted"));

            var second = CreateService();
            second.LoadAsync().GetAwaiter().GetResult();
            Assert.IsTrue(second.Decline());
            Assert.IsTrue(second.IsDeclined());
            Assert.IsFalse(second.NeedsConsent());
        }

        [Test]
        public void Registry_RegisteredMigrationIsUsedByChain()
        {
            SettingsMigrationRegistry.Clear();
            try
            {
                SettingsMigrationRegistry.Register(new MasterToHalfMigration());
                SettingsData data = new SettingsData { schemaVersion = 0 };
                data.audio.master = 0.2f;
                string error;
                Assert.IsTrue(SettingsMigrationRegistry.CreateChain().TryMigrate(data, out error), error);
                Assert.AreEqual(0.5f, data.audio.master, 0.0001f);
            }
            finally { SettingsMigrationRegistry.Clear(); }
        }

        [Test]
        public void ResolutionResolver_PrefersExactThenNearest()
        {
            var available = new[]
            {
                new Resolution { width = 1920, height = 1080, refreshRate = 60 },
                new Resolution { width = 1920, height = 1080, refreshRate = 144 },
                new Resolution { width = 2560, height = 1440, refreshRate = 60 }
            };

            Resolution resolved;
            Assert.IsTrue(DisplayResolutionResolver.TryResolve(1920, 1080, 120, available, out resolved));
            Assert.AreEqual(144, resolved.refreshRate);

            Assert.IsTrue(DisplayResolutionResolver.TryResolve(1920, 1080, 0, available, out resolved));
            Assert.AreEqual(144, resolved.refreshRate);

            Assert.IsTrue(DisplayResolutionResolver.TryResolve(1600, 900, 0, available, out resolved));
            Assert.AreEqual(1920, resolved.width);
            Assert.AreEqual(1080, resolved.height);

            Assert.IsFalse(DisplayResolutionResolver.TryResolve(0, 0, 0, available, out resolved));
            Assert.IsFalse(DisplayResolutionResolver.TryResolve(1920, 1080, 0, new Resolution[0], out resolved));
        }

        private SettingsService CreateService(SettingsMigrationChain migrations = null, SettingsSafeMode safeMode = null)
        {
            return new SettingsService(new SettingsStore(settingsPath),
                new SettingsRegistry(SettingsPlatform.Desktop | SettingsPlatform.Editor),
                new ISettingsApplier[0], null, null, migrations, safeMode);
        }

        private sealed class MasterToHalfMigration : ISettingsMigration
        {
            public int FromVersion { get { return 0; } }
            public int ToVersion { get { return 1; } }
            public void Migrate(SettingsData data) { data.audio.master = 0.5f; }
        }
    }
}
