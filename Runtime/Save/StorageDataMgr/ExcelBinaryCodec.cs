using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace UPandaGF
{
    /// <summary>
    /// Excel 二进制表 v2 的列类型。
    /// <para>数值是写进文件头的稳定编号，只能追加、不能改已有值，否则旧表会被按错误类型解析。</para>
    /// </summary>
    public enum ExcelColumnType : byte
    {
        Unknown = 0,
        Int = 1,
        Float = 2,
        Bool = 3,
        String = 4,
    }

    /// <summary>v2 表头中的一列：列名 + 类型。</summary>
    public sealed class ExcelColumn
    {
        public string Name;
        public ExcelColumnType Type;

        public ExcelColumn() { }

        public ExcelColumn(string name, ExcelColumnType type)
        {
            Name = name;
            Type = type;
        }
    }

    /// <summary>
    /// v2 表的列结构（表名、主键列名、全部列）。
    /// <para>生成端按 Excel 的字段名行/类型行构造，运行端按这些列名映射到数据类字段；
    /// 因此 Excel 调换列顺序不会破坏读取。</para>
    /// </summary>
    public sealed class ExcelTableSchema
    {
        private readonly List<ExcelColumn> columns = new List<ExcelColumn>();

        /// <summary>表名（与 Excel 工作表名一致，也是容器类名的一部分）。</summary>
        public string TableName;

        /// <summary>主键列名（Excel 第 3 行的 <c>key</c> 标记；没有标记时为第一列）。</summary>
        public string KeyColumn;

        /// <summary>全部列（顺序与文件中的行宽一致）。</summary>
        public IList<ExcelColumn> Columns { get { return columns; } }

        public int ColumnCount { get { return columns.Count; } }

        /// <summary>追加一列；空列名/未知类型/重名列直接抛错，避免生成出无法读取的表。</summary>
        public void AddColumn(string name, ExcelColumnType type)
        {
            if (string.IsNullOrEmpty(name)) throw new InvalidDataException("Excel 二进制表的列名不能为空。");
            if (type == ExcelColumnType.Unknown) throw new InvalidDataException("Excel 二进制表的列类型不能是 Unknown：" + name);
            if (IndexOf(name) >= 0) throw new InvalidDataException("Excel 二进制表存在重名列：" + name);
            columns.Add(new ExcelColumn(name, type));
        }

        /// <summary>按列名查列序号；找不到返回 -1。</summary>
        public int IndexOf(string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            for (int i = 0; i < columns.Count; i++)
                if (string.Equals(columns[i].Name, name, StringComparison.Ordinal)) return i;
            return -1;
        }

        public ExcelColumn Find(string name)
        {
            int index = IndexOf(name);
            return index >= 0 ? columns[index] : null;
        }
    }

    /// <summary>
    /// 解码后的 v2 表数据：列结构和按行存放的装箱值（int/float/bool/string）。
    /// <para>只用于加载期，不住留在业务对象里；业务侧应把值映射到自己的数据类。</para>
    /// </summary>
    public sealed class ExcelTableData
    {
        private readonly List<object[]> rows = new List<object[]>();

        public ExcelTableData(ExcelTableSchema schema)
        {
            if (schema == null) throw new ArgumentNullException(nameof(schema));
            Schema = schema;
        }

        public ExcelTableSchema Schema { get; private set; }

        public IList<object[]> Rows { get { return rows; } }

        public int RowCount { get { return rows.Count; } }

        public void AddRow(object[] row)
        {
            if (row == null) throw new ArgumentNullException(nameof(row));
            if (row.Length != Schema.ColumnCount)
                throw new InvalidDataException(
                    "Excel 二进制表行宽与列结构不一致：表 " + Schema.TableName + " 期望 " + Schema.ColumnCount + " 列，实际 " + row.Length + " 列。");
            rows.Add(row);
        }
    }

    /// <summary>
    /// Excel 二进制表 v2 编解码器：生成端（<c>Editor/Excel/ExcelTool.cs</c>）与运行端
    /// （<c>StorageDataMgr/BinaryMgr/BinaryDataMgr.cs</c>）共用同一份实现，避免再次出现“写读不一致”。
    ///
    /// <para>文件布局（小端）：</para>
    /// <code>
    /// [0..4)   magic "UPXF"
    /// [4..6)   ushort version（当前 2）
    /// [6..8)   ushort flags（保留，当前 0）
    ///          int32 表名长度 + UTF-8 表名
    ///          int32 主键列名长度 + UTF-8 主键列名
    ///          ushort 列数
    ///          每列：int32 列名长度 + UTF-8 列名 + byte 列类型
    ///          int32 行数
    ///          每行：按列顺序的 int32/float/bool(4 字节)/string(长度+UTF8)
    ///          末尾 uint32 CRC32（覆盖前面全部字节）
    /// </code>
    ///
    /// <para>读取端会校验 magic、版本、列结构、行数与 CRC；任何截断/损坏都以
    /// <see cref="InvalidDataException"/> 报错，而不是静默读出半张表。</para>
    /// </summary>
    public static class ExcelBinaryCodec
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        // ---- 类型 ⇄ 文本 ----

        /// <summary>把 Excel 类型行的文本解析为列类型；不支持的类型返回 false。</summary>
        public static bool TryParseColumnType(string text, out ExcelColumnType type)
        {
            type = ExcelColumnType.Unknown;
            if (string.IsNullOrEmpty(text)) return false;
            switch (text.Trim().ToLowerInvariant())
            {
                case "int": type = ExcelColumnType.Int; return true;
                case "float": type = ExcelColumnType.Float; return true;
                case "bool": type = ExcelColumnType.Bool; return true;
                case "string": type = ExcelColumnType.String; return true;
                default: return false;
            }
        }

        /// <summary>把列类型解析为对应的 CLR 类型（数据类字段声明用）。</summary>
        public static Type ToClrType(ExcelColumnType type)
        {
            switch (type)
            {
                case ExcelColumnType.Int: return typeof(int);
                case ExcelColumnType.Float: return typeof(float);
                case ExcelColumnType.Bool: return typeof(bool);
                case ExcelColumnType.String: return typeof(string);
                default: return null;
            }
        }

        /// <summary>数据类字段类型是否与文件列类型一致。</summary>
        public static bool Matches(Type clrType, ExcelColumnType columnType)
        {
            if (clrType == null) return false;
            switch (columnType)
            {
                case ExcelColumnType.Int: return clrType == typeof(int);
                case ExcelColumnType.Float: return clrType == typeof(float);
                case ExcelColumnType.Bool: return clrType == typeof(bool);
                case ExcelColumnType.String: return clrType == typeof(string);
                default: return false;
            }
        }

        // ---- 写 ----

        /// <summary>
        /// 把列结构与行数据编码为 v2 字节数组。
        /// <para>行值必须是 int / float / bool / string 之一（允许 float 列传 double、int 列传可转换的整数）；
        /// 值类型与列类型不匹配、行宽不符、重名列都会抛 <see cref="InvalidDataException"/>。</para>
        /// </summary>
        public static byte[] Encode(ExcelTableSchema schema, IList<object[]> rows)
        {
            if (schema == null) throw new ArgumentNullException(nameof(schema));
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            ValidateSchema(schema);
            if (schema.ColumnCount > ushort.MaxValue)
                throw new InvalidDataException("Excel 二进制表列数超过上限：" + schema.ColumnCount);

            byte[] body;
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, Utf8, true))
                {
                    writer.Write(ExcelBinaryFormat.MagicBytes);
                    writer.Write((ushort)ExcelBinaryFormat.CurrentVersion);
                    writer.Write((ushort)0); // flags：保留位，读取端忽略
                    WriteString(writer, schema.TableName);
                    WriteString(writer, schema.KeyColumn);
                    writer.Write((ushort)schema.ColumnCount);
                    for (int c = 0; c < schema.ColumnCount; c++)
                    {
                        ExcelColumn column = schema.Columns[c];
                        WriteString(writer, column.Name);
                        writer.Write((byte)column.Type);
                    }

                    writer.Write(rows.Count);
                    for (int r = 0; r < rows.Count; r++)
                    {
                        object[] row = rows[r];
                        if (row == null || row.Length != schema.ColumnCount)
                            throw new InvalidDataException(
                                "Excel 二进制表第 " + (r + 1) + " 行宽度与列结构不一致：期望 " + schema.ColumnCount + " 列，实际 " +
                                (row == null ? "null" : row.Length.ToString()) + " 列。");
                        for (int c = 0; c < schema.ColumnCount; c++)
                            WriteValue(writer, schema.Columns[c], row[c], r, c);
                    }

                    writer.Flush();
                }
                body = stream.ToArray();
            }

            uint crc = Crc32.Compute(body, 0, body.Length);
            byte[] result = new byte[body.Length + 4];
            Buffer.BlockCopy(body, 0, result, 0, body.Length);
            WriteUInt32(result, body.Length, crc);
            return result;
        }

        private static void ValidateSchema(ExcelTableSchema schema)
        {
            if (schema.ColumnCount == 0)
                throw new InvalidDataException("Excel 二进制表至少要有一列。");
            if (string.IsNullOrEmpty(schema.TableName))
                throw new InvalidDataException("Excel 二进制表缺少表名。");
            if (string.IsNullOrEmpty(schema.KeyColumn))
                throw new InvalidDataException("Excel 二进制表缺少主键列名：" + schema.TableName);
            if (schema.IndexOf(schema.KeyColumn) < 0)
                throw new InvalidDataException("Excel 二进制表的主键列不在列结构中：" + schema.TableName + "." + schema.KeyColumn);
        }

        private static void WriteString(BinaryWriter writer, string value)
        {
            byte[] bytes = Utf8.GetBytes(value ?? string.Empty);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        private static void WriteValue(BinaryWriter writer, ExcelColumn column, object value, int row, int columnIndex)
        {
            string where = "第 " + (row + 1) + " 行第 " + (columnIndex + 1) + " 列（" + column.Name + "）";
            try
            {
                switch (column.Type)
                {
                    case ExcelColumnType.Int:
                        if (value == null) throw new InvalidDataException("值为 null");
                        writer.Write(Convert.ToInt32(value, CultureInfo.InvariantCulture));
                        break;
                    case ExcelColumnType.Float:
                        if (value == null) throw new InvalidDataException("值为 null");
                        writer.Write(Convert.ToSingle(value, CultureInfo.InvariantCulture));
                        break;
                    case ExcelColumnType.Bool:
                        if (value == null) throw new InvalidDataException("值为 null");
                        writer.Write(Convert.ToBoolean(value, CultureInfo.InvariantCulture) ? 1 : 0);
                        break;
                    case ExcelColumnType.String:
                        WriteString(writer, value as string ?? (value == null ? string.Empty : Convert.ToString(value, CultureInfo.InvariantCulture)));
                        break;
                    default:
                        throw new InvalidDataException("列类型未知：" + column.Type);
                }
            }
            catch (Exception e) when (e is InvalidDataException || e is FormatException || e is InvalidCastException || e is OverflowException)
            {
                throw new InvalidDataException("Excel 二进制表 " + where + " 的值无法按 " + column.Type + " 写入：" + value, e);
            }
        }

        // ---- 读 ----

        /// <summary>字节流是否以 v2 magic 开头（否则按 legacy v1 布局处理）。</summary>
        public static bool IsVersion2(byte[] bytes)
        {
            return ExcelBinaryFormat.HasMagic(bytes);
        }

        /// <summary>
        /// 解码 v2 字节流。校验失败抛 <see cref="InvalidDataException"/>，不返回半张表。
        /// </summary>
        public static ExcelTableData Decode(byte[] bytes)
        {
            string error;
            ExcelTableData table;
            if (!TryDecode(bytes, out table, out error)) throw new InvalidDataException(error);
            return table;
        }

        /// <summary>非异常版本：失败时返回 false 并给出可读原因（供工具自检/诊断使用）。</summary>
        public static bool TryDecode(byte[] bytes, out ExcelTableData table, out string error)
        {
            table = null;
            error = null;
            if (bytes == null) { error = "Excel 二进制表内容为 null。"; return false; }
            if (bytes.Length < 12) { error = "Excel 二进制表长度不足，无法包含文件头：" + bytes.Length + " 字节。"; return false; }
            if (!ExcelBinaryFormat.HasMagic(bytes))
            {
                error = "不是 v2 格式（缺少 " + ExcelBinaryFormat.MagicText + " magic；可能是旧版 v1 表）。";
                return false;
            }

            var reader = new Reader(bytes);
            reader.Skip(ExcelBinaryFormat.MagicBytes.Length);
            ushort version = reader.ReadUInt16();
            if (version != ExcelBinaryFormat.CurrentVersion)
            {
                error = "不支持的 Excel 二进制表版本：" + version + "（当前支持 " + ExcelBinaryFormat.CurrentVersion + "）。";
                return false;
            }
            reader.ReadUInt16(); // flags：当前没有语义

            var schema = new ExcelTableSchema();
            try
            {
                schema.TableName = reader.ReadString("表名");
                schema.KeyColumn = reader.ReadString("主键列名");
                int columnCount = reader.ReadUInt16();
                if (columnCount <= 0) throw new InvalidDataException("列数必须大于 0。");
                for (int c = 0; c < columnCount; c++)
                {
                    string name = reader.ReadString("第 " + (c + 1) + " 列的列名");
                    byte typeValue = reader.ReadByte();
                    if (typeValue < (byte)ExcelColumnType.Int || typeValue > (byte)ExcelColumnType.String)
                        throw new InvalidDataException("第 " + (c + 1) + " 列的类型编号无效：" + typeValue);
                    schema.AddColumn(name, (ExcelColumnType)typeValue);
                }
                if (schema.IndexOf(schema.KeyColumn) < 0)
                    throw new InvalidDataException("主键列 " + schema.KeyColumn + " 不在列结构中。");

                int rowCount = reader.ReadInt32();
                if (rowCount < 0) throw new InvalidDataException("行数无效：" + rowCount);

                var result = new ExcelTableData(schema);
                for (int r = 0; r < rowCount; r++)
                {
                    var row = new object[columnCount];
                    for (int c = 0; c < columnCount; c++) row[c] = reader.ReadValue(schema.Columns[c].Type);
                    result.AddRow(row);
                }

                if (reader.Remaining != 4)
                    throw new InvalidDataException(
                        "文件尾部长度不符（期望 4 字节 CRC，实际剩余 " + reader.Remaining + " 字节；文件可能被截断或被追加了内容）。");
                uint storedCrc = reader.ReadUInt32();
                uint actualCrc = Crc32.Compute(bytes, 0, bytes.Length - 4);
                if (storedCrc != actualCrc)
                    throw new InvalidDataException("CRC32 校验失败，表文件已损坏。");

                table = result;
                return true;
            }
            catch (Exception e) when (e is InvalidDataException || e is DecoderFallbackException || e is ArgumentException)
            {
                error = "Excel 二进制表解析失败（偏移 " + reader.Position + "）：" + e.Message;
                return false;
            }
        }

        /// <summary>读取器：所有读操作先做边界检查，异常统一为 <see cref="InvalidDataException"/>。</summary>
        private sealed class Reader
        {
            private readonly byte[] bytes;
            private int position;

            public Reader(byte[] bytes) { this.bytes = bytes; }

            public int Position { get { return position; } }

            public int Remaining { get { return bytes.Length - position; } }

            public void Skip(int count)
            {
                Ensure(count);
                position += count;
            }

            public byte ReadByte()
            {
                Ensure(1);
                return bytes[position++];
            }

            public ushort ReadUInt16()
            {
                Ensure(2);
                ushort value = (ushort)(bytes[position] | (bytes[position + 1] << 8));
                position += 2;
                return value;
            }

            public int ReadInt32()
            {
                Ensure(4);
                int value = bytes[position] | (bytes[position + 1] << 8) | (bytes[position + 2] << 16) | (bytes[position + 3] << 24);
                position += 4;
                return value;
            }

            public uint ReadUInt32()
            {
                Ensure(4);
                uint value = (uint)(bytes[position] | (bytes[position + 1] << 8) | (bytes[position + 2] << 16) | (bytes[position + 3] << 24));
                position += 4;
                return value;
            }

            public float ReadSingle()
            {
                return BitConverter.ToSingle(BitConverter.GetBytes(ReadInt32()), 0);
            }

            public string ReadString(string label)
            {
                int length = ReadInt32();
                if (length < 0) throw new InvalidDataException(label + "长度无效：" + length);
                if (length == 0) return string.Empty;
                Ensure(length);
                string value;
                try { value = Utf8.GetString(bytes, position, length); }
                catch (DecoderFallbackException e) { throw new InvalidDataException(label + "不是合法的 UTF-8。", e); }
                position += length;
                return value;
            }

            public object ReadValue(ExcelColumnType type)
            {
                switch (type)
                {
                    case ExcelColumnType.Int: return ReadInt32();
                    case ExcelColumnType.Float: return ReadSingle();
                    case ExcelColumnType.Bool: return ReadInt32() != 0;
                    case ExcelColumnType.String: return ReadString("字符串");
                    default: throw new InvalidDataException("列类型无效：" + type);
                }
            }

            private void Ensure(int count)
            {
                if (count < 0) throw new InvalidDataException("读取长度不能为负数：" + count);
                if (position > bytes.Length - count)
                    throw new InvalidDataException(
                        "数据不完整（偏移 " + position + " 需要 " + count + " 字节，剩余 " + (bytes.Length - position) + " 字节）。");
            }
        }

        private static void WriteUInt32(byte[] target, int offset, uint value)
        {
            target[offset] = (byte)(value & 0xFF);
            target[offset + 1] = (byte)((value >> 8) & 0xFF);
            target[offset + 2] = (byte)((value >> 16) & 0xFF);
            target[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        /// <summary>标准 IEEE CRC32（多项式 0xEDB88320），用于发现截断/篡改的二进制表。</summary>
        private static class Crc32
        {
            private static readonly uint[] Table = CreateTable();

            private static uint[] CreateTable()
            {
                var table = new uint[256];
                for (uint i = 0; i < 256; i++)
                {
                    uint value = i;
                    for (int bit = 0; bit < 8; bit++)
                        value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
                    table[i] = value;
                }
                return table;
            }

            public static uint Compute(byte[] data, int offset, int count)
            {
                uint crc = 0xFFFFFFFFu;
                for (int i = offset; i < offset + count; i++)
                    crc = Table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
                return crc ^ 0xFFFFFFFFu;
            }
        }
    }
}
