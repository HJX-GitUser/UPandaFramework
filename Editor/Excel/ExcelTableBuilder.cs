using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using UnityEngine;
using UPandaGF;

namespace UPandaGF.GFEditor
{
    /// <summary>
    /// Excel 工作表 → v2 二进制表字节的纯转换层（不写文件、不依赖窗口状态）。
    /// <para>ExcelTool 负责读写文件，本类负责“DataTable → 列结构 + 强类型行 → <see cref="ExcelBinaryCodec"/> 编码”，
    /// 这样生成端逻辑可以被 EditMode 测试直接覆盖，不需要打开 Excel 文件。</para>
    /// </summary>
    public static class ExcelTableBuilder
    {
        /// <summary>内容行默认起始行：0=字段名、1=类型、2=主键标记、3=注释、4 起是数据。</summary>
        public const int DefaultBeginIndex = 4;

        /// <summary>把字段名行/类型行/主键标记行转换成 v2 列结构。</summary>
        public static ExcelTableSchema BuildSchema(DataTable table)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            if (table.Rows.Count < 2)
                throw new InvalidDataException($"Excel 表 {table.TableName} 至少需要字段名行与类型行。");

            DataRow nameRow = table.Rows[0];
            DataRow typeRow = table.Rows[1];
            int keyIndex = GetKeyIndex(table);
            var schema = new ExcelTableSchema { TableName = table.TableName };

            for (int i = 0; i < table.Columns.Count; i++)
            {
                string name = CellText(nameRow, i);
                if (string.IsNullOrEmpty(name))
                    throw new InvalidDataException($"Excel 表 {table.TableName} 第 {i + 1} 列缺少字段名。");

                string typeText = CellText(typeRow, i);
                ExcelColumnType type;
                if (!ExcelBinaryCodec.TryParseColumnType(typeText, out type))
                    throw new InvalidDataException(
                        $"Excel 表 {table.TableName} 第 {i + 1} 列类型无效：{typeText}（只支持 int / float / bool / string）");

                schema.AddColumn(name, type);
            }

            schema.KeyColumn = schema.Columns[Mathf.Clamp(keyIndex, 0, schema.ColumnCount - 1)].Name;
            return schema;
        }

        /// <summary>按列结构把内容行解析成强类型值（空单元格 int/float→0、bool→false、string→空串）。</summary>
        public static List<object[]> BuildRows(DataTable table, ExcelTableSchema schema, int beginIndex = DefaultBeginIndex)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            if (schema == null) throw new ArgumentNullException(nameof(schema));
            if (beginIndex < 0) throw new ArgumentOutOfRangeException(nameof(beginIndex));
            if (schema.ColumnCount != table.Columns.Count)
                throw new InvalidDataException(
                    $"Excel 表 {table.TableName} 的列结构与工作表不一致：结构 {schema.ColumnCount} 列，工作表 {table.Columns.Count} 列。");

            var rows = new List<object[]>(Math.Max(0, table.Rows.Count - beginIndex));
            for (int i = beginIndex; i < table.Rows.Count; i++)
            {
                DataRow row = table.Rows[i];
                var values = new object[schema.ColumnCount];
                for (int j = 0; j < schema.ColumnCount; j++)
                {
                    ExcelColumn column = schema.Columns[j];
                    string cell = CellText(row, j);
                    switch (column.Type)
                    {
                        case ExcelColumnType.Int:
                            values[j] = ParseIntCell(cell, table.TableName, j);
                            break;
                        case ExcelColumnType.Float:
                            values[j] = ParseFloatCell(cell, table.TableName, j);
                            break;
                        case ExcelColumnType.Bool:
                            values[j] = ParseBoolCell(cell, table.TableName, j);
                            break;
                        case ExcelColumnType.String:
                            values[j] = cell;
                            break;
                        default:
                            throw new InvalidDataException(
                                $"Excel 表 {table.TableName} 第 {j + 1} 列类型无效：{column.Type}");
                    }
                }
                rows.Add(values);
            }
            return rows;
        }

        /// <summary>DataTable → v2 字节（编码后立即用同一份 codec 解码自检）。</summary>
        public static byte[] Encode(DataTable table, out ExcelTableSchema schema, out int rowCount, int beginIndex = DefaultBeginIndex)
        {
            schema = BuildSchema(table);
            List<object[]> rows = BuildRows(table, schema, beginIndex);
            byte[] bytes = ExcelBinaryCodec.Encode(schema, rows);

            ExcelTableData check;
            string error;
            if (!ExcelBinaryCodec.TryDecode(bytes, out check, out error))
                throw new InvalidDataException($"Excel 表 {table.TableName} 生成后自检失败：{error}");
            if (check.RowCount != rows.Count || check.Schema.ColumnCount != schema.ColumnCount)
                throw new InvalidDataException(
                    $"Excel 表 {table.TableName} 生成后自检失败：写入 {rows.Count} 行/{schema.ColumnCount} 列，" +
                    $"读回 {check.RowCount} 行/{check.Schema.ColumnCount} 列。");

            rowCount = rows.Count;
            return bytes;
        }

        /// <summary>获取主键索引（第 3 行的 key 标记）；没有标记时默认第一列。</summary>
        public static int GetKeyIndex(DataTable table)
        {
            if (table.Rows.Count <= 2) return 0;
            DataRow row = table.Rows[2];
            for (int i = 0; i < table.Columns.Count; i++)
            {
                if (row[i].ToString() == "key") return i;
            }
            return 0;
        }

        private static string CellText(DataRow row, int index)
        {
            return row[index] == null || row[index] == DBNull.Value ? string.Empty : row[index].ToString().Trim();
        }

        private static int ParseIntCell(string value, string table, int column)
        {
            if (string.IsNullOrEmpty(value)) { Debug.LogWarning($"[ExcelTool] {table} 第 {column + 1} 列为空，按 0 处理"); return 0; }
            int result;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
                throw new InvalidDataException($"Excel 表 {table} 第 {column + 1} 列不是 int：{value}");
            return result;
        }

        private static float ParseFloatCell(string value, string table, int column)
        {
            if (string.IsNullOrEmpty(value)) { Debug.LogWarning($"[ExcelTool] {table} 第 {column + 1} 列为空，按 0 处理"); return 0f; }
            float result;
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
                throw new InvalidDataException($"Excel 表 {table} 第 {column + 1} 列不是 float：{value}");
            return result;
        }

        private static bool ParseBoolCell(string value, string table, int column)
        {
            if (string.IsNullOrEmpty(value)) { Debug.LogWarning($"[ExcelTool] {table} 第 {column + 1} 列为空，按 false 处理"); return false; }
            bool result;
            if (bool.TryParse(value, out result)) return result;
            if (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
            throw new InvalidDataException($"Excel 表 {table} 第 {column + 1} 列不是 bool：{value}");
        }
    }
}
