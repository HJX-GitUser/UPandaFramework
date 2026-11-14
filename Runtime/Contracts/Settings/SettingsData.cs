using System;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>
    /// 玩家系统设置的强类型数据（设备/账号偏好），可被 JsonUtility 序列化。
    /// <para>与存档、开发者配置分离：删档不影响设置；渠道配置不覆盖玩家值。</para>
    /// <para>新增设置项 = 在对应分区加字段 + 在 <c>SettingsRegistry</c> 加描述表条目；
    /// UI、存储、迁移会自动跟随（字段缺失时保留默认值）。</para>
    /// <para>字段名与描述表 key 的后缀一一对应（如 <c>audio.master</c> ↔ <see cref="AudioOptions.master"/>），
    /// 由 EditMode 测试的反射校验守住。</para>
    /// </summary>
    [Serializable]
    public sealed class SettingsData
    {
        /// <summary>当前设置文件结构版本。只有改名/语义变化/结构变化才递增。</summary>
        public const int CurrentSchemaVersion = 1;

        public int schemaVersion = CurrentSchemaVersion;
        public AudioOptions audio = new AudioOptions();
        public GraphicsOptions graphics = new GraphicsOptions();
        public DisplayOptions display = new DisplayOptions();
        public InputOptions input = new InputOptions();
        public LanguageOptions language = new LanguageOptions();
        public AccessibilityOptions accessibility = new AccessibilityOptions();
        public GameplayOptions gameplay = new GameplayOptions();
        public PrivacyOptions privacy = new PrivacyOptions();
        public SystemOptions system = new SystemOptions();

        /// <summary>深拷贝（用于预览/回滚/诊断）。新增字段自动包含，无需改拷贝代码。</summary>
        public SettingsData Clone()
        {
            return JsonUtility.FromJson<SettingsData>(JsonUtility.ToJson(this));
        }

        /// <summary>补齐缺失的分区与数组（JsonUtility 读到旧文件/空对象时使用）。</summary>
        public void EnsureDefaults()
        {
            if (audio == null) audio = new AudioOptions();
            if (graphics == null) graphics = new GraphicsOptions();
            if (display == null) display = new DisplayOptions();
            if (input == null) input = new InputOptions();
            if (language == null) language = new LanguageOptions();
            if (accessibility == null) accessibility = new AccessibilityOptions();
            if (gameplay == null) gameplay = new GameplayOptions();
            if (privacy == null) privacy = new PrivacyOptions();
            if (system == null) system = new SystemOptions();
            if (input.bindings == null) input.bindings = Array.Empty<InputBinding>();
        }
    }

    /// <summary>音频设置。当前框架只有 BGM/SFX 两路音量；UI/Voice 通道留待 AudioMixer 接入后启用。</summary>
    [Serializable]
    public sealed class AudioOptions
    {
        /// <summary>主音量（0–1），写入 AudioListener.volume；静音时临时置 0。</summary>
        public float master = 0.8f;
        /// <summary>背景音乐（0–1）。</summary>
        public float music = 0.7f;
        /// <summary>音效（0–1）。</summary>
        public float sfx = 0.8f;
        /// <summary>界面音效（0–1）；需由 AudioMixer binder 消费。</summary>
        public float ui = 1f;
        /// <summary>语音（0–1）；需由 AudioMixer binder 消费。</summary>
        public float voice = 1f;
        /// <summary>全部静音；不改变 master 的存储值。</summary>
        public bool muteAll;
        /// <summary>应用失焦时静音。</summary>
        public bool muteWhenUnfocused;
    }

    /// <summary>画面设置。预设会展开为子项；玩家单独改子项后预设显示为 Custom。</summary>
    [Serializable]
    public sealed class GraphicsOptions
    {
        public QualityPreset preset = QualityPreset.High;
        public AntiAliasingMode antiAliasing = AntiAliasingMode.x4;
        public ShadowQualityLevel shadowQuality = ShadowQualityLevel.High;
        public TextureQualityLevel textureQuality = TextureQualityLevel.High;
        /// <summary>渲染缩放（0.5–1.0）。存储值；实际由渲染管线/业务消费。</summary>
        public float renderScale = 1f;
        /// <summary>亮度/伽马（0.5–1.5）。存储值；实际由渲染管线/业务消费。</summary>
        public float brightness = 1f;
        /// <summary>相机视野（50–90）；由 SettingsCameraBinder 消费。</summary>
        public float fieldOfView = 60f;
    }

    /// <summary>显示设置。移动端只读/隐藏桌面专属项；宽高为 0 表示沿用平台当前值。</summary>
    [Serializable]
    public sealed class DisplayOptions
    {
        public DisplayModeSetting mode = DisplayModeSetting.Borderless;
        /// <summary>显示器索引（0 为主显示器）；由 DisplaySettingsApplier 移动窗口。</summary>
        public int monitor;
        /// <summary>目标宽度；0 = 不修改平台当前分辨率。</summary>
        public int width;
        /// <summary>目标高度；0 = 不修改平台当前分辨率。</summary>
        public int height;
        /// <summary>目标刷新率；0 = 使用平台默认。</summary>
        public int refreshRate;
        public bool vsync = true;
        public FrameRateLimit frameRateLimit = FrameRateLimit.Fps60;
    }

    /// <summary>输入设置。bindings 只保存相对默认表的覆盖（见 IInputService.ExportOverrides）。</summary>
    [Serializable]
    public sealed class InputOptions
    {
        public float mouseSensitivity = 1f;
        public float gamepadSensitivity = 1f;
        public bool invertY;
        public float gamepadDeadzone = 0.2f;
        public bool vibration = true;
        public AimMode aimMode = AimMode.Hold;
        public InputBinding[] bindings = Array.Empty<InputBinding>();
    }

    /// <summary>语言与文本设置。语言码为 BCP-47（如 zh-Hans、en）。</summary>
    [Serializable]
    public sealed class LanguageOptions
    {
        /// <summary>界面语言；空字符串表示跟随系统。</summary>
        public string ui = string.Empty;
        public bool subtitles = true;
        public SubtitleSize subtitleSize = SubtitleSize.Medium;
    }

    /// <summary>辅助功能设置；具体效果由玩法/UI/渲染层读取快照后消费。</summary>
    [Serializable]
    public sealed class AccessibilityOptions
    {
        /// <summary>UI 缩放（0.8–1.3）。</summary>
        public float uiScale = 1f;
        /// <summary>屏幕震动强度（0–1，0 = 完全关闭）。</summary>
        public float screenShake = 1f;
        /// <summary>闪光强度（0–1）。</summary>
        public float flashIntensity = 1f;
        public ColorBlindMode colorblindMode = ColorBlindMode.Off;
        public bool reduceMotion;
    }

    /// <summary>游戏性设置。</summary>
    [Serializable]
    public sealed class GameplayOptions
    {
        public DifficultyLevel difficulty = DifficultyLevel.Normal;
        public bool autosave = true;
    }

    /// <summary>隐私与合规设置；默认全部关闭，首次启动需显式同意。</summary>
    [Serializable]
    public sealed class PrivacyOptions
    {
        public bool telemetry;
        public bool crashReport;
        public bool personalizedAds;
        /// <summary>已同意的条款版本；0=未同意，-1=已拒绝，正数=已同意版本。</summary>
        public int termsVersion;
        /// <summary>同意/拒绝时间（UTC ISO-8601）。</summary>
        public string consentUtc = string.Empty;
    }

    /// <summary>系统状态（非玩家可调）。</summary>
    [Serializable]
    public sealed class SystemOptions
    {
        public bool firstRunCompleted;
    }
}
