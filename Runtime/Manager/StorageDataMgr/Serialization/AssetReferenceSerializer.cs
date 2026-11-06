using System;
using System.IO;
using System.Security.Cryptography;

namespace UPandaGF
{
    /// <summary>AB 生成端与运行端共用编解码，AES 包裹与原始文件备份保持一致。</summary>
    public static class AssetReferenceSerializer
    {
        public static byte[] Encode(ABSourcesRelated data, AssetBundleClassificationWindowConfig config = null)
        {
            AssetReferenceValidation.Validate(data);
            byte[] bytes = VersionedDataSerializer.Serialize(data, "assetref");
            return config != null && config.enable ? AESEncryption.AESEncrypt(bytes, config.AESKEY, config.AESIV) : bytes;
        }
        public static ABSourcesRelated Decode(byte[] bytes, AssetBundleClassificationWindowConfig config = null)
        {
            if (config != null && config.enable) bytes = AESEncryption.AESDecrypt(bytes, config.AESKEY, config.AESIV);
            bool legacy;
            var data = VersionedDataSerializer.Deserialize<ABSourcesRelated>(bytes, "assetref", out legacy);
            AssetReferenceValidation.Validate(data); return data;
        }
        public static void SaveFile(string path, ABSourcesRelated data, AssetBundleClassificationWindowConfig config = null)
        {
            byte[] replacement = Encode(data, config);
            FileUpdateTransaction.Recover(Path.GetFullPath(path) + ".recovery");
            if (File.Exists(path))
            {
                byte[] original = VersionedDataSerializer.ReadFile(path);
                bool encrypted = config != null && config.enable;
                bool requiresBackup = !VersionedDataSerializer.IsVersioned(original);
                if (encrypted)
                {
                    // 支持由明文切换为 AES 或更换密钥；不能用新密钥解开旧文件时仍保留旧原文。
                    requiresBackup = true;
                    try { if (!VersionedDataSerializer.IsVersioned(original)) requiresBackup = !VersionedDataSerializer.IsVersioned(AESEncryption.AESDecrypt(original, config.AESKEY, config.AESIV)); }
                    catch (CryptographicException) { }
                    catch (ArgumentException) { }
                }
                if (requiresBackup) { VersionedDataSerializer.MigrateFile(path, original, replacement); return; }
            }
            VersionedDataSerializer.WriteFile(path, replacement);
        }
    }
}
