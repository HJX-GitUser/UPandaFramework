using System;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>语言目录（catalog.json）。内置目录与项目覆盖目录结构相同。</summary>
    [Serializable]
    public sealed class LocalizationCatalog
    {
        public int schemaVersion = 1;
        public string defaultLocale = "zh-Hans";
        public string fallbackLocale = "en";
        public LocaleInfo[] locales = Array.Empty<LocaleInfo>();

        public void EnsureDefaults()
        {
            if (string.IsNullOrWhiteSpace(defaultLocale)) defaultLocale = "zh-Hans";
            if (string.IsNullOrWhiteSpace(fallbackLocale)) fallbackLocale = "en";
            if (locales == null) locales = Array.Empty<LocaleInfo>();
        }

        public static LocalizationCatalog FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                LocalizationCatalog catalog = JsonUtility.FromJson<LocalizationCatalog>(json);
                if (catalog == null) return null;
                catalog.EnsureDefaults();
                return catalog;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static string ToJson(LocalizationCatalog catalog)
        {
            return JsonUtility.ToJson(catalog ?? new LocalizationCatalog(), true);
        }
    }

    /// <summary>单个语言表文件（&lt;locale&gt;.json）。</summary>
    [Serializable]
    public sealed class LocalizationTableFile
    {
        public int schemaVersion = 1;
        public string locale;
        public LocalizationEntry[] entries = Array.Empty<LocalizationEntry>();

        public void EnsureDefaults()
        {
            if (entries == null) entries = Array.Empty<LocalizationEntry>();
        }

        public static LocalizationTableFile FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                LocalizationTableFile table = JsonUtility.FromJson<LocalizationTableFile>(json);
                if (table == null) return null;
                table.EnsureDefaults();
                return table;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static string ToJson(LocalizationTableFile table)
        {
            return JsonUtility.ToJson(table ?? new LocalizationTableFile(), true);
        }
    }

    /// <summary>一条 key → 文案。</summary>
    [Serializable]
    public sealed class LocalizationEntry
    {
        public string key;
        public string value;

        public LocalizationEntry() { }

        public LocalizationEntry(string key, string value)
        {
            this.key = key;
            this.value = value;
        }
    }
}
