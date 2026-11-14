using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UPandaGF;

namespace UPandaGF.Tests
{
    /// <summary>设置消费冒烟：FOV 通过 SettingsCameraBinder 实际写到相机。</summary>
    public class SettingsConsumptionPlayModeTests
    {
        private string directory;
        private SettingsService settings;
        private GameObject cameraObject;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "upandagf-settings-consumption", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            settings = new SettingsService(new SettingsStore(Path.Combine(directory, "settings.json")),
                new SettingsRegistry(SettingsPlatform.Desktop | SettingsPlatform.Editor),
                new ISettingsApplier[0], null, null);
            settings.LoadAsync().GetAwaiter().GetResult();
        }

        [TearDown]
        public void TearDown()
        {
            if (cameraObject != null) UnityEngine.Object.DestroyImmediate(cameraObject);
            try { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
            catch (Exception) { }
        }

        [Test]
        public void CameraBinder_AppliesFieldOfView()
        {
            cameraObject = new GameObject("SettingsCamera", typeof(Camera));
            Camera camera = cameraObject.GetComponent<Camera>();
            SettingsCameraBinder binder = cameraObject.AddComponent<SettingsCameraBinder>();
            binder.Bind(settings);

            Assert.AreEqual(60f, camera.fieldOfView, 0.01f);
            Assert.IsTrue(settings.Set("graphics.fieldOfView", 75f));
            Assert.AreEqual(75f, camera.fieldOfView, 0.01f);
        }
    }
}
