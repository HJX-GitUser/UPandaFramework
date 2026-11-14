using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>
    /// 设置应用器：把某个设置域写入平台 API。由 <see cref="SettingsService"/> 按 Order 排序执行，
    /// 单个应用器异常会被隔离（记录日志并触发 ApplyFailed），不影响其它域。
    /// <para>新增平台调用时：实现本接口并注册到组合根/服务构造参数，不要改面板或存储。</para>
    /// </summary>
    public interface ISettingsApplier
    {
        /// <summary>诊断名称。</summary>
        string Name { get; }
        /// <summary>执行顺序，越小越先执行。</summary>
        int Order { get; }
        /// <summary>当前数据/平台是否允许应用（能力探测）；false 时跳过。</summary>
        bool CanApply(SettingsData data);
        /// <summary>
        /// 应用设置。previous 为应用前的值（Boot 时等于 next），实现可据此跳过未变化字段。
        /// </summary>
        void Apply(SettingsData previous, SettingsData next, SettingsApplyReason reason);
    }

    /// <summary>默认应用器集合：音频、画质/帧率、显示分辨率、输入绑定。</summary>
    public static class SettingsAppliers
    {
        public static ISettingsApplier[] CreateDefaults(IAudioService audio, IInputService input, ILogService log)
        {
            return new ISettingsApplier[]
            {
                new AudioSettingsApplier(audio, log),
                new GraphicsSettingsApplier(),
                new DisplaySettingsApplier(log),
                new InputSettingsApplier(input),
                new FrameRateSettingsApplier(log)
            };
        }
    }

    /// <summary>帧率：最后应用，避免分辨率/画质初始化覆盖 VSync 或 targetFrameRate。</summary>
    public sealed class FrameRateSettingsApplier : ISettingsApplier
    {
        private readonly ILogService log;

        public FrameRateSettingsApplier(ILogService log = null) { this.log = log; }

        public string Name { get { return "FrameRate"; } }
        public int Order { get { return 90; } }
        public bool CanApply(SettingsData data) { return data != null; }

        public void Apply(SettingsData previous, SettingsData next, SettingsApplyReason reason)
        {
            if (next == null) return;
            bool boot = reason == SettingsApplyReason.Boot || previous == null;
            if (boot || previous.display.vsync != next.display.vsync)
                QualitySettings.vSyncCount = next.display.vsync ? 1 : 0;
            if (boot || previous.display.frameRateLimit != next.display.frameRateLimit)
                Application.targetFrameRate = ToTarget(next.display.frameRateLimit);

            if (QualitySettings.vSyncCount > 0
                && next.display.frameRateLimit != FrameRateLimit.Unlimited
                && log != null)
            {
                log.Warning("[Settings] 垂直同步开启时 Application.targetFrameRate 会被平台忽略；" +
                            "要让帧率上限生效，请关闭 display.vsync。");
            }
        }

        /// <summary>Unlimited 使用平台默认值 -1；其它值直接写入枚举整数。</summary>
        public static int ToTarget(FrameRateLimit limit)
        {
            return (int)limit;
        }
    }

    /// <summary>音频：主音量/静音 → AudioListener.volume；音乐/音效 → IAudioService（当前框架两通道）。</summary>
    public sealed class AudioSettingsApplier : ISettingsApplier
    {
        private readonly IAudioService audio;
        private readonly ILogService log;

        public string Name { get { return "Audio"; } }
        public int Order { get { return 0; } }

        public AudioSettingsApplier(IAudioService audio, ILogService log = null)
        {
            this.audio = audio;
            this.log = log;
        }

        public bool CanApply(SettingsData data) { return audio != null; }

        public void Apply(SettingsData previous, SettingsData next, SettingsApplyReason reason)
        {
            if (next == null || audio == null) return;
            AudioOptions settings = next.audio;

            // 主音量用 AudioListener.volume：暂停用的是 AudioListener.pause，两者互不覆盖。
            float effectiveMaster = settings.muteAll ? 0f : Mathf.Clamp01(settings.master);
            if (reason == SettingsApplyReason.Boot || previous == null || !Mathf.Approximately(AudioListener.volume, effectiveMaster))
                AudioListener.volume = effectiveMaster;

            bool musicChanged = reason == SettingsApplyReason.Boot || previous == null
                || !Mathf.Approximately(previous.audio.music, settings.music);
            if (musicChanged) audio.ChangeBKValue(Mathf.Clamp01(settings.music));

            bool sfxChanged = reason == SettingsApplyReason.Boot || previous == null
                || !Mathf.Approximately(previous.audio.sfx, settings.sfx);
            if (sfxChanged) audio.ChangeSoundValue(Mathf.Clamp01(settings.sfx));

            // UI/语音通道当前框架没有独立 Mixer 通道；数据已保存，等音频层扩展后由宿主接入。
            if (reason == SettingsApplyReason.Boot && log != null)
                log.Info("[Settings] 音频应用为 Master/Music/SFX 三通道；UI/语音通道待 AudioMixer 接入。");
        }
    }

    /// <summary>画质与帧率：预设展开为 MSAA/阴影/纹理，以及 VSync 与帧率上限。</summary>
    public sealed class GraphicsSettingsApplier : ISettingsApplier
    {
        public string Name { get { return "Graphics"; } }
        public int Order { get { return 10; } }

        public bool CanApply(SettingsData data) { return data != null; }

        public void Apply(SettingsData previous, SettingsData next, SettingsApplyReason reason)
        {
            if (next == null) return;
            bool boot = reason == SettingsApplyReason.Boot || previous == null;

            if (boot || GraphicsChanged(previous.graphics, next.graphics))
            {
                int antiAliasing;
                ShadowQuality shadows;
                ShadowResolution shadowResolution;
                int textureLimit;
                ResolveGraphics(next.graphics, out antiAliasing, out shadows, out shadowResolution, out textureLimit);

                QualitySettings.antiAliasing = antiAliasing;
                QualitySettings.shadows = shadows;
                QualitySettings.shadowResolution = shadowResolution;
                QualitySettings.masterTextureLimit = textureLimit;
                float renderScale = Mathf.Clamp(next.graphics.renderScale, 0.5f, 1f);
                if (boot || previous == null || !Mathf.Approximately(previous.graphics.renderScale, renderScale))
                    ScalableBufferManager.ResizeBuffers(renderScale, renderScale);
            }
        }

        private static bool GraphicsChanged(GraphicsOptions left, GraphicsOptions right)
        {
            return left.preset != right.preset
                || left.antiAliasing != right.antiAliasing
                || left.shadowQuality != right.shadowQuality
                || left.textureQuality != right.textureQuality;
        }

        private static void ResolveGraphics(GraphicsOptions settings, out int antiAliasing,
            out ShadowQuality shadows, out ShadowResolution shadowResolution, out int textureLimit)
        {
            switch (settings.preset)
            {
                case QualityPreset.Low:
                    antiAliasing = 0;
                    shadows = ShadowQuality.Disable;
                    shadowResolution = ShadowResolution.Low;
                    textureLimit = 2;
                    return;
                case QualityPreset.Medium:
                    antiAliasing = 2;
                    shadows = ShadowQuality.HardOnly;
                    shadowResolution = ShadowResolution.Medium;
                    textureLimit = 1;
                    return;
                case QualityPreset.High:
                    antiAliasing = 4;
                    shadows = ShadowQuality.All;
                    shadowResolution = ShadowResolution.High;
                    textureLimit = 0;
                    return;
                case QualityPreset.Ultra:
                    antiAliasing = 8;
                    shadows = ShadowQuality.All;
                    shadowResolution = ShadowResolution.VeryHigh;
                    textureLimit = 0;
                    return;
                default:
                    antiAliasing = (int)settings.antiAliasing;
                    shadows = ToShadowQuality(settings.shadowQuality);
                    shadowResolution = ToShadowResolution(settings.shadowQuality);
                    textureLimit = ToTextureLimit(settings.textureQuality);
                    return;
            }
        }

        private static ShadowQuality ToShadowQuality(ShadowQualityLevel level)
        {
            switch (level)
            {
                case ShadowQualityLevel.Off: return ShadowQuality.Disable;
                case ShadowQualityLevel.Low: return ShadowQuality.HardOnly;
                default: return ShadowQuality.All;
            }
        }

        private static ShadowResolution ToShadowResolution(ShadowQualityLevel level)
        {
            switch (level)
            {
                case ShadowQualityLevel.Off:
                case ShadowQualityLevel.Low: return ShadowResolution.Low;
                case ShadowQualityLevel.Medium: return ShadowResolution.Medium;
                default: return ShadowResolution.High;
            }
        }

        private static int ToTextureLimit(TextureQualityLevel level)
        {
            switch (level)
            {
                case TextureQualityLevel.Low: return 2;
                case TextureQualityLevel.Medium: return 1;
                default: return 0;
            }
        }
    }

    /// <summary>
    /// 分辨率选择：优先精确匹配（刷新率为 0 时取该分辨率最高刷新率），否则按尺寸近似选择。
    /// 纯函数，便于在不改屏幕的 EditMode 测试里覆盖。
    /// </summary>
    public static class DisplayResolutionResolver
    {
        public static bool TryResolve(int width, int height, int refreshRate,
            Resolution[] available, out Resolution resolved)
        {
            resolved = default(Resolution);
            if (width <= 0 || height <= 0 || available == null || available.Length == 0) return false;

            bool hasExact = false;
            Resolution bestExact = default(Resolution);
            int bestExactRefreshDelta = int.MaxValue;
            Resolution bestNear = default(Resolution);
            long bestNearScore = long.MaxValue;

            for (int i = 0; i < available.Length; i++)
            {
                Resolution candidate = available[i];
                if (candidate.width <= 0 || candidate.height <= 0) continue;

                if (candidate.width == width && candidate.height == height)
                {
                    int delta = refreshRate <= 0 ? 0 : Math.Abs(candidate.refreshRate - refreshRate);
                    if (!hasExact || delta < bestExactRefreshDelta
                        || (delta == bestExactRefreshDelta && candidate.refreshRate > bestExact.refreshRate))
                    {
                        hasExact = true;
                        bestExact = candidate;
                        bestExactRefreshDelta = delta;
                    }
                    continue;
                }

                long sizeDelta = Math.Abs((long)candidate.width - width) + Math.Abs((long)candidate.height - height);
                long refreshDelta = refreshRate <= 0 ? 0 : Math.Abs(candidate.refreshRate - refreshRate);
                long score = sizeDelta * 1000L + refreshDelta;
                if (score < bestNearScore)
                {
                    bestNearScore = score;
                    bestNear = candidate;
                }
            }

            if (hasExact)
            {
                resolved = bestExact;
                return true;
            }
            if (bestNearScore != long.MaxValue)
            {
                resolved = bestNear;
                return true;
            }
            return false;
        }
    }

    /// <summary>显示：分辨率/全屏模式/刷新率（PC）。移动端描述表不暴露这些项，应用器也会跳过无效宽高。</summary>
    public sealed class DisplaySettingsApplier : ISettingsApplier
    {
        private readonly ILogService log;

        public string Name { get { return "Display"; } }
        public int Order { get { return 30; } }

        public DisplaySettingsApplier(ILogService log = null) { this.log = log; }

        public bool CanApply(SettingsData data)
        {
            return data != null && !Application.isMobilePlatform;
        }

        public void Apply(SettingsData previous, SettingsData next, SettingsApplyReason reason)
        {
            if (next == null || Application.isMobilePlatform) return;
            DisplayOptions display = next.display;
            if (display.width <= 0 || display.height <= 0) return; // 0 = 沿用平台当前值

            bool boot = reason == SettingsApplyReason.Boot || previous == null;
            if (!boot
                && previous.display.width == display.width
                && previous.display.height == display.height
                && previous.display.mode == display.mode
                && previous.display.refreshRate == display.refreshRate
                && previous.display.monitor == display.monitor)
                return;

            TryMoveMonitor(display.monitor, boot, previous, display);

            Resolution target = new Resolution
            {
                width = display.width,
                height = display.height,
                refreshRate = display.refreshRate
            };
            Resolution resolved;
            if (DisplayResolutionResolver.TryResolve(display.width, display.height, display.refreshRate,
                    Screen.resolutions, out resolved))
            {
                target = resolved;
                if (target.width != display.width || target.height != display.height
                    || (display.refreshRate > 0 && target.refreshRate != display.refreshRate))
                {
                    if (log != null)
                        log.Warning("[Settings] 请求分辨率 " + display.width + "×" + display.height + "@" +
                                    display.refreshRate + " 不受支持，改用 " + target.width + "×" + target.height +
                                    "@" + target.refreshRate + "。");
                    // 把实际生效的合法值写回数据，面板/下次启动不再显示一个不存在的模式。
                    display.width = target.width;
                    display.height = target.height;
                    display.refreshRate = target.refreshRate;
                }
            }

            try
            {
                Screen.SetResolution(target.width, target.height, ToFullScreenMode(display.mode), target.refreshRate);
                ScheduleVerify(target.width, target.height);
            }
            catch (Exception exception)
            {
                if (log != null) log.Error("[Settings] 分辨率应用失败：" + exception.Message);
                SettingsSafeMode.Default.RecordFailure();
                // 回退窗口化 + 安全分辨率，避免黑屏后无法操作；同时把数据改成安全值。
                display.mode = DisplayModeSetting.Windowed;
                display.width = 1280;
                display.height = 720;
                display.refreshRate = 0;
                try { Screen.SetResolution(1280, 720, FullScreenMode.Windowed, 0); }
                catch (Exception) { }
            }
        }

        private void TryMoveMonitor(int monitor, bool boot, SettingsData previous, DisplayOptions current)
        {
            if (!boot && previous != null && previous.display.monitor == monitor) return;
            try
            {
                var layout = new List<DisplayInfo>();
                Screen.GetDisplayLayout(layout);
                if (monitor < 0 || monitor >= layout.Count) return;
                Screen.MoveMainWindowTo(layout[monitor], new Vector2Int(0, 0));
            }
            catch (Exception exception)
            {
                if (log != null) log.Warning("[Settings] 切换显示器失败：" + exception.Message);
                if (current != null) current.monitor = previous != null ? previous.display.monitor : 0;
            }
        }

        private static FullScreenMode ToFullScreenMode(DisplayModeSetting mode)
        {
            switch (mode)
            {
                case DisplayModeSetting.ExclusiveFullScreen: return FullScreenMode.ExclusiveFullScreen;
                case DisplayModeSetting.Borderless: return FullScreenMode.FullScreenWindow;
                default: return FullScreenMode.Windowed;
            }
        }

        /// <summary>Player 里等两帧核对实际分辨率；某些平台 SetResolution 会静默失败。</summary>
        private void ScheduleVerify(int width, int height)
        {
            if (!Application.isPlaying || Application.isEditor || PublicMono.Instance == null) return;
            _ = PublicMono.Instance.RunCoroutine(VerifyAfterFrames(width, height));
        }

        private IEnumerator VerifyAfterFrames(int width, int height)
        {
            yield return null;
            yield return null;
            if (Screen.width == width && Screen.height == height) yield break;
            if (log != null)
                log.Error("[Settings] 分辨率未生效：请求 " + width + "×" + height +
                          "，实际 " + Screen.width + "×" + Screen.height + "，回退窗口化 1280×720。");
            SettingsSafeMode.Default.RecordFailure();
            try { Screen.SetResolution(1280, 720, FullScreenMode.Windowed, 0); }
            catch (Exception) { }
        }
    }

    /// <summary>输入：键位覆盖层（ResetToDefaults + ApplyOverrides）；灵敏度等由玩法读取快照消费。</summary>
    public sealed class InputSettingsApplier : ISettingsApplier
    {
        private readonly IInputService input;

        public string Name { get { return "Input"; } }
        public int Order { get { return 60; } }

        public InputSettingsApplier(IInputService input) { this.input = input; }

        public bool CanApply(SettingsData data) { return input != null; }

        public void Apply(SettingsData previous, SettingsData next, SettingsApplyReason reason)
        {
            if (next == null || input == null) return;
            bool boot = reason == SettingsApplyReason.Boot || previous == null;
            if (!boot && BindingsEqual(previous.input.bindings, next.input.bindings)) return;

            // 先用默认表复位，再叠加覆盖层：这样“删掉覆盖”也能真正回到默认键位。
            input.ResetToDefaults();
            if (next.input.bindings != null && next.input.bindings.Length > 0)
                input.ApplyOverrides(next.input.bindings);
        }

        private static bool BindingsEqual(InputBinding[] left, InputBinding[] right)
        {
            int leftCount = left == null ? 0 : left.Length;
            int rightCount = right == null ? 0 : right.Length;
            if (leftCount != rightCount) return false;
            for (int i = 0; i < leftCount; i++)
            {
                InputBinding a = left[i];
                InputBinding b = right[i];
                if (a == null || b == null) { if (!ReferenceEquals(a, b)) return false; continue; }
                if (a.action != b.action || a.context != b.context || a.axis != b.axis
                    || a.positive != b.positive || a.negative != b.negative) return false;
            }
            return true;
        }
    }
}
