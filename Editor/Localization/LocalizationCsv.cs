using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UPandaGF;

namespace UPandaGF.GFEditor
{
    /// <summary>CSV 解析结果（首行 key + 语言列；值按 key 保存）。</summary>
    public sealed class LocalizationCsvData
    {
        public readonly List<string> Locales = new List<string>();
        public readonly List<string> Keys = new List<string>();
        public readonly Dictionary<string, string[]> Values = new Dictionary<string, string[]>(StringComparer.Ordinal);
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();

        public bool HasErrors { get { return Errors.Count > 0; } }
    }

    /// <summary>本地化 CSV 解析/校验/导出 JSON 的纯逻辑层（可被 EditMode 测试直接调用）。</summary>
    public static class LocalizationCsv
    {
        private static readonly Regex LocalePattern = new Regex("^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*$", RegexOptions.Compiled);
        private static readonly Regex PlaceholderPattern = new Regex(@"\{(\d+)(?::[^{}]*)?\}", RegexOptions.Compiled);

        public static LocalizationCsvData Parse(string csv)
        {
            var data = new LocalizationCsvData();
            if (string.IsNullOrWhiteSpace(csv))
            {
                data.Errors.Add("CSV 内容为空");
                return data;
            }

            List<List<string>> rows = Tokenize(csv.TrimStart('\uFEFF'));
            if (rows.Count == 0)
            {
                data.Errors.Add("CSV 没有有效行");
                return data;
            }

            List<string> header = rows[0];
            if (header.Count < 2)
            {
                data.Errors.Add("CSV 首行必须包含 key 与至少一个语言列");
                return data;
            }
            if (!string.Equals(header[0].Trim(), "key", StringComparison.OrdinalIgnoreCase))
                data.Errors.Add("CSV 第一列必须是 key（当前：" + header[0] + "）");

            var seenLocales = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < header.Count; i++)
            {
                string locale = header[i].Trim();
                if (string.IsNullOrEmpty(locale))
                {
                    data.Errors.Add("第 " + (i + 1) + " 列语言码为空");
                    continue;
                }
                if (!LocalePattern.IsMatch(locale))
                    data.Errors.Add("语言码格式无效：" + locale);
                if (!seenLocales.Add(locale))
                    data.Errors.Add("语言列重复：" + locale);
                data.Locales.Add(locale);
            }

            for (int r = 1; r < rows.Count; r++)
            {
                List<string> row = rows[r];
                if (row.Count == 0 || IsEmptyRow(row)) continue;
                string key = row.Count > 0 ? row[0].Trim() : string.Empty;
                if (string.IsNullOrEmpty(key))
                {
                    data.Errors.Add("第 " + (r + 1) + " 行 key 为空");
                    continue;
                }
                if (data.Values.ContainsKey(key))
                {
                    data.Errors.Add("key 重复：" + key);
                    continue;
                }

                var values = new string[data.Locales.Count];
                for (int c = 0; c < data.Locales.Count; c++)
                {
                    values[c] = c + 1 < row.Count ? row[c + 1] : string.Empty;
                    if (string.IsNullOrEmpty(values[c]))
                        data.Warnings.Add("缺少翻译：" + key + " / " + data.Locales[c]);
                }
                data.Keys.Add(key);
                data.Values[key] = values;
            }

            ValidatePlaceholders(data);
            if (data.Keys.Count == 0) data.Errors.Add("CSV 没有数据行");
            return data;
        }

        private static void ValidatePlaceholders(LocalizationCsvData data)
        {
            for (int i = 0; i < data.Keys.Count; i++)
            {
                string key = data.Keys[i];
                string[] values = data.Values[key];
                if (values.Length == 0) continue;
                HashSet<string> reference = ExtractPlaceholders(values[0]);
                for (int c = 1; c < values.Length; c++)
                {
                    HashSet<string> current = ExtractPlaceholders(values[c]);
                    if (current.Count == 0 && string.IsNullOrEmpty(values[c])) continue;
                    if (!reference.SetEquals(current))
                        data.Errors.Add("占位符不一致：" + key + " / " + data.Locales[c]);
                }
            }
        }

