using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace UPandaGF
{
    /// <summary>AB 清单 / 存档的 AES 加解密（生成端与运行端共用）。</summary>
    public static class AESEncryption
    {
        // 加密
        public static byte[] AESEncrypt(byte[] dataToEncrypt, string key, string iv)
        {
            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.Key = Encoding.UTF8.GetBytes(key);  // 密钥必须是 16、24 或 32 字节
                aesAlg.IV = Encoding.UTF8.GetBytes(iv);    // IV（初始化向量）长度为 16 字节

                using (ICryptoTransform encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV))
                {
                    using (MemoryStream ms = new MemoryStream())
                    {
                        using (CryptoStream cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                        {
                            cs.Write(dataToEncrypt, 0, dataToEncrypt.Length);
                            cs.FlushFinalBlock();
                        }
                        return ms.ToArray();
                    }
                }
            }
        }

        // AES 解密
        public static byte[] AESDecrypt(byte[] dataToDecrypt, string key, string iv)
        {
            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.Key = Encoding.UTF8.GetBytes(key);
                aesAlg.IV = Encoding.UTF8.GetBytes(iv);

                using (ICryptoTransform decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV))
                {
                    using (MemoryStream ms = new MemoryStream(dataToDecrypt))
                    {
                        using (CryptoStream cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
                        {
                            using (MemoryStream resultMs = new MemoryStream())
                            {
                                cs.CopyTo(resultMs);
                                return resultMs.ToArray();
                            }
                        }
                    }
                }
            }
        }
    }
}