using System.Collections.Generic;

namespace UPandaGF
{
    /// <summary>
    /// 设置面板的内置中文文案（无本地化系统时的兜底）。
    /// <para>业务接入本地化后，可给 <see cref="SettingsPresenter"/> 传 localize 回调覆盖；
    /// 未命中时回退到设置项 key，不会出现空白行。</para>
    /// </summary>
    public static class SettingsText
    {
        private static readonly Dictionary<string, string> labels = new Dictionary<string, string>
        {
            { "audio.master", "主音量" },
            { "audio.music", "背景音乐" },
            { "audio.sfx", "音效" },
            { "audio.ui", "界面音效" },
            { "audio.voice", "语音" },
            { "audio.muteWhenUnfocused", "失焦时静音" },
            { "audio.muteAll", "全部静音" },

            { "graphics.preset", "画质预设" },
            { "graphics.antiAliasing", "抗锯齿" },
            { "graphics.shadowQuality", "阴影质量" },
            { "graphics.textureQuality", "纹理质量" },
            { "graphics.renderScale", "渲染缩放" },
            { "graphics.brightness", "亮度" },
            { "graphics.fieldOfView", "相机视野" },

            { "display.mode", "显示模式" },
            { "display.monitor", "显示器" },
            { "display.width", "分辨率宽度" },
            { "display.height", "分辨率高度" },
            { "display.refreshRate", "刷新率" },
            { "display.vsync", "垂直同步" },
            { "display.frameRateLimit", "帧率上限" },

            { "input.mouseSensitivity", "鼠标灵敏度" },
            { "input.gamepadSensitivity", "手柄灵敏度" },
            { "input.invertY", "反转 Y 轴" },
            { "input.gamepadDeadzone", "手柄死区" },
            { "input.vibration", "震动" },
            { "input.aimMode", "瞄准方式" },
            { "input.bindings", "键位绑定" },

            { "language.ui", "界面语言" },
            { "language.subtitles", "字幕" },
            { "language.subtitleSize", "字幕大小" },

            { "accessibility.uiScale", "界面缩放" },
            { "accessibility.screenShake", "屏幕震动强度" },
            { "accessibility.flashIntensity", "闪光强度" },
            { "accessibility.colorblindMode", "色觉辅助" },
            { "accessibility.reduceMotion", "减弱动态效果" },

            { "gameplay.difficulty", "难度" },
            { "gameplay.autosave", "自动存档" },

            { "privacy.telemetry", "数据统计" },
            { "privacy.crashReport", "崩溃上报" },
            { "privacy.personalizedAds", "个性化广告" },

            { "system.firstRunCompleted", "首次启动完成" }
        };

        private static readonly Dictionary<string, string> categories = new Dictionary<string, string>
        {
            { "Audio", "音频" },
            { "Graphics", "画面" },
            { "Display", "显示" },
            { "Input", "操作" },
            { "Language", "语言" },
            { "Accessibility", "辅助功能" },
            { "Gameplay", "游戏" },
            { "Privacy", "隐私与网络" },
            { "System", "系统" }
        };

        private static readonly Dictionary<string, string> options = new Dictionary<string, string>
        {
            { "Low", "低" }, { "Medium", "中" }, { "High", "高" }, { "Ultra", "极高" }, { "Custom", "自定义" },
            { "Off", "关" }, { "x2", "2x" }, { "x4", "4x" }, { "x8", "8x" },
            { "ExclusiveFullScreen", "独占全屏" }, { "Borderless", "无边框窗口" }, { "Windowed", "窗口" },
            { "Fps30", "30" }, { "Fps60", "60" }, { "Fps120", "120" }, { "Fps144", "144" },
            { "Fps240", "240" }, { "Unlimited", "不限制" },
            { "Small", "小" }, { "Large", "大" }, { "ExtraLarge", "特大" },
            { "Easy", "简单" }, { "Normal", "普通" }, { "Hard", "困难" },
            { "Hold", "按住" }, { "Toggle", "切换" },
            { "Protanopia", "红色盲" }, { "Deuteranopia", "绿色盲" }, { "Tritanopia", "蓝色盲" }
        };

        /// <summary>设置项名称；未知 key 原样返回，便于发现遗漏。</summary>
        public static string Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            return labels.TryGetValue(key, out string value) ? value : key;
        }

        /// <summary>分区名称。</summary>
        public static string Category(SettingsCategory category)
        {
            string name = category.ToString();
            return categories.TryGetValue(name, out string value) ? value : name;
        }

        /// <summary>枚举选项名称；未命中返回原枚举名。</summary>
        public static string Option(string key, string optionName)
        {
            if (string.IsNullOrEmpty(optionName)) return string.Empty;
            return options.TryGetValue(optionName, out string value) ? value : optionName;
        }
    }
}
