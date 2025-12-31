using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
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
        private static string DATA_BINARY_PATH = Application.persistentDataPath + "/BinaryData/";
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
            //读取 excel表对应的2进制文件 来进行解析
            using (FileStream fs = File.Open(DATA_BINARY_PATH + typeof(K).Name + FILE_EXTENSION, FileMode.Open, FileAccess.Read))
            {
                //声明字节容器
                byte[] bytes = new byte[fs.Length];
                //路径下的文件内容读进字节容器里
                fs.Read(bytes, 0, bytes.Length);
                fs.Close();

                //用于记录当前读取了多少字节了
                int index = 0;

                //读取一共有多少行数据
                int count = BitConverter.ToInt32(bytes, index);
                index += 4;

                //读取主键的名字
                int keyNameLength = BitConverter.ToInt32(bytes, index);
                index += 4;
                string keyName = Encoding.UTF8.GetString(bytes, index, keyNameLength);
                index += keyNameLength;

                //创建容器类对象
                Type contaninerType = typeof(T);
                object contaninerObj = Activator.CreateInstance(contaninerType);
                //得到数据结构类的Type
                Type classType = typeof(K);
                //通过反射 得到数据结构类 所有字段的信息
                FieldInfo[] infos = classType.GetFields();
                //读取每一行的信息
                for (int i = 0; i < count; i++)
                {
                    //实例化一个数据结构类 对象
                    object dataObj = Activator.CreateInstance(classType);
                    foreach (FieldInfo info in infos)
                    {
                        if (info.FieldType == typeof(int))
                        {
                            //相当于就是把2进制数据转为int 然后赋值给了对应的字段
                            info.SetValue(dataObj, BitConverter.ToInt32(bytes, index));
                            index += 4;
                        }
                        else if (info.FieldType == typeof(float))
                        {
                            info.SetValue(dataObj, BitConverter.ToSingle(bytes, index));
                            index += 4;
                        }
                        else if (info.FieldType == typeof(bool))
                        {
                            info.SetValue(dataObj, BitConverter.ToBoolean(bytes, index));
                            index += 4;
                        }
                        else if (info.FieldType == typeof(string))
                        {
                            //先读取字符串字节数组的长度
                            int length = BitConverter.ToInt32(bytes, index);
                            index += 4;
                            info.SetValue(dataObj, Encoding.UTF8.GetString(bytes, index, length));
                            index += length;
                        }
                    }
                    //读取完一行的数据了 应该把这个数据添加到容器对象中
                    //得到容器对象中的 字典对象
                    object dicObject = contaninerType.GetField("dataDic").GetValue(contaninerObj);
                    //通过字典对象得到其中的 Add方法
                    MethodInfo mInfo = dicObject.GetType().GetMethod("Add");
                    //得到数据结构类对象中 指定主键字段的值
                    object keyValue = classType.GetField(keyName).GetValue(dataObj);
                    mInfo.Invoke(dicObject, new object[] { keyValue, dataObj });
                }
                //把读取完的表记录下来
                tableDic.Add(typeof(T).Name, contaninerObj);

                fs.Close();
            }
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
            //先判断路径文件夹有没有
            if (!Directory.Exists(SAVE_PATH))
            {
                Directory.CreateDirectory(SAVE_PATH);
            }

            /*这是用文件流的方式存数据，
            using (FileStream fs = new FileStream(SAVE_PATH + fileName + FILE_EXTENSION, FileMode.OpenOrCreate, FileAccess.Write))
            {
                //把类序列化成二进制数据的工具  命名空间：using System.Runtime.Serialization.Formatters.Binary;
                BinaryFormatter bf = new BinaryFormatter();
                bf.Serialize(fs, obj);
                fs.Close();
            }
            */

            //改用内存流的方式可以对数据再做些操作，比如进行加密
            using (MemoryStream ms = new MemoryStream())
            {
                BinaryFormatter bf = new BinaryFormatter();
                bf.Serialize(ms, obj);

                byte[] bytes = ms.GetBuffer();
                //ToDo:..在这里可以做一些加密的工作
                File.WriteAllBytes(SAVE_PATH + fileName + FILE_EXTENSION, bytes);
                ms.Close();
            }

        }
        /// <summary>
        /// 读取2进制数据，并转换成对象
        /// </summary>
        /// <typeparam name="T">对象类型</typeparam>
        /// <param name="fileName">存储文件名称</param>
        /// <returns>数据对象</returns>
        public T Load<T>(string fileName) where T : class
        {
            //如果不存在这个文件 就直接返回泛型对象的默认值
            if (!File.Exists(SAVE_PATH + fileName + FILE_EXTENSION))
            {
                return default(T);
            }
            T obj = null;

            /*这是用文件流的方式读数据，
            using (FileStream fs = File.Open(SAVE_PATH + fileName + FILE_EXTENSION, FileMode.Open, FileAccess.Read))
            {
                BinaryFormatter bf = new BinaryFormatter();
                obj = bf.Deserialize(fs) as T;
                fs.Close();
            }
            */

            byte[] bytes = File.ReadAllBytes(SAVE_PATH + fileName + FILE_EXTENSION);
            //ToDo..这里可以根据规则做解密的工作
            using (MemoryStream ms = new MemoryStream(bytes))
            {
                BinaryFormatter bf = new BinaryFormatter();
                obj = bf.Deserialize(ms) as T;
                ms.Close();
            }
            return obj;
        }
    }

    public class AESEncryption
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

