using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UPandaGF;

namespace UPandaGF.Tests
{
    /// <summary>UI 相机在 Built-in / SRP 下的适配与 UIManager 接入。</summary>
    public class UICameraPipelineTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--)
                if (objects[i] != null) UnityEngine.Object.DestroyImmediate(objects[i]);
            objects.Clear();
        }

        private GameObject Create(string name, params Type[] components)
        {
            GameObject go = new GameObject(name, components);
            objects.Add(go);
            return go;
        }

        [Test]
        public void DetectReturnsDefinedPipelineKind()
        {
            UIRenderPipelineKind kind = UICameraPipeline.Detect();
            Assert.IsTrue(Enum.IsDefined(typeof(UIRenderPipelineKind), kind), kind.ToString());
        }

        [Test]
        public void AdaptBuiltInRaisesDepthButKeepsCameraSettings()
        {
            Camera camera = Create("UICamera", typeof(Camera)).GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.depth = 0;

            UICameraPipeline.Adapt(camera, UIRenderPipelineKind.BuiltIn);

            Assert.AreEqual(CameraClearFlags.SolidColor, camera.clearFlags);
            Assert.GreaterOrEqual(camera.depth, UICameraPipeline.DefaultDepth);
        }

        [Test]
        public void AdaptIsIdempotent()
        {
            Camera camera = Create("UICamera", typeof(Camera)).GetComponent<Camera>();
            UICameraPipeline.Adapt(camera, UIRenderPipelineKind.BuiltIn);
            int components = camera.gameObject.GetComponents<Component>().Length;
            float depth = camera.depth;

            UICameraPipeline.Adapt(camera, UIRenderPipelineKind.BuiltIn);

            Assert.AreEqual(depth, camera.depth, 0.001f);
            Assert.AreEqual(components, camera.gameObject.GetComponents<Component>().Length);
        }

        [Test]
        public void ManagerCreatesConfiguredUiCameraAndCanvas()
        {
            GameObject uiObject = Create("UI");
            UIManager manager = uiObject.AddComponent<UIManager>();
            try
            {
                manager.Configure(new NoopAssets());
                manager.Init();
                manager.RefreshCameraStack();

                Assert.IsNotNull(manager.uiCamera);
                Assert.AreEqual(CameraClearFlags.Depth, manager.uiCamera.clearFlags);
                Assert.AreEqual(1 << LayerMask.NameToLayer("UI"), manager.uiCamera.cullingMask);
                Assert.GreaterOrEqual(manager.uiCamera.depth, UICameraPipeline.DefaultDepth);

                Assert.IsNotNull(manager.uiCanvas);
                Assert.AreEqual(RenderMode.ScreenSpaceCamera, manager.uiCanvas.renderMode);
                Assert.AreSame(manager.uiCamera, manager.uiCanvas.worldCamera);
            }
            finally
            {
                manager.Shutdown();
            }
        }

        [Test]
        public void ResetCameraAdaptsRegisteredCamera()
        {
            GameObject uiObject = Create("UI");
            UIManager manager = uiObject.AddComponent<UIManager>();
            Camera sceneCamera = Create("SceneUICamera", typeof(Camera)).GetComponent<Camera>();
            try
            {
                manager.Configure(new NoopAssets());
                manager.Init();
                manager.ResetCamera(sceneCamera);

                Assert.AreSame(sceneCamera, manager.uiCamera);
                Assert.AreSame(sceneCamera, manager.uiCanvas.worldCamera);
                Assert.GreaterOrEqual(sceneCamera.depth, UICameraPipeline.DefaultDepth);
            }
            finally
            {
                manager.Shutdown();
            }
        }

        private sealed class NoopAssets : IAssetService
        {
            public Task<AssetLease<T>> AcquireAsync<T>(string path,
                AssetLoadMethod method = AssetLoadMethod.Resources,
                CancellationToken token = default) where T : UnityEngine.Object
            {
                return Task.FromResult<AssetLease<T>>(null);
            }
        }
    }
}