        public static HashSet<string> ExtractPlaceholders(string value)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(value)) return result;
            foreach (Match match in PlaceholderPattern.Matches(value)) result.Add(match.Groups[1].Value);
            return result;
        }

        /// <summary>生成 catalog 与各语言 JSON；输出相对 Assets 的 Resources 目录。</summary>
        public static void WriteAssets(LocalizationCsvData data, string outputFolder)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.HasErrors) throw new InvalidDataException("CSV 校验失败：\n" + string.Join("\n", data.Errors));
            if (string.IsNullOrWhiteSpace(outputFolder)) throw new ArgumentException("输出目录为空");

            EditorAssetPathUtility.EnsureFolder(outputFolder);
            string resourcesRelative = ToResourcesRelative(outputFolder);
            var catalog = new LocalizationCatalog
            {
                schemaVersion = 1,
                defaultLocale = data.Locales.Contains("zh-Hans") ? "zh-Hans" : data.Locales[0],
                fallbackLocale = data.Locales.Contains("en") ? "en" : data.Locales[0],
                locales = new LocaleInfo[data.Locales.Count]
            };

            for (int i = 0; i < data.Locales.Count; i++)
            {
                string locale = data.Locales[i];
                string tablePath = resourcesRelative + "/" + locale;
                bool builtIn = locale.Equals("zh-Hans", StringComparison.OrdinalIgnoreCase)
                    || locale.Equals("en", StringComparison.OrdinalIgnoreCase);
                catalog.locales[i] = new LocaleInfo
                {
                    code = locale,
                    displayName = locale,
                    table = builtIn ? string.Empty : tablePath,
                    overrideTable = tablePath
                };

                var table = new LocalizationTableFile { schemaVersion = 1, locale = locale, entries = new LocalizationEntry[data.Keys.Count] };
                for (int k = 0; k < data.Keys.Count; k++)
                {
                    string key = data.Keys[k];
                    table.entries[k] = new LocalizationEntry(key, data.Values[key][i] ?? string.Empty);
                }
                WriteAtomic(Path.Combine(outputFolder, locale + ".json"), LocalizationTableFile.ToJson(table));
            }

            WriteAtomic(Path.Combine(outputFolder, "catalog.json"), LocalizationCatalog.ToJson(catalog));
            AssetDatabase.Refresh();
        }

        private static string ToResourcesRelative(string outputFolder)
        {
            string normalized = outputFolder.Replace('\\', '/').TrimEnd('/');
            int index = normalized.IndexOf("/Resources/", StringComparison.OrdinalIgnoreCase);
            if (index >= 0) return normalized.Substring(index + "/Resources/".Length);
            if (normalized.EndsWith("/Resources", StringComparison.OrdinalIgnoreCase))
                return string.Empty;
            throw new InvalidDataException("输出目录必须位于某个 Resources 下（当前：" + outputFolder + "）");
        }

        private static void WriteAtomic(string path, string content)
        {
            string temp = path + ".tmp";
            File.WriteAllText(temp, content, new UTF8Encoding(false));
            if (File.Exists(path))
            {
                string backup = path + ".bak";
                File.Replace(temp, path, backup);
                if (File.Exists(backup)) File.Delete(backup);
            }
            else
            {
                File.Move(temp, path);
            }
        }

        private static bool IsEmptyRow(List<string> row)
        {
            for (int i = 0; i < row.Count; i++) if (!string.IsNullOrWhiteSpace(row[i])) return false;
            return true;
        }

        private static List<List<string>> Tokenize(string text)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var cell = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                        else quoted = false;
                    }
                    else cell.Append(c);
                    continue;
                }

                if (c == '"') { quoted = true; continue; }
                if (c == ',') { row.Add(cell.ToString()); cell.Length = 0; continue; }
                if (c == '\r') continue;
                if (c == '\n') { row.Add(cell.ToString()); rows.Add(row); row = new List<string>(); cell.Length = 0; continue; }
                cell.Append(c);
            }
            if (quoted)
            {
                // 未闭合的引号：按普通文本处理，交给校验报错
                row.Add(cell.ToString());
                rows.Add(row);
                return rows;
            }
            row.Add(cell.ToString());
            rows.Add(row);
            return rows;
        }
    }

    /// <summary>本地化 CSV 导入/校验菜单。</summary>
    public static class LocalizationCsvImporter
    {
        public const string DefaultOutputFolder = "Assets/UPandaFrameworkLocalization/Resources/LocalizationProject";

        [MenuItem("UPandaGF/本地化/导入 CSV...", false, 40)]
        public static void ImportFromMenu()
        {
            string csvPath = EditorUtility.OpenFilePanel("选择本地化 CSV", "", "csv");
            if (string.IsNullOrEmpty(csvPath)) return;
            Import(csvPath, DefaultOutputFolder);
        }

        [MenuItem("UPandaGF/本地化/校验 CSV...", false, 41)]
        public static void ValidateFromMenu()
        {
            string csvPath = EditorUtility.OpenFilePanel("校验本地化 CSV", "", "csv");
            if (string.IsNullOrEmpty(csvPath)) return;
            LocalizationCsvData data = LocalizationCsv.Parse(File.ReadAllText(csvPath, Encoding.UTF8));
            Print(data, csvPath);
        }

        public static LocalizationCsvData Import(string csvPath, string outputFolder)
        {
            LocalizationCsvData data = LocalizationCsv.Parse(File.ReadAllText(csvPath, Encoding.UTF8));
            Print(data, csvPath);
            if (data.HasErrors) return data;

            try
            {
                LocalizationCsv.WriteAssets(data, outputFolder);
                Debug.Log("[Localization] 已生成：" + outputFolder +
                          "\n请在 UPGameRoot 的 localization.projectCatalogPath 填 \"" +
                          ToRelativeCatalogPath(outputFolder) + "\"。");
            }
            catch (Exception exception)
            {
                Debug.LogError("[Localization] 写入失败：" + exception.Message);
                data.Errors.Add(exception.Message);
            }
            return data;
        }

        private static void Print(LocalizationCsvData data, string source)
        {
            if (data.HasErrors)
                Debug.LogError("[Localization] " + source + " 校验失败：\n" + string.Join("\n", data.Errors));
            else
                Debug.Log("[Localization] " + source + " 校验通过：" + data.Keys.Count + " keys / " +
                          data.Locales.Count + " languages" +
                          (data.Warnings.Count > 0 ? "（" + data.Warnings.Count + " 条警告）" : string.Empty));
            if (data.Warnings.Count > 0)
                Debug.LogWarning("[Localization] 警告：\n" + string.Join("\n", data.Warnings));
        }

        private static string ToRelativeCatalogPath(string outputFolder)
        {
            string normalized = outputFolder.Replace('\\', '/').TrimEnd('/');
            int index = normalized.IndexOf("/Resources/", StringComparison.OrdinalIgnoreCase);
            return index >= 0 ? normalized.Substring(index + "/Resources/".Length) + "/catalog" : "LocalizationProject/catalog";
        }
    }
}
