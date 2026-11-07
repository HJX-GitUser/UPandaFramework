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
        /// <summary>写入存档（自动建目录；同槽位覆盖）。</summary>
        public void Save<T>(string slot, T data) where T : class
        {
            string path = PathFor(slot);
            Directory.CreateDirectory(directory);
            VersionedDataSerializer.SaveFile(path, data, "save");
        }
        /// <summary>读取存档；文件不存在返回 null（不是异常，方便首次运行）。</summary>
        public T Load<T>(string slot) where T : class
        {
            string path = PathFor(slot);
            if (!File.Exists(path)) return null;
            bool legacy;
            return VersionedDataSerializer.Deserialize<T>(VersionedDataSerializer.ReadFile(path), "save", out legacy);
        }
        /// <summary>槽位是否有存档文件。</summary>
        public bool Exists(string slot) => File.Exists(PathFor(slot));
    }
}
