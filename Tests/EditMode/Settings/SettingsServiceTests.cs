using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UPandaGF;

namespace UPandaGF.Tests
{
    /// <summary>
    /// 系统设置服务 EditMode 测试：描述表/默认值/归一化/持久化/预览回滚/平台可用性/键位覆盖/生命周期保存。
    /// <para>不依赖真实平台 API：用法 ManualTimer + RecordingApplier + FakeAudio/FakeInput 做替身。</para>
    /// </summary>
    public class SettingsServiceTests
    {
        private string directory;
        private string path;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "upandagf-settings-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            path = Path.Combine(directory, "settings.json");
        }

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
            catch (Exception) { }
        }

        private SettingsRegistry DesktopRegistry() => new SettingsRegistry(SettingsPlatform.Desktop | SettingsPlatform.Editor);

        private SettingsService CreateService(SettingsRegistry registry, ManualTimer timer,
            RecordingApplier applier, FakeLog log = null)
        {
            var appliers = new List<ISettingsApplier>();
            if (applier != null) appliers.Add(applier);
            return new SettingsService(new SettingsStore(path), registry, appliers, log ?? new FakeLog(), timer);
        }

        // ============================ 描述表 ============================

        [Test]
        public void Registry_Validate_Passes()
        {
            IReadOnlyList<string> issues = DesktopRegistry().Validate();
            Assert.AreEqual(0, issues.Count, string.Join("\n", issues));
        }

        [Test]
        public void Registry_EverySettingsFieldHasDescriptor()
        {
            var registry = DesktopRegistry();
            var mappings = new[]
            {
                new SectionMapping(typeof(AudioOptions), "audio"),
                new SectionMapping(typeof(GraphicsOptions), "graphics"),
                new SectionMapping(typeof(DisplayOptions), "display"),
                new SectionMapping(typeof(InputOptions), "input"),
                new SectionMapping(typeof(LanguageOptions), "language"),
                new SectionMapping(typeof(AccessibilityOptions), "accessibility"),
                new SectionMapping(typeof(GameplayOptions), "gameplay"),
                new SectionMapping(typeof(PrivacyOptions), "privacy"),
                new SectionMapping(typeof(SystemOptions), "system")
            };

            int fieldCount = 0;
            foreach (SectionMapping mapping in mappings)
            {
                FieldInfo[] fields = mapping.Type.GetFields(BindingFlags.Public | BindingFlags.Instance);
                foreach (FieldInfo field in fields)
                {
                    string key = mapping.Prefix + "." + field.Name;
                    Assert.IsNotNull(registry.Get(key), "缺少描述表条目：" + key);
                    fieldCount++;
                }
            }
            Assert.AreEqual(fieldCount, registry.Descriptors.Count,
                "描述表条目数与 SettingsData 字段数不一致：新增字段必须同步加描述，删除字段必须删除描述。");
        }

        // ============================ 加载与默认值 ============================

        [Test]
        public void Load_NoFile_UsesDefaults()
        {
            var service = CreateService(DesktopRegistry(), new ManualTimer(), new RecordingApplier());
            bool loaded = false;
            service.Loaded += () => loaded = true;

            service.LoadAsync().GetAwaiter().GetResult();

            Assert.IsTrue(loaded);
            Assert.IsTrue(service.IsLoaded);
            Assert.AreEqual(0.8f, service.Current.audio.master, 0.0001f);
            Assert.AreEqual(0.7f, service.Current.audio.music, 0.0001f);
            Assert.AreEqual(QualityPreset.High, service.Current.graphics.preset);
            Assert.IsFalse(File.Exists(path), "加载默认值不应主动写文件（首次保存才落盘）。");
        }

        // ============================ 修改与归一化 ============================

        [Test]
        public void Set_ClampsAndPersists()
        {
            var service = CreateService(DesktopRegistry(), new ManualTimer(), new RecordingApplier());
            service.LoadAsync().GetAwaiter().GetResult();

            Assert.IsTrue(service.Set("audio.master", 5f));
            Assert.AreEqual(1f, service.Current.audio.master, 0.0001f);
            Assert.IsTrue(service.Set("audio.master", -2f));
            Assert.AreEqual(0f, service.Current.audio.master, 0.0001f);

            service.SaveAsync(SettingsSaveReason.Explicit).GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(path));

            var reloaded = CreateService(DesktopRegistry(), new ManualTimer(), new RecordingApplier());
            reloaded.LoadAsync().GetAwaiter().GetResult();
            Assert.AreEqual(0f, reloaded.Current.audio.master, 0.0001f);
        }

        [Test]
        public void Set_RejectsUnknownAndWrongType()
        {
            var service = CreateService(DesktopRegistry(), new ManualTimer(), new RecordingApplier());
            service.LoadAsync().GetAwaiter().GetResult();

            Assert.IsFalse(service.Set("audio.notExist", 1f));
            Assert.IsFalse(service.Set("audio.master", "not a number"));
            Assert.IsFalse(service.Set("audio.master", true));
            Assert.AreEqual(0.8f, service.Current.audio.master, 0.0001f);
        }

        [Test]
        public void Set_Immediate_AppliesAndRaisesChanged()
        {
            var applier = new RecordingApplier();
            var service = CreateService(DesktopRegistry(), new ManualTimer(), applier);
            service.LoadAsync().GetAwaiter().GetResult();

            SettingsChange change = default;
            service.Changed += value => change = value;

            Assert.IsTrue(service.Set("audio.master", 0.25f));
            Assert.AreEqual(0.25f, service.Current.audio.master, 0.0001f);
            Assert.AreEqual("audio.master", change.Key);
            Assert.AreEqual(0.8f, (float)change.OldValue, 0.0001f);
            Assert.AreEqual(0.25f, (float)change.NewValue, 0.0001f);
            Assert.IsTrue(change.Applied);
            Assert.IsFalse(change.Preview);
            Assert.GreaterOrEqual(applier.Calls, 1);
            Assert.AreEqual(SettingsApplyReason.UserChange, applier.LastReason);
        }

        // ============================ 预览与回滚 ============================

        [Test]
        public void Preview_Commit_KeepsValues()
        {
            var applier = new RecordingApplier();
            var service = CreateService(DesktopRegistry(), new ManualTimer(), applier);
            service.LoadAsync().GetAwaiter().GetResult();

            Assert.IsTrue(service.Set("graphics.antiAliasing", AntiAliasingMode.x2));
            Assert.IsTrue(service.IsPreviewing);
            Assert.AreEqual(AntiAliasingMode.x4, service.Current.graphics.antiAliasing);
            Assert.AreEqual(AntiAliasingMode.x2, service.Pending.graphics.antiAliasing);

            Assert.IsTrue(service.CommitAsync().GetAwaiter().GetResult());
            Assert.IsFalse(service.IsPreviewing);
            Assert.AreEqual(AntiAliasingMode.x2, service.Current.graphics.antiAliasing);
            Assert.AreEqual(AntiAliasingMode.x2, service.Pending.graphics.antiAliasing);
        }

        [Test]
        public void Preview_Timeout_RollsBack()
        {
            var timer = new ManualTimer();
            var applier = new RecordingApplier();
            var service = CreateService(DesktopRegistry(), timer, applier);
            service.LoadAsync().GetAwaiter().GetResult();
            service.PreviewSeconds = 5f;

            Assert.IsTrue(service.Set("graphics.antiAliasing", AntiAliasingMode.x2));
            Assert.IsTrue(service.IsPreviewing);

            timer.Advance(5f);

            Assert.IsFalse(service.IsPreviewing);
            Assert.AreEqual(AntiAliasingMode.x4, service.Current.graphics.antiAliasing);
            Assert.AreEqual(AntiAliasingMode.x4, service.Pending.graphics.antiAliasing);
            Assert.IsTrue(applier.Reasons.Contains(SettingsApplyReason.Rollback));
        }

        [Test]
        public void GraphicsPreset_ExpandsSubItems_AndOverrideMarksCustom()
        {
            var service = CreateService(DesktopRegistry(), new ManualTimer(), new RecordingApplier());
            service.LoadAsync().GetAwaiter().GetResult();

            Assert.IsTrue(service.Set("graphics.preset", QualityPreset.Low));
            Assert.AreEqual(AntiAliasingMode.Off, service.Pending.graphics.antiAliasing);
            Assert.AreEqual(ShadowQualityLevel.Off, service.Pending.graphics.shadowQuality);
            Assert.AreEqual(TextureQualityLevel.Low, service.Pending.graphics.textureQuality);
            Assert.AreEqual(0.75f, service.Pending.graphics.renderScale, 0.0001f);
            Assert.IsTrue(service.CommitAsync().GetAwaiter().GetResult());

            Assert.IsTrue(service.Set("graphics.antiAliasing", AntiAliasingMode.x8));
            Assert.AreEqual(QualityPreset.Custom, service.Pending.graphics.preset);
            Assert.IsTrue(service.CommitAsync().GetAwaiter().GetResult());
            Assert.AreEqual(QualityPreset.Custom, service.Current.graphics.preset);

            Assert.IsTrue(service.Set("graphics.preset", QualityPreset.Ultra));
            Assert.AreEqual(AntiAliasingMode.x8, service.Pending.graphics.antiAliasing);
            Assert.AreEqual(ShadowQualityLevel.High, service.Pending.graphics.shadowQuality);
            Assert.AreEqual(1f, service.Pending.graphics.renderScale, 0.0001f);
        }

        [Test]
        public void FrameRateChange_DisablesVsync()
        {
            var service = CreateService(DesktopRegistry(), new ManualTimer(), new RecordingApplier());
            service.LoadAsync().GetAwaiter().GetResult();
            service.Current.display.vsync = true;
            service.Current.display.frameRateLimit = FrameRateLimit.Fps60;

            Assert.IsTrue(service.Set("display.frameRateLimit", FrameRateLimit.Fps120));
            Assert.IsFalse(service.Current.display.vsync,
                "帧率上限优先：改帧率应自动关闭 VSync，否则 targetFrameRate 会被平台忽略。");
        }

        [Test]
        public void Reset_All_RestoresDefaults()
        {
            var service = CreateService(DesktopRegistry(), new ManualTimer(), new RecordingApplier());
            service.LoadAsync().GetAwaiter().GetResult();
            service.Set("audio.master", 0.1f);
            service.Set("graphics.preset", QualityPreset.Low);

            service.Reset(SettingsScope.All);

            Assert.AreEqual(0.8f, service.Current.audio.master, 0.0001f);
            Assert.AreEqual(QualityPreset.High, service.Current.graphics.preset);
        }

        // ============================ 平台可用性 ============================

        [Test]
        public void Availability_MobileHidesDesktopItems()
        {
            var registry = new SettingsRegistry(SettingsPlatform.Mobile);
            var service = CreateService(registry, new ManualTimer(), new RecordingApplier());
            service.LoadAsync().GetAwaiter().GetResult();

            string reason;
            Assert.IsFalse(service.IsAvailable("display.width", out reason));
            Assert.IsNotNull(reason);
            Assert.IsFalse(service.Set("display.width", 1920));
            Assert.IsTrue(service.IsAvailable("display.frameRateLimit", out reason));
        }

        // ============================ 键位覆盖 ============================

        [Test]
        public void Bindings_ApplyAndPersist()
        {
            var input = new FakeInput();
            var service = new SettingsService(new SettingsStore(path), DesktopRegistry(),
                new ISettingsApplier[] { new InputSettingsApplier(input) }, new FakeLog(), new ManualTimer());
            service.LoadAsync().GetAwaiter().GetResult();

            var binding = new InputBinding { action = "Jump", context = InputContexts.Gameplay, positive = KeyCode.Q };
            Assert.IsTrue(service.Set("input.bindings", new[] { binding }));

            Assert.AreEqual(1, input.ResetCount);
            Assert.IsNotNull(input.Applied);
            Assert.AreEqual(1, input.Applied.Length);
            Assert.AreEqual(KeyCode.Q, input.Applied[0].positive);

            service.SaveAsync(SettingsSaveReason.Explicit).GetAwaiter().GetResult();
            var reloadedInput = new FakeInput();
            var reloaded = new SettingsService(new SettingsStore(path), DesktopRegistry(),
                new ISettingsApplier[] { new InputSettingsApplier(reloadedInput) }, new FakeLog(), new ManualTimer());
            reloaded.LoadAsync().GetAwaiter().GetResult();
            Assert.AreEqual(1, reloaded.Current.input.bindings.Length);
            Assert.AreEqual(KeyCode.Q, reloaded.Current.input.bindings[0].positive);
        }

        // ============================ 损坏与备份 ============================

        [Test]
        public void Load_CorruptPrimary_UsesBackup()
        {
            var registry = DesktopRegistry();
            var store = new SettingsStore(path);
            SettingsData first = registry.CreateDefaults();
            first.audio.master = 0.3f;
            Assert.IsTrue(store.Save(first, out string error1), error1);
            SettingsData second = registry.CreateDefaults();
            second.audio.master = 0.4f;
            Assert.IsTrue(store.Save(second, out string error2), error2);

            File.WriteAllText(path, "{ broken json");

            var service = CreateService(registry, new ManualTimer(), new RecordingApplier());
            string loadError = null;
            service.LoadFailed += value => loadError = value;
            service.LoadAsync().GetAwaiter().GetResult();

            Assert.IsNull(loadError, "备份可读时不应上报加载失败。");
            Assert.AreEqual(0.3f, service.Current.audio.master, 0.0001f);
        }

        [Test]
        public void Load_BothCorrupt_UsesDefaultsAndRaisesLoadFailed()
        {
            File.WriteAllText(path, "{ broken json");
            File.WriteAllText(path + ".bak", "also broken");

            var service = CreateService(DesktopRegistry(), new ManualTimer(), new RecordingApplier());
            string loadError = null;
            service.LoadFailed += value => loadError = value;
            service.LoadAsync().GetAwaiter().GetResult();

            Assert.IsTrue(service.IsLoaded);
            Assert.IsNotNull(loadError);
            Assert.AreEqual(0.8f, service.Current.audio.master, 0.0001f);
        }

        // ============================ 生命周期与启动应用 ============================

        [Test]
        public void Lifecycle_Pause_Saves()
        {
            var lifecycle = new FakeLifecycle();
            var service = CreateService(DesktopRegistry(), new ManualTimer(), new RecordingApplier());
            service.LoadAsync().GetAwaiter().GetResult();
            service.AttachLifecycle(lifecycle);
            service.Set("audio.master", 0.55f);

            lifecycle.RaisePause(true);

            Assert.IsTrue(File.Exists(path));
            var reloaded = CreateService(DesktopRegistry(), new ManualTimer(), new RecordingApplier());
            reloaded.LoadAsync().GetAwaiter().GetResult();
            Assert.AreEqual(0.55f, reloaded.Current.audio.master, 0.0001f);
        }

        [Test]
        public void Boot_AppliesAllAppliers()
        {
            var applier = new RecordingApplier();
            var service = CreateService(DesktopRegistry(), new ManualTimer(), applier);
            service.LoadAsync().GetAwaiter().GetResult();
            service.ApplyBootAsync().GetAwaiter().GetResult();
            Assert.AreEqual(SettingsApplyReason.Boot, applier.LastReason);
        }

        // ============================ 替身 ============================

        private sealed class SectionMapping
        {
            public readonly Type Type;
            public readonly string Prefix;
            public SectionMapping(Type type, string prefix) { Type = type; Prefix = prefix; }
        }

        private sealed class ManualTimer : ITimerService
        {
            private readonly List<ManualHandle> handles = new List<ManualHandle>();

            private sealed class ManualHandle : ITimerHandle
            {
                public float Remaining;
                public Action Callback;
                public bool Completed;
                public bool Canceled;
                public bool IsCompleted { get { return Completed; } }
                public bool IsCanceled { get { return Canceled; } }
                public void Cancel() { Canceled = true; }
                public void Dispose() { Canceled = true; }
            }

            public ITimerHandle Delay(float seconds, Action callback, bool scaled = true)
            {
                var handle = new ManualHandle { Remaining = Mathf.Max(0f, seconds), Callback = callback };
                handles.Add(handle);
                return handle;
            }

            public ITimerHandle Every(float interval, Action callback, bool scaled = true)
            {
                return Delay(interval, callback, scaled);
            }

            public Task DelayAsync(float seconds, CancellationToken token = default, bool scaled = true)
            {
                return Task.CompletedTask;
            }

            public void Advance(float seconds)
            {
                var snapshot = new List<ManualHandle>(handles);
                var due = new List<ManualHandle>();
                foreach (ManualHandle handle in snapshot)
                {
                    if (handle.Canceled || handle.Completed) continue;
                    handle.Remaining -= seconds;
                    if (handle.Remaining <= 0f) due.Add(handle);
                }
                foreach (ManualHandle handle in due)
                {
                    if (handle.Canceled) continue;
                    handle.Completed = true;
                    handle.Callback?.Invoke();
                }
                handles.RemoveAll(handle => handle.Canceled || handle.Completed);
            }
        }

        private sealed class RecordingApplier : ISettingsApplier
        {
            public string Name { get { return "Recording"; } }
            public int Order { get { return 0; } }
            public int Calls;
            public SettingsApplyReason LastReason;
            public SettingsData Last;
            public readonly List<SettingsApplyReason> Reasons = new List<SettingsApplyReason>();

            public bool CanApply(SettingsData data) { return true; }

            public void Apply(SettingsData previous, SettingsData next, SettingsApplyReason reason)
            {
                Calls++;
                LastReason = reason;
                Last = next.Clone();
                Reasons.Add(reason);
            }
        }

        private sealed class FakeAudio : IAudioService
        {
            public float Music;
            public float Sfx;
            public void PlayBKMusic(string path, AssetLoadMethod method = AssetLoadMethod.Resources, float fadeDuration = -1f, bool restartIfSame = false) { }
            public void StopBKMusic(float fadeDuration = -1f) { }
            public void PlayUniqueSound(string path, AssetLoadMethod method = AssetLoadMethod.Resources) { }
            public void StopAllSound() { }
            public void ChangeBKValue(float volume) { Music = volume; }
            public void ChangeSoundValue(float volume) { Sfx = volume; }
        }

        private sealed class FakeInput : IInputService
        {
            public int ResetCount;
            public InputBinding[] Applied;
            public event Action BindingsChanged;

            public float GetAxis(string action, string context = InputContexts.Gameplay) { return 0f; }
            public bool GetButton(string action, string context = InputContexts.Gameplay) { return false; }
            public bool GetButtonDown(string action, string context = InputContexts.Gameplay) { return false; }
            public bool GetButtonUp(string action, string context = InputContexts.Gameplay) { return false; }
            public void Bind(InputBinding binding) { BindingsChanged?.Invoke(); }
            public bool Unbind(string action, string context = InputContexts.Gameplay) { return false; }
            public IReadOnlyList<InputBinding> GetBindings() { return Array.Empty<InputBinding>(); }
            public IDisposable PushContext(string context, bool blockLower = true) { return new DummyDisposable(); }
            public InputRebindResult TryRebind(InputBinding binding, bool allowReservedKey = false) { return InputRebindResult.Success; }
            public bool TryCaptureKey(out KeyCode key) { key = KeyCode.None; return false; }
            public bool ResetToDefaults() { ResetCount++; return true; }
            public IReadOnlyList<InputBinding> GetDefaultBindings() { return Array.Empty<InputBinding>(); }
            public InputBinding[] ExportOverrides() { return Array.Empty<InputBinding>(); }
            public void ApplyOverrides(IEnumerable<InputBinding> overrides)
            {
                var list = new List<InputBinding>();
                if (overrides != null) foreach (InputBinding binding in overrides) list.Add(binding);
                Applied = list.ToArray();
            }
        }

        private sealed class DummyDisposable : IDisposable
        {
            public void Dispose() { }
        }

        private sealed class FakeLog : ILogService
        {
            public readonly List<string> Messages = new List<string>();
            public bool IsEnabled(LogLevel level) { return true; }
            public void Log(LogLevel level, object message) { Messages.Add(message == null ? "null" : message.ToString()); }
            public void LogException(Exception exception) { Messages.Add(exception == null ? "null" : exception.ToString()); }
        }

        private sealed class FakeLifecycle : IAppLifecycle
        {
            public bool IsPaused { get; private set; }
            public bool IsFocused { get; private set; }
            public event Action<bool> PauseChanged;
            public event Action<bool> FocusChanged;
            public event Action LowMemory;
            public event Action Quitting;

            public void RaisePause(bool paused) { IsPaused = paused; PauseChanged?.Invoke(paused); }
            public void RaiseFocus(bool focused) { IsFocused = focused; FocusChanged?.Invoke(focused); }
            public void RaiseLowMemory() { LowMemory?.Invoke(); }
            public void RaiseQuitting() { Quitting?.Invoke(); }
        }
    }
}
