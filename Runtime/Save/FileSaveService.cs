/* ==========================================================================================
 * FileSaveService —— 版本化存档适配器
 *
 * 【职责】按“槽位名”读写存档文件，目录固定、格式沿用 VersionedDataSerializer。
 *   不另造序列化格式：存档的版本兼容由序列化器负责（读旧版本时通过 out legacy 告知调用方）。
 *
 * 【安全】槽位名会被拼进文件名，因此必须校验：
 *   空串、非法文件名字符、/ 和 \（路径分隔符）、"/../" 这类相对路径都要拒绝，
 *   防止调用方传入 "../../xxx" 把文件写到目录之外。
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
            TrySynchronizeBackup(path);
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
                    T value = ReadSlot<T>(path);
                    TrySynchronizeBackup(path);
                    return value;
                }
            }
            catch (Exception e) { primaryError = e; }

            try
            {
                FileUpdateTransaction.Recover(RecoveryPath(backup));
                if (File.Exists(backup))
                {
                    T value = ReadSlot<T>(backup);
                    UnityEngine.Debug.LogWarning(primaryError == null
                        ? "主存档缺失，已从备份恢复：" + path
                        : "主存档读取失败，已从备份恢复：" + path + "\n" + primaryError.Message);
                    TryRestorePrimary(path, backup);
                    return value;
                }
            }
            catch (Exception backupError)
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

        /// <summary>删除主存档、备份和恢复状态；不存在返回 false。</summary>
        public bool Delete(string slot)
        {
            string path = PathFor(slot);
            string backup = BackupPath(path);
            bool existed = File.Exists(path) || File.Exists(backup);
            DeleteRecovery(RecoveryPath(path));
            DeleteRecovery(RecoveryPath(backup));
            File.Delete(backup);
            File.Delete(backup + ".legacy.bak");
            File.Delete(path + ".legacy.bak");
            File.Delete(path);
            return existed;
        }
        private static void DeleteRecovery(string path)
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        private static T ReadSlot<T>(string path) where T : class
        {
            bool legacy;
            return VersionedDataSerializer.Deserialize<T>(VersionedDataSerializer.ReadFile(path), "save", out legacy);
        }
        private static void TrySynchronizeBackup(string path)
        {
            try
            {
                byte[] current = VersionedDataSerializer.ReadFile(path);
                string backup = BackupPath(path);
                bool same = false;
                try { same = File.Exists(backup) && VersionedDataSerializer.ReadFile(backup).SequenceEqual(current); }
                catch (Exception e) when (e is IOException || e is InvalidDataException || e is UnauthorizedAccessException) { }
                if (!same) VersionedDataSerializer.WriteFile(backup, current);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("主存档已可用，但备份同步失败：" + path + "\n" + e.Message);
            }
        }
        private static void TryRestorePrimary(string path, string backup)
        {
            try { VersionedDataSerializer.WriteFile(path, VersionedDataSerializer.ReadFile(backup)); }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("备份存档已读取，但主文件修复失败：" + path + "\n" + e.Message);
            }
        }
    }
}
