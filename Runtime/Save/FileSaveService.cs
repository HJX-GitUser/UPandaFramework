/* ==========================================================================================
 * FileSaveService —— 版本化存档适配器
 *
 * 【职责】按“槽位名”读写存档文件，目录固定、格式沿用 VersionedDataSerializer。
 *   不另造序列化格式：存档的版本兼容由序列化器负责（读旧版本时通过 out legacy 告知调用方）。
 *
 * 【安全】槽位名会被拼进文件名，因此必须校验：
 *   空串、非法文件名字符、/ 和 \（路径分隔符）、"/../" 这类相对路径都要拒绝，
 *   防止调用方传入 "../../xxx" 把文件写到目录之外。
 *
 * 【恢复策略】读取顺序：重放 <slot>.save.recovery 未完成事务 → 读主档 → 主档缺失或内容损坏时
 *   读 <slot>.save.bak 并尝试事务化修复主档。只有 InvalidDataException（文件内容不可用）才回退到备份；
 *   文件被占用等 IO/权限错误直接抛出，避免把瞬时故障误判成损坏、反而用旧备份覆盖较新的主档。
 * ========================================================================================== */

using System;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;

namespace UPandaGF
{
    /// <summary>版本化存档适配器。数据格式沿用 VersionedDataSerializer，不另造序列化格式。</summary>
    public sealed class FileSaveService : ISaveService
    {
        /// <summary>存档目录（构造时转成绝对路径，避免受当前工作目录影响）。</summary>
        private readonly string directory;
        public FileSaveService(string directory) { this.directory = Path.GetFullPath(directory); }
        /// <summary>槽位名 → 完整文件路径。非法槽位直接抛（绝不拼接进路径）。</summary>
        private string PathFor(string slot)
        {
            if (string.IsNullOrWhiteSpace(slot) || slot.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || slot.Contains("/") || slot.Contains("\\") || slot == "." || slot == "..") throw new ArgumentException("存档槽名称无效。", nameof(slot));
            return Path.Combine(directory, slot + ".save");
        }
        private static string BackupPath(string path) => path + ".bak";
        private static string RecoveryPath(string path) => path + ".recovery";

        /// <summary>事务写入主存档，成功后同步有效备份。</summary>
        public void Save<T>(string slot, T data) where T : class
        {
            string path = PathFor(slot);
            Directory.CreateDirectory(directory);
            VersionedDataSerializer.SaveFile(path, data, "save");
            TrySynchronizeBackup(BackupPath(path), VersionedDataSerializer.ReadFile(path));
        }
        /// <summary>先恢复中断的事务；主文件缺失或损坏时读取有效备份并尝试修复主文件。</summary>
        public T Load<T>(string slot) where T : class
        {
            string path = PathFor(slot);
            string backup = BackupPath(path);
            Exception primaryError = null;
            try
            {
                FileUpdateTransaction.Recover(RecoveryPath(path));
                if (File.Exists(path))
                {
                    // 只读一次主档：字节同时用于反序列化和备份比对。
                    byte[] bytes = VersionedDataSerializer.ReadFile(path);
                    T value = ReadSlot<T>(bytes);
                    TrySynchronizeBackup(backup, bytes);
                    return value;
                }
            }
            catch (InvalidDataException e)
            {
                // 仅“内容不可用”才回退；IO/权限错误继续向上抛。
                primaryError = e;
            }

            try
            {
                FileUpdateTransaction.Recover(RecoveryPath(backup));
                if (File.Exists(backup))
                {
                    byte[] bytes = VersionedDataSerializer.ReadFile(backup);
                    T value = ReadSlot<T>(bytes);
                    UnityEngine.Debug.LogWarning(primaryError == null
                        ? "主存档缺失，已从备份恢复：" + path
                        : "主存档读取失败，已从备份恢复：" + path + "\n" + primaryError.Message);
                    TryRestorePrimary(path, bytes);
                    return value;
                }
            }
            catch (InvalidDataException backupError)
            {
                if (primaryError != null)
                    throw new InvalidDataException("主存档和备份均无法读取。", new AggregateException(primaryError, backupError));
                throw;
            }

            if (primaryError != null) ExceptionDispatchInfo.Capture(primaryError).Throw();
            return null;
        }
        /// <summary>主存档或备份是否存在。</summary>
        public bool Exists(string slot)
        {
            string path = PathFor(slot);
            return File.Exists(path) || File.Exists(BackupPath(path));
        }

        /// <summary>删除主存档、备份、恢复状态和旧格式迁移留下的副本；都不存在返回 false。</summary>
        public bool Delete(string slot)
        {
            string path = PathFor(slot);
            string backup = BackupPath(path);
            bool existed = File.Exists(path) || File.Exists(backup);
            DeleteRecovery(RecoveryPath(path));
            DeleteRecovery(RecoveryPath(backup));
            DeleteLegacyBackups(path);
            DeleteLegacyBackups(backup);
            File.Delete(backup);
            File.Delete(path);
            return existed;
        }
        private static void DeleteRecovery(string path)
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        /// <summary>清理 MigrateFile 可能留下的 &lt;path&gt;.legacy.bak 与 &lt;path&gt;.legacy.&lt;guid&gt;.bak。</summary>
        private static void DeleteLegacyBackups(string path)
        {
            string folder = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
            string prefix = Path.GetFileName(path) + ".legacy.";
            foreach (string candidate in Directory.EnumerateFiles(folder, prefix + "*"))
            {
                if (!candidate.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)) continue;
                try { File.Delete(candidate); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        private static T ReadSlot<T>(byte[] bytes) where T : class
        {
            bool legacy;
            return VersionedDataSerializer.Deserialize<T>(bytes, "save", out legacy);
        }
        private static void TrySynchronizeBackup(string backup, byte[] current)
        {
            try
            {
                bool same = false;
                try { same = File.Exists(backup) && VersionedDataSerializer.ReadFile(backup).SequenceEqual(current); }
                catch (Exception e) when (e is IOException || e is InvalidDataException || e is UnauthorizedAccessException) { }
                if (!same) VersionedDataSerializer.WriteFile(backup, current);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("主存档已可用，但备份同步失败：" + backup + "\n" + e.Message);
            }
        }
        private static void TryRestorePrimary(string path, byte[] bytes)
        {
            try { VersionedDataSerializer.WriteFile(path, bytes); }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("备份存档已读取，但主文件修复失败：" + path + "\n" + e.Message);
            }
        }
    }
}
