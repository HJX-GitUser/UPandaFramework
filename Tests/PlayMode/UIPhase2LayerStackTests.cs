using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UPandaGF;

namespace UPandaGF.Tests
{
    [UILoadInfo(E_UI_Layer.Background, AssetLoadMethod.Resources, "UI/Phase2Background")]
    public class Phase2BackgroundPanel : BasePanel
    {
        public override void OnOpen(object panelArg = null) { }
        public override void OnClose() { }
    }

    [UILoadInfo(E_UI_Layer.Main, AssetLoadMethod.Resources, "UI/Phase2Main")]
    public class Phase2MainPanel : BasePanel
    {
        public int Paused;
        public int Resumed;
        public override void OnOpen(object panelArg = null) { }
        public override void OnClose() { }
        public override void OnPause() { Paused++; }
        public override void OnResume() { Resumed++; }
    }

    [UILoadInfo(E_UI_Layer.Popup, AssetLoadMethod.Resources, "UI/Phase2Popup",
        modal = true, pauseGame = true)]
    public class Phase2PopupPanel : BasePanel
    {
        public override void OnOpen(object panelArg = null) { }
        public override void OnClose() { }
    }

    [UILoadInfo(E_UI_Layer.Popup, AssetLoadMethod.Resources, "UI/Phase2Blocking",
        modal = true, closeOnBack = false)]
    public class Phase2BlockingPanel : BasePanel
    {
        public override void OnOpen(object panelArg = null) { }
        public override void OnClose() { }
    }

    [UILoadInfo(E_UI_Layer.Guide, AssetLoadMethod.Resources, "UI/Phase2Guide")]
    public class Phase2GuidePanel : BasePanel
    {
        public override void OnOpen(object panelArg = null) { }
        public override void OnClose() { }
    }

    [UILoadInfo(E_UI_Layer.Toast, AssetLoadMethod.Resources, "UI/Phase2Toast")]
    public class Phase2ToastPanel : BasePanel
    {
        public override void OnOpen(object panelArg = null) { }
        public override void OnClose() { }
    }

    [UILoadInfo(E_UI_Layer.Main, AssetLoadMethod.Resources, "UI/Phase3Pooled", pool = true)]
    public class Phase3PooledPanel : BasePanel
    {
        public int Opened;
        public int Closed;
        public override void OnOpen(object panelArg = null) { Opened++; }
        public override void OnClose() { Closed++; }
    }

    /// <summary>
    /// Phase 2 层栈、模态暂停/恢复、返回键、Toast 与句柄代次回归。
    /// </summary>
    public class UIPhase2LayerStackTests
    {
        private sealed class CaptureLog : ILogService
        {
            public readonly List<string> Errors = new List<string>();
            public bool IsEnabled(LogLevel level) { return true; }
            public void Log(LogLevel level, object message)
            {
                if (level == LogLevel.Error || level == LogLevel.Exception)
                    Errors.Add(message != null ? message.ToString() : "null");
            }
            public void LogException(Exception exception) { Errors.Add(exception != null ? exception.ToString() : "null"); }
        }

        private sealed class DeferredAssets : IAssetService
        {
            public readonly Queue<TaskCompletionSource<UnityEngine.Object>> Requests =
                new Queue<TaskCompletionSource<UnityEngine.Object>>();
            public int Released;

            public TaskCompletionSource<UnityEngine.Object> Next()
            {
                Assert.Greater(Requests.Count, 0, "没有待完成的资源请求");
                return Requests.Dequeue();
            }

            public Task<AssetLease<T>> AcquireAsync<T>(string path, AssetLoadMethod method = AssetLoadMethod.Resources,
                CancellationToken token = default) where T : UnityEngine.Object
            {
                TaskCompletionSource<UnityEngine.Object> request =
                    new TaskCompletionSource<UnityEngine.Object>(TaskCreationOptions.RunContinuationsAsynchronously);
                Requests.Enqueue(request);
                return Await<T>(request.Task);
            }

            private async Task<AssetLease<T>> Await<T>(Task<UnityEngine.Object> loading) where T : UnityEngine.Object
            {
                UnityEngine.Object asset = await loading;
                return asset != null ? new AssetLease<T>((T)asset, () => Released++) : null;
            }
        }

