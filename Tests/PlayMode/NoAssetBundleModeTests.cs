using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace UPandaGF.Tests
{
    /// <summary>仅使用 Resources 目录时的启动与加载回归。</summary>
    public class NoAssetBundleModeTests
    {
        [UnityTest]
        public IEnumerator AssetsLoaderInitializesAndLoadsResourcesWithoutManifest()
        {
            var go = new GameObject("ResourcesAssetsLoader");
            var loader = go.AddComponent<AssetsLoader>();
            try
            {
                Task init = loader.Init(AssetLoaddingMethod.NoAssetBundle, "", "", null);
                while (!init.IsCompleted) yield return null;
                Assert.IsFalse(init.IsFaulted, init.Exception?.ToString());
                Assert.IsTrue(loader.IsInitialized, "NoAssetBundle 模式初始化后应立即可用，不依赖 AB 清单/主包。");
                Assert.IsNull(loader.BundleLoader, "NoAssetBundle 模式不应创建 ABLoadMgr。");
                Assert.IsNull(SceneService.SelectAssetProvider(AssetLoaddingMethod.NoAssetBundle, loader),
                    "NoAssetBundle 模式的场景应直接走 Build Settings，不装配 AB provider。");

                Task<AssetLease<TextAsset>> load = loader.AcquireAsync<TextAsset>("LogConsoleResourcesProbe");
                while (!load.IsCompleted) yield return null;
                Assert.IsFalse(load.IsFaulted, load.Exception?.ToString());
                Assert.IsNotNull(load.Result, "Resources 探针资源未加载到。");
                Assert.AreEqual("resources-probe", load.Result.Asset.text.Trim());
                load.Result.Dispose();
            }
            finally
            {
                loader.Shutdown();
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void NoAssetBundleConfigDoesNotRequireBundleSettings()
        {
            var config = new UPGameRootConfig
            {
                method = AssetLoaddingMethod.NoAssetBundle,
                LoadAssetPath = "",
                remoteURL = "",
                enableAssetUpdate = false,
            };
            Assert.IsEmpty(UPGameRootConfigFile.Validate(config, false), "NoAssetBundle 模式应允许空的 AB 路径/URL。");

            config.enableAssetUpdate = true;
            Assert.IsNotEmpty(UPGameRootConfigFile.Validate(config, false), "NoAssetBundle 模式不应允许资源热更。");
        }

        [UnityTest]
        public IEnumerator GameRootStartsInNoAssetBundleMode()
        {
            string dataDirectory = Path.Combine(Application.streamingAssetsPath, "Data");
            Directory.CreateDirectory(dataDirectory);
            string logConfigPath = Path.Combine(dataDirectory, "LogConfig.json");
            byte[] originalLogConfig = File.Exists(logConfigPath) ? File.ReadAllBytes(logConfigPath) : null;
            File.WriteAllText(logConfigPath, JsonUtility.ToJson(new LogConfig()));

            var go = new GameObject("ResourcesGameRoot");
            go.SetActive(false);
            var root = go.AddComponent<UPGameRoot>();
            root.SetConfig(new UPGameRootConfig
            {
                method = AssetLoaddingMethod.NoAssetBundle,
                enableAssetUpdate = false,
                EnableDebugModel = false,
            });
            go.SetActive(true);

            Task start = root.WaitUntilReadyAsync();
            while (!start.IsCompleted) yield return null;
            Exception failure = null;
            try
            {
                Assert.IsFalse(start.IsFaulted, start.Exception?.ToString());
                Assert.IsTrue(root.IsInited, "NoAssetBundle 模式启动应到达 Ready。");
                Assert.IsTrue(((AssetsLoader)root.GetAssetsLoader()).IsInitialized);
                Assert.AreEqual(AssetLoaddingMethod.NoAssetBundle, root.Config.method);
            }
            catch (Exception e) { failure = e; }

            Task shutdown = root.ShutdownAsync();
            while (!shutdown.IsCompleted) yield return null;
            UnityEngine.Object.DestroyImmediate(go);
            if (originalLogConfig == null) File.Delete(logConfigPath);
            else File.WriteAllBytes(logConfigPath, originalLogConfig);
            if (failure != null) throw failure;
        }
    }
}
