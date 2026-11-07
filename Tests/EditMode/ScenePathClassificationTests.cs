using NUnit.Framework;
using UPandaGF;

namespace UPandaGF.Tests.EditMode
{
    /// <summary>
    /// 场景归属判定：决定 SceneService 在加载前是否需要准备 AB。
    /// <para>核心回归点：<b>清单优先</b> —— 清单里登记的场景即使 Unity 也能直接加载，也必须走 AB；
    /// 反过来判断会跳过资源准备，进场景后资源缺失。</para>
    /// </summary>
    public class ScenePathClassificationTests
    {
        private static readonly string[] Manifest =
        {
            "Assets/Scenes/GameScene.unity",
            "Assets/Scenes/Login.unity",
            "Assets/Prefabs/Widget.prefab",
        };

        private static bool NeverLoadable(string path) { return false; }
        private static bool AlwaysLoadable(string path) { return true; }

        [Test]
        public void FullPathInManifest_IsManifest()
        {
            string resolved;
            Assert.AreEqual(ScenePathKind.Manifest,
                AssetsLoader.ClassifyScenePath("Assets/Scenes/GameScene.unity", Manifest, NeverLoadable, out resolved));
            Assert.AreEqual("Assets/Scenes/GameScene.unity", resolved);
        }

        [Test]
        public void ShortNameInManifest_ResolvesToFullPath()
        {
            string resolved;
            Assert.AreEqual(ScenePathKind.Manifest, AssetsLoader.ClassifyScenePath("Login", Manifest, NeverLoadable, out resolved));
            Assert.AreEqual("Assets/Scenes/Login.unity", resolved);
        }

        [Test]
        public void ManifestWinsOverDirectlyLoadable()
        {
            // Unity 认为可直接加载（例如同时在 Build Settings 里），也不能跳过 AB 准备
            string resolved;
            Assert.AreEqual(ScenePathKind.Manifest, AssetsLoader.ClassifyScenePath("GameScene", Manifest, AlwaysLoadable, out resolved));
            Assert.AreEqual("Assets/Scenes/GameScene.unity", resolved);
        }

        [Test]
        public void DuplicateShortName_IsAmbiguous()
        {
            var keys = new[] { "Assets/A/Dup.unity", "Assets/B/Dup.unity" };
            string resolved;
            Assert.AreEqual(ScenePathKind.Ambiguous, AssetsLoader.ClassifyScenePath("Dup", keys, AlwaysLoadable, out resolved));
            Assert.IsNull(resolved);
        }

        [Test]
        public void SceneOutsideManifest_ButLoadable_IsBuiltin()
        {
            string resolved;
            Assert.AreEqual(ScenePathKind.Builtin,
                AssetsLoader.ClassifyScenePath("Assets/Scenes/Bootstrap.unity", Manifest, AlwaysLoadable, out resolved));
            Assert.IsNull(resolved, "随包场景不需要准备 AB，不应返回清单路径。");
        }

        [Test]
        public void SceneOutsideManifest_NotLoadable_IsMissing()
        {
            string resolved;
            Assert.AreEqual(ScenePathKind.Missing,
                AssetsLoader.ClassifyScenePath("Assets/Scenes/Typo.unity", Manifest, NeverLoadable, out resolved));
            Assert.IsNull(resolved);
        }

        [Test]
        public void NonSceneAssetWithSameName_IsNotASceneMatch()
        {
            // 清单里只有同名 .prefab：不能当成场景命中，否则会去准备一个不含场景的包
            string resolved;
            Assert.AreEqual(ScenePathKind.Missing, AssetsLoader.ClassifyScenePath("Widget", Manifest, NeverLoadable, out resolved));
            Assert.IsNull(resolved);
        }
    }
}
