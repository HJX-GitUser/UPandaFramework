using System;
using System.Collections.Generic;
using System.IO;

namespace UPandaGF
{
    /// <summary>同一应用独占目录中的文件批次提交；失败/下次启动时由日志恢复旧文件。</summary>
    internal static class FileUpdateTransaction
    {
        public static void Recover(string directory)
        {
            string journal = Path.Combine(directory, "journal");
            if (!File.Exists(journal)) return;
            // 先完整读取日志，避免恢复过程中覆盖日志中仍需读取的数据。
            var targets = new List<string>();
            var existed = new List<bool>();
            using (var reader = new BinaryReader(File.OpenRead(journal)))
            {
                int count = reader.ReadInt32();
                if (count < 0 || count > 100000) throw new InvalidDataException("Invalid update journal");
                for (int i = 0; i < count; i++)
                {
                    targets.Add(reader.ReadString());
                    existed.Add(reader.ReadBoolean());
                }
            }
            for (int i = 0; i < targets.Count; i++)
            {
                if (existed[i]) File.Copy(Path.Combine(directory, i + ".bak"), targets[i], true);
                else if (File.Exists(targets[i])) File.Delete(targets[i]);
            }
            File.Delete(journal);
            CleanupBackups(directory, targets.Count);
        }

        public static void Commit(IReadOnlyList<string> staged, IReadOnlyList<string> targets, string directory)
        {
            if (staged.Count != targets.Count) throw new ArgumentException("Update file count mismatch");
            Recover(directory);
            Directory.CreateDirectory(directory);
            string journal = Path.Combine(directory, "journal");
            string pending = journal + ".tmp";
            var existed = new bool[targets.Count];
            // 所有备份与日志均准备好之前，不改动任何目标文件。
            for (int i = 0; i < targets.Count; i++)
            {
                if (!File.Exists(staged[i])) throw new FileNotFoundException("Staged update missing", staged[i]);
                Directory.CreateDirectory(Path.GetDirectoryName(targets[i]));
                existed[i] = File.Exists(targets[i]);
                if (existed[i]) File.Copy(targets[i], Path.Combine(directory, i + ".bak"), true);
            }
            using (var stream = new FileStream(pending, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(targets.Count);
                for (int i = 0; i < targets.Count; i++)
                {
                    writer.Write(targets[i]);
                    writer.Write(existed[i]);
                }
                writer.Flush();
                stream.Flush(true);
            }
            File.Move(pending, journal);
            try
            {
                // 清单由调用方放在最后，且在此之前已经完成全部文件校验。
                for (int i = 0; i < targets.Count; i++) File.Copy(staged[i], targets[i], true);
                File.Delete(journal); // 提交点：此后不再回滚。
            }
            catch
            {
                Recover(directory);
                throw;
            }
            // 清理备份失败不影响已提交批次。
            CleanupBackups(directory, targets.Count);
        }

        private static void CleanupBackups(string directory, int count)
        {
            for (int i = 0; i < count; i++)
                try { File.Delete(Path.Combine(directory, i + ".bak")); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
        }
    }
}
