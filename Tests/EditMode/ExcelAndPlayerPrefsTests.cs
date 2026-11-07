using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UPandaGF;

namespace UPandaGF.Tests.EditMode
{
    /// <summary>P0 数据管线的 EditMode 回归：Excel 二进制布局与 PlayerPrefs 反射适配。</summary>
    public class ExcelAndPlayerPrefsTests
    {
        [Serializable]
        public class Row
        {
            public int id;
            public bool flag;
            public string name;
            public float value;
        }

        public class Container
        {
            public Dictionary<int, Row> dataDic = new Dictionary<int, Row>();
        }

        public enum SampleMode { Idle = 1, Running = 2 }

        [Serializable]
        public class Sample
        {
            public int hp;
            public string label;
            public bool flag;
            public int[] values;
            public SampleMode mode;
        }

        [Test]
        public void BinaryTableRoundTripsBoolStringAndFloat()
        {
            string directory = Path.Combine(Path.GetTempPath(), "upanda-excel-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string path = Path.Combine(directory, typeof(Row).Name + ".binary");
                using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
                using (var writer = new BinaryWriter(stream, Encoding.UTF8))
                {
                    writer.Write(2);
                    byte[] key = Encoding.UTF8.GetBytes("id");
                    writer.Write(key.Length);
                    writer.Write(key);

                    WriteRow(writer, new Row { id = 1, flag = true, name = "中文", value = 1.5f });
                    WriteRow(writer, new Row { id = 2, flag = false, name = string.Empty, value = -2f });
                }

                BinaryDataMgr.Instance.InitData(directory + Path.DirectorySeparatorChar, ".binary");
                BinaryDataMgr.Instance.LoadTable<Container, Row>();
                var table = BinaryDataMgr.Instance.GetTable<Container>();

                Assert.IsNotNull(table);
                Assert.AreEqual(2, table.dataDic.Count);
                Assert.IsTrue(table.dataDic[1].flag);
                Assert.AreEqual("中文", table.dataDic[1].name);
                Assert.AreEqual(1.5f, table.dataDic[1].value);
                Assert.IsFalse(table.dataDic[2].flag);
                Assert.AreEqual(string.Empty, table.dataDic[2].name);
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        private static void WriteRow(BinaryWriter writer, Row row)
        {
            writer.Write(row.id);
            // 与 ExcelBinaryFormat.BoolByteSize 对齐：bool 固定 4 字节。
            writer.Write(row.flag ? 1 : 0);
            byte[] text = Encoding.UTF8.GetBytes(row.name ?? string.Empty);
            writer.Write(text.Length);
            writer.Write(text);
            writer.Write(row.value);
        }

        [Test]
        public void PlayerPrefsRoundTripsNullArrayAndEnum()
        {
            string key = "upanda_pp_" + Guid.NewGuid().ToString("N");
            var data = new Sample
            {
                hp = 7,
                label = null,
                flag = true,
                values = new[] { 3, 5, 8 },
                mode = SampleMode.Running
            };

            PlayerPrefsDataMgr.Instance.SaveData(data, key);
            var loaded = PlayerPrefsDataMgr.Instance.LoadData(typeof(Sample), key) as Sample;

            Assert.IsNotNull(loaded);
            Assert.AreEqual(7, loaded.hp);
            Assert.AreEqual(string.Empty, loaded.label); // null 字符串读回为空串
            Assert.IsTrue(loaded.flag);
            CollectionAssert.AreEqual(new[] { 3, 5, 8 }, loaded.values);
            Assert.AreEqual(SampleMode.Running, loaded.mode);

            // 清理本用例生成的键，避免污染宿主的 PlayerPrefs
            foreach (string field in new[] { "hp", "label", "flag", "values", "mode" })
                PlayerPrefs.DeleteKey(key + "_Sample_" + FieldTypeName(field) + "_" + field);
            for (int i = 0; i < 3; i++) PlayerPrefs.DeleteKey(key + "_Sample_Int32[]_values" + i);
            PlayerPrefs.Save();
        }

        private static string FieldTypeName(string field)
        {
            switch (field)
            {
                case "hp": return "Int32";
                case "label": return "String";
                case "flag": return "Boolean";
                case "values": return "Int32[]";
                default: return "SampleMode";
            }
        }
    }
}
