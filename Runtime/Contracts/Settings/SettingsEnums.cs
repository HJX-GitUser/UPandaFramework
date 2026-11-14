using System;

namespace UPandaGF
{
    /// <summary>设置分区。新增分区时同步追加枚举值、数据分区与描述表（见 Runtime/Core/Settings/README.md）。</summary>
    public enum SettingsCategory
    {
        Audio,
        Graphics,
        Display,
        Input,
        Language,
        Accessibility,
        Gameplay,
        Privacy,
        System
    }

    /// <summary>设置项的值类型；决定描述表的默认控件、校验方式与 UI 生成。</summary>
    public enum SettingsValueType
    {
        Bool,
        Int,
        Float,
        Enum,
        String,
        /// <summary>强类型对象（如键位覆盖数组）；由业务自行解释。</summary>
        Object
    }

    /// <summary>生效方式。预览确认项在超时未提交时自动回滚。</summary>
    public enum SettingsApplyMode
    {
        /// <summary>改完立即应用并保存。</summary>
        Immediate,
        /// <summary>先应用，等待玩家确认；超时/取消自动回滚。</summary>
        PreviewConfirm,
        /// <summary>只保存，下次启动/新游戏生效。</summary>
        NextLaunch,
        /// <summary>需要重启应用才能生效。</summary>
        RestartRequired
    }

    /// <summary>应用器执行原因；应用器可据此决定是否跳过未变化的字段。</summary>
    public enum SettingsApplyReason
    {
        Boot,
        UserChange,
        Rollback,
        Reset,
        External
    }

    /// <summary>恢复默认的范围。</summary>
    public enum SettingsScope
    {
        Item,
        Category,
        All
    }

    /// <summary>保存原因；用于日志、诊断与云同步触发。</summary>
    public enum SettingsSaveReason
    {
        Auto,
        Explicit,
        AppPause,
        Quit,
        LowMemory
    }

    /// <summary>云同步字段分级；本地实现不依赖它，P1 云同步按此筛选。</summary>
    public enum SettingsSyncScope
    {
        Device,
        Account,
        Never
    }

    /// <summary>设置项适用平台（描述表元数据）。</summary>
    [Flags]
    public enum SettingsPlatform
    {
        None = 0,
        Desktop = 1,
        Mobile = 2,
        Editor = 4,
        All = Desktop | Mobile | Editor
    }

    /// <summary>画质预设；<see cref="Custom"/> 表示玩家单独改过子项。</summary>
    public enum QualityPreset
    {
        Low,
        Medium,
        High,
        Ultra,
        Custom
    }

    /// <summary>抗锯齿档位（值为 MSAA 采样数）。</summary>
    public enum AntiAliasingMode
    {
        Off = 0,
        x2 = 2,
        x4 = 4,
        x8 = 8
    }

    /// <summary>阴影质量档位。</summary>
    public enum ShadowQualityLevel
    {
        Off,
        Low,
        Medium,
        High
    }

    /// <summary>纹理质量档位（映射到 mipmap 限制）。</summary>
    public enum TextureQualityLevel
    {
        Low,
        Medium,
        High
    }

    /// <summary>显示模式（避免与 UnityEngine.FullScreenMode 同名）。</summary>
    public enum DisplayModeSetting
    {
        ExclusiveFullScreen,
        Borderless,
        Windowed
    }

    /// <summary>字幕字号。</summary>
    public enum SubtitleSize
    {
        Small,
        Medium,
        Large,
        ExtraLarge
    }

    /// <summary>难度。</summary>
    public enum DifficultyLevel
    {
        Easy,
        Normal,
        Hard,
        Custom
    }

    /// <summary>瞄准方式（按住/切换）。</summary>
    public enum AimMode
    {
        Hold,
        Toggle
    }

    /// <summary>色觉辅助模式。</summary>
    public enum ColorBlindMode
    {
        Off,
        Protanopia,
        Deuteranopia,
        Tritanopia
    }

    /// <summary>帧率上限；<see cref="Unlimited"/> 写入 Application.targetFrameRate = -1。</summary>
    public enum FrameRateLimit
    {
        Fps30 = 30,
        Fps60 = 60,
        Fps120 = 120,
        Fps144 = 144,
        Fps240 = 240,
        Unlimited = -1
    }
}
