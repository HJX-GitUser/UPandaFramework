using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using NUnit.Framework;
using UPandaGF.GFEditor;

namespace UPandaGF.Tests.EditMode
{
    /// <summary>
    /// P1 Excel 生成端回归：DataTable → v2 字节 → 运行端 BinaryDataMgr 的完整往返，
    /// 以及主键识别、类型/单元格错误提示。生成端逻辑已抽到 <see cref="ExcelTableBuilder"/>，
    /// 因此不需要真实 .xlsx 也能在 EditMode 覆盖。
    /// </summary>
    public class ExcelGenerationPipelineTests
    {
        [Serializable]
        public class WeaponRow
        {
            public int id;
            public string name;
            public float damage;
            public bool rare;
        }

        public class WeaponContainer
        {
            public Dictionary<int, WeaponRow> dataDic = new Dictionary<int, WeaponRow>();
        }

        /// <summary>模拟 Excel 工作表：4 行表头 + 3 行数据（含中文、空串、负浮点、bool 文本）。</summary>
        private static DataTable BuildWeaponTable()
        {
            var table = new DataTable("Weapon");
            foreach (string column in new[] { "id", "name", "damage", "rare" })
                table.Columns.Add(column, typeof(string));

            table.Rows.Add("id", "name", "damage", "rare");      // 0 字段名
            table.Rows.Add("int", "string", "float", "bool");    // 1 类型
            table.Rows.Add("key", "", "", "");                   // 2 主键标记
            table.Rows.Add("编号", "名称", "伤害", "稀有");        // 3 注释
            table.Rows.Add("1", "长剑", "12.5", "TRUE");         // 4
            table.Rows.Add("2", "", "3", "0");                   // 5
            table.Rows.Add("3", "匕首", "-1", "false");          // 6
            return table;
        }

        [Test]
        public void PipelineEncodesAndDecodesTable()
        {
            ExcelTableSchema schema;
            int rowCount;
            byte[] bytes = ExcelTableBuilder.Encode(BuildWeaponTable(), out schema, out rowCount);

            Assert.AreEqual("Weapon", schema.TableName);
            Assert.AreEqual("id", schema.KeyColumn);
            Assert.AreEqual(4, schema.ColumnCount);
            Assert.AreEqual(3, rowCount);
            Assert.IsTrue(ExcelBinaryFormat.HasMagic(bytes));

            ExcelTableData data = ExcelBinaryCodec.Decode(bytes);
            Assert.AreEqual(3, data.RowCount);
            Assert.AreEqual(1, data.Rows[0][0]);
            Assert.AreEqual("长剑", data.Rows[0][1]);
            Assert.AreEqual(12.5f, (float)data.Rows[0][2]);
            Assert.AreEqual(true, data.Rows[0][3]);
            Assert.AreEqual(string.Empty, data.Rows[1][1], "空单元格应生成空字符串");
            Assert.AreEqual(false, data.Rows[1][3], "0 应解析为 false");
            Assert.AreEqual(-1f, (float)data.Rows[2][2], "负浮点必须保留符号");
        }

        [Test]
        public void GeneratedBytesLoadThroughRuntimeManager()
        {
            ExcelTableSchema schema;
            int rowCount;
            byte[] bytes = ExcelTableBuilder.Encode(BuildWeaponTable(), out schema, out rowCount);

            BinaryDataMgr.Instance.LoadTable<WeaponContainer, WeaponRow>(bytes, "Weapon.binary");
            WeaponContainer container = BinaryDataMgr.Instance.GetTable<WeaponContainer>();

            Assert.IsNotNull(container);
            Assert.AreEqual(3, container.dataDic.Count);
            Assert.AreEqual("长剑", container.dataDic[1].name);
            Assert.AreEqual(12.5f, container.dataDic[1].damage);
            Assert.IsTrue(container.dataDic[1].rare);
            Assert.AreEqual(string.Empty, container.dataDic[2].name);
            Assert.IsFalse(container.dataDic[2].rare);
            Assert.AreEqual(-1f, container.dataDic[3].damage);
        }

        [Test]
        public void KeyColumnFallsBackToFirstColumnWithoutMarker()
        {
            var table = new DataTable("NoMarker");
            table.Columns.Add("code", typeof(string));
            table.Columns.Add("value", typeof(string));
            table.Rows.Add("code", "value");
            table.Rows.Add("string", "int");
            table.Rows.Add("", "");          // 没有 key 标记
            table.Rows.Add("", "");
            table.Rows.Add("alpha", "1");

            ExcelTableSchema schema = ExcelTableBuilder.BuildSchema(table);
            Assert.AreEqual("code", schema.KeyColumn);
        }

        [Test]
        public void InvalidTypeRowAndBadCellAreRejected()
        {
            DataTable badType = BuildWeaponTable();
            badType.Rows[1]["damage"] = "double";
            Assert.Throws<InvalidDataException>(() => ExcelTableBuilder.BuildSchema(badType));

            DataTable badCell = BuildWeaponTable();
            badCell.Rows[4]["damage"] = "不是数字";
            ExcelTableSchema schema;
            int rowCount;
            Assert.Throws<InvalidDataException>(() => ExcelTableBuilder.Encode(badCell, out schema, out rowCount));
        }

        [Test]
        public void MissingColumnNameIsRejected()
        {
            var table = new DataTable("NoName");
            table.Columns.Add("a", typeof(string));
            table.Columns.Add("b", typeof(string));
            table.Rows.Add("", "值");
            table.Rows.Add("int", "int");
            Assert.Throws<InvalidDataException>(() => ExcelTableBuilder.BuildSchema(table));
        }
    }
}
