using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace UPandaGF.Tests.EditMode
{
    /// <summary>
    /// P1 Excel 二进制 v2 回归：编解码往返、按列名映射（列顺序可变）、legacy 兼容判定、
    /// 损坏/截断/尾部冗余检测，以及运行端 BinaryDataMgr 的映射错误提示。
    /// </summary>
    public class ExcelBinaryV2Tests
    {
        [Serializable]
        public class ItemRow
        {
            public int id;
            public string name;
            public float price;
            public bool stackable;
        }

        public class ItemContainer
        {
            public Dictionary<int, ItemRow> dataDic = new Dictionary<int, ItemRow>();
        }

        [Serializable]
        public class ReorderedRow
        {
            public int id;
            public string title;
            public float weight;
        }

        public class ReorderedContainer
        {
            public Dictionary<int, ReorderedRow> dataDic = new Dictionary<int, ReorderedRow>();
        }

        [Serializable]
        public class MismatchRow
        {
            public int id;
            public int price; // 表里 price 是 float，用于验证类型不一致会报错
        }

        public class MismatchContainer
        {
            public Dictionary<int, MismatchRow> dataDic = new Dictionary<int, MismatchRow>();
        }

        [Serializable]
        public class MissingKeyRow
        {
            public string name;
        }

        public class MissingKeyContainer
        {
            public Dictionary<string, MissingKeyRow> dataDic = new Dictionary<string, MissingKeyRow>();
        }

        // ---- 夹具 ----

        private static ExcelTableSchema ItemSchema()
        {
            var schema = new ExcelTableSchema { TableName = "Item", KeyColumn = "id" };
            schema.AddColumn("id", ExcelColumnType.Int);
            schema.AddColumn("name", ExcelColumnType.String);
            schema.AddColumn("price", ExcelColumnType.Float);
            schema.AddColumn("stackable", ExcelColumnType.Bool);
            return schema;
        }

        private static List<object[]> ItemRows()
        {
            return new List<object[]>
            {
                new object[] { 1, "中文名", 1.5f, true },
                new object[] { 2, string.Empty, -2f, false },
                new object[] { 3, new string('x', 512), 0f, true },
            };
        }

        // ---- 编解码 ----

        [Test]
        public void EncodeDecodeRoundTripsAllTypesAndKeepsContent()
        {
            byte[] bytes = ExcelBinaryCodec.Encode(ItemSchema(), ItemRows());

            Assert.IsTrue(ExcelBinaryFormat.HasMagic(bytes), "v2 文件必须以 magic 开头");
            Assert.IsTrue(ExcelBinaryCodec.IsVersion2(bytes));

            ExcelTableData table = ExcelBinaryCodec.Decode(bytes);
            Assert.AreEqual("Item", table.Schema.TableName);
            Assert.AreEqual("id", table.Schema.KeyColumn);
            Assert.AreEqual(4, table.Schema.ColumnCount);
            Assert.AreEqual(3, table.RowCount);

            Assert.AreEqual(1, table.Rows[0][0]);
            Assert.AreEqual("中文名", table.Rows[0][1]);
            Assert.AreEqual(1.5f, (float)table.Rows[0][2]);
            Assert.AreEqual(true, table.Rows[0][3]);

            Assert.AreEqual(string.Empty, table.Rows[1][1], "空字符串必须原样读回");
            Assert.AreEqual(-2f, (float)table.Rows[1][2]);
            Assert.AreEqual(false, table.Rows[1][3]);

            Assert.AreEqual(512, ((string)table.Rows[2][1]).Length);
        }

        [Test]
        public void ParseColumnTypeIsCaseInsensitiveAndRejectsUnknown()
        {
            ExcelColumnType type;
            Assert.IsTrue(ExcelBinaryCodec.TryParseColumnType(" INT ", out type));
            Assert.AreEqual(ExcelColumnType.Int, type);
            Assert.IsTrue(ExcelBinaryCodec.TryParseColumnType("Float", out type));
            Assert.AreEqual(ExcelColumnType.Float, type);
            Assert.IsTrue(ExcelBinaryCodec.TryParseColumnType("bool", out type));
            Assert.AreEqual(ExcelColumnType.Bool, type);
            Assert.IsTrue(ExcelBinaryCodec.TryParseColumnType("string", out type));
            Assert.AreEqual(ExcelColumnType.String, type);
            Assert.IsFalse(ExcelBinaryCodec.TryParseColumnType("double", out type));
            Assert.IsFalse(ExcelBinaryCodec.TryParseColumnType(string.Empty, out type));
        }

        // ---- 运行端映射 ----

        [Test]
        public void LoadTableMapsV2ColumnsByNameWhenColumnOrderDiffers()
        {
            var schema = new ExcelTableSchema { TableName = "Reordered", KeyColumn = "id" };
            // 故意与数据类字段声明顺序不同
            schema.AddColumn("title", ExcelColumnType.String);
            schema.AddColumn("weight", ExcelColumnType.Float);
            schema.AddColumn("id", ExcelColumnType.Int);
            var rows = new List<object[]>
            {
                new object[] { "轻", 1.25f, 7 },
                new object[] { "重", 9f, 8 },
            };

            byte[] bytes = ExcelBinaryCodec.Encode(schema, rows);
            BinaryDataMgr.Instance.LoadTable<ReorderedContainer, ReorderedRow>(bytes, "Reordered.binary");
            ReorderedContainer container = BinaryDataMgr.Instance.GetTable<ReorderedContainer>();

            Assert.IsNotNull(container);
            Assert.AreEqual(2, container.dataDic.Count);
            Assert.AreEqual("轻", container.dataDic[7].title);
            Assert.AreEqual(1.25f, container.dataDic[7].weight);
            Assert.AreEqual(9f, container.dataDic[8].weight);
        }

        [Test]
        public void LoadTableV2FromFileUsesRuntimeReader()
        {
            string directory = Path.Combine(Path.GetTempPath(), "upanda-excel-v2-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                File.WriteAllBytes(Path.Combine(directory, typeof(ItemRow).Name + ".binary"),
                    ExcelBinaryCodec.Encode(ItemSchema(), ItemRows()));

                BinaryDataMgr.Instance.InitData(directory + Path.DirectorySeparatorChar, ".binary");
                BinaryDataMgr.Instance.LoadTable<ItemContainer, ItemRow>();
                ItemContainer container = BinaryDataMgr.Instance.GetTable<ItemContainer>();

                Assert.IsNotNull(container);
                Assert.AreEqual(3, container.dataDic.Count);
                Assert.AreEqual("中文名", container.dataDic[1].name);
                Assert.IsTrue(container.dataDic[1].stackable);
                Assert.AreEqual(-2f, container.dataDic[2].price);
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [Test]
        public void LoadTableV2RejectsColumnTypeMismatch()
        {
            byte[] bytes = ExcelBinaryCodec.Encode(ItemSchema(), ItemRows());
            var error = Assert.Throws<InvalidDataException>(
                () => BinaryDataMgr.Instance.LoadTable<MismatchContainer, MismatchRow>(bytes, "Item.binary"));
            StringAssert.Contains("price", error.Message);
            StringAssert.Contains("类型", error.Message);
        }

        [Test]
        public void LoadTableV2RejectsMissingKeyField()
        {
            byte[] bytes = ExcelBinaryCodec.Encode(ItemSchema(), ItemRows());
            var error = Assert.Throws<InvalidDataException>(
                () => BinaryDataMgr.Instance.LoadTable<MissingKeyContainer, MissingKeyRow>(bytes, "Item.binary"));
            StringAssert.Contains("id", error.Message);
            StringAssert.Contains("主键", error.Message);
        }

        // ---- 自检 / 损坏检测 ----

        [Test]
        public void DecodeRejectsCrcMismatch()
        {
            byte[] bytes = ExcelBinaryCodec.Encode(ItemSchema(), ItemRows());
            bytes[bytes.Length - 1] ^= 0xFF; // 破坏末尾 CRC

            ExcelTableData table;
            string error;
            Assert.IsFalse(ExcelBinaryCodec.TryDecode(bytes, out table, out error));
            Assert.IsNull(table);
            StringAssert.Contains("CRC", error);
        }

        [Test]
        public void DecodeRejectsTruncatedFile()
        {
            byte[] bytes = ExcelBinaryCodec.Encode(ItemSchema(), ItemRows());
            var truncated = new byte[bytes.Length - 6];
            Array.Copy(bytes, truncated, truncated.Length);

            ExcelTableData table;
            string error;
            Assert.IsFalse(ExcelBinaryCodec.TryDecode(truncated, out table, out error));
            Assert.IsNotNull(error);
        }

        [Test]
        public void DecodeRejectsTrailingGarbage()
        {
            byte[] bytes = ExcelBinaryCodec.Encode(ItemSchema(), ItemRows());
            var padded = new byte[bytes.Length + 1];
            Array.Copy(bytes, padded, bytes.Length);

            ExcelTableData table;
            string error;
            Assert.IsFalse(ExcelBinaryCodec.TryDecode(padded, out table, out error));
            StringAssert.Contains("尾部", error);
        }

        [Test]
        public void DecodeRejectsLegacyBytesAndReportsIt()
        {
            // legacy v1 没有 magic，读取端应走 legacy 分支；直接按 v2 解码必须报错而不是乱读。
            var legacy = new byte[] { 1, 0, 0, 0, 1, 0, 0, 0, 105, 2, 0, 0, 0 };
            Assert.IsFalse(ExcelBinaryCodec.IsVersion2(legacy));

            ExcelTableData table;
            string error;
            Assert.IsFalse(ExcelBinaryCodec.TryDecode(legacy, out table, out error));
            StringAssert.Contains("v2", error);
            Assert.Throws<InvalidDataException>(() => ExcelBinaryCodec.Decode(legacy));
        }

        [Test]
        public void EncodeRejectsWidthMismatchAndSchemaProblems()
        {
            var schema = ItemSchema();
            var badRows = new List<object[]> { new object[] { 1, "只有两列" } };
            Assert.Throws<InvalidDataException>(() => ExcelBinaryCodec.Encode(schema, badRows));

            var noKey = new ExcelTableSchema { TableName = "NoKey", KeyColumn = "missing" };
            noKey.AddColumn("id", ExcelColumnType.Int);
            Assert.Throws<InvalidDataException>(() => ExcelBinaryCodec.Encode(noKey, new List<object[]>()));

            var duplicate = new ExcelTableSchema { TableName = "Dup", KeyColumn = "id" };
            duplicate.AddColumn("id", ExcelColumnType.Int);
            Assert.Throws<InvalidDataException>(() => duplicate.AddColumn("id", ExcelColumnType.String));

            var wrongValue = new List<object[]> { new object[] { "不是数字", "x", 1f, true } };
            Assert.Throws<InvalidDataException>(() => ExcelBinaryCodec.Encode(schema, wrongValue));
        }
    }
}
