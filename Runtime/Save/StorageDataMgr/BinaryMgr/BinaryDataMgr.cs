using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>
    /// 读取二进制的内容，通过反射得到数据，并把数据存入容器。再用这个二进制数据管理器统一管理。
    ///
    /// <para>支持两种表格式：</para>
    /// <list type="bullet">
    /// <item>v2（当前生成端输出）：带 magic/版本/列结构/CRC，按<b>列名</b>映射到数据类字段，列顺序可以变化；</item>
    /// <item>v1（legacy）：无文件头，按数据类字段声明顺序逐个读取；保留读取能力用于旧包/旧表迁移。</item>
    /// </list>
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

        /// <summary>
        /// 数据类字段缓存：按列名映射 v2 表列，避免每个单元格都做一次反射查找。
        /// </summary>
        private static readonly Dictionary<Type, Dictionary<string, FieldInfo>> RowBindings =
            new Dictionary<Type, Dictionary<string, FieldInfo>>();

        public void InitData(string savePath, string extension)
        {
            DATA_BINARY_PATH =  savePath;
            FILE_EXTENSION = extension;
            PLogger.Log($"BinaryDataMgr Init!\n存储路径：{DATA_BINARY_PATH}，后缀：{FILE_EXTENSION}");
        }

        /// <summary>
        /// 加载Excel表的2进制数据到内存中（从运行时表目录）。
        /// </summary>
        /// <typeparam name="T">容器类</typeparam>
        /// <typeparam name="K">数据结构类</typeparam>
        public void LoadTable<T, K>()
        {
            string tablePath = Path.Combine(DATA_BINARY_PATH, typeof(K).Name + FILE_EXTENSION);
            if (!File.Exists(tablePath))
                throw new FileNotFoundException($"Excel 二进制表不存在：{tablePath}。请用 ExcelTool 生成并确认已安装到运行时目录。", tablePath);

            byte[] bytes = File.ReadAllBytes(tablePath);
            LoadTable<T, K>(bytes, tablePath);
        }

        /// <summary>
        /// 从字节加载表（测试 / 自定义存储 / 热更安装使用）。
        /// <para>带 v2 magic 的按列名映射；否则按 legacy v1 布局读取。</para>
        /// </summary>
        /// <param name="bytes">表文件内容</param>
        /// <param name="sourceName">错误信息里展示的来源（默认用数据类名）</param>
        public void LoadTable<T, K>(byte[] bytes, string sourceName = null)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            string source = string.IsNullOrEmpty(sourceName) ? typeof(K).Name + FILE_EXTENSION : sourceName;
            if (ExcelBinaryCodec.IsVersion2(bytes))
            {
                LoadTableVersion2<T, K>(ExcelBinaryCodec.Decode(bytes), source);
                return;
            }
            LoadTableLegacy<T, K>(bytes, source);
        }

        /// <summary>
        /// v2 读取：列结构来自文件头，列按名字映射到数据类字段；主键列必须存在，未知列忽略并告警。
        /// </summary>
        private void LoadTableVersion2<T, K>(ExcelTableData data, string source)
        {
            Type containerType = typeof(T);
            Type rowType = typeof(K);
            ExcelTableSchema schema = data.Schema;

            Dictionary<string, FieldInfo> fields = GetRowBinding(rowType);
            FieldInfo keyField;
            if (!fields.TryGetValue(schema.KeyColumn, out keyField) || keyField == null)
                throw new InvalidDataException(
                    $"Excel 表 {schema.TableName}（{source}）的主键列 {schema.KeyColumn} 在数据类 {rowType.Name} 中不存在。");

            var columnFields = new FieldInfo[schema.ColumnCount];
            for (int c = 0; c < schema.ColumnCount; c++)
            {
                ExcelColumn column = schema.Columns[c];
                FieldInfo field;
                if (!fields.TryGetValue(column.Name, out field) || field == null)
                {
                    PLogger.LogWarning(
                        $"[BinaryDataMgr] 表 {schema.TableName} 的列 {column.Name} 在数据类 {rowType.Name} 中没有对应字段，已忽略。");
                    continue;
                }
                if (!ExcelBinaryCodec.Matches(field.FieldType, column.Type))
                    throw new InvalidDataException(
                        $"Excel 表 {schema.TableName}（{source}）的列 {column.Name} 类型是 {column.Type}，" +
                        $"但数据类 {rowType.Name} 的字段类型是 {field.FieldType.Name}。");
                columnFields[c] = field;
            }
            foreach (var pair in fields)
            {
                if (schema.IndexOf(pair.Key) < 0)
                    PLogger.LogWarning(
                        $"[BinaryDataMgr] 数据类 {rowType.Name} 的字段 {pair.Key} 在表 {schema.TableName} 中不存在，使用默认值。");
            }

            object containerObj = Activator.CreateInstance(containerType);
            object dicObject;
            MethodInfo addMethod = GetContainerAddMethod(containerType, containerObj, out dicObject);
            var arguments = new object[2];
            for (int r = 0; r < data.RowCount; r++)
            {
                object rowObj = Activator.CreateInstance(rowType);
                object[] row = data.Rows[r];
                for (int c = 0; c < columnFields.Length; c++)
                    if (columnFields[c] != null) columnFields[c].SetValue(rowObj, row[c]);

                arguments[0] = keyField.GetValue(rowObj);
                arguments[1] = rowObj;
                try { addMethod.Invoke(dicObject, arguments); }
                catch (TargetInvocationException e)
                {
                    throw new InvalidDataException(
                        $"Excel 表 {schema.TableName}（{source}）第 {r + 1} 行无法写入容器（主键重复？）：" +
                        (e.InnerException != null ? e.InnerException.Message : e.Message), e.InnerException ?? e);
                }
            }

            RegisterTable(containerType, containerObj);
        }

        /// <summary>
        /// legacy v1 读取：无文件头，字段顺序 = 生成时的列顺序。
        /// <para>保留是为了兼容旧包里的表；新表一律走 v2。</para>
        /// </summary>
        private void LoadTableLegacy<T, K>(byte[] bytes, string source)
        {
            if (bytes.Length < 8)
                throw new InvalidDataException($"Excel 二进制表损坏（长度不足）：{source}");

            int index = 0;
            int count = BitConverter.ToInt32(bytes, index); index += 4;
            int keyNameLength = BitConverter.ToInt32(bytes, index); index += 4;
            EnsureAvailable(bytes, index, keyNameLength, source);
            string keyName = Encoding.UTF8.GetString(bytes, index, keyNameLength); index += keyNameLength;
            if (count < 0) throw new InvalidDataException($"Excel 二进制表行数无效：{source}");

            Type containerType = typeof(T);
            object containerObj = Activator.CreateInstance(containerType);
            Type classType = typeof(K);
            FieldInfo[] infos = classType.GetFields(BindingFlags.Public | BindingFlags.Instance);
            FieldInfo keyField = classType.GetField(keyName, BindingFlags.Public | BindingFlags.Instance);
            if (keyField == null) throw new InvalidDataException($"Excel 表 {classType.Name} 缺少主键字段：{keyName}");

            object dicObject;
            MethodInfo addMethod = GetContainerAddMethod(containerType, containerObj, out dicObject);
            var arguments = new object[2];
            for (int i = 0; i < count; i++)
            {
                object dataObj = Activator.CreateInstance(classType);
                foreach (FieldInfo info in infos)
                {
                    if (info.FieldType == typeof(int))
                    {
                        EnsureAvailable(bytes, index, 4, source);
                        info.SetValue(dataObj, BitConverter.ToInt32(bytes, index));
                        index += 4;
                    }
                    else if (info.FieldType == typeof(float))
                    {
                        EnsureAvailable(bytes, index, 4, source);
                        info.SetValue(dataObj, BitConverter.ToSingle(bytes, index));
                        index += 4;
                    }
                    else if (info.FieldType == typeof(bool))
                    {
                        // 生成端按 ExcelBinaryFormat.BoolByteSize（4 字节）写入，读取端必须一致推进。
                        EnsureAvailable(bytes, index, ExcelBinaryFormat.BoolByteSize, source);
                        info.SetValue(dataObj, BitConverter.ToInt32(bytes, index) != 0);
                        index += ExcelBinaryFormat.BoolByteSize;
                    }
                    else if (info.FieldType == typeof(string))
                    {
                        EnsureAvailable(bytes, index, 4, source);
                        int length = BitConverter.ToInt32(bytes, index);
                        index += 4;
                        if (length < 0) throw new InvalidDataException($"Excel 表 {classType.Name} 字符串长度无效（{source}）。");
                        EnsureAvailable(bytes, index, length, source);
                        info.SetValue(dataObj, Encoding.UTF8.GetString(bytes, index, length));
                        index += length;
                    }
                    else
                    {
                        throw new InvalidDataException(
                            $"Excel 表 {classType.Name} 字段 {info.Name} 类型不支持：{info.FieldType.Name}（只支持 int / float / bool / string）");
                    }
                }

                arguments[0] = keyField.GetValue(dataObj);
                arguments[1] = dataObj;
                try { addMethod.Invoke(dicObject, arguments); }
                catch (TargetInvocationException e)
                {
                    throw new InvalidDataException(
                        $"Excel 表 {classType.Name}（{source}）第 {i + 1} 行无法写入容器（主键重复？）：" +
                        (e.InnerException != null ? e.InnerException.Message : e.Message), e.InnerException ?? e);
                }
            }

            RegisterTable(containerType, containerObj);
        }

        private static Dictionary<string, FieldInfo> GetRowBinding(Type rowType)
        {
            lock (RowBindings)
            {
                Dictionary<string, FieldInfo> cached;
                if (RowBindings.TryGetValue(rowType, out cached)) return cached;

                cached = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
                foreach (FieldInfo field in rowType.GetFields(BindingFlags.Public | BindingFlags.Instance))
                    if (!cached.ContainsKey(field.Name)) cached.Add(field.Name, field);
                RowBindings.Add(rowType, cached);
                return cached;
            }
        }

        private static MethodInfo GetContainerAddMethod(Type containerType, object containerObj, out object dicObject)
        {
            FieldInfo dicField = containerType.GetField("dataDic", BindingFlags.Public | BindingFlags.Instance);
            dicObject = dicField != null ? dicField.GetValue(containerObj) : null;
            if (dicObject == null)
                throw new InvalidDataException($"Excel 容器 {containerType.Name} 缺少 dataDic 字段。");
            MethodInfo addMethod = dicObject.GetType().GetMethod("Add");
            if (addMethod == null)
                throw new InvalidDataException($"Excel 容器 {containerType.Name} 的 dataDic 没有 Add 方法。");
            return addMethod;
        }

        private void RegisterTable(Type containerType, object containerObj)
        {
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

        private static void EnsureAvailable(byte[] bytes, int index, int size, string source)
        {
            if (index < 0 || size < 0 || index > bytes.Length - size)
                throw new InvalidDataException($"Excel 二进制表数据不完整（{source} 字段长度越界）。");
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
}
