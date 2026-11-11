using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UPandaGF;

namespace UPandaGF.Tests
{
    [UILoadInfo(E_UI_Layer.Mid, AssetLoadMethod.Resources, "UI/Phase1Panel")]
    public class Phase1Panel : BasePanel
    {
        public int Opened;
        public int Closed;
        public int Refreshed;
        public bool SelfClose;
        public bool CheckControl;
        public bool ControlFound;

        public override void OnOpen(object panelArg = null)
        {
            Opened++;
            if (CheckControl) ControlFound = GetControl<Button>("Phase1Button") != null;
            if (SelfClose) uiManager.ClosePanel<Phase1Panel>();
        }

        public override void OnClose() { Closed++; }
        public override void OnRefresh(object panelArg = null) { Refreshed++; }
    }

    [UILoadInfo(E_UI_Layer.Mid, AssetLoadMethod.Resources, "UI/Phase1Panel")]
    public class Phase1ConflictPanel : BasePanel
    {
        public override void OnOpen(object panelArg = null) { }
        public override void OnClose() { }
    }

    [UILoadInfo(E_UI_Layer.Mid, AssetLoadMethod.Resources, "UI/Phase1WindowPanel",
        placement = UIPanelPlacement.Window)]
    public class Phase1WindowPanel : BasePanel
    {
        public override void OnOpen(object panelArg = null) { }
        public override void OnClose() { }
    }

    public class Phase1Consumer : MonoBehaviour, IGameServicesConsumer
    {
        public IGameServices Services;
        public void Configure(IGameServices services) { Services = services; }
    }

    public class Phase1UiConsumer : MonoBehaviour, IUIServiceConsumer
    {
        public IUIService UI;
        public void ConfigureUi(IUIService ui) { UI = ui; }
    }

    /// <summary>
    /// Phase 1 正确性回归：重复打开、关闭加载、自关闭、类型冲突、注入、Canvas 补层、
    /// inactive 预制体、外部销毁与 Shutdown。
    /// </summary>
    public class UIPhase1RegressionTests
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
            public sealed class Request
            {
                public readonly TaskCompletionSource<UnityEngine.Object> Completion =
                    new TaskCompletionSource<UnityEngine.Object>(TaskCreationOptions.RunContinuationsAsynchronously);
                public CancellationToken Token;
            }

            public readonly Queue<Request> Requests = new Queue<Request>();
            public int Released;

            public Request Next()
            {
                Assert.Greater(Requests.Count, 0, "没有待完成的资源请求");
                return Requests.Dequeue();
            }

            public Task<AssetLease<T>> AcquireAsync<T>(string path, AssetLoadMethod method = AssetLoadMethod.Resources,
                CancellationToken token = default) where T : UnityEngine.Object
            {
                Request request = new Request { Token = token };
                Requests.Enqueue(request);
                return Await<T>(request.Completion.Task);
            }

