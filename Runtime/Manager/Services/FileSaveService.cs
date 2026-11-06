using System;
using System.IO;

namespace UPandaGF
{
    /// <summary>版本化存档适配器。数据格式沿用 VersionedDataSerializer，不另造序列化格式。</summary>
    public sealed class FileSaveService : ISaveService
    {
        private readonly string directory;
        public FileSaveService(string directory) { this.directory = Path.GetFullPath(directory); }
        private string PathFor(string slot)
        {
            if (string.IsNullOrWhiteSpace(slot) || slot.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || slot.Contains("/") || slot.Contains("\\") || slot == "." || slot == "..") throw new ArgumentException("存档槽名称无效。", nameof(slot));
            return Path.Combine(directory, slot + ".save");
        }
        public void Save<T>(string slot, T data) where T : class
        {
            string path = PathFor(slot);
            Directory.CreateDirectory(directory);
            VersionedDataSerializer.SaveFile(path, data, "save");
        }
        public T Load<T>(string slot) where T : class
        {
            string path = PathFor(slot);
            if (!File.Exists(path)) return null;
            bool legacy;
            return VersionedDataSerializer.Deserialize<T>(VersionedDataSerializer.ReadFile(path), "save", out legacy);
        }
        public bool Exists(string slot) => File.Exists(PathFor(slot));
    }
}
