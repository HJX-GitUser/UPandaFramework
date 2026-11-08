using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UPandaGF;

namespace UPandaGF.Tests.EditMode
{
    /// <summary>
    /// 场景资源准备者的装配规则：Editor 直读模式必须没有 provider，
    /// 场景直接从 Build Settings 加载，绝不经过 AB / 资源清单。
    /// </summary>
    public class SceneServiceAssetProviderTests
    {
        private sealed class RecordingProvider : ISceneAssetProvider
        {
            public int Calls { get; private set; }
            public Task<IDisposable> PrepareSceneAsync(string path, CancellationToken token)
            {
                Calls++;
                return Task.FromResult<IDisposable>(null);
            }
        }

        [Test]
        public void EditorMode_DropsAssetProvider()
        {
            var provider = new RecordingProvider();
            Assert.IsNull(SceneService.SelectAssetProvider(AssetLoaddingMethod.Editor, provider),
                "Editor 模式必须不装配 provider，否则加载前会去准备 AB。");
            Assert.AreEqual(0, provider.Calls);
        }

        [Test]
        public void AssetBundleMode_KeepsAssetProvider()
        {
            var provider = new RecordingProvider();
            Assert.AreSame(provider, SceneService.SelectAssetProvider(AssetLoaddingMethod.Assetbundles, provider));
        }

        [Test]
        public void EditorMode_ToleratesNullProvider()
        {
            Assert.IsNull(SceneService.SelectAssetProvider(AssetLoaddingMethod.Editor, null));
        }
    }
}