        private sealed class FakePause : IPauseService
        {
            public int Holds;
            public bool IsPaused => Holds > 0;
            public IDisposable Pause(string reason = "Gameplay")
            {
                Holds++;
                return new DisposableAction(() => Holds = Math.Max(0, Holds - 1));
            }
        }

        private sealed class FakeInput : IInputService
        {
            public int Contexts;
            public event Action BindingsChanged;
            public float GetAxis(string action, string context = InputContexts.Gameplay) { return 0f; }
            public bool GetButton(string action, string context = InputContexts.Gameplay) { return false; }
            public bool GetButtonDown(string action, string context = InputContexts.Gameplay) { return false; }
            public bool GetButtonUp(string action, string context = InputContexts.Gameplay) { return false; }
            public void Bind(InputBinding binding) { BindingsChanged?.Invoke(); }
            public bool Unbind(string action, string context = InputContexts.Gameplay) { return false; }
            public System.Collections.Generic.IReadOnlyList<InputBinding> GetBindings()
            {
                return System.Array.Empty<InputBinding>();
            }
            public InputRebindResult TryRebind(InputBinding binding, bool allowReservedKey = false) { return InputRebindResult.Success; }
            public bool TryCaptureKey(out KeyCode key) { key = KeyCode.None; return false; }
            public bool ResetToDefaults() { return false; }
            public System.Collections.Generic.IReadOnlyList<InputBinding> GetDefaultBindings()
            {
                return System.Array.Empty<InputBinding>();
            }
            public InputBinding[] ExportOverrides() { return System.Array.Empty<InputBinding>(); }
            public void ApplyOverrides(System.Collections.Generic.IEnumerable<InputBinding> overrides) { }
            public IDisposable PushContext(string context, bool blockLower = true)
            {
                Contexts++;
                return new DisposableAction(() => Contexts = Math.Max(0, Contexts - 1));
            }
        }

        private sealed class FakeServices : IGameServices
        {
            public IAssetService Assets { get; set; }
            public ILogService Log { get; set; }
            public IPauseService Pause { get; set; }
            public IInputService Input { get; set; }
            public ISettingsService Settings => null;
            public ILocalizationService Localization => null;
            public IAudioService Audio => null;
            public ISaveService Saves => null;
            public ISceneService Scenes => null;
            public IGameClock Clock => null;
            public ITimerService Timer => null;
            public IConfigService Config => null;
            public IGameFlowService Flow => null;
            public IAppLifecycle AppLifecycle => null;
            public IHttpService Http => null;
            public IPoolService Pools => null;
        }

        private UIManager ui;
        private DeferredAssets assets;
        private CaptureLog log;
        private FakePause pause;
        private FakeInput input;
        private GameObject prefab;
        private readonly List<GameObject> objects = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            GameObject uiObject = new GameObject("Phase2UI");
            objects.Add(uiObject);
            ui = uiObject.AddComponent<UIManager>();
            assets = new DeferredAssets();
            log = new CaptureLog();
            pause = new FakePause();
            input = new FakeInput();
            ui.Configure(new FakeServices { Assets = assets, Log = log, Pause = pause, Input = input });
            ui.Init();

            prefab = new GameObject("Phase2PanelPrefab", typeof(RectTransform));
            objects.Add(prefab);
        }

        [TearDown]
        public void TearDown()
        {
            if (ui != null) ui.Shutdown();
            for (int i = 0; i < objects.Count; i++)
                if (objects[i] != null) UnityEngine.Object.DestroyImmediate(objects[i]);
            objects.Clear();
        }

