using System.IO;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>
    /// Excel 二进制表的共享格式约定。
    /// <para>生成端（Editor/Excel/ExcelTool.cs）与运行端（StorageDataMgr/BineryMgr）必须使用同一组常量，
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
