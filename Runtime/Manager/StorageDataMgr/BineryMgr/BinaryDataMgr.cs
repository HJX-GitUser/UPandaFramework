using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>
    /// 读取二进制的内容，通过反射得到数据，并把数据存入容器。再用这个二进制数据管理器统一管理
    /// </summary>
    public class BinaryDataMgr : LazySingletonBase<BinaryDataMgr>
    {
        /// <summary>
        /// 数据类存储的位置
        /// </summary>
        private static string SAVE_PATH = Application.persistentDataPath + "/Data/";

        /// <summary>
        /// 存储文件后缀
        /// </summary>
        private static string FILE_EXTENSION = ".binary";

        /// <summary>
        /// Excel配置的 2进制数据类的 存储位置路径
        /// 路径和ExcelTool里的数据存储路径一致
        /// </summary>
        private static string DATA_BINARY_PATH = Application.persistentDataPath + "/Data/BinaryData/";
        /// <summary>
        /// 用于存储 所有Excel表数据容器类 的容器
        /// </summary>
        private Dictionary<string, object> tableDic = new Dictionary<string, object>();


        public void InitData(string savePath, string extension)
        {
            DATA_BINARY_PATH =  savePath;
            FILE_EXTENSION = extension;
            PLogger.Log($"BinaryDataMgr Init!\n存储路径：{DATA_BINARY_PATH}，后缀：{FILE_EXTENSION}");
        }
        /// <summary>
        /// 加载Excel表的2进制数据到内存中
        /// </summary>
        /// <typeparam name="T">容器类</typeparam>
        /// <typeparam name="K">数据结构类</typeparam>
        public void LoadTable<T, K>()
        {
            string tablePath = Path.Combine(DATA_BINARY_PATH, typeof(K).Name + FILE_EXTENSION);
            if (!File.Exists(tablePath))
                throw new FileNotFoundException($"Excel 二进制表不存在：{tablePath}。请用 ExcelTool 生成并确认已安装到运行时目录。", tablePath);

            byte[] bytes = File.ReadAllBytes(tablePath);
            if (bytes.Length < 8)
                throw new InvalidDataException($"Excel 二进制表损坏（长度不足）：{tablePath}");

            int index = 0;
            int count = BitConverter.ToInt32(bytes, index); index += 4;
            int keyNameLength = BitConverter.ToInt32(bytes, index); index += 4;
            EnsureAvailable(bytes, index, keyNameLength);
            string keyName = Encoding.UTF8.GetString(bytes, index, keyNameLength); index += keyNameLength;
            if (count < 0) throw new InvalidDataException($"Excel 二进制表行数无效：{tablePath}");

            Type containerType = typeof(T);
            object containerObj = Activator.CreateInstance(containerType);
            Type classType = typeof(K);
            FieldInfo[] infos = classType.GetFields(BindingFlags.Public | BindingFlags.Instance);
            FieldInfo keyField = classType.GetField(keyName, BindingFlags.Public | BindingFlags.Instance);
            if (keyField == null) throw new InvalidDataException($"Excel 表 {classType.Name} 缺少主键字段：{keyName}");

            for (int i = 0; i < count; i++)
            {
                object dataObj = Activator.CreateInstance(classType);
                foreach (FieldInfo info in infos)
                {
                    if (info.FieldType == typeof(int))
                    {
                        EnsureAvailable(bytes, index, 4);
                        info.SetValue(dataObj, BitConverter.ToInt32(bytes, index));
                        index += 4;
                    }
                    else if (info.FieldType == typeof(float))
                    {
                        EnsureAvailable(bytes, index, 4);
                        info.SetValue(dataObj, BitConverter.ToSingle(bytes, index));
                        index += 4;
                    }
                    else if (info.FieldType == typeof(bool))
                    {
                        // 生成端按 ExcelBinaryFormat.BoolByteSize（4 字节）写入，读取端必须一致推进。
                        EnsureAvailable(bytes, index, ExcelBinaryFormat.BoolByteSize);
                        info.SetValue(dataObj, BitConverter.ToInt32(bytes, index) != 0);
                        index += ExcelBinaryFormat.BoolByteSize;
                    }
                    else if (info.FieldType == typeof(string))
                    {
                        EnsureAvailable(bytes, index, 4);
                        int length = BitConverter.ToInt32(bytes, index);
                        index += 4;
                        if (length < 0) throw new InvalidDataException($"Excel 表 {classType.Name} 字符串长度无效。");
                        EnsureAvailable(bytes, index, length);
                        info.SetValue(dataObj, Encoding.UTF8.GetString(bytes, index, length));
                        index += length;
                    }
                    else
                    {
                        throw new InvalidDataException(
                            $"Excel 表 {classType.Name} 字段 {info.Name} 类型不支持：{info.FieldType.Name}（只支持 int / float / bool / string）");
                    }
                }

                FieldInfo dicField = containerType.GetField("dataDic", BindingFlags.Public | BindingFlags.Instance);
                object dicObject = dicField != null ? dicField.GetValue(containerObj) : null;
                if (dicObject == null)
                    throw new InvalidDataException($"Excel 容器 {containerType.Name} 缺少 dataDic 字段。");
                MethodInfo addMethod = dicObject.GetType().GetMethod("Add");
                if (addMethod == null)
                    throw new InvalidDataException($"Excel 容器 {containerType.Name} 的 dataDic 没有 Add 方法。");
                addMethod.Invoke(dicObject, new object[] { keyField.GetValue(dataObj), dataObj });
            }

            if (tableDic.ContainsKey(containerType.Name))
            {
                PLogger.LogWarning($"Excel 表 {containerType.Name} 重复加载，已替换旧数据。");
                tableDic[containerType.Name] = containerObj;
            }
            else
            {
                tableDic.Add(containerType.Name, containerObj);
            }
        }

        private static void EnsureAvailable(byte[] bytes, int index, int size)
        {
            if (index < 0 || size < 0 || index > bytes.Length - size)
                throw new InvalidDataException("Excel 二进制表数据不完整（字段长度越界）。");
        }
        /// <summary>
        /// 得到一张表的信息
        /// </summary>
        /// <typeparam name="T">容器类名</typeparam>
        /// <returns></returns>
        public T GetTable<T>() where T : class
        {
            string tableName = typeof(T).Name;
            if (tableDic.ContainsKey(tableName))
            {
                return tableDic[tableName] as T;
            }
            return null;
        }

        /// <summary>
        /// 存储类对象数据
        /// </summary>
        /// <param name="obj">数据类</param>
        /// <param name="fileName">存储文件名称</param>
        public void Save(object obj, string fileName)
        {
            string target = Path.Combine(SAVE_PATH, fileName + FILE_EXTENSION);
            VersionedDataSerializer.SaveFile(target, obj, "save");
        }
        /// <summary>
        /// 读取2进制数据，并转换成对象
        /// </summary>
        /// <typeparam name="T">对象类型</typeparam>
        /// <param name="fileName">存储文件名称</param>
        /// <returns>数据对象</returns>
        public T Load<T>(string fileName) where T : class
        {
            string path = Path.Combine(SAVE_PATH, fileName + FILE_EXTENSION);
            try
            {
                return VersionedDataSerializer.LoadFile<T>(path, "save");
            }
            catch (Exception e)
            {
                PLogger.LogError("读取存档失败：" + path + "\n" + e.Message);
                return default(T);
            }
        }
    }

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