        private static IEnumerator Wait(Task task, float seconds = 10f)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!task.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(task.IsCompleted, "UI 异步请求没有结束");
            if (task.IsFaulted) throw task.Exception;
        }

        [UnityTest]
        public IEnumerator FiveLayersAndCloseTopCloseAllKeepOrder()
        {
            Task<UIPanelHandle<Phase2BackgroundPanel>> background = ui.OpenAsync<Phase2BackgroundPanel>();
            assets.Next().SetResult(prefab);
            yield return Wait(background);

            Task<UIPanelHandle<Phase2MainPanel>> main = ui.OpenAsync<Phase2MainPanel>();
            assets.Next().SetResult(prefab);
            yield return Wait(main);

            Task<UIPanelHandle<Phase2PopupPanel>> popup = ui.OpenAsync<Phase2PopupPanel>();
            assets.Next().SetResult(prefab);
            yield return Wait(popup);

            Task<UIPanelHandle<Phase2GuidePanel>> guide = ui.OpenAsync<Phase2GuidePanel>();
            assets.Next().SetResult(prefab);
            yield return Wait(guide);

            Task<UIPanelHandle<Phase2ToastPanel>> toast = ui.OpenAsync<Phase2ToastPanel>();
            assets.Next().SetResult(prefab);
            yield return Wait(toast);

            Assert.IsNotNull(background.Result);
            Assert.IsNotNull(main.Result);
            Assert.IsNotNull(popup.Result);
            Assert.IsNotNull(guide.Result);
            Assert.IsNotNull(toast.Result);
            Assert.AreEqual(E_UI_Layer.Background, background.Result.Layer);
            Assert.AreEqual(E_UI_Layer.Main, main.Result.Layer);
            Assert.AreEqual(E_UI_Layer.Popup, popup.Result.Layer);
            Assert.AreEqual(E_UI_Layer.Guide, guide.Result.Layer);
            Assert.AreEqual(E_UI_Layer.Toast, toast.Result.Layer);

            IReadOnlyList<UIPanelHandle> open = ui.GetOpenPanels();
            Assert.AreEqual(5, open.Count);
            Assert.AreEqual(E_UI_Layer.Toast, open[4].Layer);

            Assert.IsTrue(ui.CloseTop(E_UI_Layer.Popup));
            Assert.IsNull(ui.GetPanel<Phase2PopupPanel>());
            Assert.IsNotNull(ui.GetPanel<Phase2GuidePanel>());

            Assert.AreEqual(1, ui.CloseAll(E_UI_Layer.Main));
            Assert.IsNull(ui.GetPanel<Phase2MainPanel>());
            Assert.IsNotNull(ui.GetPanel<Phase2BackgroundPanel>());
            Assert.IsNotNull(ui.GetPanel<Phase2GuidePanel>());
        }

        [UnityTest]
        public IEnumerator ModalPausesLowerPanelAndReleasesPauseAndInput()
        {
            Task<UIPanelHandle<Phase2MainPanel>> main = ui.OpenAsync<Phase2MainPanel>();
            assets.Next().SetResult(prefab);
            yield return Wait(main);
            Phase2MainPanel mainPanel = main.Result.Panel;
            Assert.IsNotNull(mainPanel);

            Task<UIPanelHandle<Phase2PopupPanel>> popup = ui.OpenAsync<Phase2PopupPanel>();
            assets.Next().SetResult(prefab);
            yield return Wait(popup);
            Assert.IsNotNull(popup.Result);

            Assert.AreEqual(1, mainPanel.Paused);
            CanvasGroup mainGroup = mainPanel.GetComponent<CanvasGroup>();
            Assert.IsNotNull(mainGroup);
            Assert.IsFalse(mainGroup.interactable);
            Assert.IsFalse(mainGroup.blocksRaycasts);
            Assert.AreEqual(1, pause.Holds);
            Assert.AreEqual(1, input.Contexts);

            Task<bool> closePopup = popup.Result.CloseAsync();
            yield return Wait(closePopup);
            Assert.IsTrue(closePopup.Result);

            Assert.AreEqual(1, mainPanel.Resumed);
            Assert.IsTrue(mainGroup.interactable);
            Assert.IsTrue(mainGroup.blocksRaycasts);
            Assert.AreEqual(0, pause.Holds);
            Assert.AreEqual(0, input.Contexts);
            Assert.IsNotNull(ui.GetPanel<Phase2MainPanel>());
        }

        [UnityTest]
        public IEnumerator HandleBackClosesOnlyTopAndBlockingPanelStopsIt()
        {
            Task<UIPanelHandle<Phase2MainPanel>> main = ui.OpenAsync<Phase2MainPanel>();
            assets.Next().SetResult(prefab);
            yield return Wait(main);

            Task<UIPanelHandle<Phase2PopupPanel>> popup = ui.OpenAsync<Phase2PopupPanel>();
            assets.Next().SetResult(prefab);
            yield return Wait(popup);

            Assert.IsTrue(ui.HandleBack());
            Assert.IsNull(ui.GetPanel<Phase2PopupPanel>());
            Assert.IsNotNull(ui.GetPanel<Phase2MainPanel>());

            Task<UIPanelHandle<Phase2BlockingPanel>> blocking = ui.OpenAsync<Phase2BlockingPanel>();
            assets.Next().SetResult(prefab);
            yield return Wait(blocking);

            Assert.IsFalse(ui.HandleBack());
            Assert.IsNotNull(ui.GetPanel<Phase2BlockingPanel>());
            Assert.IsNotNull(ui.GetPanel<Phase2MainPanel>());
        }

        [UnityTest]
        public IEnumerator ToastAutoClosesAndDoesNotParticipateInBackStack()
        {
            Task<UIPanelHandle<Phase2MainPanel>> main = ui.OpenAsync<Phase2MainPanel>();
            assets.Next().SetResult(prefab);
            yield return Wait(main);

            Task<UIPanelHandle<Phase2ToastPanel>> toast = ui.ShowToastAsync<Phase2ToastPanel>(null, 0.1f);
            assets.Next().SetResult(prefab);
            yield return Wait(toast);
            Assert.IsNotNull(toast.Result);
            Assert.IsTrue(toast.Result.IsOpen);

            Assert.IsTrue(ui.HandleBack());
            Assert.IsNull(ui.GetPanel<Phase2MainPanel>());
            Assert.IsNotNull(ui.GetPanel<Phase2ToastPanel>());

            yield return new WaitForSeconds(0.2f);
            Assert.IsFalse(toast.Result.IsOpen);
            Assert.IsNull(ui.GetPanel<Phase2ToastPanel>());
        }

        [UnityTest]
        public IEnumerator ToastQueueShowsOneAtATime()
        {
            Task<UIPanelHandle<Phase2ToastPanel>> first = ui.ShowToastAsync<Phase2ToastPanel>(null, 0.1f);
            assets.Next().SetResult(prefab);
            yield return Wait(first);
            Assert.IsNotNull(first.Result);
            Assert.IsTrue(first.Result.IsOpen);

            Task<UIPanelHandle<Phase2ToastPanel>> second = ui.ShowToastAsync<Phase2ToastPanel>(null, 0.1f);
            yield return null;
            Assert.IsFalse(second.IsCompleted);

            yield return new WaitForSeconds(0.2f);
            Assert.Greater(assets.Requests.Count, 0);
            assets.Next().SetResult(prefab);
            yield return Wait(second);

            Assert.IsNotNull(second.Result);
            Assert.IsTrue(second.Result.IsOpen);
            Assert.IsFalse(first.Result.IsOpen);
        }

        [UnityTest]
        public IEnumerator NonGenericOpenAndCloseByType()
        {
            Task<UIPanelHandle> open = ui.OpenAsync(typeof(Phase2MainPanel));
            assets.Next().SetResult(prefab);
            yield return Wait(open);

            Assert.IsNotNull(open.Result);
            Assert.IsInstanceOf<Phase2MainPanel>(open.Result.Panel);
            Assert.AreSame(open.Result.Panel, ui.GetPanel(typeof(Phase2MainPanel)));

            ui.ClosePanel(typeof(Phase2MainPanel));
            Assert.IsNull(ui.GetPanel(typeof(Phase2MainPanel)));
            Assert.AreEqual(UIPanelState.Closed, open.Result.State);
        }

        [UnityTest]
        public IEnumerator RegisterPanelThenOpenByLoadPath()
        {
            Assert.IsTrue(ui.RegisterPanel(typeof(Phase2BackgroundPanel)));

            Task<UIPanelHandle> open = ui.OpenAsync("UI/Phase2Background");
            assets.Next().SetResult(prefab);
            yield return Wait(open);

            Assert.IsNotNull(open.Result);
            Assert.AreEqual(E_UI_Layer.Background, open.Result.Layer);
            Assert.IsInstanceOf<Phase2BackgroundPanel>(open.Result.Panel);
        }

        [UnityTest]
        public IEnumerator NonGenericToastAndResetForHotUpdate()
        {
            Task<UIPanelHandle> toast = ui.ShowToastAsync(typeof(Phase2ToastPanel), null, 10f);
            assets.Next().SetResult(prefab);
            yield return Wait(toast);
            Assert.IsNotNull(toast.Result);
            Assert.IsTrue(toast.Result.IsOpen);

            ui.ResetForHotUpdate();
            yield return null;

            Assert.IsFalse(toast.Result.IsOpen);
            Assert.IsNull(ui.GetPanel(typeof(Phase2ToastPanel)));
            Assert.AreEqual(0, ui.GetOpenPanels().Count);
        }

        [UnityTest]
        public IEnumerator PooledPanelReusesInstanceWithoutReload()
        {
            ui.MaxPooledPerPath = 2;
            Task<UIPanelHandle<Phase3PooledPanel>> first = ui.OpenAsync<Phase3PooledPanel>();
            assets.Next().SetResult(prefab);
            yield return Wait(first);
            Assert.IsNotNull(first.Result);
            Phase3PooledPanel instance = first.Result.Panel;

            yield return Wait(first.Result.CloseAsync());
            Assert.AreEqual(1, ui.DiagnosticPooledPanelCount);
            Assert.AreEqual(0, assets.Requests.Count);

            for (int i = 0; i < 100; i++)
            {
                Task<UIPanelHandle<Phase3PooledPanel>> open = ui.OpenAsync<Phase3PooledPanel>();
                Assert.AreEqual(0, assets.Requests.Count, "命中池后面板不应再请求资源");
                yield return Wait(open);
                Assert.IsNotNull(open.Result);
                Assert.AreSame(instance, open.Result.Panel);
                yield return Wait(open.Result.CloseAsync());
            }

            Assert.AreEqual(0, ui.DiagnosticOpenPanelCount);
            Assert.AreEqual(1, ui.DiagnosticPooledPanelCount);
            Assert.AreEqual(0, assets.Released);
            Assert.AreEqual(101, instance.Opened);
            Assert.AreEqual(101, instance.Closed);

            UIDiagnosticsSnapshot diagnostics = ui.CaptureDiagnostics();
            Assert.AreEqual(1, diagnostics.InstancesCreated);
            Assert.AreEqual(100, diagnostics.PoolHits);
            Assert.AreEqual(0, diagnostics.OpenPanels);
            Assert.AreEqual(1, diagnostics.PooledPanels);

            ui.ResetForHotUpdate();
            yield return null;
            Assert.AreEqual(0, ui.DiagnosticPooledPanelCount);
            Assert.AreEqual(1, assets.Released);
        }

        [UnityTest]
        public IEnumerator StaleHandleCannotCloseReopenedPanel()
        {
            Task<UIPanelHandle<Phase2MainPanel>> first = ui.OpenAsync<Phase2MainPanel>();
            assets.Next().SetResult(prefab);
            yield return Wait(first);
            Assert.IsNotNull(first.Result);

            Task<bool> closeFirst = first.Result.CloseAsync();
            yield return Wait(closeFirst);
            Assert.IsTrue(closeFirst.Result);

            Task<UIPanelHandle<Phase2MainPanel>> second = ui.OpenAsync<Phase2MainPanel>();
            assets.Next().SetResult(prefab);
            yield return Wait(second);
            Assert.IsNotNull(second.Result);

            Task<bool> closeStale = first.Result.CloseAsync();
            yield return Wait(closeStale);
            Assert.IsFalse(closeStale.Result);
            Assert.IsTrue(second.Result.IsOpen);
            Assert.IsNotNull(ui.GetPanel<Phase2MainPanel>());
        }
    }
}
