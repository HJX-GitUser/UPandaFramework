using System.IO;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>
    /// Excel 二进制表的共享格式约定。
    /// <para>生成端（Editor/Excel/ExcelTool.cs）与运行端（StorageDataMgr/BinaryMgr）必须使用同一组常量，
    /// 避免再次出现“写 1 字节、读 4 字节”或“生成到 StreamingAssets、运行读 persistentDataPath”的错位。</para>
    /// </summary>
    public static class ExcelBinaryFormat
    {
        /// <summary>bool 在二进制表中的固定宽度（与 int 对齐，读取端必须按 4 字节推进）。</summary>
        public const int BoolByteSize = 4;

        /// <summary>表文件相对 StreamingAssets / persistentDataPath 的目录。</summary>
        public const string RelativeFolder = "Data/BinaryData";

        /// <summary>表文件扩展名。</summary>
        public const string FileExtension = ".binary";

        /// <summary>v2 文件头的 magic 文本（4 字节 ASCII）。</summary>
        public const string MagicText = "UPXF";

        /// <summary>v2 文件头的 magic 字节。只在编码时写入；读取端用 <see cref="HasMagic"/> 判断。</summary>
        public static readonly byte[] MagicBytes = { (byte)'U', (byte)'P', (byte)'X', (byte)'F' };

        /// <summary>当前写入的格式版本。v1 是无文件头的旧布局，读取端仍兼容。</summary>
        public const int CurrentVersion = 2;

        /// <summary>最小合法 v2 文件长度：magic(4) + version(2) + flags(2) + 两个空字符串长度(8) + 列数(2) + 行数(4) + CRC(4)。</summary>
        public const int MinimumV2Length = 26;

        /// <summary>字节流是否带 v2 magic；不带则按 legacy v1 布局读取。</summary>
        public static bool HasMagic(byte[] bytes)
        {
            if (bytes == null || bytes.Length < MagicBytes.Length) return false;
            for (int i = 0; i < MagicBytes.Length; i++)
                if (bytes[i] != MagicBytes[i]) return false;
            return true;
        }

        /// <summary>随包表目录（构建时由 ExcelTool 写入；Player 内可能是只读/压缩路径）。</summary>
        public static string StreamingAssetsDirectory
        {
            get { return Path.Combine(Application.streamingAssetsPath, "Data", "BinaryData"); }
        }

        /// <summary>运行时表目录（必须可写；桌面端首次启动会从 StreamingAssets 安装）。</summary>
        public static string PersistentDirectory
        {
            get { return Path.Combine(Application.persistentDataPath, "Data", "BinaryData"); }
        }
    }
}
