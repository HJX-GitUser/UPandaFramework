using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UPandaGF;

namespace UPandaGF.Tests
{
    /// <summary>UI-041：通用行池的复用、上限与清理。</summary>
    public class UIItemPoolTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--)
            {
                if (objects[i] != null) UnityEngine.Object.DestroyImmediate(objects[i]);
            }
            objects.Clear();
        }

        private GameObject CreatePrefab()
        {
            GameObject prefab = new GameObject("Item", typeof(RectTransform));
            prefab.SetActive(false);
            objects.Add(prefab);
            return prefab;
        }

        private GameObject CreateParent()
        {
            GameObject parent = new GameObject("Parent", typeof(RectTransform));
            objects.Add(parent);
            return parent;
        }

        [Test]
        public void ReusesReleasedInstanceAndTracksCounters()
        {
            GameObject prefab = CreatePrefab();
            GameObject parent = CreateParent();
            UIItemPool pool = new UIItemPool(prefab, parent.transform, 4);

            GameObject first = pool.Acquire(parent.transform);
            Assert.IsNotNull(first);
            Assert.IsTrue(first.activeSelf);
            Assert.AreEqual(1, pool.CreatedCount);

            pool.Release(first);
            Assert.AreEqual(1, pool.IdleCount);
            Assert.IsFalse(first.activeSelf);

            GameObject second = pool.Acquire(parent.transform);
            Assert.AreSame(first, second);
            Assert.AreEqual(1, pool.CreatedCount);
            Assert.AreEqual(1, pool.ReusedCount);
            Assert.AreEqual(0, pool.IdleCount);
        }

        [Test]
        public void ReleaseBeyondLimitDestroysAndZeroDisablesCache()
        {
            GameObject prefab = CreatePrefab();
            GameObject parent = CreateParent();
            UIItemPool pool = new UIItemPool(prefab, parent.transform, 1);

            GameObject a = pool.Acquire(parent.transform);
            GameObject b = pool.Acquire(parent.transform);
            pool.Release(a);
            pool.Release(b);

            Assert.AreEqual(1, pool.IdleCount);
            Assert.AreEqual(1, pool.DestroyedCount);

            UIItemPool noCache = new UIItemPool(prefab, parent.transform, 0);
            GameObject c = noCache.Acquire(parent.transform);
            noCache.Release(c);
            Assert.AreEqual(0, noCache.IdleCount);
            Assert.AreEqual(1, noCache.DestroyedCount);
        }

        [Test]
        public void ClearDestroysOnlyIdleInstances()
        {
            GameObject prefab = CreatePrefab();
            GameObject parent = CreateParent();
            UIItemPool pool = new UIItemPool(prefab, parent.transform, 4);

            GameObject idle = pool.Acquire(parent.transform);
            GameObject inUse = pool.Acquire(parent.transform);
            pool.Release(idle);
            pool.Clear();

            Assert.AreEqual(0, pool.IdleCount);
            Assert.AreEqual(1, pool.DestroyedCount);
            Assert.IsNotNull(inUse);
            Assert.IsTrue(inUse.activeSelf);
        }

        [Test]
        public void DuplicateReleaseIsIgnored()
        {
            GameObject prefab = CreatePrefab();
            GameObject parent = CreateParent();
            UIItemPool pool = new UIItemPool(prefab, parent.transform, 4);

            GameObject item = pool.Acquire(parent.transform);
            pool.Release(item);
            pool.Release(item);

            Assert.AreEqual(1, pool.IdleCount);
            Assert.AreEqual(1, pool.ReleasedCount);
        }
    }

    /// <summary>UI-041：描述表驱动的设置行创建、复用与绑定。</summary>
    public class UISettingListTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private GameObject content;
        private UISettingList list;
        private UISettingToggleRow togglePrefab;
        private UISettingSliderRow sliderPrefab;
        private UISettingDropdownRow dropdownPrefab;
        private UISettingButtonRow buttonPrefab;
        private UISettingReadOnlyRow readOnlyPrefab;

        [SetUp]
        public void SetUp()
        {
            content = new GameObject("Content", typeof(RectTransform));
            objects.Add(content);

            GameObject listObject = new GameObject("SettingList", typeof(RectTransform));
            objects.Add(listObject);
            list = listObject.AddComponent<UISettingList>();
            list.Content = content.GetComponent<RectTransform>();

            togglePrefab = CreateToggleRow();
            sliderPrefab = CreateSliderRow();
            dropdownPrefab = CreateDropdownRow();
            buttonPrefab = CreateButtonRow();
            readOnlyPrefab = CreateReadOnlyRow();

            list.SetRowPrefab(UISettingControl.Toggle, togglePrefab);
            list.SetRowPrefab(UISettingControl.Slider, sliderPrefab);
            list.SetRowPrefab(UISettingControl.Dropdown, dropdownPrefab);
            list.SetRowPrefab(UISettingControl.Button, buttonPrefab);
            list.SetRowPrefab(UISettingControl.ReadOnly, readOnlyPrefab);
        }

        [TearDown]
        public void TearDown()
        {
            list.Clear();
            for (int i = objects.Count - 1; i >= 0; i--)
            {
                if (objects[i] != null) UnityEngine.Object.DestroyImmediate(objects[i]);
            }
            objects.Clear();
        }

        private static Text AddText(Transform parent, string name)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            return go.GetComponent<Text>();
        }

        private T AddChild<T>(Transform parent, string name) where T : Component
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(parent, false);
            return go.AddComponent<T>();
        }

        private GameObject CreateRowRoot(string name)
        {
            GameObject root = new GameObject(name, typeof(RectTransform));
            root.SetActive(false);
            objects.Add(root);
            return root;
        }

        private UISettingToggleRow CreateToggleRow()
        {
            GameObject root = CreateRowRoot("ToggleRowPrefab");
            AddText(root.transform, "Label");
            AddChild<Toggle>(root.transform, "Toggle");
            return root.AddComponent<UISettingToggleRow>();
        }

        private UISettingSliderRow CreateSliderRow()
        {
            GameObject root = CreateRowRoot("SliderRowPrefab");
            AddText(root.transform, "Label");
            AddText(root.transform, "Value");
            AddChild<Slider>(root.transform, "Slider");
            return root.AddComponent<UISettingSliderRow>();
        }

        private UISettingDropdownRow CreateDropdownRow()
        {
            GameObject root = CreateRowRoot("DropdownRowPrefab");
            AddText(root.transform, "Label");
            AddChild<Dropdown>(root.transform, "Dropdown");
            return root.AddComponent<UISettingDropdownRow>();
        }

        private UISettingButtonRow CreateButtonRow()
        {
            GameObject root = CreateRowRoot("ButtonRowPrefab");
            AddText(root.transform, "Label");
            AddText(root.transform, "Value");
            AddChild<Button>(root.transform, "Button");
            return root.AddComponent<UISettingButtonRow>();
        }

        private UISettingReadOnlyRow CreateReadOnlyRow()
        {
            GameObject root = CreateRowRoot("ReadOnlyRowPrefab");
            AddText(root.transform, "Label");
            AddText(root.transform, "Value");
            return root.AddComponent<UISettingReadOnlyRow>();
        }

        [Test]
        public void CreatesRowPerControlTypeAndBindsValues()
        {
            BoolObservable muted = new BoolObservable(true);
            FloatObservable volume = new FloatObservable(0.5f);
            IntObservable mode = new IntObservable(1);
            StringObservable version = new StringObservable("0.8.0");
            bool clicked = false;

            List<UISettingItem> items = new List<UISettingItem>
            {
                UISettingItem.CreateToggle("audio.mute", "静音", muted),
                UISettingItem.CreateSlider("audio.volume", "音量", volume),
                UISettingItem.CreateDropdown("display.mode", "显示模式", mode,
                    new[] { "全屏", "窗口", "无边框" }),
                UISettingItem.CreateButton("about.reset", "恢复默认", () => clicked = true),
                UISettingItem.CreateReadOnly("about.version", "版本", version)
            };

            list.SetItems(items);

            Assert.AreEqual(5, list.RowCount);
            Assert.IsInstanceOf<UISettingToggleRow>(list.GetRow(0));
            Assert.IsInstanceOf<UISettingSliderRow>(list.GetRow(1));
            Assert.IsInstanceOf<UISettingDropdownRow>(list.GetRow(2));
            Assert.IsInstanceOf<UISettingButtonRow>(list.GetRow(3));
            Assert.IsInstanceOf<UISettingReadOnlyRow>(list.GetRow(4));

            Toggle toggle = list.GetRow(0).GetComponentInChildren<Toggle>(true);
            Assert.IsTrue(toggle.isOn);
            muted.Value = false;
            Assert.IsFalse(toggle.isOn);
            toggle.isOn = true;
            Assert.IsTrue(muted.Value);

            Slider slider = list.GetRow(1).GetComponentInChildren<Slider>(true);
            Assert.AreEqual(0.5f, slider.value, 0.001f);
            slider.value = 0.75f;
            Assert.AreEqual(0.75f, volume.Value, 0.001f);
            volume.Value = 0.25f;
            Assert.AreEqual(0.25f, slider.value, 0.001f);

            Dropdown dropdown = list.GetRow(2).GetComponentInChildren<Dropdown>(true);
            Assert.AreEqual(3, dropdown.options.Count);
            Assert.AreEqual(1, dropdown.value);
            dropdown.value = 2;
            Assert.AreEqual(2, mode.Value);
            mode.Value = 0;
            Assert.AreEqual(0, dropdown.value);

            Button button = list.GetRow(3).GetComponentInChildren<Button>(true);
            button.onClick.Invoke();
            Assert.IsTrue(clicked);

            Text versionText = list.GetRow(4).GetComponentsInChildren<Text>(true)[1];
            Assert.AreEqual("0.8.0", versionText.text);
            version.Value = "0.9.0";
            Assert.AreEqual("0.9.0", versionText.text);
        }

        [Test]
        public void ReusesRowsWhenItemsRefreshAndReleasesTrimmedRows()
        {
            List<UISettingItem> items = new List<UISettingItem>
            {
                UISettingItem.CreateToggle("a", "A", new BoolObservable()),
                UISettingItem.CreateToggle("b", "B", new BoolObservable()),
                UISettingItem.CreateToggle("c", "C", new BoolObservable())
            };

            list.SetItems(items);
            UISettingRow first = list.GetRow(0);
            UISettingRow second = list.GetRow(1);
            UISettingRow third = list.GetRow(2);
            Assert.AreEqual(3, list.CreatedRows);

            list.SetItems(items);
            Assert.AreSame(first, list.GetRow(0));
            Assert.AreSame(second, list.GetRow(1));
            Assert.AreSame(third, list.GetRow(2));
            Assert.AreEqual(3, list.CreatedRows, "同类型刷新不应重新实例化");
            Assert.AreEqual(3, list.ReusedRows);

            list.SetItems(new List<UISettingItem> { items[0] });
            Assert.AreEqual(1, list.RowCount);
            Assert.AreEqual(2, list.ReleasedRows);
        }

        [Test]
        public void ControlTypeChangeSwapsRowAndReturnedRowIsReused()
        {
            list.SetItems(new List<UISettingItem>
            {
                UISettingItem.CreateToggle("a", "A", new BoolObservable())
            });
            UISettingRow toggleRow = list.GetRow(0);

            list.SetItems(new List<UISettingItem>
            {
                UISettingItem.CreateSlider("a", "A", new FloatObservable())
            });
            Assert.IsInstanceOf<UISettingSliderRow>(list.GetRow(0));
            Assert.AreNotSame(toggleRow, list.GetRow(0));
            Assert.AreEqual(1, list.ReleasedRows);

            list.SetItems(new List<UISettingItem>
            {
                UISettingItem.CreateToggle("a", "A", new BoolObservable())
            });
            Assert.AreSame(toggleRow, list.GetRow(0), "切回 Toggle 应复用池中的行");
            Assert.AreEqual(2, list.CreatedRows, "Toggle + Slider 各创建一次，切回不再新建");
            Assert.GreaterOrEqual(list.ReusedRows, 1);
        }

        [Test]
        public void KeyRowFallsBackToButtonRowAndShowsKeyName()
        {
            list.SetRowPrefab(UISettingControl.Button, buttonPrefab);
            StringObservable keyName = new StringObservable("Space");
            int rebinds = 0;

            list.SetItems(new List<UISettingItem>
            {
                UISettingItem.CreateKey("input.jump", "跳跃", keyName, () => rebinds++)
            });

            UISettingRow row = list.GetRow(0);
            Assert.IsInstanceOf<UISettingButtonRow>(row);

            Text valueText = row.GetComponentsInChildren<Text>(true)[1];
            Assert.AreEqual("Space", valueText.text);
            keyName.Value = "E";
            Assert.AreEqual("E", valueText.text);

            row.GetComponentInChildren<Button>(true).onClick.Invoke();
            Assert.AreEqual(1, rebinds);
        }

        [Test]
        public void SliderSnapsToStepAndFormatsUnitRange()
        {
            FloatObservable volume = new FloatObservable(0.5f);
            list.SetItems(new List<UISettingItem>
            {
                UISettingItem.CreateSlider("v", "音量", volume, 0f, 1f, 0.25f)
            });

            Slider slider = list.GetRow(0).GetComponentInChildren<Slider>(true);
            Text valueText = list.GetRow(0).GetComponentsInChildren<Text>(true)[1];
            Assert.AreEqual("50%", valueText.text);

            slider.value = 0.6f;
            Assert.AreEqual(0.5f, volume.Value, 0.001f, "0.6 应吸附到 0.5");
            Assert.AreEqual(0.5f, slider.value, 0.001f);

            volume.Value = 0.9f;
            Assert.AreEqual(1f, slider.value, 0.001f);
        }

        [Test]
        public void RebindDoesNotDuplicateValueListeners()
        {
            BoolObservable value = new BoolObservable(false);
            UISettingItem item = UISettingItem.CreateToggle("a", "A", value);
            list.SetItems(new List<UISettingItem> { item, item });

            // 两个索引各创建一行；同一行反复 Bind 时旧监听必须释放。
            UISettingRow row = list.GetRow(0);
            row.Bind(item);
            row.Bind(item);

            Toggle toggle = row.GetComponentInChildren<Toggle>(true);
            int callbackCount = 0;
            IDisposable probe = value.Subscribe(v => callbackCount++);
            toggle.isOn = true;

            Assert.AreEqual(1, callbackCount);
            probe.Dispose();
        }

        [Test]
        public void InvalidDescriptorLogsAndStillCreatesRow()
        {
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("描述无效"));
            list.SetItems(new List<UISettingItem>
            {
                new UISettingItem { Key = "bad", Label = "Bad", Control = UISettingControl.Toggle }
            });
            Assert.AreEqual(1, list.RowCount);
        }
    }

    /// <summary>UI-042：焦点导航与默认选中/焦点恢复。</summary>
    public class UIFocusNavigatorTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private EventSystem eventSystem;
        private GameObject panel;
        private UIFocusNavigator navigator;
        private Button left;
        private Button right;
        private Slider slider;

        [SetUp]
        public void SetUp()
        {
            GameObject eventObject = new GameObject("EventSystem", typeof(EventSystem));
            UIManager.AddDefaultInputModule(eventObject);
            objects.Add(eventObject);
            eventSystem = eventObject.GetComponent<EventSystem>();

            panel = new GameObject("Panel", typeof(RectTransform));
            objects.Add(panel);

            left = CreateButton("Left", new Vector2(0f, 0f));
            right = CreateButton("Right", new Vector2(120f, 0f));
            slider = CreateSlider("Slider", new Vector2(0f, -80f));

            left.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnRight = right };
            right.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = left };

            navigator = panel.AddComponent<UIFocusNavigator>();
            navigator.DefaultFocus = left;
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--)
            {
                if (objects[i] != null) UnityEngine.Object.DestroyImmediate(objects[i]);
            }
            objects.Clear();
        }

        private Button CreateButton(string name, Vector2 position)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(panel.transform, false);
            go.GetComponent<RectTransform>().anchoredPosition = position;
            return go.GetComponent<Button>();
        }

        private Slider CreateSlider(string name, Vector2 position)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Slider));
            go.transform.SetParent(panel.transform, false);
            go.GetComponent<RectTransform>().anchoredPosition = position;
            Slider result = go.GetComponent<Slider>();
            result.minValue = 0f;
            result.maxValue = 1f;
            result.value = 0f;
            return result;
        }

        [UnityTest]
        public IEnumerator DefaultFocusAndDirectionalNavigation()
        {
            yield return null;
            Assert.AreSame(left.gameObject, eventSystem.currentSelectedGameObject);
            Assert.IsTrue(navigator.HasFocus);

            Assert.IsTrue(navigator.Navigate(Vector2.right));
            Assert.AreSame(right.gameObject, eventSystem.currentSelectedGameObject);

            Assert.IsTrue(navigator.Navigate(Vector2.left));
            Assert.AreSame(left.gameObject, eventSystem.currentSelectedGameObject);

            Assert.IsFalse(navigator.Navigate(Vector2.up));
        }

        [UnityTest]
        public IEnumerator SubmitAndCancelRouteToFocusedControl()
        {
            yield return null;
            int clicks = 0;
            left.onClick.AddListener(() => clicks++);

            Assert.IsTrue(navigator.Focus(left));
            Assert.IsTrue(navigator.Submit());
            Assert.AreEqual(1, clicks);

            int backs = 0;
            Action onBack = () => backs++;
            navigator.BackRequested += onBack;
            Assert.IsTrue(navigator.Cancel());
            Assert.AreEqual(1, backs);
            navigator.BackRequested -= onBack;
            Assert.IsFalse(navigator.Cancel());
        }

        [UnityTest]
        public IEnumerator RememberAndRestoreFocus()
        {
            yield return null;
            Assert.IsTrue(navigator.Focus(right));
            navigator.RememberFocus();
            eventSystem.SetSelectedGameObject(null);
            Assert.IsFalse(navigator.HasFocus);

            Assert.IsTrue(navigator.RestoreFocus());
            Assert.AreSame(right.gameObject, eventSystem.currentSelectedGameObject);
        }

        [UnityTest]
        public IEnumerator SliderAdjustsWithHorizontalInput()
        {
            yield return null;
            Assert.IsTrue(navigator.Focus(slider));

            Assert.IsTrue(navigator.Adjust(1f, 0.5f));
            Assert.AreEqual(0.5f, slider.value, 0.001f);
            Assert.IsTrue(navigator.NavigateOrAdjust(new Vector2(1f, 0f), 0.1f));
            Assert.AreEqual(0.6f, slider.value, 0.001f);
            Assert.IsTrue(navigator.NavigateOrAdjust(new Vector2(-1f, 0f), 0.1f));
            Assert.AreEqual(0.5f, slider.value, 0.001f);
        }

        [UnityTest]
        public IEnumerator FocusFirstSkipsDisabledAndRestoresFallback()
        {
            left.interactable = false;
            navigator.DefaultFocus = null;
            yield return null;

            Assert.IsTrue(navigator.FocusFirst());
            Assert.AreSame(right.gameObject, eventSystem.currentSelectedGameObject);

            left.interactable = true;
            Assert.IsTrue(navigator.Focus(left));
            navigator.RememberFocus();
            left.interactable = false;
            Assert.IsTrue(navigator.RestoreFocus());
            Assert.AreSame(right.gameObject, eventSystem.currentSelectedGameObject);
        }
    }

    /// <summary>UI-042：UI 缩放换算与 CanvasScaler 接入。</summary>
    public class UICanvasScaleTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--)
            {
                if (objects[i] != null) UnityEngine.Object.DestroyImmediate(objects[i]);
            }
            objects.Clear();
        }

        private UICanvasScale CreateScale(Vector2 reference)
        {
            GameObject go = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            objects.Add(go);
            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = reference;
            UICanvasScale scale = go.AddComponent<UICanvasScale>();
            scale.ReferenceResolution = reference;
            return scale;
        }

        [Test]
        public void ScaleChangesReferenceResolutionInverse()
        {
            UICanvasScale scale = CreateScale(new Vector2(1920f, 1080f));
            CanvasScaler scaler = scale.GetComponent<CanvasScaler>();

            scale.Scale = 1.25f;
            Assert.AreEqual(1536f, scaler.referenceResolution.x, 0.01f);
            Assert.AreEqual(864f, scaler.referenceResolution.y, 0.01f);

            scale.Scale = 1f;
            Assert.AreEqual(1920f, scaler.referenceResolution.x, 0.01f);
            Assert.AreEqual(1080f, scaler.referenceResolution.y, 0.01f);
        }

        [Test]
        public void ScaleIsClampedToSupportedRange()
        {
            UICanvasScale scale = CreateScale(new Vector2(1920f, 1080f));
            CanvasScaler scaler = scale.GetComponent<CanvasScaler>();

            scale.Scale = 10f;
            Assert.AreEqual(UICanvasScale.MaxScale, scale.Scale, 0.001f);
            Assert.AreEqual(960f, scaler.referenceResolution.x, 0.01f);

            scale.Scale = 0.1f;
            Assert.AreEqual(UICanvasScale.MinScale, scale.Scale, 0.001f);
            Assert.AreEqual(3840f, scaler.referenceResolution.x, 0.01f);
        }

        [Test]
        public void BindFollowsObservable()
        {
            UICanvasScale scale = CreateScale(new Vector2(1920f, 1080f));
            CanvasScaler scaler = scale.GetComponent<CanvasScaler>();
            FloatObservable source = new FloatObservable(1.3f);

            IDisposable binding = scale.Bind(source);
            Assert.AreEqual(1.3f, scale.Scale, 0.001f);
            Assert.AreEqual(1920f / 1.3f, scaler.referenceResolution.x, 0.01f);

            source.Value = 1f;
            Assert.AreEqual(1920f, scaler.referenceResolution.x, 0.01f);

            binding.Dispose();
            source.Value = 1.5f;
            Assert.AreEqual(1f, scale.Scale, 0.001f);
            source.Dispose();
        }

        [Test]
        public void ManagerAppliesUiScaleToOwnCanvas()
        {
            GameObject uiObject = new GameObject("UI");
            objects.Add(uiObject);
            UIManager manager = uiObject.AddComponent<UIManager>();
            try
            {
                manager.Configure(new NoopAssets());
                manager.Init();

                Assert.AreEqual(1f, manager.UiScale, 0.001f);
                manager.SetUiScale(1.25f);
                Assert.AreEqual(1.25f, manager.UiScale, 0.001f);

                Canvas canvas = manager.uiCanvas;
                Assert.IsNotNull(canvas);
                CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
                Assert.IsNotNull(scaler);
                Assert.AreEqual(1536f, scaler.referenceResolution.x, 0.01f);
                Assert.AreEqual(864f, scaler.referenceResolution.y, 0.01f);

                manager.SetUiScale(5f);
                Assert.AreEqual(UICanvasScale.MaxScale, manager.UiScale, 0.001f);
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
