using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace UPandaGF
{
    /// <summary>独占目录的可恢复提交；调用方可放到后台。提交点前取消会恢复旧文件。</summary>
    internal static class FileUpdateTransaction
    {
        public static void Recover(string directory) { RecoverCancelable(directory, CancellationToken.None); }
        public static void Commit(IReadOnlyList<string> staged, IReadOnlyList<string> targets, string directory)
        { CommitCancelable(staged, targets, directory, CancellationToken.None); }
        public static void RecoverCancelable(string directory, CancellationToken token, Action<string> validate = null)
        {
            string journal = Path.Combine(directory, "journal");
            if (!File.Exists(journal)) { CleanupScratch(directory); return; }
            var targets = new List<string>(); var existed = new List<bool>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var reader = new BinaryReader(File.OpenRead(journal), new UTF8Encoding(false, true)))
            {
                int count = reader.ReadInt32();
                if (count < 0 || count > 100000) throw new InvalidDataException("Invalid update journal");
                for (int i = 0; i < count; i++)
                {
                    string target = ReadPath(reader); validate?.Invoke(target);
                    if (!seen.Add(Path.GetFullPath(target))) throw new InvalidDataException("Duplicate update journal target");
                    targets.Add(target); existed.Add(reader.ReadBoolean());
                }
                if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("Invalid update journal tail");
            }
            // 先校验完整日志，之后才恢复；部分恢复失败时保留日志和备份供下一次继续。
            for (int i = 0; i < targets.Count; i++)
            {
                token.ThrowIfCancellationRequested(); validate?.Invoke(targets[i]);
                if (existed[i]) Copy(Path.Combine(directory, i + ".bak"), targets[i], token, null);
                else if (File.Exists(targets[i])) File.Delete(targets[i]);
            }
            File.Delete(journal); CleanupScratch(directory);
        }
        public static void CommitCancelable(IReadOnlyList<string> staged, IReadOnlyList<string> targets, string directory,
            CancellationToken token, Action<string> validate = null, Action<long> copied = null)
        {
            if (staged.Count != targets.Count || staged.Count > 100000) throw new ArgumentException("Update file count mismatch");
            RecoverCancelable(directory, token, validate); Directory.CreateDirectory(directory);
            string journal = Path.Combine(directory, "journal"), pending = journal + ".tmp";
            var existed = new bool[targets.Count]; var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    token.ThrowIfCancellationRequested(); validate?.Invoke(targets[i]);
                    if (!seen.Add(Path.GetFullPath(targets[i]))) throw new InvalidDataException("Duplicate update target");
                    if (!File.Exists(staged[i])) throw new FileNotFoundException("Staged update missing", staged[i]);
                    Directory.CreateDirectory(Path.GetDirectoryName(targets[i]));
                    existed[i] = File.Exists(targets[i]);
                    if (existed[i]) Copy(targets[i], Path.Combine(directory, i + ".bak"), token, copied);
                }
                using (var stream = new FileStream(pending, FileMode.Create, FileAccess.Write))
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(targets.Count);
                    for (int i = 0; i < targets.Count; i++) { writer.Write(targets[i]); writer.Write(existed[i]); }
                    writer.Flush(); stream.Flush(true);
                }
                token.ThrowIfCancellationRequested(); File.Move(pending, journal);
                for (int i = 0; i < targets.Count; i++) { token.ThrowIfCancellationRequested(); validate?.Invoke(targets[i]); Copy(staged[i], targets[i], token, copied); }
                token.ThrowIfCancellationRequested(); File.Delete(journal); // 提交点：清单由调用方最后提交。
            }
            catch
            {
                if (File.Exists(journal)) RecoverCancelable(directory, CancellationToken.None, validate);
                else CleanupScratch(directory);
                throw;
            }
            CleanupScratch(directory);
        }
        private static void Copy(string source, string target, CancellationToken token, Action<long> copied)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(256 * 1024);
            try
            {
                using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    int count;
                    while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
                    { token.ThrowIfCancellationRequested(); output.Write(buffer, 0, count); copied?.Invoke(count); }
                    output.Flush(true);
                }
            }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
        }
        private static string ReadPath(BinaryReader reader)
        {
            uint length = 0; int shift = 0;
            for (int i = 0; i < 5; i++)
            {
                byte part = reader.ReadByte(); if (i == 4 && (part & 0xf0) != 0) break;
                length |= (uint)(part & 0x7f) << shift;
                if ((part & 0x80) == 0)
                {
                    if (length == 0 || length > 131072 || length > reader.BaseStream.Length - reader.BaseStream.Position) break;
                    return new UTF8Encoding(false, true).GetString(reader.ReadBytes((int)length));
                }
                shift += 7;
            }
            throw new InvalidDataException("Invalid journal path");
        }
        private static void CleanupScratch(string directory)
        {
            if (!Directory.Exists(directory) || File.Exists(Path.Combine(directory, "journal"))) return;
            foreach (string path in Directory.EnumerateFiles(directory))
            {
                string name = Path.GetFileName(path);
                bool owned = name == "journal.tmp" || (name.EndsWith(".bak", StringComparison.Ordinal) && int.TryParse(Path.GetFileNameWithoutExtension(name), out int number) && number >= 0);
                if (!owned || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) continue;
                try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }
}
