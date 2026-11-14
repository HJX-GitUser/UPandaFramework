using System;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>系统语言 → 项目 locale 的默认映射。未支持的语言返回 null，由服务回退 default。</summary>
    public static class SystemLocaleMapper
    {
        public static string Map(SystemLanguage language)
        {
            switch (language)
            {
                case SystemLanguage.ChineseSimplified: return "zh-Hans";
                case SystemLanguage.ChineseTraditional: return "zh-Hant";
                case SystemLanguage.English: return "en";
                case SystemLanguage.Japanese: return "ja";
                case SystemLanguage.Korean: return "ko";
                case SystemLanguage.French: return "fr";
                case SystemLanguage.German: return "de";
                case SystemLanguage.Spanish: return "es";
                case SystemLanguage.Russian: return "ru";
                case SystemLanguage.Portuguese: return "pt";
                case SystemLanguage.Italian: return "it";
                case SystemLanguage.Polish: return "pl";
                case SystemLanguage.Turkish: return "tr";
                case SystemLanguage.Arabic: return "ar";
                default: return null;
            }
        }

        /// <summary>把常见区域写法归一到项目语言码（en-US→en、zh-CN→zh-Hans、zh-TW→zh-Hant）。</summary>
        public static string Normalize(string locale)
        {
            if (string.IsNullOrWhiteSpace(locale)) return null;
            string value = locale.Trim().Replace('_', '-');
            if (value.Equals("zh-CN", StringComparison.OrdinalIgnoreCase)
                || value.Equals("zh-SG", StringComparison.OrdinalIgnoreCase)) return "zh-Hans";
            if (value.Equals("zh-TW", StringComparison.OrdinalIgnoreCase)
                || value.Equals("zh-HK", StringComparison.OrdinalIgnoreCase)) return "zh-Hant";
            return value;
        }

        public static SystemLanguage FromLocalePrefix(string locale)
        {
            if (string.IsNullOrEmpty(locale)) return SystemLanguage.Unknown;
            int dash = locale.IndexOf('-');
            string prefix = dash > 0 ? locale.Substring(0, dash) : locale;
            switch (prefix.ToLowerInvariant())
            {
                case "zh": return SystemLanguage.Chinese;
                case "en": return SystemLanguage.English;
                case "ja": return SystemLanguage.Japanese;
                case "ko": return SystemLanguage.Korean;
                case "fr": return SystemLanguage.French;
                case "de": return SystemLanguage.German;
                case "es": return SystemLanguage.Spanish;
                case "ru": return SystemLanguage.Russian;
                case "pt": return SystemLanguage.Portuguese;
                default: return SystemLanguage.Unknown;
            }
        }
    }
}
