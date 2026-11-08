using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UPandaGF;

namespace UPandaGF.Tests.EditMode
{
    /// <summary>
    /// Excel 表运行时目录的回归：Android/iOS 的系统根目录只读，
    /// 历史配置里的 "/Data/BinaryData/" 必须解析到 persistentDataPath 下，不能再当绝对路径。
    /// </summary>
    public class BinaryDataMgrInitPathTests
    {
        [Test]
        public void LeadingSlashSavePathStaysUnderPersistentDataPath()
        {
            string resolved = BinaryDataMgrInit.ResolveSaveDirectory("/Data/BinaryData/");
            Assert.AreEqual(
                Normalize(Path.Combine(Application.persistentDataPath, "Data", "BinaryData")),
                Normalize(resolved));
        }

        [Test]
        public void RelativeAndLeadingSlashFormsResolveToSameDirectory()
        {
            Assert.AreEqual(
                Normalize(BinaryDataMgrInit.ResolveSaveDirectory("Data/BinaryData")),
                Normalize(BinaryDataMgrInit.ResolveSaveDirectory("/Data/BinaryData")));
        }

        [Test]
        public void EmptySavePathFallsBackToPersistentBinaryDirectory()
        {
            Assert.AreEqual(
                Normalize(ExcelBinaryFormat.PersistentDirectory),
                Normalize(BinaryDataMgrInit.ResolveSaveDirectory("  ")));
        }

        [Test]
        public void ResolvedPersistentPathIsIdempotent()
        {
            string first = BinaryDataMgrInit.ResolveSaveDirectory("/Data/BinaryData/");
            Assert.AreEqual(Normalize(first), Normalize(BinaryDataMgrInit.ResolveSaveDirectory(first)));
        }

        [Test]
        public void DotDotSegmentIsRejected()
        {
            Assert.Throws<ArgumentException>(() => BinaryDataMgrInit.ResolveSaveDirectory("../outside"));
        }

        private static string Normalize(string path)
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
