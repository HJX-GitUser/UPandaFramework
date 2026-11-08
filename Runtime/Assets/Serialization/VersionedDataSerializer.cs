using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace UPandaGF
{
    /// <summary>显式根类型的版本化数据封装；旧 NRBF 只读，写入不使用 BinaryFormatter。</summary>
    public static class VersionedDataSerializer
    {
        public const int MaxFileBytes = 64 * 1024 * 1024;
        public const int MaxGraphItems = 1000000;
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("UPANDA_DATA\n");
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private static readonly HashSet<Type> knownTypes = new HashSet<Type>();
        /// <summary>多态值需在读写前注册；只接受代码提供的类型，不从文件加载 CLR 类型。</summary>
        public static void RegisterKnownType<T>() { FieldGraphCodec.ValidateType(typeof(T)); lock (knownTypes) knownTypes.Add(typeof(T)); }
        internal static Type[] KnownTypes { get { lock (knownTypes) return knownTypes.ToArray(); } }
        public static bool IsVersioned(byte[] bytes)
        {
            if (bytes == null || bytes.Length < Magic.Length) return false;
            for (int i = 0; i < Magic.Length; i++) if (bytes[i] != Magic[i]) return false;
            return true;
        }
        public static byte[] Serialize(object value, string kind)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (string.IsNullOrEmpty(kind) || Utf8.GetByteCount(kind) > 128) throw new ArgumentException("数据类别无效", nameof(kind));
            Type type = value.GetType();
            if (Utf8.GetByteCount(FieldGraphCodec.Id(type)) > 16384) throw new InvalidDataException("根类型名称超过限制");
            byte[] payload = FieldGraphCodec.Encode(value, KnownTypes);
            if (payload.Length > MaxFileBytes) throw new InvalidDataException("数据超过 64 MiB 限制");
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Utf8))
            using (var sha = SHA256.Create())
            {
                writer.Write(Magic); writer.Write(1); writer.Write(kind); writer.Write(FieldGraphCodec.Id(type));
                writer.Write(payload.Length); writer.Write(sha.ComputeHash(payload)); writer.Write(payload);
                if (stream.Length > MaxFileBytes) throw new InvalidDataException("数据超过 64 MiB 限制");
                return stream.ToArray();
            }
        }
        public static T Deserialize<T>(byte[] bytes, string kind, out bool legacy) where T : class
        { return (T)Deserialize(bytes, typeof(T), kind, out legacy); }
        public static object Deserialize(byte[] bytes, Type type, string kind, out bool legacy)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > (kind == "replay" ? 128 * 1024 * 1024 : MaxFileBytes)) throw new InvalidDataException("数据长度无效");
            legacy = !IsVersioned(bytes);
            if (legacy) return LegacyNrbfReader.Read(bytes, type, KnownTypes);
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream, Utf8))
            {
                reader.ReadBytes(Magic.Length);
                if (reader.ReadInt32() != 1) throw new InvalidDataException("不支持的数据版本");
                if (ReadBoundedString(reader, 128) != kind) throw new InvalidDataException("数据类别不匹配");
                string rootId = ReadBoundedString(reader, 16384);
                if (rootId != FieldGraphCodec.Id(type))
                {
                    Type requested = type;
                    type = KnownTypes.SingleOrDefault(candidate => requested.IsAssignableFrom(candidate) && FieldGraphCodec.Id(candidate) == rootId);
                    if (type == null) throw new InvalidDataException("数据根类型不匹配；派生根类型需注册");
                }
                int length = reader.ReadInt32(); byte[] digest = reader.ReadBytes(32);
                if (length <= 0 || length > MaxFileBytes || stream.Length - stream.Position != length) throw new InvalidDataException("数据负载长度无效");
                byte[] payload = reader.ReadBytes(length);
                using (var sha = SHA256.Create()) if (!sha.ComputeHash(payload).SequenceEqual(digest)) throw new InvalidDataException("数据完整性校验失败");
                return FieldGraphCodec.Decode(payload, type, KnownTypes);
            }
        }
        internal static string ReadBoundedString(BinaryReader reader, int limit)
        {
            uint length = 0; int shift = 0;
            for (int i = 0; i < 5; i++)
            {
                byte part = reader.ReadByte();
                if (i == 4 && (part & 0xf0) != 0) throw new InvalidDataException("字符串长度无效");
                length |= (uint)(part & 0x7f) << shift;
                if ((part & 0x80) == 0)
                {
                    if (length > limit || length > reader.BaseStream.Length - reader.BaseStream.Position) throw new InvalidDataException("字符串超过限制或不完整");
                    return Utf8.GetString(reader.ReadBytes((int)length));
                }
                shift += 7;
            }
            throw new InvalidDataException("字符串长度无效");
        }
        public static byte[] ReadFile(string path, int maxBytes = MaxFileBytes)
        {
            using (var stream = File.OpenRead(path))
            {
                if (stream.Length <= 0 || stream.Length > maxBytes) throw new InvalidDataException("文件长度无效");
                var bytes = new byte[(int)stream.Length]; int offset = 0;
                while (offset < bytes.Length) { int count = stream.Read(bytes, offset, bytes.Length - offset); if (count == 0) throw new EndOfStreamException(); offset += count; }
                return bytes;
            }
        }
        /// <summary>直接保存覆盖旧格式时也保留原始备份，不要求调用方先 Load。</summary>
        public static void SaveFile(string path, object value, string kind)
        {
            byte[] replacement = Serialize(value, kind);
            FileUpdateTransaction.Recover(Path.GetFullPath(path) + ".recovery");
            if (File.Exists(path))
            {
                byte[] original = ReadFile(path);
                if (!IsVersioned(original)) { MigrateFile(path, original, replacement); return; }
            }
            WriteFile(path, replacement);
        }
        public static void WriteFile(string path, byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > MaxFileBytes) throw new InvalidDataException("写入数据长度无效");
            string full = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(full));
            FileUpdateTransaction.Recover(full + ".recovery");
            string staged = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllBytes(staged, bytes); FileUpdateTransaction.Commit(new[] { staged }, new[] { full }, full + ".recovery"); }
            finally { if (File.Exists(staged)) File.Delete(staged); }
        }
        /// <summary>验证旧数据可完整转换后备份并提交；失败保持原文件，备份永不覆盖。</summary>
        public static void MigrateFile(string path, byte[] original, byte[] replacement)
        {
            if (!ReadFile(path).SequenceEqual(original)) throw new IOException("迁移期间源文件已变化");
            string backup = path + ".legacy.bak";
            if (File.Exists(backup))
            {
                if (!ReadFile(backup).SequenceEqual(original)) backup = path + ".legacy." + Guid.NewGuid().ToString("N") + ".bak";
            }
            if (!File.Exists(backup)) File.WriteAllBytes(backup, original);
            WriteFile(path, replacement);
        }
        public static T LoadFile<T>(string path, string kind, bool migrateLegacy = true) where T : class
        {
            FileUpdateTransaction.Recover(Path.GetFullPath(path) + ".recovery");
            if (!File.Exists(path)) return null;
            byte[] bytes = ReadFile(path); bool legacy;
            T value = Deserialize<T>(bytes, kind, out legacy);
            if (legacy && migrateLegacy)
            {
                try
                {
                    byte[] replacement = Serialize(value, kind); bool ignored;
                    Deserialize<T>(replacement, kind, out ignored);
                    MigrateFile(path, bytes, replacement);
                }
                catch (Exception e) { UnityEngine.Debug.LogWarning("旧数据已读取，迁移未提交，保留原文件：" + e.Message); }
            }
            return value;
        }
    }
}