            private async Task<AssetLease<T>> Await<T>(Task<UnityEngine.Object> loading) where T : UnityEngine.Object
            {
                UnityEngine.Object asset = await loading;
                return asset != null ? new AssetLease<T>((T)asset, () => Released++) : null;
            }
        }

        private sealed class FakeServices : IGameServices
        {
            public IAssetService Assets { get; set; }
            public ILogService Log { get; set; }
            public IAudioService Audio => null;
            public ISaveService Saves => null;
            public ISceneService Scenes => null;
            public IInputService Input => null;
            public IPauseService Pause => null;
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
        private GameObject prefab;
        private readonly List<GameObject> objects = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            GameObject uiObject = new GameObject("Phase1UI");
            objects.Add(uiObject);
            ui = uiObject.AddComponent<UIManager>();
            assets = new DeferredAssets();
            log = new CaptureLog();
            ui.Configure(assets, log);
            ui.Init();

            prefab = new GameObject("Phase1PanelPrefab", typeof(RectTransform));
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
        public IEnumerator ConcurrentOpenCallsOnOpenOnce()
        {
            Task<Phase1Panel> first = ui.ShowPanelAsync<Phase1Panel>();
            Task<Phase1Panel> second = ui.ShowPanelAsync<Phase1Panel>();
            assets.Next().Completion.SetResult(prefab);

            yield return Wait(Task.WhenAll(first, second));

            Assert.IsNotNull(first.Result);
            Assert.AreSame(first.Result, second.Result);
            Assert.AreEqual(1, first.Result.Opened);
            Assert.AreEqual(1, ui.DiagnosticOpenPanelCount);
        }

        [UnityTest]
        public IEnumerator ReopenBringsToFrontWithoutSecondOpen()
        {
            Task<Phase1Panel> first = ui.ShowPanelAsync<Phase1Panel>();
            assets.Next().Completion.SetResult(prefab);
            yield return Wait(first);

            Task<Phase1Panel> second = ui.ShowPanelAsync<Phase1Panel>();
            yield return Wait(second);

            Assert.AreSame(first.Result, second.Result);
            Assert.AreEqual(1, first.Result.Opened);
            Assert.AreEqual(1, first.Result.Refreshed);
            Assert.AreEqual(1, ui.DiagnosticOpenPanelCount);
        }

        [UnityTest]
        public IEnumerator CloseWhileLoadingCancelsRequestAndDisposesLateLease()
        {
            Task<Phase1Panel> task = ui.ShowPanelAsync<Phase1Panel>();
            DeferredAssets.Request request = assets.Next();

            ui.ClosePanel<Phase1Panel>();
            Assert.IsTrue(request.Token.IsCancellationRequested, "关闭加载中的面板必须取消本次请求");

            request.Completion.SetResult(prefab);
            yield return Wait(task);
            yield return null;

            Assert.IsNull(task.Result);
            Assert.AreEqual(0, ui.DiagnosticOpenPanelCount);
            Assert.AreEqual(0, ui.DiagnosticPendingPanelCount);
            Assert.AreEqual(1, assets.Released);
        }

        [UnityTest]
        public IEnumerator OnOpenSelfCloseReturnsNullAndReleasesLease()
        {
            Phase1Panel component = prefab.AddComponent<Phase1Panel>();
            component.SelfClose = true;

            Task<Phase1Panel> task = ui.ShowPanelAsync<Phase1Panel>();
            assets.Next().Completion.SetResult(prefab);
            yield return Wait(task);
            yield return null;

            Assert.IsNull(task.Result);
            Assert.IsNull(ui.GetPanel<Phase1Panel>());
            Assert.AreEqual(0, ui.DiagnosticOpenPanelCount);
            Assert.AreEqual(1, assets.Released);
        }

        [UnityTest]
        public IEnumerator SamePathDifferentTypeIsRejectedBeforeLoad()
        {
            Task<Phase1Panel> first = ui.ShowPanelAsync<Phase1Panel>();
            DeferredAssets.Request request = assets.Next();

            Task<Phase1ConflictPanel> conflict = ui.ShowPanelAsync<Phase1ConflictPanel>();
            yield return Wait(conflict);

            Assert.IsNull(conflict.Result);
            Assert.IsTrue(log.Errors.Exists(value => value.Contains("不同面板类型")));

            request.Completion.SetResult(prefab);
            yield return Wait(first);
            Assert.IsNotNull(first.Result);
        }

        [UnityTest]
        public IEnumerator InactivePrefabActivatesBeforeOnOpen()
        {
            prefab.SetActive(false);
            Phase1Panel component = prefab.AddComponent<Phase1Panel>();
            component.CheckControl = true;
            GameObject button = new GameObject("Phase1Button", typeof(RectTransform), typeof(Button));
            button.transform.SetParent(prefab.transform, false);

            Task<Phase1Panel> task = ui.ShowPanelAsync<Phase1Panel>();
            assets.Next().Completion.SetResult(prefab);
            yield return Wait(task);

            Assert.IsNotNull(task.Result);
            Assert.IsTrue(task.Result.ControlFound);
            Assert.AreEqual(1, task.Result.Opened);
        }

        [UnityTest]
        public IEnumerator CustomCanvasWithoutLayersGetsThemCreated()
        {
            GameObject canvasObject = new GameObject("Phase1CustomCanvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            objects.Add(canvasObject);
            ui.ResetCanvas(canvasObject.GetComponent<Canvas>());

            Task<Phase1Panel> task = ui.ShowPanelAsync<Phase1Panel>();
            assets.Next().Completion.SetResult(prefab);
            yield return Wait(task);

            Assert.IsNotNull(task.Result);
            Assert.AreSame(ui.GetLayerFather(E_UI_Layer.Mid), task.Result.transform.parent);
        }

        [UnityTest]
        public IEnumerator WindowPlacementPreservesPrefabSize()
        {
            RectTransform rect = prefab.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(420f, 240f);

            Task<Phase1WindowPanel> task = ui.ShowPanelAsync<Phase1WindowPanel>();
            assets.Next().Completion.SetResult(prefab);
            yield return Wait(task);

            Assert.IsNotNull(task.Result);
            RectTransform panelRect = task.Result.transform as RectTransform;
            Assert.IsNotNull(panelRect);
            Assert.AreEqual(new Vector2(420f, 240f), panelRect.sizeDelta);
        }

        [UnityTest]
        public IEnumerator ServicesAreInjectedIntoPanelChildren()
        {
            GameObject child = new GameObject("Phase1Consumer");
            child.transform.SetParent(prefab.transform, false);
            Phase1Consumer consumer = child.AddComponent<Phase1Consumer>();

            FakeServices services = new FakeServices { Assets = assets, Log = log };
            ui.Configure(services);

            Task<Phase1Panel> task = ui.ShowPanelAsync<Phase1Panel>();
            assets.Next().Completion.SetResult(prefab);
            yield return Wait(task);

            Assert.IsNotNull(task.Result);
            Assert.AreSame(services, task.Result.Services);
            Phase1Consumer cloned = task.Result.GetComponentInChildren<Phase1Consumer>();
            Assert.IsNotNull(cloned);
            Assert.AreSame(services, cloned.Services);
            Assert.AreSame(consumer.GetType(), cloned.GetType());
        }

        [UnityTest]
        public IEnumerator UIServiceIsInjectedWithoutGameServices()
        {
            GameObject child = new GameObject("Phase1UiConsumer");
            child.transform.SetParent(prefab.transform, false);
            child.AddComponent<Phase1UiConsumer>();

            Task<Phase1Panel> task = ui.ShowPanelAsync<Phase1Panel>();
            assets.Next().Completion.SetResult(prefab);
            yield return Wait(task);

            Assert.IsNotNull(task.Result);
            Phase1UiConsumer cloned = task.Result.GetComponentInChildren<Phase1UiConsumer>();
            Assert.IsNotNull(cloned);
            Assert.AreSame(ui, cloned.UI);
        }

        [UnityTest]
        public IEnumerator ExternalDestroyRemovesRecordAndReleasesLease()
        {
            Task<Phase1Panel> task = ui.ShowPanelAsync<Phase1Panel>();
            assets.Next().Completion.SetResult(prefab);
            yield return Wait(task);
            Phase1Panel panel = task.Result;
            Assert.IsNotNull(panel);

            UnityEngine.Object.Destroy(panel.gameObject);
            yield return null;
            yield return null;

            Assert.IsNull(ui.GetPanel<Phase1Panel>());
            Assert.AreEqual(0, ui.DiagnosticOpenPanelCount);
            Assert.AreEqual(1, assets.Released);
            Assert.AreEqual(1, panel.Closed);
        }

        [UnityTest]
        public IEnumerator ShutdownCancelsPendingRequest()
        {
            Task<Phase1Panel> task = ui.ShowPanelAsync<Phase1Panel>();
            DeferredAssets.Request request = assets.Next();

            ui.Shutdown();
            Assert.IsTrue(request.Token.IsCancellationRequested);

            request.Completion.SetResult(prefab);
            yield return Wait(task);
            Assert.IsNull(task.Result);
            Assert.AreEqual(0, ui.DiagnosticOpenPanelCount);
            Assert.AreEqual(0, ui.DiagnosticPendingPanelCount);
        }
    }
}
