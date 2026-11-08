using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UPandaGF;
using UPandaGF.GFEditor;

namespace UPandaGF.Tests.EditMode
{
    /// <summary>
    /// P1 编辑器护栏回归：破坏性操作的路径判定（可无弹窗测试的部分）与 AB 构建目标校验。
    /// </summary>
    public class EditorGuardTests
    {
        [Test]
        public void PathsOverlapDetectsNestedAndDisjointDirectories()
        {
            string root = Path.Combine(Path.GetTempPath(), "upanda-guard");
            string child = Path.Combine(root, "child");
            string sibling = Path.Combine(Path.GetTempPath(), "upanda-guard-2");

            Assert.IsTrue(EditorDestructiveGuard.PathsOverlap(root, child));
            Assert.IsTrue(EditorDestructiveGuard.PathsOverlap(child, root), "包含关系应双向判定");
            Assert.IsFalse(EditorDestructiveGuard.PathsOverlap(root, sibling));
            Assert.IsTrue(EditorDestructiveGuard.IsWithin(child, root));
            Assert.IsFalse(EditorDestructiveGuard.IsWithin(root, child));
        }

        [Test]
        public void IsInsideAssetsRejectsOutsidePathsAndSimilarPrefixes()
        {
            Assert.IsTrue(EditorDestructiveGuard.IsInsideAssets(Path.Combine(Application.dataPath, "Generated")));
            Assert.IsFalse(EditorDestructiveGuard.IsInsideAssets(Path.GetTempPath()));
            // 仅前缀相同但不在 Assets 目录内的路径必须拒绝
            Assert.IsFalse(EditorDestructiveGuard.IsInsideAssets(Application.dataPath + "X"));
        }

        [Test]
        public void ConfirmOverwriteAllowsNewFileWithoutDialog()
        {
            string path = Path.Combine(Path.GetTempPath(), "upanda-guard-" + Guid.NewGuid().ToString("N") + ".txt");
            Assert.IsTrue(EditorDestructiveGuard.ConfirmOverwrite("测试", path));
        }

        [Test]
        public void BundleTargetNameMatchesRuntimeConvention()
        {
            Assert.AreEqual("StandaloneWindows64", FrameworkBuildPipeline.BundleTargetName(BuildTarget.StandaloneWindows64));
            Assert.AreEqual("StandaloneWindows64", FrameworkBuildPipeline.BundleTargetName(BuildTarget.StandaloneWindows));
            Assert.AreEqual("Android", FrameworkBuildPipeline.BundleTargetName(BuildTarget.Android));
            Assert.AreEqual("iOS", FrameworkBuildPipeline.BundleTargetName(BuildTarget.iOS));
            Assert.AreEqual("WebGL", FrameworkBuildPipeline.BundleTargetName(BuildTarget.WebGL));
            Assert.AreEqual("StandaloneOSX", FrameworkBuildPipeline.BundleTargetName(BuildTarget.StandaloneOSX));
            Assert.AreEqual("StandaloneLinux64", FrameworkBuildPipeline.BundleTargetName(BuildTarget.StandaloneLinux64));
        }

        [Test]
        public void ValidateAssetBundleOutputSkipsEditorModeAndRemoteMainBundle()
        {
            var editor = new UPGameRootConfig { method = AssetLoaddingMethod.Editor };
            Assert.DoesNotThrow(() => FrameworkBuildPipeline.ValidateAssetBundleOutput(editor));
            Assert.DoesNotThrow(() => FrameworkBuildPipeline.ValidateAssetBundleOutput(null));

            var remote = new UPGameRootConfig
            {
                method = AssetLoaddingMethod.AssetBundle,
                LoadAssetPath = "P1Missing_" + Guid.NewGuid().ToString("N") + "/",
                AssetAESConfig = new AssetBundleClassificationWindowConfig { mainBundleLoadPath = ABLoadPath.RemotePath }
            };
            Assert.DoesNotThrow(() => FrameworkBuildPipeline.ValidateAssetBundleOutput(remote));
        }

        [Test]
        public void ValidateAssetBundleOutputRejectsMissingDirectory()
        {
            var config = new UPGameRootConfig
            {
                method = AssetLoaddingMethod.AssetBundle,
                LoadAssetPath = "P1Missing_" + Guid.NewGuid().ToString("N") + "/"
            };
            var error = Assert.Throws<InvalidOperationException>(() => FrameworkBuildPipeline.ValidateAssetBundleOutput(config));
            StringAssert.Contains("不存在", error.Message);
        }

        [Test]
        public void ValidateAssetBundleOutputRejectsDirectoryWithoutManifest()
        {
            string relative = "P1EmptyBundle_" + Guid.NewGuid().ToString("N");
            string directory = Path.Combine(Application.streamingAssetsPath, relative);
            Directory.CreateDirectory(directory);
            try
            {
                var config = new UPGameRootConfig
                {
                    method = AssetLoaddingMethod.AssetBundle,
                    LoadAssetPath = relative + "/"
                };
                var error = Assert.Throws<InvalidOperationException>(() => FrameworkBuildPipeline.ValidateAssetBundleOutput(config));
                StringAssert.Contains(".manifest", error.Message);
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }
    }
}
