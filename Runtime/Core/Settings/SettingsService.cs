using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>
    /// 系统设置服务实现：加载/归一化/应用/保存/恢复默认/预览回滚。
    /// <para>数据流：UI/业务 → Set（校验+归一化）→ Applier 应用 → 标脏 → 防抖保存；
    /// 预览项先写 Pending 并应用，Commit 保留，Rollback/超时回滚。</para>
    /// <para>扩展方式：新增设置项只改 SettingsData + SettingsRegistry（必要时加 Applier 映射）；
    /// 本类无需修改。</para>
    /// </summary>
    public sealed class SettingsService : ISettingsService, IDisposable
    {
        private const float AutoSaveDelaySeconds = 1f;

        private readonly SettingsStore store;
        private readonly SettingsRegistry registry;
        private readonly SettingsMigrationChain migrations;
        private readonly SettingsSafeMode safeMode;
        private readonly List<ISettingsApplier> appliers = new List<ISettingsApplier>();
        private readonly ILogService log;
        private readonly ITimerService timer;

        private SettingsData current;
        private SettingsData pending;
        private SettingsData previewSnapshot;
        private bool loaded;
        private bool previewing;
        private bool dirty;
        private bool disposed;
        private float previewSeconds = 15f;
        private ITimerHandle saveHandle;
        private ITimerHandle previewHandle;
        private IAppLifecycle lifecycle;

        public SettingsService(SettingsStore store, SettingsRegistry registry,
            IEnumerable<ISettingsApplier> appliers = null, ILogService log = null, ITimerService timer = null,
            SettingsMigrationChain migrations = null, SettingsSafeMode safeMode = null)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
            this.log = log;
            this.timer = timer;
            this.migrations = migrations ?? new SettingsMigrationChain();
            this.safeMode = safeMode;

            if (appliers != null)
            {
                foreach (ISettingsApplier applier in appliers)
                {
                    if (applier != null) this.appliers.Add(applier);
                }
            }
            this.appliers.Sort((left, right) => left.Order.CompareTo(right.Order));

            current = registry.CreateDefaults();
            pending = current.Clone();
        }

        #region 属性与事件

        public bool IsLoaded { get { return loaded; } }
        public bool IsPreviewing { get { return previewing; } }
        /// <summary>本次启动是否因连续失败进入了安全模式（强制窗口化/低画质）。</summary>
        public bool SafeModeActive { get; private set; }
        /// <summary>迁移链；自定义迁移需在 <see cref="LoadAsync"/> 之前追加。</summary>
        public SettingsMigrationChain Migrations { get { return migrations; } }
        public SettingsData Current { get { return current; } }
        public SettingsData Pending { get { return previewing ? pending : current; } }

        public float PreviewSeconds
        {
            get { return previewSeconds; }
            set { previewSeconds = Mathf.Clamp(value, 0f, 120f); }
        }

        public event Action Loaded;
        public event Action<string> LoadFailed;
        public event Action<SettingsChange> Changed;
        public event Action<SettingsSaveReason> Saved;
        public event Action<string> SaveFailed;
        public event Action<SettingsApplyFailure> ApplyFailed;
        public event Action<bool> PreviewStateChanged;

        #endregion

        #region 加载与应用

        /// <inheritdoc />
        public Task LoadAsync(CancellationToken token = default)
        {
            if (loaded || disposed) return Task.CompletedTask;
            if (token.IsCancellationRequested) return Task.FromCanceled(token);

            LoadOutcome outcome;
            try
            {
                // 设置文件很小（< 64KB），同步读取可控；避免在 Unity 主线程同步等待 Task 造成死锁。
                outcome = LoadFromDisk();
            }
            catch (Exception exception)
            {
                outcome = new LoadOutcome(false, null, exception.Message, false);
            }

            current = outcome.Data != null ? outcome.Data : registry.CreateDefaults();
            current.EnsureDefaults();
            bool migrated = false;
            if (current.schemaVersion != SettingsData.CurrentSchemaVersion)
            {
                string migrationError;
                int fromVersion = current.schemaVersion;
                if (migrations.TryMigrate(current, out migrationError))
                {
                    migrated = true;
                    if (log != null)
                        log.Info("[Settings] 设置结构已从 v" + fromVersion + " 迁移到 v" + SettingsData.CurrentSchemaVersion + "。");
                }
                else
                {
                    if (log != null) log.Error("[Settings] 设置迁移失败，已使用默认值：" + migrationError);
                    current = registry.CreateDefaults();
                    outcome = new LoadOutcome(false, current, migrationError, false);
                }
            }

            SafeModeActive = safeMode != null && safeMode.IsActive;
            if (SafeModeActive)
            {
                ApplySafeMode(current);
                if (log != null) log.Warning("[Settings] 连续启动失败达到阈值，已进入安全模式（窗口化 1280×720 + 低画质）。");
            }

            current.EnsureDefaults();
            int normalized = registry.NormalizeAll(current);
            if (normalized > 0 && log != null)
                log.Warning("[Settings] 加载时修正了 " + normalized + " 个越界/非法设置值。");
            if (outcome.RecoveredFromBackup && log != null)
                log.Warning("[Settings] " + outcome.Error);

            pending = current.Clone();
            loaded = true;
            dirty = false;
            if (migrated || SafeModeActive)
            {
                dirty = true;
                ScheduleSave();
            }

            if (!outcome.Success && !string.IsNullOrEmpty(outcome.Error))
            {
                if (log != null) log.Error("[Settings] 设置加载失败，已使用默认值：" + outcome.Error);
                LoadFailed?.Invoke(outcome.Error);
            }
            Loaded?.Invoke();
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task ApplyBootAsync(CancellationToken token = default)
        {
            if (!loaded || disposed) return Task.CompletedTask;
            if (token.IsCancellationRequested) return Task.FromCanceled(token);
            ApplyAll(current.Clone(), current, SettingsApplyReason.Boot);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public T Get<T>(string key)
        {
            T value;
            if (!TryGet(key, out value)) throw new KeyNotFoundException("未注册或类型不符的设置项：" + key);
            return value;
        }

        /// <inheritdoc />
        public bool TryGet<T>(string key, out T value)
        {
            value = default;
            SettingDescriptor descriptor = registry.Get(key);
            if (descriptor == null) return false;
            object raw = descriptor.Get(current);
            if (raw is T typed) { value = typed; return true; }
            return false;
        }

        /// <inheritdoc />
        public bool Set<T>(string key, T value, SettingsApplyMode? mode = null)
        {
            if (!loaded || disposed) return false;
            SettingDescriptor descriptor = registry.Get(key);
            if (descriptor == null)
            {
                if (log != null) log.Warning("[Settings] 未知设置项：" + key);
                return false;
            }
            string reason;
            if (!IsAvailable(key, out reason))
            {
                if (log != null) log.Warning("[Settings] 设置项不可用：" + key + "（" + reason + "）");
                return false;
            }

            if (!MatchesRuntimeType(descriptor, value))
            {
                if (log != null) log.Warning("[Settings] 设置值类型不符：" + key);
                return false;
            }

            object normalized = descriptor.Normalize(value);
            if (!MatchesRuntimeType(descriptor, normalized))
            {
                if (log != null) log.Warning("[Settings] 归一化后类型不符：" + key);
                return false;
            }

            SettingsApplyMode applyMode = mode ?? descriptor.ApplyMode;
            bool startPreview = applyMode == SettingsApplyMode.PreviewConfirm && !previewing;
            SettingsData target = startPreview ? current.Clone() : (previewing ? pending : current);
            object oldValue = descriptor.Get(target);
            if (SettingsValueUtility.ValuesEqual(oldValue, normalized)) return true;

            SettingsData before = target.Clone();
            try { descriptor.Set(target, normalized); }
            catch (Exception exception)
            {
                if (log != null) log.Error("[Settings] 写入设置失败：" + key + "：" + exception.Message);
                return false;
            }

            // 帧率上限与 VSync 冲突：桌面/移动平台在 vSyncCount>0 时会忽略 targetFrameRate。
            // 玩家显式改帧率时自动关 VSync，保证设置立即生效；之后再手动开 VSync 则按玩家选择优先。
            if (key == "display.frameRateLimit")
            {
                SettingDescriptor vsyncDescriptor = registry.Get("display.vsync");
                if (vsyncDescriptor != null && target.display.vsync)
                {
                    vsyncDescriptor.Set(target, false);
                    if (log != null)
                        log.Info("[Settings] 修改帧率上限，已自动关闭垂直同步（VSync 会覆盖 targetFrameRate）。");
                }
            }

            if (startPreview)
            {
                previewSnapshot = current.Clone();
                pending = target;
                previewing = true;
            }

            bool deferred = applyMode == SettingsApplyMode.NextLaunch || applyMode == SettingsApplyMode.RestartRequired;
            if (!deferred) ApplyAll(before, target, SettingsApplyReason.UserChange);

            dirty = true;
            ScheduleSave();
            // 逐项广播：afterSet 可能连带修改其它字段（例如改画质子项把预设标为 Custom）。
            RaiseChanges(before, target, !deferred, previewing);

            if (startPreview)
            {
                StartPreviewTimer();
                PreviewStateChanged?.Invoke(true);
            }
            return true;
        }

        /// <inheritdoc />
        public Task<bool> CommitAsync(CancellationToken token = default)
        {
            if (!loaded || disposed) return Task.FromResult(false);
            if (token.IsCancellationRequested) return Task.FromCanceled<bool>(token);

            if (previewing)
            {
                SettingsData before = current.Clone();
                SettingsData applied = pending.Clone();
                current = applied;
                pending = current.Clone();
                previewing = false;
                previewSnapshot = null;
                CancelPreviewTimer();

                ApplyAll(applied, current, SettingsApplyReason.UserChange);
                dirty = true;
                ScheduleSave();
                RaiseChanges(before, current, true, false);
                PreviewStateChanged?.Invoke(false);
            }

            return Task.FromResult(SaveCurrent(SettingsSaveReason.Explicit));
        }

        /// <inheritdoc />
        public void Rollback()
        {
            if (!loaded || disposed || !previewing) return;

            SettingsData applied = pending.Clone();
            SettingsData restored = previewSnapshot != null ? previewSnapshot.Clone() : current.Clone();
            previewing = false;
            previewSnapshot = null;
            CancelPreviewTimer();
            ApplyAll(applied, restored, SettingsApplyReason.Rollback);

            // current 在预览期间未被修改，restored 与 current 等价；统一用 current 作为真源。
            pending = current.Clone();
            RaiseChanges(applied, current, true, false);
            PreviewStateChanged?.Invoke(false);
        }

        /// <inheritdoc />
        public Task SaveAsync(SettingsSaveReason reason, CancellationToken token = default)
        {
            if (!loaded || disposed) return Task.CompletedTask;
            if (token.IsCancellationRequested) return Task.FromCanceled(token);

            SaveCurrent(reason);
            return Task.CompletedTask;
        }

        private bool SaveCurrent(SettingsSaveReason reason)
        {
            CancelSaveTimer();
            string error;
            bool success = store.Save(current, out error);
            if (success)
            {
                dirty = false;
                Saved?.Invoke(reason);
            }
            else
            {
                if (log != null) log.Error("[Settings] 设置保存失败：" + error);
                SaveFailed?.Invoke(error);
            }
            return success;
        }

        /// <inheritdoc />
        public void Reset(SettingsScope scope, string categoryOrKey = null)
        {
            if (!loaded || disposed) return;
            // 预览未提交时先回滚，避免恢复默认只改 current 而平台仍停留在预览值。
            if (previewing) Rollback();

            SettingsCategory? categoryFilter = null;
            if (scope == SettingsScope.Category)
            {
                if (string.IsNullOrWhiteSpace(categoryOrKey))
                {
                    if (log != null) log.Warning("[Settings] 恢复默认缺少分区名。");
                    return;
                }
                SettingsCategory parsed;
                if (!Enum.TryParse(categoryOrKey, true, out parsed))
                {
                    if (log != null) log.Warning("[Settings] 恢复默认的分区无效：" + categoryOrKey);
                    return;
                }
                categoryFilter = parsed;
            }
            else if (scope == SettingsScope.Item && string.IsNullOrWhiteSpace(categoryOrKey))
            {
                if (log != null) log.Warning("[Settings] 恢复默认缺少设置项 key。");
                return;
            }

            SettingsData before = current.Clone();
            SettingsData defaults = registry.CreateDefaults();
            IReadOnlyList<SettingDescriptor> all = registry.Describe(null);
            for (int i = 0; i < all.Count; i++)
            {
                SettingDescriptor descriptor = all[i];
                bool matched;
                switch (scope)
                {
                    case SettingsScope.All: matched = true; break;
                    case SettingsScope.Category: matched = categoryFilter.HasValue && descriptor.Category == categoryFilter.Value; break;
                    default: matched = string.Equals(descriptor.Key, categoryOrKey, StringComparison.Ordinal); break;
                }
                if (matched) descriptor.Set(current, descriptor.Get(defaults));
            }

            // 子项 afterSet 可能把预设标成 Custom；恢复分区/全部时用默认预设覆盖回来。
            if (scope != SettingsScope.Item)
            {
                SettingDescriptor preset = registry.Get("graphics.preset");
                if (preset != null && (scope == SettingsScope.All || categoryFilter == SettingsCategory.Graphics))
                    preset.Set(current, preset.Get(defaults));
            }

            if (previewing)
            {
                previewing = false;
                previewSnapshot = null;
                CancelPreviewTimer();
                PreviewStateChanged?.Invoke(false);
            }
            pending = current.Clone();

            ApplyAll(before, current, SettingsApplyReason.Reset);
            dirty = true;
            ScheduleSave();
            RaiseChanges(before, current, true, false);
        }

        /// <inheritdoc />
        public bool IsAvailable(string key, out string reason)
        {
            SettingDescriptor descriptor = registry.Get(key);
            if (descriptor == null)
            {
                reason = "未知设置项";
                return false;
            }
            return descriptor.IsAvailable(registry.Platform, current, out reason);
        }

        /// <inheritdoc />
        public IReadOnlyList<SettingDescriptor> Describe(SettingsCategory? category = null)
        {
            return registry.Describe(category);
        }

        #endregion

        #region 生命周期接入

        /// <summary>
        /// 接入应用生命周期：切后台/退出/低内存时保存。
        /// <para>由组合根在创建 IAppLifecycle 后调用；重复调用会先解除旧订阅。</para>
        /// </summary>
        public void AttachLifecycle(IAppLifecycle value)
        {
            DetachLifecycle();
            lifecycle = value;
            if (lifecycle == null) return;
            lifecycle.PauseChanged += OnPauseChanged;
            lifecycle.Quitting += OnQuitting;
            lifecycle.LowMemory += OnLowMemory;
        }

        private void DetachLifecycle()
        {
            if (lifecycle == null) return;
            lifecycle.PauseChanged -= OnPauseChanged;
            lifecycle.Quitting -= OnQuitting;
            lifecycle.LowMemory -= OnLowMemory;
            lifecycle = null;
        }

        private void OnPauseChanged(bool paused)
        {
            if (paused) _ = SaveAsync(SettingsSaveReason.AppPause);
        }

        private void OnQuitting() { _ = SaveAsync(SettingsSaveReason.Quit); }

        private void OnLowMemory() { _ = SaveAsync(SettingsSaveReason.LowMemory); }

        /// <summary>释放：取消计时器、解除生命周期订阅；保留最后一次落盘由组合根负责。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            DetachLifecycle();
            CancelSaveTimer();
            CancelPreviewTimer();
            previewing = false;
            previewSnapshot = null;
        }

        #endregion

        #region 内部实现

        private LoadOutcome LoadFromDisk()
        {
            SettingsData data;
            string error;
            bool recovered;
            bool success = store.TryLoad(out data, out error, out recovered);
            return new LoadOutcome(success, data, error, recovered);
        }

        /// <summary>安全模式只改“可能导致黑屏/不可操作”的显示与画质，不动语言、音频和隐私选择。</summary>
        private static void ApplySafeMode(SettingsData data)
        {
            if (data == null) return;
            data.display.mode = DisplayModeSetting.Windowed;
            data.display.width = 1280;
            data.display.height = 720;
            data.display.refreshRate = 0;
            data.display.vsync = true;
            data.display.frameRateLimit = FrameRateLimit.Fps60;
            data.graphics.preset = QualityPreset.Low;
            data.graphics.antiAliasing = AntiAliasingMode.Off;
            data.graphics.shadowQuality = ShadowQualityLevel.Off;
            data.graphics.textureQuality = TextureQualityLevel.Low;
            data.graphics.renderScale = 0.75f;
            data.accessibility.uiScale = 1f;
        }

        private void ApplyAll(SettingsData previous, SettingsData next, SettingsApplyReason reason)
        {
            if (next == null) return;
            for (int i = 0; i < appliers.Count; i++)
            {
                ISettingsApplier applier = appliers[i];
                try
                {
                    if (!applier.CanApply(next)) continue;
                    applier.Apply(previous, next, reason);
                }
                catch (Exception exception)
                {
                    if (log != null) log.Error("[Settings] 应用器失败（" + applier.Name + "）：" + exception.Message);
                    ApplyFailed?.Invoke(new SettingsApplyFailure(applier.Name, exception.Message));
                }
            }
        }

        private void ScheduleSave()
        {
            CancelSaveTimer();
            if (timer == null)
            {
                _ = SaveAsync(SettingsSaveReason.Auto);
                return;
            }
            saveHandle = timer.Delay(AutoSaveDelaySeconds, () =>
            {
                saveHandle = null;
                _ = SaveAsync(SettingsSaveReason.Auto);
            });
        }

        private void CancelSaveTimer()
        {
            if (saveHandle == null) return;
            saveHandle.Dispose();
            saveHandle = null;
        }

        private void StartPreviewTimer()
        {
            CancelPreviewTimer();
            if (previewSeconds <= 0f || timer == null) return;
            previewHandle = timer.Delay(previewSeconds, () =>
            {
                previewHandle = null;
                if (!previewing) return;
                if (log != null) log.Info("[Settings] 预览超时，已自动回滚。");
                Rollback();
            });
        }

        private void CancelPreviewTimer()
        {
            if (previewHandle == null) return;
            previewHandle.Dispose();
            previewHandle = null;
        }

        private void RaiseChanged(SettingDescriptor descriptor, object oldValue, object newValue, bool applied, bool preview)
        {
            Changed?.Invoke(new SettingsChange(descriptor.Key, descriptor.Category, oldValue, newValue, applied, preview));
        }

        private void RaiseChanges(SettingsData before, SettingsData after, bool applied, bool preview)
        {
            if (before == null || after == null) return;
            IReadOnlyList<SettingDescriptor> all = registry.Describe(null);
            for (int i = 0; i < all.Count; i++)
            {
                SettingDescriptor descriptor = all[i];
                object oldValue = descriptor.Get(before);
                object newValue = descriptor.Get(after);
                if (SettingsValueUtility.ValuesEqual(oldValue, newValue)) continue;
                RaiseChanged(descriptor, oldValue, newValue, applied, preview);
            }
        }

        private static bool MatchesRuntimeType(SettingDescriptor descriptor, object value)
        {
            switch (descriptor.ValueType)
            {
                case SettingsValueType.Bool: return value is bool;
                case SettingsValueType.Int: return value is int;
                case SettingsValueType.Float: return value is float;
                case SettingsValueType.Enum: return value != null && value.GetType() == descriptor.DefaultValue.GetType();
                case SettingsValueType.String: return value is string;
                default: return true;
            }
        }

        private sealed class LoadOutcome
        {
            public readonly bool Success;
            public readonly SettingsData Data;
            public readonly string Error;
            public readonly bool RecoveredFromBackup;

            public LoadOutcome(bool success, SettingsData data, string error, bool recoveredFromBackup)
            {
                Success = success;
                Data = data;
                Error = error;
                RecoveredFromBackup = recoveredFromBackup;
            }
        }

        #endregion
    }
}
