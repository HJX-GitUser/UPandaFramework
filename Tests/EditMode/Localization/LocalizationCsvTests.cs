using System.IO;
using NUnit.Framework;
using UPandaGF;
using UPandaGF.GFEditor;

namespace UPandaGF.Tests
{
    /// <summary>本地化 CSV 解析/校验测试（编辑器工具纯逻辑层）。</summary>
    public class LocalizationCsvTests
    {
        [Test]
        public void Parse_ValidCsv_ReadsLocalesAndValues()
        {
            string csv = "key,zh-Hans,en\n" +
                         "settings.panel.title,系统设置,Settings\n" +
                         "quest.gold,\"金币, 数量\",Gold Amount\n";
            LocalizationCsvData data = LocalizationCsv.Parse(csv);

            Assert.IsFalse(data.HasErrors, string.Join("\n", data.Errors));
            CollectionAssert.AreEqual(new[] { "zh-Hans", "en" }, data.Locales);
            Assert.AreEqual(2, data.Keys.Count);
            Assert.AreEqual("金币, 数量", data.Values["quest.gold"][0]);
            Assert.AreEqual("Gold Amount", data.Values["quest.gold"][1]);
        }

        [Test]
        public void Parse_DuplicateKey_ReportsError()
        {
            string csv = "key,zh-Hans\nhello,你好\nhello,再见了\n";
            LocalizationCsvData data = LocalizationCsv.Parse(csv);
            Assert.IsTrue(data.Errors.Exists(error => error.Contains("重复")));
        }

        [Test]
        public void Parse_PlaceholderMismatch_ReportsError()
        {
            string csv = "key,zh-Hans,en\nquest.gold,金币：{0},Gold: {1}\n";
            LocalizationCsvData data = LocalizationCsv.Parse(csv);
            Assert.IsTrue(data.Errors.Exists(error => error.Contains("占位符")));
        }

        [Test]
        public void Parse_InvalidLocale_ReportsError()
        {
            string csv = "key,zh_Hans\nhello,你好\n";
            LocalizationCsvData data = LocalizationCsv.Parse(csv);
            Assert.IsTrue(data.Errors.Exists(error => error.Contains("语言码格式")));
        }

        [Test]
        public void Parse_MissingTranslation_ReportsWarning()
        {
            string csv = "key,zh-Hans,en\nhello,你好,\n";
            LocalizationCsvData data = LocalizationCsv.Parse(csv);
            Assert.IsTrue(data.Warnings.Exists(warning => warning.Contains("缺少翻译")));
            Assert.IsFalse(data.HasErrors);
        }
    }
}
