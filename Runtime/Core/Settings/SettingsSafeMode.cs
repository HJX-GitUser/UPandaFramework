using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>
    /// 启动失败计数与安全模式状态：连续启动失败达到阈值后，下次启动强制窗口化/低画质/1280×720，
    /// 避免坏的分辨率或画质设置让玩家无法进入游戏。
    /// <para>文件独立于 settings.json，损坏不影响正常设置；启动成功后清除。</para>
    /// </summary>
    public sealed class SettingsSafeMode
    {
        public const int FailureThreshold = 2;
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static SettingsSafeMode defaultInstance;

        public static string DefaultPath
        {
            get { return System.IO.Path.Combine(SettingsStore.DefaultDirectory, "safe-mode.json"); }
        }

        /// <summary>框架默认实例（persistentDataPath/Settings/safe-mode.json）。</summary>
        public static SettingsSafeMode Default
        {
            get
            {
                if (defaultInstance == null) defaultInstance = new SettingsSafeMode();
                return defaultInstance;
            }
        }

        public string Path { get; }

        public SettingsSafeMode(string path = null)
        {
            Path = string.IsNullOrWhiteSpace(path) ? DefaultPath : System.IO.Path.GetFullPath(path);
        }

        public int FailureCount { get { return Read().consecutiveFailures; } }

        public bool IsActive { get { return FailureCount >= FailureThreshold; } }

        /// <summary>记录一次启动失败；返回记录后是否已达到安全模式阈值。</summary>
        public bool RecordFailure()
        {
            SettingsSafeModeRecord record = Read();
            record.consecutiveFailures = Math.Max(0, record.consecutiveFailures) + 1;
            record.lastFailureUtc = DateTime.UtcNow.ToString("o");
            Write(record);
            return record.consecutiveFailures >= FailureThreshold;
        }

        /// <summary>启动成功：清除计数，下次启动恢复正常设置。</summary>
        public void MarkSuccess()
        {
            try { if (File.Exists(Path)) File.Delete(Path); }
            catch (Exception) { }
        }

        public void Clear() { MarkSuccess(); }

        private SettingsSafeModeRecord Read()
        {
            var empty = new SettingsSafeModeRecord();
            try
            {
                if (!File.Exists(Path)) return empty;
                string json = File.ReadAllText(Path, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json)) return empty;
                SettingsSafeModeRecord record = JsonUtility.FromJson<SettingsSafeModeRecord>(json);
                return record ?? empty;
            }
            catch (Exception) { return empty; }
        }

        private void Write(SettingsSafeModeRecord record)
        {
            try
            {
                string directory = System.IO.Path.GetDirectoryName(Path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                string temp = Path + ".tmp";
                File.WriteAllText(temp, JsonUtility.ToJson(record, true), Utf8NoBom);
                if (File.Exists(Path)) File.Delete(Path);
                File.Move(temp, Path);
            }
            catch (Exception)
            {
                // 安全模式状态写入失败不能反过来阻断启动；下次仍按已有文件判断。
            }
        }

        [Serializable]
        private sealed class SettingsSafeModeRecord
        {
            public int consecutiveFailures;
            public string lastFailureUtc;
        }
    }
}
