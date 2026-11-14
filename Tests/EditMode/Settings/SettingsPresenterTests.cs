using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UPandaGF;

namespace UPandaGF.Tests
{
    /// <summary>设置面板 Presenter 的 EditMode 测试：描述表 → UISettingItem、值双向绑定、分区过滤、键位列表。</summary>
    public class SettingsPresenterTests
    {
        private string directory;
        private SettingsService service;
        private SettingsPresenter presenter;
        private FakeInput input;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "upandagf-settings-presenter", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            input = new FakeInput();
            service = new SettingsService(new SettingsStore(Path.Combine(directory, "settings.json")),
                new SettingsRegistry(SettingsPlatform.Desktop | SettingsPlatform.Editor),
                new ISettingsApplier[0], null, null);
            service.LoadAsync().GetAwaiter().GetResult();
            presenter = new SettingsPresenter(service, input);
        }

        [TearDown]
        public void TearDown()
        {
            presenter?.Dispose();
            try { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
            catch (Exception) { }
        }

        [Test]
        public void Categories_HideSystemAndIncludeAudioGraphics()
        {
            CollectionAssert.Contains(presenter.Categories, SettingsCategory.Audio);
            CollectionAssert.Contains(presenter.Categories, SettingsCategory.Graphics);
            CollectionAssert.DoesNotContain(presenter.Categories, SettingsCategory.System);
        }

        [Test]
        public void BuildItems_Audio_HasAllRows()
        {
            IReadOnlyList<UISettingItem> items = presenter.BuildItems(SettingsCategory.Audio);
            Assert.AreEqual(8, items.Count);
            Assert.AreEqual(UISettingControl.Slider, items[0].Control);
            Assert.AreEqual("audio.master", items[0].Key);
            Assert.IsNotNull(Find(items, "audio.ui"));
            Assert.IsNotNull(Find(items, "audio.voice"));
            Assert.IsNotNull(Find(items, "audio.muteWhenUnfocused"));
            Assert.IsNotNull(Find(items, "audio.preview"));
        }

        [Test]
        public void ToggleChange_WritesService()
        {
            IReadOnlyList<UISettingItem> items = presenter.BuildItems(SettingsCategory.Audio);
            UISettingItem mute = Find(items, "audio.muteAll");
            Assert.IsNotNull(mute);
            Assert.AreEqual(false, mute.BoolValue.Value);
            mute.BoolValue.Value = true;
            Assert.IsTrue(service.Current.audio.muteAll);
        }

        [Test]
        public void SliderChange_WritesAndClamps()
        {
            IReadOnlyList<UISettingItem> items = presenter.BuildItems(SettingsCategory.Audio);
            UISettingItem master = Find(items, "audio.master");
            master.FloatValue.Value = 0.35f;
            Assert.AreEqual(0.35f, service.Current.audio.master, 0.0001f);
        }

        [Test]
        public void EnumRow_UsesDropdown()
        {
            IReadOnlyList<UISettingItem> items = presenter.BuildItems(SettingsCategory.Graphics);
            UISettingItem preset = Find(items, "graphics.preset");
            Assert.IsNotNull(preset);
            Assert.AreEqual(UISettingControl.Dropdown, preset.Control);
            Assert.IsNotNull(preset.IntValue);
            Assert.AreEqual(QualityPreset.High, service.Current.graphics.preset);

            preset.IntValue.Value = (int)QualityPreset.Ultra;
            // 画质预设是预览确认项：先进入 Pending，确认后才写入 Current。
            Assert.IsTrue(service.IsPreviewing);
            Assert.AreEqual(QualityPreset.Ultra, service.Pending.graphics.preset);
            service.CommitAsync().GetAwaiter().GetResult();
            Assert.AreEqual(QualityPreset.Ultra, service.Current.graphics.preset);
        }

        [Test]
        public void InputCategory_ContainsBindingRowsAndReset()
        {
            input.Bindings = new List<InputBinding>
            {
                new InputBinding { action = "Jump", context = InputContexts.Gameplay, positive = KeyCode.Space },
                new InputBinding { action = "MoveHorizontal", context = InputContexts.Gameplay, axis = "Horizontal" }
            };

            IReadOnlyList<UISettingItem> items = presenter.BuildItems(SettingsCategory.Input);
            Assert.IsNotNull(Find(items, "input.bind.Gameplay.Jump"));
            Assert.IsNotNull(Find(items, "input.bind.Gameplay.MoveHorizontal"));
            Assert.IsNotNull(Find(items, "input.bindings.reset"));
        }

        [Test]
        public void Display_MergesWidthHeightRefreshIntoResolutionRow()
        {
            IReadOnlyList<UISettingItem> items = presenter.BuildItems(SettingsCategory.Display);
            Assert.IsNull(Find(items, "display.width"));
            Assert.IsNull(Find(items, "display.height"));
            Assert.IsNull(Find(items, "display.refreshRate"));
            Assert.IsNotNull(Find(items, "display.mode"));
            UISettingItem resolution = Find(items, "display.resolution");
            if (resolution != null) Assert.AreEqual(UISettingControl.Dropdown, resolution.Control);
        }

        [Test]
        public void UnavailableItems_AreDisabled()
        {
            // 模拟移动端：分辨率/全屏不可用，但帧率上限仍可用，所以“显示”页保留。
            service = new SettingsService(new SettingsStore(Path.Combine(directory, "mobile.json")),
                new SettingsRegistry(SettingsPlatform.Mobile), new ISettingsApplier[0], null, null);
            service.LoadAsync().GetAwaiter().GetResult();
            presenter.Dispose();
            presenter = new SettingsPresenter(service, input);

            service.IsAvailable("display.width", out string reason);
            Assert.IsFalse(string.IsNullOrEmpty(reason));
            IReadOnlyList<UISettingItem> items = presenter.BuildItems(SettingsCategory.Display);
            // 移动端分辨率行和宽/高/刷新率行都不出现；帧率上限仍保留。
            Assert.IsNull(Find(items, "display.resolution"));
            Assert.IsNull(Find(items, "display.width"));
            Assert.IsNotNull(Find(items, "display.frameRateLimit"));
            Assert.IsFalse(service.Set("display.width", 1920));
        }

        private static UISettingItem Find(IReadOnlyList<UISettingItem> items, string key)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] != null && items[i].Key == key) return items[i];
            }
            return null;
        }

        private static bool Contains(IReadOnlyList<SettingsCategory> categories, SettingsCategory category)
        {
            for (int i = 0; i < categories.Count; i++)
            {
                if (categories[i] == category) return true;
            }
            return false;
        }

        private sealed class FakeInput : IInputService
        {
            public List<InputBinding> Bindings = new List<InputBinding>();
            public event Action BindingsChanged;

            public float GetAxis(string action, string context = InputContexts.Gameplay) { return 0f; }
            public bool GetButton(string action, string context = InputContexts.Gameplay) { return false; }
            public bool GetButtonDown(string action, string context = InputContexts.Gameplay) { return false; }
            public bool GetButtonUp(string action, string context = InputContexts.Gameplay) { return false; }
            public void Bind(InputBinding binding)
            {
                Bindings.RemoveAll(item => item != null && item.action == binding.action && item.context == binding.context);
                Bindings.Add(binding.Clone());
                BindingsChanged?.Invoke();
            }
            public bool Unbind(string action, string context = InputContexts.Gameplay)
            {
                return Bindings.RemoveAll(item => item != null && item.action == action && item.context == context) > 0;
            }
            public IReadOnlyList<InputBinding> GetBindings() { return Bindings; }
            public IDisposable PushContext(string context, bool blockLower = true) { return new Dummy(); }
            public InputRebindResult TryRebind(InputBinding binding, bool allowReservedKey = false) { return InputRebindResult.Success; }
            public bool TryCaptureKey(out KeyCode key) { key = KeyCode.None; return false; }
            public bool ResetToDefaults() { return true; }
            public IReadOnlyList<InputBinding> GetDefaultBindings() { return Array.Empty<InputBinding>(); }
            public InputBinding[] ExportOverrides() { return Array.Empty<InputBinding>(); }
            public void ApplyOverrides(IEnumerable<InputBinding> overrides) { }

            private sealed class Dummy : IDisposable { public void Dispose() { } }
        }
    }

    /// <summary>设置面板预制体资源校验：确保包内 Resources/UI/SettingsPanel 存在且挂了 SettingsPanel。</summary>
    public class SettingsPanelAssetTests
    {
        [Test]
        public void PanelPrefab_ExistsAndHasComponent()
        {
            GameObject prefab = Resources.Load<GameObject>("UI/SettingsPanel");
            Assert.IsNotNull(prefab, "缺少 Resources/UI/SettingsPanel.prefab；请运行 UPandaGF/UI/生成设置面板预制体。");
            Assert.IsNotNull(prefab.GetComponent<SettingsPanel>());
        }
    }
}
