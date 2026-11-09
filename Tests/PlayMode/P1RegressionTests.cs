using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UPandaGF;
using UPandaGF.Gameplay;
using UPandaGF.Samples;

namespace UPandaGF.Tests
{
using Object = UnityEngine.Object;

public static class P1TestWait
{
    public static IEnumerator For(Task task)
    {
        float end = Time.realtimeSinceStartup + 15;
        while (!task.IsCompleted && Time.realtimeSinceStartup < end) yield return null;
        Assert.IsTrue(task.IsCompleted, "P1 operation did not settle");
    }
}
public class P1ScopeAndInputTests
{
    private struct Message { }
    private class Listener : IEventListener<Message> { public int Hits; public void OnEvent(Message message) { Hits++; } }
    private class Backend : IInputBackend
    {
        public float Axis(string axis) => .75f;
        public bool Held(KeyCode key) => key == KeyCode.Space || key == KeyCode.Return;
        public bool Down(KeyCode key) => Held(key);
    }
    [Test] public void ScopeCancelsBeforeReleasingInReverseEvenIfCleanupThrows()
    {
        var scope = new LifetimeScope("test"); var calls = new List<int>();
        scope.Own(new DisposableAction(() => { Assert.IsTrue(scope.Token.IsCancellationRequested); calls.Add(1); }));
        scope.Own(new DisposableAction(() => { calls.Add(2); throw new InvalidOperationException("probe"); }));
        Assert.Throws<AggregateException>(scope.Dispose); scope.Dispose();
        CollectionAssert.AreEqual(new[] { 2, 1 }, calls);
        int late = 0; Assert.Throws<ObjectDisposedException>(() => scope.Own(new DisposableAction(() => late++))); Assert.AreEqual(1, late);
    }
    [Test] public void ScopeUnsubscribesOnlyItsOwnListener()
    {
        var scope = new LifetimeScope("test"); var bus = new EventBus(); var a = new Listener(); var b = new Listener();
        scope.Subscribe(bus, a); bus.Subscribe(b); bus.Dispatch(new Message()); scope.Dispose(); bus.Dispatch(new Message());
        Assert.AreEqual(1, a.Hits); Assert.AreEqual(2, b.Hits); Assert.AreEqual(1, bus.GetSubscriptionCount<Message>());
    }
    private class LegacyMessage : EventArgBase { }
    [Test] public void ScopedSubscriptionDoesNotRemoveIdenticalExternalListener()
    {
        ILifetimeScope scope = new LifetimeScope("test"); var bus = new EventBus(); var listener = new Listener();
        bus.Subscribe(listener); scope.Subscribe(bus, listener); scope.Own(new DisposableAction(() => { }));
        ((IDisposable)scope).Dispose(); bus.Dispatch(new Message()); Assert.AreEqual(1, listener.Hits);
        var legacy = new EventCenter(); int hits = 0; UnityEngine.Events.UnityAction<LegacyMessage> callback = value => hits++;
        legacy.AddEventListener(callback); var other = new LifetimeScope("legacy"); ((ILifetimeScope)other).Subscribe(legacy, callback);
        other.Dispose(); legacy.EventTrigger(new LegacyMessage()); Assert.AreEqual(1, hits);
    }
    [Test] public void CancelingScopeRejectsAndReleasesNewOwnershipBeforeFinalDispose()
    {
        var scope = new LifetimeScope("Canceled"); scope.Cancel(); int releases = 0;
        Assert.Throws<OperationCanceledException>(() => scope.Own(new DisposableAction(() => releases++)));
        Assert.AreEqual(1, releases); scope.Dispose();
    }
    [Test] public void NestedPauseRestoresPriorScaleAndPreservesMenuInput()
    {
        float scale = Time.timeScale; bool audio = AudioListener.pause;
        using (var time = new GameTimeService())
        using (var input = new InputService(new Backend(), time))
        {
            try
            {
                Time.timeScale = .5f; AudioListener.pause = false;
                var a = time.Pause("Menu"); var b = time.Pause("Application");
                Assert.AreEqual(0, Time.timeScale); Assert.IsTrue(AudioListener.pause);
                Assert.AreEqual(0, input.GetAxis("MoveVertical")); Assert.IsFalse(input.GetButton("Handbrake"));
                Assert.IsTrue(input.GetButtonDown("Confirm", "UI"));
                a.Dispose(); Assert.IsTrue(time.IsPaused);
                time.SetTimeScale(.25f); b.Dispose(); b.Dispose();
                Assert.IsFalse(time.IsPaused); Assert.AreEqual(.25f, Time.timeScale); Assert.IsFalse(AudioListener.pause);
            }
            finally { Time.timeScale = scale; AudioListener.pause = audio; }
        }
    }
    [Test] public void InputContextsCanBeReleasedOutOfOrderAndBindingsAreSnapshots()
    {
        using (var input = new InputService(new Backend(), null))
        {
            var binding = new InputBinding { action = "Custom", positive = KeyCode.Space }; input.Bind(binding); binding.positive = KeyCode.None;
            Assert.IsTrue(input.GetButton("Custom"));
            var first = input.PushContext("UI"); var second = input.PushContext("Console"); first.Dispose();
            Assert.AreEqual(0, input.GetAxis("MoveVertical")); Assert.IsFalse(input.GetButtonDown("Confirm", "UI"));
            second.Dispose(); Assert.AreEqual(.75f, input.GetAxis("MoveVertical")); Assert.IsTrue(input.GetButton("Custom"));
        }
    }
    [Test] public void PauseDisposalRestoresGlobalStateAndInvalidScaleIsRejected()
    {
        float original = Time.timeScale; bool audio = AudioListener.pause;
        var time = new GameTimeService(); var handle = time.Pause();
        time.Dispose(); handle.Dispose(); Assert.AreEqual(original, Time.timeScale); Assert.AreEqual(audio, AudioListener.pause);
        Assert.Throws<ObjectDisposedException>(() => time.Pause());
        using (var valid = new GameTimeService()) Assert.Throws<ArgumentOutOfRangeException>(() => valid.SetTimeScale(float.NaN));
    }
    [UnityTest] public IEnumerator CanceledAcquireDisposesLateAssetExactlyOnce()
    {
        var native = new TaskCompletionSource<AssetLease<AudioClip>>(); int released = 0;
        var clip = AudioClip.Create("P1Late", 100, 1, 1000, false);
        using (var cts = new CancellationTokenSource())
        {
            var task = AssetService.WaitForLeaseAsync(native.Task, cts.Token); cts.Cancel(); yield return P1TestWait.For(task);
            Assert.IsTrue(task.IsCanceled);
            native.SetResult(new AssetLease<AudioClip>(clip, () => released++)); yield return null;
            Assert.AreEqual(1, released);
        }
        Object.DestroyImmediate(clip);
    }
    [Test] public void FrameworkModulesDoNotReferenceGameplayOrSamples()
    {
        Assert.AreEqual("UPandaGF.Contracts", typeof(IAssetService).Assembly.GetName().Name);
        var modules = new[]
        {
            typeof(IAssetService).Assembly, typeof(PLogger).Assembly, typeof(TimerService).Assembly,
            typeof(ABLoadMgr).Assembly, typeof(UIManager).Assembly, typeof(AudioMgr).Assembly,
            typeof(BinaryDataMgr).Assembly, typeof(UPGameRoot).Assembly
        };
        foreach (var assembly in modules)
            Assert.IsFalse(
                assembly.GetReferencedAssemblies().Any(x =>
                    x.Name == "UPandaGF.Samples" || x.Name == "UPandaGF.Gameplay" || x.Name.StartsWith("UPandaGF.Gameplay.")),
                assembly.GetName().Name + " 不应引用玩法/示例程序集");
        Assert.AreEqual("UPandaGF.Gameplay", typeof(VehicleInput).Assembly.GetName().Name);
        Assert.AreEqual("UPandaGF.Samples", typeof(GameLaunchExample).Assembly.GetName().Name);
    }
    [Test] public void ConfigSnapshotDoesNotShareNestedObjectsAndRejectsInvalidSettings()
    {
        var source = new UPGameRootConfig(); var snapshot = UPGameRootConfigFile.SnapshotAndValidate(source, true);
        source.inputBindings[0].axis = "Changed"; source.hotUpdate.retainedManifestCount = 7;
        Assert.AreEqual("Vertical", snapshot.inputBindings[0].axis); Assert.AreEqual(2, snapshot.hotUpdate.retainedManifestCount);
        source.schemaVersion = 999; source.downloadBatchTimeout = float.NaN;
        Assert.GreaterOrEqual(UPGameRootConfigFile.Validate(source, false).Count, 3);
        Assert.Throws<InvalidDataException>(() => UPGameRootConfigFile.SnapshotAndValidate(source, false));
    }
    [TestCase("../escape/")][TestCase("C:/outside/")][TestCase("/absolute/")]
    public void ConfigRejectsInvalidBundleRoot(string path)
    {
        var config = new UPGameRootConfig { method = AssetLoaddingMethod.AssetBundle, LoadAssetPath = path };
        Assert.IsNotEmpty(UPGameRootConfigFile.Validate(config, false, "StandaloneWindows64"));
    }
    [Test] public void ConfigChecksTargetUrlAesAndDuplicateActions()
    {
        var config = new UPGameRootConfig { method = AssetLoaddingMethod.AssetBundle, remoteURL = "file:///tmp", AssetAESConfig = new AssetBundleClassificationWindowConfig { enable = true, AESKEY = "bad" } };
        config.inputBindings = new[] { new InputBinding { action = "A", positive = KeyCode.Space }, new InputBinding { action = "A", positive = KeyCode.Space } };
        Assert.GreaterOrEqual(UPGameRootConfigFile.Validate(config, false, "Android").Count, 4);
    }
    [Serializable] public class SaveData { public int score; }
    [Test] public void SaveServiceRoundTripsAndRejectsTraversal()
    {
        string folder = Path.Combine(Application.temporaryCachePath, "P1Save_" + Guid.NewGuid().ToString("N"));
        try
        {
            ISaveService saves = new FileSaveService(folder); Assert.IsFalse(saves.Exists("one"));
            saves.Save("one", new SaveData { score = 42 }); Assert.AreEqual(42, saves.Load<SaveData>("one").score);
            string path = Path.Combine(folder, "one.save");
            Assert.IsTrue(File.Exists(path + ".bak"));
            Assert.IsNull(saves.Load<SaveData>("missing")); Assert.Throws<ArgumentException>(() => saves.Save("../escape", new SaveData()));
            Assert.IsTrue(saves.Delete("one")); Assert.IsFalse(saves.Exists("one")); Assert.IsFalse(File.Exists(path + ".bak"));
            Assert.IsNull(saves.Load<SaveData>("one")); Assert.IsFalse(saves.Delete("one"));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    private static void SimulateInterruptedOverwrite(string path, byte[] replacement)
    {
        string recovery = path + ".recovery";
        Directory.CreateDirectory(recovery);
        File.Copy(path, Path.Combine(recovery, "0.bak"), true);
        using (var stream = new FileStream(Path.Combine(recovery, "journal"), FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(1); writer.Write(path); writer.Write(true);
            writer.Flush(); stream.Flush(true);
        }
        File.WriteAllBytes(path, replacement);
    }
    [Test] public void SaveServiceRecoversInterruptedCommitBeforeLoad()
    {
        string folder = Path.Combine(Application.temporaryCachePath, "P1Save_" + Guid.NewGuid().ToString("N"));
        try
        {
            ISaveService saves = new FileSaveService(folder);
            saves.Save("one", new SaveData { score = 42 });
            string path = Path.Combine(folder, "one.save");
            SimulateInterruptedOverwrite(path, VersionedDataSerializer.Serialize(new SaveData { score = 99 }, "save"));
            Assert.AreEqual(42, saves.Load<SaveData>("one").score);
            Assert.IsFalse(File.Exists(path + ".recovery/journal"));
            Assert.AreEqual(42, saves.Load<SaveData>("one").score);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    [TestCase(false)] [TestCase(true)] public void SaveServiceRestoresBackupWhenPrimaryUnavailable(bool deletePrimary)
    {
        string folder = Path.Combine(Application.temporaryCachePath, "P1Save_" + Guid.NewGuid().ToString("N"));
        try
        {
            ISaveService saves = new FileSaveService(folder);
            saves.Save("one", new SaveData { score = 42 }); saves.Save("one", new SaveData { score = 43 });
            string path = Path.Combine(folder, "one.save");
            CollectionAssert.AreEqual(File.ReadAllBytes(path), File.ReadAllBytes(path + ".bak"));
            if (deletePrimary) File.Delete(path); else File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
            Assert.IsTrue(saves.Exists("one")); Assert.AreEqual(43, saves.Load<SaveData>("one").score);
            Assert.IsTrue(VersionedDataSerializer.IsVersioned(VersionedDataSerializer.ReadFile(path)));
            Assert.AreEqual(43, saves.Load<SaveData>("one").score);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    [Test] public void SaveServiceThrowsWhenPrimaryAndBackupAreBothCorrupt()
    {
        string folder = Path.Combine(Application.temporaryCachePath, "P1Save_" + Guid.NewGuid().ToString("N"));
        try
        {
            ISaveService saves = new FileSaveService(folder); saves.Save("one", new SaveData { score = 42 });
            string path = Path.Combine(folder, "one.save");
            File.WriteAllBytes(path, new byte[] { 1, 2, 3 }); File.WriteAllBytes(path + ".bak", new byte[] { 4, 5, 6 });
            Assert.Throws<InvalidDataException>(() => saves.Load<SaveData>("one"));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}

public class P1SceneCoordinationTests
{
    private sealed class Backend : ISceneBackend
    {
        public readonly List<TaskCompletionSource<Scene>> Loads = new List<TaskCompletionSource<Scene>>();
        public Task<Scene> LoadAsync(string path, LoadSceneMode mode, Action<float> progress)
        {
            var pending = new TaskCompletionSource<Scene>(); Loads.Add(pending); progress(.5f); return pending.Task;
        }
        public async Task UnloadAsync(Scene scene)
        {
            var native = SceneManager.UnloadSceneAsync(scene); while (native != null && !native.isDone) await Task.Yield();
        }
    }
    private sealed class Provider : ISceneAssetProvider
    {
        public int Held, Released;
        public Task<IDisposable> PrepareSceneAsync(string path, CancellationToken token)
        { Held++; return Task.FromResult<IDisposable>(new DisposableAction(() => Released++)); }
    }
    private SceneService service; private Backend backend; private Provider provider;
    private readonly List<Scene> created = new List<Scene>();
    [SetUp] public void Setup() { backend = new Backend(); provider = new Provider(); service = new SceneService(backend, provider); }
    private Scene Create() { var scene = SceneManager.CreateScene("P1Scene_" + Guid.NewGuid().ToString("N")); created.Add(scene); return scene; }
    private static IEnumerator Until(Func<bool> predicate)
    {
        float end = Time.realtimeSinceStartup + 10; while (!predicate() && Time.realtimeSinceStartup < end) yield return null; Assert.IsTrue(predicate());
    }
    [UnityTearDown] public IEnumerator Cleanup()
    {
        foreach (var load in backend.Loads) if (!load.Task.IsCompleted) load.TrySetCanceled();
        yield return P1TestWait.For(service.ShutdownAsync());
        foreach (var scene in created) if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        created.Clear();
    }
    [UnityTest] public IEnumerator CancelingNativeWaitDoesNotStartNextRequestEarly()
    {
        using (var cts = new CancellationTokenSource())
        {
            var first = service.LoadAsync("One", LoadSceneMode.Additive, cts.Token); yield return Until(() => backend.Loads.Count == 1);
            cts.Cancel(); yield return P1TestWait.For(first); Assert.IsTrue(first.IsCanceled);
            var second = service.LoadAsync("Two", LoadSceneMode.Additive); yield return null;
            Assert.AreEqual(1, backend.Loads.Count);
            var one = Create(); backend.Loads[0].SetResult(one); yield return Until(() => backend.Loads.Count == 2);
            Assert.AreEqual(0, provider.Released, "Canceled native scene must retain its real resources until unload");
            var two = Create(); backend.Loads[1].SetResult(two); yield return P1TestWait.For(second); Assert.IsFalse(second.IsFaulted);
            yield return P1TestWait.For(service.UnloadAsync(one)); Assert.AreEqual(1, provider.Released);
            yield return P1TestWait.For(service.UnloadAsync(two)); Assert.AreEqual(2, provider.Released);
        }
    }
    [UnityTest] public IEnumerator CanceledQueuedRequestNeverReachesBackend()
    {
        var first = service.LoadAsync("One"); yield return Until(() => backend.Loads.Count == 1);
        using (var cts = new CancellationTokenSource())
        {
            var queued = service.LoadAsync("Canceled", token: cts.Token); cts.Cancel(); yield return P1TestWait.For(queued);
            Assert.IsTrue(queued.IsCanceled); backend.Loads[0].SetResult(Create()); yield return P1TestWait.For(first); yield return null;
            Assert.AreEqual(1, backend.Loads.Count);
        }
    }
    [UnityTest] public IEnumerator ShutdownWaitsForNativeThenReleasesAllScopes()
    {
        var loading = service.LoadAsync("One"); yield return Until(() => backend.Loads.Count == 1);
        var stop = service.ShutdownAsync(); yield return null; Assert.IsFalse(stop.IsCompleted);
        backend.Loads[0].SetResult(Create()); yield return P1TestWait.For(stop);
        Assert.IsFalse(stop.IsFaulted); Assert.AreEqual(1, provider.Released); Assert.IsTrue(loading.IsCanceled);
        var late = service.LoadAsync("Late"); yield return P1TestWait.For(late); Assert.IsTrue(late.IsFaulted);
    }
    [UnityTest] public IEnumerator BackendFailureDoesNotPoisonQueueAndProgressHasUniqueIds()
    {
        var ids = new HashSet<long>(); service.Progress += p => ids.Add(p.RequestId);
        var first = service.LoadAsync("One"); var second = service.LoadAsync("Two");
        yield return Until(() => backend.Loads.Count == 1); backend.Loads[0].SetException(new IOException("probe"));
        yield return Until(() => backend.Loads.Count == 2); backend.Loads[1].SetResult(Create()); yield return P1TestWait.For(second);
        Assert.IsTrue(first.IsFaulted); Assert.IsFalse(second.IsFaulted); Assert.AreEqual(2, ids.Count); Assert.AreEqual(1, provider.Released);
    }
    [UnityTest] public IEnumerator ExternalSceneUnloadCancelsAndDisposesScope()
    {
        var scene = Create(); var scope = service.GetScope(scene); int closed = 0;
        scope.Own(new DisposableAction(() => closed++)); yield return SceneManager.UnloadSceneAsync(scene);
        Assert.IsTrue(scope.Token.IsCancellationRequested); Assert.IsTrue(scope.IsDisposed); Assert.AreEqual(1, closed);
    }
    [UnityTest] public IEnumerator TimeoutCancelsWaitButKeepsNativeSerialized()
    {
        var first = service.LoadAsync("One", timeoutSeconds: .02f); yield return Until(() => backend.Loads.Count == 1);
        yield return P1TestWait.For(first); Assert.IsTrue(first.IsCanceled);
        var second = service.LoadAsync("Two"); yield return null; Assert.AreEqual(1, backend.Loads.Count);
        backend.Loads[0].SetResult(Create()); yield return Until(() => backend.Loads.Count == 2);
        backend.Loads[1].SetResult(Create()); yield return P1TestWait.For(second); Assert.IsFalse(second.IsFaulted);
    }
}

[PrebuildSetup(typeof(LifecycleFixtures))]
[PostBuildCleanup(typeof(LifecycleFixtures))]
public class P1ResourceIntegrationTests
{
    private sealed class AssetsOnlyServices : IGameServices
    {
        public IAssetService Assets { get; }
        private readonly PoolService pools;
        public AssetsOnlyServices(IAssetService assets) { Assets = assets; pools = new PoolService(assets); }
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
        public IPoolService Pools => pools;
        public ILogService Log => null;
    }
    private string relative, folder; private LifecycleFixtureConfig fixtures;
    [SetUp] public void Setup()
    {
        fixtures = JsonUtility.FromJson<LifecycleFixtureConfig>(File.ReadAllText(LifecycleFixtureConfig.FilePath));
        relative = "P1Bundles_" + Guid.NewGuid().ToString("N"); folder = Path.Combine(Application.persistentDataPath, relative); Directory.CreateDirectory(folder);
    }
    [TearDown] public void Cleanup()
    {
        foreach (var go in TestObjectSearch.All<ABLoadMgr>()) Object.DestroyImmediate(go.gameObject);
        foreach (var go in TestObjectSearch.All<ResourcesLoader>()) Object.DestroyImmediate(go.gameObject);
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
    }
    private IEnumerator Initialize()
    {
        foreach (var name in new[] { "AuditBundles", "audit_target", "audit_shared" }) File.Copy(Path.Combine(fixtures.bundleDirectory, name), Path.Combine(folder, name), true);
        var task = ABLoadMgr.Instance.Init(relative, "AuditBundles", ABLoadPath.PersistentDataPath); yield return P1TestWait.For(task);
        Assert.IsTrue(ABLoadMgr.Instance.IsInitialized);
    }
    private static bool Loaded(string name) => AssetBundle.GetAllLoadedAssetBundles().Any(x => x.name == name);
    [UnityTest] public IEnumerator TwoManagedHoldersProtectTargetAndSharedDependencyUntilLastRelease()
    {
        yield return Initialize(); var manager = ABLoadMgr.Instance;
        var a = manager.AcquireAsync<GameObject>("audit_target", "Box", ABLoadPath.PersistentDataPath);
        var b = manager.AcquireAsync<GameObject>("audit_target", "Box", ABLoadPath.PersistentDataPath);
        yield return P1TestWait.For(Task.WhenAll(a, b)); Assert.IsNotNull(a.Result); Assert.IsNotNull(b.Result);
        Assert.AreEqual(2, manager.GetLeaseCount("audit_shared")); Assert.IsFalse(manager.UnLoadAB("audit_shared"));
        a.Result.Dispose(); Assert.IsTrue(Loaded("audit_shared")); Assert.AreEqual(1, manager.GetLeaseCount("audit_target"));
        b.Result.Dispose(); yield return null;
        Assert.IsFalse(Loaded("audit_target")); Assert.IsFalse(Loaded("audit_shared")); Assert.IsTrue(manager.IsInitialized);
    }
    [UnityTest] public IEnumerator OldGenerationLeaseCannotDecrementNewBundlePins()
    {
        yield return Initialize(); var manager = ABLoadMgr.Instance;
        var old = manager.AcquireAsync<GameObject>("audit_target", "Box", ABLoadPath.PersistentDataPath); yield return P1TestWait.For(old);
        var reset = manager.Init(relative, "AuditBundles", ABLoadPath.PersistentDataPath); yield return P1TestWait.For(reset);
        var current = manager.AcquireAsync<GameObject>("audit_target", "Box", ABLoadPath.PersistentDataPath); yield return P1TestWait.For(current);
        old.Result.Dispose(); Assert.AreEqual(1, manager.GetLeaseCount("audit_target")); Assert.IsTrue(Loaded("audit_target")); current.Result.Dispose();
    }
    [UnityTest] public IEnumerator LegacyRawResourceRemainsExplicitlyOwnedWhenManagedLeaseEnds()
    {
        yield return Initialize(); var manager = ABLoadMgr.Instance;
        var raw = manager.LoadResAsync<GameObject>("audit_target", "Box", ABLoadPath.PersistentDataPath); yield return P1TestWait.For(raw);
        var owned = manager.AcquireAsync<GameObject>("audit_target", "Box", ABLoadPath.PersistentDataPath); yield return P1TestWait.For(owned);
        owned.Result.Dispose(); yield return null; Assert.IsTrue(Loaded("audit_target")); Assert.IsTrue(Loaded("audit_shared"));
        // 旧接口占用纳入租约模型：有 legacy pin 时 UnLoadAB 应拒绝，必须先显式释放。
        Assert.IsTrue(manager.IsLegacyPinned("audit_target"));
        Assert.IsFalse(manager.UnLoadAB("audit_target"));
        Assert.GreaterOrEqual(manager.ReleaseLegacyBundles(), 1);
        Assert.IsFalse(manager.IsLegacyPinned("audit_target"));
        Assert.IsTrue(manager.UnLoadAB("audit_target"));
    }
    [UnityTest] public IEnumerator LegacyLoadPinsBundleUntilReleaseLegacyBundles()
    {
        yield return Initialize(); var manager = ABLoadMgr.Instance;
        var legacy = manager.GetAssetBundle("audit_target", ABLoadPath.PersistentDataPath); yield return P1TestWait.For(legacy);
        Assert.IsNotNull(legacy.Result);
        Assert.IsTrue(manager.IsLegacyPinned("audit_target"));
        Assert.IsTrue(manager.IsLegacyPinned("audit_shared"), "依赖包也必须被 legacy pin 保护");
        Assert.GreaterOrEqual(manager.GetDiagnostics().LegacyPins, 2);
        Assert.IsFalse(manager.UnLoadAB("audit_target"));
        Assert.GreaterOrEqual(manager.ReleaseLegacyBundles(), 2);
        Assert.AreEqual(0, manager.GetDiagnostics().LegacyPins);
        Assert.IsFalse(manager.IsLegacyPinned("audit_shared"));
    }
    [UnityTest] public IEnumerator LegacySceneNamePreparationFindsUniqueFullManifestPath()
    {
        yield return Initialize();
        var go = new GameObject("P1LegacySceneLoader"); var loader = go.AddComponent<AssetsLoader>();
        try
        {
            var refs = new ABSourcesRelated { mainBundleInfo = new AssetBundleLoadInfo { bundleName = "AuditBundles", loadPath = ABLoadPath.PersistentDataPath } };
            refs.bundleInfo["audit_target"] = new AssetBundleLoadInfo { bundleName = "audit_target", loadPath = ABLoadPath.PersistentDataPath };
            refs.bundleInfo["audit_shared"] = new AssetBundleLoadInfo { bundleName = "audit_shared", loadPath = ABLoadPath.PersistentDataPath };
            refs.sourcesDic["Assets/Scenes/P1Legacy.unity"] = new AssetRelatedArg("audit_target", "Box");
            var init = loader.Init(AssetLoaddingMethod.AssetBundle, "http://localhost/", relative, refs); yield return P1TestWait.For(init); Assert.IsTrue(loader.IsInitialized);
            var prepared = loader.PrepareSceneAsync("P1Legacy", CancellationToken.None); yield return P1TestWait.For(prepared);
            Assert.IsFalse(prepared.IsFaulted, prepared.Exception?.ToString()); Assert.AreEqual(1, ABLoadMgr.Instance.GetLeaseCount("audit_target"));
            prepared.Result.Dispose(); Assert.AreEqual(0, ABLoadMgr.Instance.GetLeaseCount("audit_target"));
        }
        finally { Object.DestroyImmediate(go); }
    }
    [UnityTest] public IEnumerator MissingAssetDoesNotKeepDependencyPinned()
    {
        yield return Initialize(); var manager = ABLoadMgr.Instance;
        var missing = manager.AcquireAsync<GameObject>("audit_target", "P1DoesNotExist", ABLoadPath.PersistentDataPath); yield return P1TestWait.For(missing);
        Assert.IsNull(missing.Result); yield return null; Assert.AreEqual(0, manager.GetLeaseCount("audit_shared")); Assert.IsFalse(Loaded("audit_target"));
    }
    [UnityTest] public IEnumerator SceneScopePoolAndAudioUseInjectedResourceOwnership()
    {
        var resources = ResourcesLoader.Instance; var assets = new AssetService(resources, null); var scope = new LifetimeScope("Scene");
        var pool = new AssetsOnlyServices(assets).CreatePool(scope);
        var load = pool.GetObjAsync(fixtures.resourceKey + "/AudioPrefab"); yield return P1TestWait.For(load); Assert.IsNotNull(load.Result);
        var active = pool.GetObjAsync(fixtures.resourceKey + "/AudioPrefab"); yield return P1TestWait.For(active);
        pool.PushObj(fixtures.resourceKey + "/AudioPrefab", load.Result);
        scope.Dispose(); yield return null; Assert.IsTrue(load.Result == null); Assert.IsTrue(active.Result == null);
        Assert.AreEqual(0, resources.GetAssetsRefCount<GameObject>(fixtures.resourceKey + "/AudioPrefab"));
        var audio = new AudioMgr(assets);
        try
        {
            var clip = new TaskCompletionSource<AudioSource>();
            audio.LoadSoundAndPlay(fixtures.resourceKey + "/Clip", true, callBack: source => clip.SetResult(source));
            yield return P1TestWait.For(clip.Task); Assert.IsNotNull(clip.Task.Result); audio.StopAllSound(); yield return null; Assert.AreEqual(0, audio.PlayingSoundCount);
        }
        finally { audio.Dispose(); }
    }
}

[UILoadInfo(E_UI_Layer.Mid, AssetLoadMethod.Resources, "P1/Panel")]
public class P1TestPanel : BasePanel
{
    public int Closed;
    public override void OnOpen(object arg = null) { }
    public override void OnClose() { Closed++; }
}
public class P1UIOwnershipTests
{
    private sealed class Assets : IAssetService
    {
        public readonly Queue<TaskCompletionSource<Object>> Requests = new Queue<TaskCompletionSource<Object>>();
        public int Released;
        public async Task<AssetLease<T>> AcquireAsync<T>(string path, AssetLoadMethod method = AssetLoadMethod.Resources, CancellationToken token = default) where T : Object
        {
            var request = new TaskCompletionSource<Object>(); Requests.Enqueue(request);
            var asset = await request.Task as T;
            return asset != null ? new AssetLease<T>(asset, () => Released++) : null;
        }
    }
    private UIManager ui; private GameObject prefab; private Assets assets;
    [SetUp] public void Setup()
    {
        ui = new GameObject("P1UI").AddComponent<UIManager>(); ui.Init(); assets = new Assets(); ui.Configure(assets);
        prefab = new GameObject("P1PanelPrefab", typeof(RectTransform));
    }
    [UnityTearDown] public IEnumerator Cleanup()
    {
        Object.DestroyImmediate(ui.gameObject); Object.DestroyImmediate(prefab); yield return null;
    }
    [UnityTest] public IEnumerator DisposingSceneScopeClosesPanelAndReturnsPrefabLease()
    {
        var scope = new LifetimeScope("UI");
        var task = ui.ShowPanelInScopeAsync<P1TestPanel>(scope); assets.Requests.Dequeue().SetResult(prefab);
        yield return P1TestWait.For(task); var panel = task.Result; Assert.IsNotNull(panel);
        scope.Dispose(); Assert.AreEqual(1, panel.Closed); yield return null;
        Assert.IsTrue(panel == null); Assert.AreEqual(1, assets.Released); Assert.IsNull(ui.GetPanel<P1TestPanel>());
    }
    [UnityTest] public IEnumerator CanceledPendingScopeCannotCloseReopenedPanelAndLateLeaseIsReturned()
    {
        var oldScope = new LifetimeScope("Old"); var old = ui.ShowPanelInScopeAsync<P1TestPanel>(oldScope); var oldRequest = assets.Requests.Dequeue();
        oldScope.Dispose(); yield return P1TestWait.For(old); Assert.IsNull(old.Result);
        var currentScope = new LifetimeScope("Current"); var current = ui.ShowPanelInScopeAsync<P1TestPanel>(currentScope);
        assets.Requests.Dequeue().SetResult(prefab); yield return P1TestWait.For(current); Assert.IsNotNull(current.Result);
        oldRequest.SetResult(prefab); yield return null; Assert.AreEqual(1, assets.Released); Assert.AreSame(current.Result, ui.GetPanel<P1TestPanel>());
        currentScope.Dispose(); yield return null; Assert.AreEqual(2, assets.Released);
    }
    [UnityTest] public IEnumerator ShutdownClosesOpenPanelsAndReleasesTheirLeases()
    {
        var scope = new LifetimeScope("ShutdownUI");
        var task = ui.ShowPanelInScopeAsync<P1TestPanel>(scope);
        assets.Requests.Dequeue().SetResult(prefab);
        yield return P1TestWait.For(task); var panel = task.Result; Assert.IsNotNull(panel);
        ui.Shutdown(); yield return null;
        Assert.AreEqual(1, panel.Closed);
        Assert.AreEqual(1, assets.Released);
        Assert.IsNull(ui.GetPanel<P1TestPanel>());
    }
}

[PrebuildSetup(typeof(CoordinationFixtures))]
[PostBuildCleanup(typeof(CoordinationFixtures))]
public class P1NativeSceneTests
{
    [UnityTest] public IEnumerator LegacyLoaderUsesManagedFullPathAndPreservesCallbackOrder()
    {
        var go = new GameObject("P1ManagedLoader"); var loader = go.AddComponent<AssetsLoader>();
        var service = new SceneService(new UnitySceneBackend(), loader);
        Scene loaded = default;
        try
        {
            var init = loader.Init(AssetLoaddingMethod.Editor, "http://localhost/", "Unused", null); yield return P1TestWait.For(init);
            loader.ConfigureScenes(service);
            var done = new TaskCompletionSource<bool>(); var callbacks = new List<string>();
            loader.LoadSceneAsync(CoordinationFixtures.ScenePath, LoadSceneMode.Additive,
                () => callbacks.Add("assets"), () => { callbacks.Add("scene"); done.TrySetResult(true); });
            yield return P1TestWait.For(done.Task); CollectionAssert.AreEqual(new[] { "assets", "scene" }, callbacks);
            loaded = SceneManager.GetSceneByPath(CoordinationFixtures.ScenePath); Assert.IsTrue(loaded.isLoaded);
            var scope = service.GetScope(loaded); yield return P1TestWait.For(service.UnloadAsync(loaded)); Assert.IsTrue(scope.IsDisposed);
        }
        finally { _ = service.ShutdownAsync(); Object.DestroyImmediate(go); }
        if (loaded.IsValid() && loaded.isLoaded) yield return SceneManager.UnloadSceneAsync(loaded);
    }
    [UnityTest] public IEnumerator NativeAdditiveLoadsHaveDistinctScopesAndUnloadCleansOwnership()
    {
        var service = new SceneService(new UnitySceneBackend());
        var loaded = new List<Scene>();
        try
        {
            var a = service.LoadAsync(CoordinationFixtures.ScenePath, LoadSceneMode.Additive);
            var b = service.LoadAsync(CoordinationFixtures.ScenePath, LoadSceneMode.Additive);
            yield return P1TestWait.For(Task.WhenAll(a, b));
            Assert.IsFalse(a.IsFaulted, a.Exception?.ToString()); Assert.IsFalse(b.IsFaulted, b.Exception?.ToString());
            loaded.Add(a.Result); loaded.Add(b.Result); Assert.AreNotEqual(a.Result.handle, b.Result.handle);
            var scope = service.GetScope(a.Result); int released = 0; scope.Own(new DisposableAction(() => released++));
            yield return P1TestWait.For(service.UnloadAsync(a.Result)); Assert.IsTrue(scope.IsDisposed); Assert.AreEqual(1, released);
            Assert.IsFalse(service.GetScope(b.Result).IsDisposed);
            yield return P1TestWait.For(service.UnloadAsync(b.Result));
        }
        finally { _ = service.ShutdownAsync(); }
        foreach (var scene in loaded) if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
    }
}

public class P1TestConsumer : MonoBehaviour, IGameServicesConsumer
{
    public IGameServices Services;
    public void Configure(IGameServices services) { Services = services; }
}
public class P1RootCompositionTests
{
    [UnityTest] public IEnumerator RootInjectsConsumerFreezesConfigAndShutdownRestoresPause()
    {
        string directory = Path.Combine(Application.streamingAssetsPath, "Data"); Directory.CreateDirectory(directory);
        var original = new Dictionary<string, byte[]>();
        foreach (string name in new[] { "GameRootConfig.json", "LogConfig.json" })
        { string path = Path.Combine(directory, name); original[path] = File.Exists(path) ? File.ReadAllBytes(path) : null; }
        File.WriteAllText(Path.Combine(directory, "GameRootConfig.json"), UPGameRootConfigFile.ToJson(new UPGameRootConfig()));
        File.WriteAllText(Path.Combine(directory, "LogConfig.json"), JsonUtility.ToJson(new LogConfig()));
        var consumerObject = new GameObject("P1Consumer"); var consumer = consumerObject.AddComponent<P1TestConsumer>();
        var rootObject = new GameObject("P1Root"); var root = rootObject.AddComponent<UPGameRoot>();
        float previousScale = Time.timeScale; bool previousAudio = AudioListener.pause;
        try
        {
            var start = root.WaitUntilReadyAsync(); yield return P1TestWait.For(start); Assert.IsFalse(start.IsFaulted, start.Exception?.ToString());
            Assert.AreSame(root.Services, consumer.Services); Assert.IsNotNull(root.Services.Audio); Assert.IsNotNull(root.Services.Saves);
            Assert.IsNotNull(root.Services.Http); Assert.IsNotNull(root.Services.Pools); Assert.IsNotNull(root.Services.Log);
            var snapshot = root.Config; snapshot.remoteURL = "https://changed.invalid"; snapshot.inputBindings[0].action = "Changed";
            Assert.AreNotEqual(snapshot.remoteURL, root.Config.remoteURL); Assert.AreEqual("MoveVertical", root.Config.inputBindings[0].action);
            Assert.Throws<InvalidOperationException>(() => root.SetConfig(new UPGameRootConfig()));
            var menuObject = new GameObject("P1Menu"); var menu = menuObject.AddComponent<PauseMenuController>(); menu.Configure(root.Services);
            menu.Open(); Assert.IsTrue(root.Services.Pause.IsPaused); menu.Close(); Assert.IsFalse(root.Services.Pause.IsPaused);
            menu.Open(); Object.DestroyImmediate(menuObject); Assert.IsFalse(root.Services.Pause.IsPaused);
            var sceneScope = root.Services.Scenes.GetScope(consumerObject.scene); var heldPause = sceneScope.Own(root.Services.Pause.Pause());
            var stop = root.ShutdownAsync(); yield return P1TestWait.For(stop); Assert.IsFalse(stop.IsFaulted, stop.Exception?.ToString());
            yield return null;
            Assert.IsNull(UnityEngine.Object.FindObjectOfType<HttpManager>());   // 容器创建的 HTTP 宿主随关闭销毁
            Assert.IsTrue(sceneScope.IsDisposed); Assert.AreEqual(previousScale, Time.timeScale); Assert.AreEqual(previousAudio, AudioListener.pause);
            heldPause.Dispose(); Assert.Throws<InvalidOperationException>(() => { var unused = root.Services; });
        }
        finally
        {
            Object.DestroyImmediate(rootObject); Object.DestroyImmediate(consumerObject);
            Time.timeScale = previousScale; AudioListener.pause = previousAudio;
            foreach (var pair in original) if (pair.Value == null) File.Delete(pair.Key); else File.WriteAllBytes(pair.Key, pair.Value);
        }
    }
}

}