using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.TestTools;
using UPandaGF;

namespace UPandaGF.Tests
{
    /// <summary>
    /// 设置面板 PlayMode 冒烟：实例化包内预制体、注入最小服务集合、执行 OnOpen 真正搭建布局，
    /// 验证运行时 UI 构建不会抛异常且能生成设置行。不依赖 UIManager/Canvas 相机。
    /// </summary>
    public class SettingsPanelSmokeTests
    {
        private string directory;
        private SettingsService settings;
        private GameObject root;
        private GameObject canvasRoot;
        private GameObject eventSystemRoot;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "upandagf-settings-panel", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            settings = new SettingsService(new SettingsStore(Path.Combine(directory, "settings.json")),
                new SettingsRegistry(SettingsPlatform.Desktop | SettingsPlatform.Editor),
                new ISettingsApplier[0], null, null);
            settings.LoadAsync().GetAwaiter().GetResult();
        }

        [TearDown]
        public void TearDown()
        {
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            if (canvasRoot != null) UnityEngine.Object.DestroyImmediate(canvasRoot);
            if (eventSystemRoot != null) UnityEngine.Object.DestroyImmediate(eventSystemRoot);
            try { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
            catch (Exception) { }
        }

        [Test]
        public void Panel_Open_BuildsRows()
        {
            GameObject prefab = Resources.Load<GameObject>("UI/SettingsPanel");
            Assert.IsNotNull(prefab, "缺少 Resources/UI/SettingsPanel.prefab。");

            root = UnityEngine.Object.Instantiate(prefab);
            SettingsPanel panel = root.GetComponent<SettingsPanel>();
            Assert.IsNotNull(panel);

            var services = new FakeServices { Settings = settings, Input = new FakeInput() };
            MethodInfo attach = typeof(BasePanel).GetMethod("AttachServices", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(attach);
            attach.Invoke(panel, new object[] { services });

            Assert.DoesNotThrow(() => panel.OnOpen(null));
            UISettingRow[] rows = panel.GetComponentsInChildren<UISettingRow>(true);
            Assert.Greater(rows.Length, 0, "面板没有生成任何设置行。");
            Assert.IsNotNull(panel.GetComponent<CanvasGroup>());
            Assert.IsNotNull(panel.GetComponent<UIFocusNavigator>(), "设置面板应挂焦点导航器。");
            Assert.IsNotNull(panel.GetComponent<UIFocusInputBinder>(), "设置面板应挂焦点输入绑定。");

            panel.OnClose();
        }

        [Test]
        public void Panel_OpenDisplay_BuildsDropdownRows()
        {
            GameObject prefab = Resources.Load<GameObject>("UI/SettingsPanel");
            Assert.IsNotNull(prefab, "缺少 Resources/UI/SettingsPanel.prefab。");

            root = UnityEngine.Object.Instantiate(prefab);
            SettingsPanel panel = root.GetComponent<SettingsPanel>();
            var services = new FakeServices { Settings = settings, Input = new FakeInput() };
            MethodInfo attach = typeof(BasePanel).GetMethod("AttachServices", BindingFlags.Instance | BindingFlags.NonPublic);
            attach.Invoke(panel, new object[] { services });

            Assert.DoesNotThrow(() => panel.OnOpen(SettingsCategory.Display));
            UISettingRow[] rows = panel.GetComponentsInChildren<UISettingRow>(true);
            Assert.Greater(rows.Length, 0);
            Assert.IsNotNull(panel.GetComponent<UIFocusNavigator>());
            panel.OnClose();
        }

        [UnityTest]
        public IEnumerator Panel_Graphics_EnumDropdownWritesSetting()
        {
            GameObject prefab = Resources.Load<GameObject>("UI/SettingsPanel");
            Assert.IsNotNull(prefab);
            root = UnityEngine.Object.Instantiate(prefab);
            SettingsPanel panel = root.GetComponent<SettingsPanel>();
            var services = new FakeServices { Settings = settings, Input = new FakeInput() };
            MethodInfo attach = typeof(BasePanel).GetMethod("AttachServices", BindingFlags.Instance | BindingFlags.NonPublic);
            attach.Invoke(panel, new object[] { services });

            Assert.DoesNotThrow(() => panel.OnOpen(SettingsCategory.Graphics));
            UISettingDropdownRow presetRow = null;
            UISettingDropdownRow[] dropdownRows = panel.GetComponentsInChildren<UISettingDropdownRow>(true);
            for (int i = 0; i < dropdownRows.Length; i++)
            {
                if (dropdownRows[i].Item != null && dropdownRows[i].Item.Key == "graphics.preset")
                {
                    presetRow = dropdownRows[i];
                    break;
                }
            }
            Assert.IsNotNull(presetRow, "画质预设应使用下拉行。");
            Dropdown dropdown = presetRow.GetComponentInChildren<Dropdown>(true);
            Assert.IsNotNull(dropdown);
            dropdown.value = (int)QualityPreset.Ultra;
            Assert.AreEqual(QualityPreset.Ultra, settings.Pending.graphics.preset);

            // 真实 Canvas 下打开列表，验证模板不会被判为无效或被遮罩裁掉。
            canvasRoot = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasRoot.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            root.transform.SetParent(canvasRoot.transform, false);
            eventSystemRoot = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            yield return null; // 等 Dropdown.Start 初始化 AlphaTweenRunner

            dropdown.Show();
            Transform list = panel.transform.Find("DropdownOverlay/Dropdown List");
            Assert.IsNotNull(list, "下拉列表未创建，模板可能无效。");
            Assert.IsTrue(list.gameObject.activeInHierarchy);
            RectTransform listRect = (RectTransform)list;
            Assert.Greater(listRect.rect.height, 0f, "下拉列表高度不能为负，否则只会显示一条细线。");
            yield return null;
            RectMask2D viewportMask = panel.GetComponentInChildren<RectMask2D>(true);
            Assert.IsNotNull(viewportMask);
            Assert.IsTrue(viewportMask.enabled, "列表已在 overlay 上，不应再关闭 viewport 遮罩。");
            dropdown.Hide();
            panel.OnClose();
        }

        [UnityTest]
        public IEnumerator Panel_Graphics_DropdownLayoutIsVisible()
        {
            GameObject prefab = Resources.Load<GameObject>("UI/SettingsPanel");
            Assert.IsNotNull(prefab);
            canvasRoot = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasRoot.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            root = UnityEngine.Object.Instantiate(prefab, canvasRoot.transform);
            SettingsPanel panel = root.GetComponent<SettingsPanel>();
            var services = new FakeServices { Settings = settings, Input = new FakeInput() };
            MethodInfo attach = typeof(BasePanel).GetMethod("AttachServices", BindingFlags.Instance | BindingFlags.NonPublic);
            attach.Invoke(panel, new object[] { services });
            panel.OnOpen(SettingsCategory.Graphics);
            yield return null;

            UISettingDropdownRow presetRow = null;
            UISettingDropdownRow[] rows = panel.GetComponentsInChildren<UISettingDropdownRow>(true);
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i].Item != null && rows[i].Item.Key == "graphics.preset") { presetRow = rows[i]; break; }
            }
            Assert.IsNotNull(presetRow);
            Dropdown dropdown = presetRow.GetComponentInChildren<Dropdown>(true);
            Assert.IsNotNull(dropdown);
            RectTransform dropdownRect = (RectTransform)dropdown.transform;
            Assert.Greater(dropdownRect.rect.width, 100f);
            Assert.Greater(dropdownRect.rect.height, 20f);
            panel.OnClose();
        }

        [UnityTest]
        public IEnumerator Panel_Graphics_DropdownList_RendersInScreenSpaceCameraCanvas()
        {
            var cameraObject = new GameObject("UICamera", typeof(Camera));
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.orthographic = true;
            canvasRoot = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;
            root = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("UI/SettingsPanel"), canvasRoot.transform);
            SettingsPanel panel = root.GetComponent<SettingsPanel>();
            var services = new FakeServices { Settings = settings, Input = new FakeInput() };
            MethodInfo attach = typeof(BasePanel).GetMethod("AttachServices", BindingFlags.Instance | BindingFlags.NonPublic);
            attach.Invoke(panel, new object[] { services });
            panel.OnOpen(SettingsCategory.Graphics);
            yield return null;

            UISettingDropdownRow presetRow = null;
            UISettingDropdownRow[] rows = panel.GetComponentsInChildren<UISettingDropdownRow>(true);
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i].Item != null && rows[i].Item.Key == "graphics.preset") { presetRow = rows[i]; break; }
            }
            Assert.IsNotNull(presetRow);
            Dropdown dropdown = presetRow.GetComponentInChildren<Dropdown>(true);
            eventSystemRoot = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            dropdown.Show();
            yield return null;
            Transform list = panel.transform.Find("DropdownOverlay/Dropdown List");
            Assert.IsNotNull(list);
            Canvas listCanvas = list.GetComponent<Canvas>();
            Assert.IsNotNull(listCanvas);
            Debug.Log("[DropdownCanvasTest] root=" + canvas.renderMode + " list=" + listCanvas.renderMode +
                      " cam=" + (listCanvas.worldCamera != null ? listCanvas.worldCamera.name : "null") +
                      " plane=" + listCanvas.planeDistance + " order=" + listCanvas.sortingOrder);
            Assert.IsNotNull(listCanvas.worldCamera, "嵌套列表 Canvas 没拿到 UI 相机，Game 视图不会渲染。");
            dropdown.Hide();
            yield return null;
            RectMask2D viewportMask = panel.GetComponentInChildren<RectMask2D>(true);
            if (viewportMask != null) Assert.IsTrue(viewportMask.enabled, "关闭下拉后应保持 viewport 遮罩。");
            panel.OnClose();
            UnityEngine.Object.DestroyImmediate(cameraObject);
        }

        private sealed class FakeServices : IGameServices
        {
            public ISettingsService Settings { get; set; }
            public ILocalizationService Localization => null;
            public IInputService Input { get; set; }
            public IAssetService Assets => null;
            public IAudioService Audio => null;
            public ISaveService Saves => null;
            public ISceneService Scenes => null;
            public IPauseService Pause => null;
            public IGameClock Clock => null;
            public ITimerService Timer => null;
            public IConfigService Config => null;
            public IGameFlowService Flow => null;
            public IAppLifecycle AppLifecycle => null;
            public IHttpService Http => null;
            public IPoolService Pools => null;
            public ILogService Log => null;
        }

        private sealed class FakeInput : IInputService
        {
            public event Action BindingsChanged;
            public float GetAxis(string action, string context = InputContexts.Gameplay) { return 0f; }
            public bool GetButton(string action, string context = InputContexts.Gameplay) { return false; }
            public bool GetButtonDown(string action, string context = InputContexts.Gameplay) { return false; }
            public bool GetButtonUp(string action, string context = InputContexts.Gameplay) { return false; }
            public void Bind(InputBinding binding) { BindingsChanged?.Invoke(); }
            public bool Unbind(string action, string context = InputContexts.Gameplay) { return false; }
            public IReadOnlyList<InputBinding> GetBindings() { return Array.Empty<InputBinding>(); }
            public IDisposable PushContext(string context, bool blockLower = true) { return new Dummy(); }
            public InputRebindResult TryRebind(InputBinding binding, bool allowReservedKey = false) { return InputRebindResult.Success; }
            public bool TryCaptureKey(out KeyCode key) { key = KeyCode.None; return false; }
            public bool ResetToDefaults() { return true; }
            public IReadOnlyList<InputBinding> GetDefaultBindings() { return Array.Empty<InputBinding>(); }
            public InputBinding[] ExportOverrides() { return Array.Empty<InputBinding>(); }
            public void ApplyOverrides(IEnumerable<InputBinding> overrides) { }

            private sealed class Dummy : IDisposable { public void Dispose() { } }
        }
    }
}
