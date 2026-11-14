using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>
    /// 设置文件读写：默认 <c>persistentDataPath/Settings/settings.json</c>。
    /// <para>与存档分离：删档/换档不影响设置。写入使用“临时文件 + 备份替换”，
    /// 主文件损坏时回退 <c>.bak</c>；两者都坏则返回错误，由服务回退默认值。</para>
    /// <para>只做 IO 与结构兜底（EnsureDefaults），业务归一化由 <see cref="SettingsRegistry"/> 负责。</para>
    /// </summary>
    public sealed class SettingsStore
    {
        public const string DefaultFileName = "settings.json";
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private string rawJson;

        /// <summary>默认目录（persistentDataPath/Settings）。</summary>
        public static string DefaultDirectory { get { return System.IO.Path.Combine(Application.persistentDataPath, "Settings"); } }

        /// <summary>默认文件路径（persistentDataPath/Settings/settings.json）。</summary>
        public static string DefaultPath { get { return System.IO.Path.Combine(DefaultDirectory, DefaultFileName); } }

        /// <summary>当前实例使用的绝对路径。</summary>
        public string Path { get; }

        public SettingsStore(string path = null)
        {
            Path = string.IsNullOrWhiteSpace(path) ? DefaultPath : System.IO.Path.GetFullPath(path);
        }

        /// <summary>文件是否存在（主文件或备份）。</summary>
        public bool Exists()
        {
            return File.Exists(Path) || File.Exists(Path + ".bak");
        }

        /// <summary>
        /// 读取设置。文件不存在返回 true 且 data=null（首次启动）；
        /// 主文件损坏时回退备份并置 <paramref name="recoveredFromBackup"/>；
        /// 两者都不可用时返回 false 并给出 error。
        /// </summary>
        public bool TryLoad(out SettingsData data, out string error, out bool recoveredFromBackup)
        {
            data = null;
            error = null;
            recoveredFromBackup = false;

            if (!File.Exists(Path))
            {
                // 主文件缺失但备份存在（例如替换过程中断电）：优先恢复备份。
                string missingBackup = Path + ".bak";
                if (File.Exists(missingBackup))
                {
                    string missingBackupError;
                    SettingsData restoredMissing = TryLoadFile(missingBackup, out missingBackupError);
                    if (restoredMissing != null)
                    {
                        restoredMissing.EnsureDefaults();
                        data = restoredMissing;
                        recoveredFromBackup = true;
                        error = "主设置文件缺失，已从备份恢复。";
                        rawJson = ReadRaw(missingBackup);
                        TryRestorePrimary(restoredMissing);
                        return true;
                    }
                    error = "主设置文件缺失，备份读取失败：" + missingBackupError;
                    return false;
                }
                return true;
            }

            data = TryLoadFile(Path, out error);
            if (data != null)
            {
                data.EnsureDefaults();
                rawJson = ReadRaw(Path);
                return true;
            }

            string backupPath = Path + ".bak";
            if (File.Exists(backupPath))
            {
                string backupError;
                SettingsData restored = TryLoadFile(backupPath, out backupError);
                if (restored != null)
                {
                    restored.EnsureDefaults();
                    data = restored;
                    recoveredFromBackup = true;
                    rawJson = ReadRaw(backupPath);
                    string primaryError = error;
                    error = "主设置文件读取失败，已回退备份：" + primaryError;
                    TryRestorePrimary(restored);
                    return true;
                }
                error = (error ?? string.Empty) + "；备份同样失败：" + backupError;
            }

            data = null;
            return false;
        }

        /// <summary>原子保存；失败返回 false 并给出 error（主文件保持原样）。</summary>
        public bool Save(SettingsData data, out string error)
        {
            error = null;
            if (data == null) { error = "设置数据为空"; return false; }

            string temp = Path + ".tmp";
            string backup = Path + ".bak";
            try
            {
                string directory = System.IO.Path.GetDirectoryName(Path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                string knownJson = JsonUtility.ToJson(data, true);
                if (string.IsNullOrEmpty(knownJson)) { error = "设置序列化结果为空"; return false; }
                string json = string.IsNullOrEmpty(rawJson) ? knownJson : SettingsJsonMerge.Merge(rawJson, knownJson);

                File.WriteAllText(temp, json, Utf8NoBom);
                // 先验证临时文件可反序列化，避免把写一半的文件替换成主文件。
                if (JsonUtility.FromJson<SettingsData>(File.ReadAllText(temp, Encoding.UTF8)) == null)
                {
                    error = "临时设置文件反序列化失败";
                    TryDelete(temp);
                    return false;
                }

                if (File.Exists(Path))
                {
                    try { File.Replace(temp, Path, backup); }
                    catch (PlatformNotSupportedException) { ReplaceByCopy(temp, Path, backup); }
                    catch (IOException) { ReplaceByCopy(temp, Path, backup); }
                }
                else
                {
                    File.Move(temp, Path);
                }
                TryDelete(temp);
                rawJson = json;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                TryDelete(temp);
                return false;
            }
        }

        /// <summary>删除主文件与备份；返回是否有文件被删除。</summary>
        public bool Delete()
        {
            bool existed = File.Exists(Path) || File.Exists(Path + ".bak");
            rawJson = null;
            TryDelete(Path);
            TryDelete(Path + ".bak");
            TryDelete(Path + ".tmp");
            return existed;
        }

        private static string ReadRaw(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null; }
            catch (Exception) { return null; }
        }

        private static SettingsData TryLoadFile(string path, out string error)
        {
            error = null;
            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json)) { error = "设置文件为空"; return null; }

                // SettingsData 的字段初始化器会把缺失的 schemaVersion 补成 1；
                // 先用探针读取原始版本，才能识别“没有版本号的旧文件”（按 0 迁移）。
                int fileVersion = 0;
                try
                {
                    SettingsSchemaProbe probe = JsonUtility.FromJson<SettingsSchemaProbe>(json);
                    if (probe != null) fileVersion = Math.Max(0, probe.schemaVersion);
                }
                catch (Exception) { }

                SettingsData data = JsonUtility.FromJson<SettingsData>(json);
                if (data == null) { error = "设置文件反序列化结果为空"; return null; }
                data.schemaVersion = fileVersion;
                return data;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return null;
            }
        }

        [Serializable]
        private sealed class SettingsSchemaProbe
        {
            public int schemaVersion;
        }

        private static void ReplaceByCopy(string temp, string path, string backup)
        {
            File.Copy(path, backup, true);
            File.Copy(temp, path, true);
        }

        private void TryRestorePrimary(SettingsData data)
        {
            try
            {
                string directory = System.IO.Path.GetDirectoryName(Path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(Path, JsonUtility.ToJson(data, true), Utf8NoBom);
            }
            catch (Exception)
            {
                // 主文件修复失败不影响本次读取；下次保存会重试。
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception) { }
        }
    }
}
