using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using ReplaySystem;
using UnityEngine;
using UnityEngine.TestTools;
using UPandaGF;
using Object = UnityEngine.Object;

public class FrameworkRegressionTests
{
    private string directory;
    private LogConfig previousLogConfig;
    private readonly List<GameObject> objects = new List<GameObject>();
    [SetUp] public void SetUp()
    {
        previousLogConfig = PLogger.cfg;
        PLogger.cfg = new LogConfig();
        directory = Path.Combine(Application.temporaryCachePath, "UPandaGFTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
    }
    [TearDown] public void TearDown()
    {
        foreach (var go in objects) if (go != null) Object.DestroyImmediate(go);
        objects.Clear();
        PLogger.cfg = previousLogConfig;
        // 仅清理本测试新建的随机临时目录。
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
    private T Component<T>() where T : Component
    {
        var go = new GameObject(typeof(T).Name);
        objects.Add(go);
        return typeof(T) == typeof(Transform) ? go.transform as T : go.AddComponent<T>();
    }
    private static IEnumerator Wait(Task task)
    {
        float deadline = Time.realtimeSinceStartup + 10f;
        while (!task.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.IsTrue(task.IsCompleted, "异步请求没有结束");
        if (task.IsFaulted) throw task.Exception;
    }
    [UnityTest] public IEnumerator MissingResourceCanBeRequestedAgain()
    {
        var loader = Component<ResourcesLoader>();
        for (int i = 0; i < 2; i++)
        {
            var done = new TaskCompletionSource<GameObject>();
            LogAssert.Expect(LogType.Error, new Regex("资源加载失败"));
            loader.LoadAsync<GameObject>("__missing_framework_test__", value => done.TrySetResult(value));
            yield return Wait(done.Task);
            Assert.IsNull(done.Task.Result);
            Assert.AreEqual(0, loader.GetAssetsRefCount<GameObject>("__missing_framework_test__"));
        }
    }
    [UnityTest] public IEnumerator ThrowingResourceCallbackDoesNotBlockOtherWaiters()
    {
        var loader = Component<ResourcesLoader>();
        var done = new TaskCompletionSource<GameObject>();
        LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: expected callback failure"));
        loader.LoadAsync<GameObject>("UI/Canvas", value => throw new InvalidOperationException("expected callback failure"));
        loader.LoadAsync<GameObject>("UI/Canvas", value => done.TrySetResult(value));
        yield return Wait(done.Task);
        Assert.IsNotNull(done.Task.Result);
        Assert.AreEqual(2, loader.GetAssetsRefCount<GameObject>("UI/Canvas"));
        loader.UnLoadAsset<GameObject>("UI/Canvas");
        loader.UnLoadAsset<GameObject>("UI/Canvas", true);
        Assert.IsNotNull(Resources.Load<GameObject>("UI/Canvas"), "释放不能 Destroy prefab 资源");
    }
    [UnityTest] public IEnumerator ClearCancelsWaiterAndDoesNotCorruptNewLoad()
    {
        var loader = Component<ResourcesLoader>();
        var canceled = new TaskCompletionSource<GameObject>();
        loader.LoadAsync<GameObject>("UI/Canvas", value => canceled.TrySetResult(value));
        loader.ClearDic();
        yield return Wait(canceled.Task);
        Assert.IsNull(canceled.Task.Result);
        var fresh = new TaskCompletionSource<GameObject>();
        loader.LoadAsync<GameObject>("UI/Canvas", value => fresh.TrySetResult(value));
        yield return Wait(fresh.Task);
        Assert.IsNotNull(fresh.Task.Result);
        Assert.AreEqual(1, loader.GetAssetsRefCount<GameObject>("UI/Canvas"));
    }
    [Test] public void TypeAndGenericReleaseKeepCacheUntilRequested()
    {
        var loader = Component<ResourcesLoader>();
        var a = loader.Load<GameObject>("UI/Canvas");
        var b = loader.Load("UI/Canvas", typeof(GameObject));
        Assert.AreSame(a, b);
        loader.UnLoadAsset("UI/Canvas", typeof(GameObject), false);
        Assert.AreEqual(0, loader.GetAssetsRefCount("UI/Canvas", typeof(GameObject)));
        Assert.AreSame(b, loader.Load("UI/Canvas", typeof(GameObject)));
        Assert.AreEqual(1, loader.GetAssetsRefCount<GameObject>("UI/Canvas"));
    }
    [UnityTest] public IEnumerator ConcurrentPanelRequestsShareInstanceAndReleaseReference()
    {
        var loader = Component<ResourcesLoader>();
        var manager = Component<UIManager>();
        manager.Init();
        manager.Configure(new AssetService(loader, null));
        var eventSystem = Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>();
        if (eventSystem != null) objects.Add(eventSystem.gameObject);
        var first = manager.ShowPanelAsync<FrameworkTestPanel>();
        var second = manager.ShowPanelAsync<FrameworkTestPanel>();
        yield return Wait(Task.WhenAll(first, second));
        Assert.IsNotNull(first.Result);
        Assert.AreSame(first.Result, second.Result);
        Assert.AreEqual(1, loader.GetAssetsRefCount<GameObject>("UI/Canvas"));
        manager.ClosePanel<FrameworkTestPanel>();
        yield return null;
        Assert.AreEqual(0, loader.GetAssetsRefCount<GameObject>("UI/Canvas"));
    }
    [UnityTest] public IEnumerator CloseWhileLoadingPreventsLatePanelCreation()
    {
        var loader = Component<ResourcesLoader>();
        var manager = Component<UIManager>();
        manager.Init();
        manager.Configure(new AssetService(loader, null));
        var eventSystem = Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>();
        if (eventSystem != null) objects.Add(eventSystem.gameObject);
        var pending = manager.ShowPanelAsync<FrameworkTestPanel>();
        manager.ClosePanel<FrameworkTestPanel>();
        yield return Wait(pending);
        Assert.IsNull(pending.Result);
        yield return null;
        yield return null;
        Assert.IsFalse(manager.panelDic.ContainsKey("UI/Canvas"));
        Assert.AreEqual(0, loader.GetAssetsRefCount<GameObject>("UI/Canvas"));
    }
    private static List<RecordFrame> SampleFrames()
    {
        var snapshot = new ObjectSnapshot(42, Vector3.one, Quaternion.identity, Vector3.one) { objectName = "sample" };
        snapshot.SetComponentData("RigidbodyVelocity", new Vector3(1, 2, 3));
        return new List<RecordFrame> { new RecordFrame(0) { snapshots = new List<ObjectSnapshot> { snapshot } } };
    }
    [Test] public void ReplayRoundTripPreservesComponentState()
    {
        string path = ReplaySystem.FileManager.Save(SampleFrames());
        Assert.IsNotNull(path);
        try
        {
            Assert.IsTrue(File.ReadAllText(path).StartsWith("UPANDA_REPLAY:2"));
            var frames = ReplaySystem.FileManager.Load(path);
            Assert.IsNotNull(frames);
            Assert.AreEqual(new Vector3(1, 2, 3), frames[0].snapshots[0].componentData["RigidbodyVelocity"]);
        }
        finally { File.Delete(path); }
    }
    [Test] public void LegacyReplayDtoRemainsReadable()
    {
        string path = Path.Combine(directory, "legacy.dat");
        var file = new SerializableRecordFile { frames = new[] { new SerializableFrame {
            time = 0, snapshots = new[] { new SerializableSnapshot { instanceID = 1, rw = 1, sx = 1, sy = 1, sz = 1 } }
        } } };
#pragma warning disable SYSLIB0011
        using (var stream = File.Create(path)) new BinaryFormatter().Serialize(stream, file);
#pragma warning restore SYSLIB0011
        Assert.AreEqual(1, ReplaySystem.FileManager.Load(path).Count);
    }
    [Test] public void CorruptReplayReturnsNull()
    {
        string path = Path.Combine(directory, "corrupt.dat");
        File.WriteAllText(path, "broken");
        Assert.IsNull(ReplaySystem.FileManager.Load(path));
        File.WriteAllText(path, "UPANDA_REPLAY:2\n{\"version\":2,\"frames\":[{\"time\":0}]}");
        Assert.IsNull(ReplaySystem.FileManager.Load(path));
    }
    private static void Transaction(string method, params object[] args)
    {
        try
        {
            typeof(ResourcesLoader).Assembly.GetType("UPandaGF.FileUpdateTransaction")
                .GetMethod(method, BindingFlags.Public | BindingFlags.Static).Invoke(null, args);
        }
        catch (TargetInvocationException e) { throw e.InnerException; }
    }
    [Test] public void UpdateBatchRollsBackIfLaterFileIsLocked()
    {
        string old = Path.Combine(directory, "old");
        string locked = Path.Combine(directory, "locked");
        string first = Path.Combine(directory, "first.stage");
        string second = Path.Combine(directory, "second.stage");
        File.WriteAllText(old, "old"); File.WriteAllText(locked, "original");
        File.WriteAllText(first, "new"); File.WriteAllText(second, "replacement");
        // 允许备份读取，禁止覆盖第二个文件，强制在第一个文件已提交后失败。
        using (File.Open(locked, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.Throws<IOException>(() => Transaction("Commit", new[] { first, second }, new[] { old, locked }, Path.Combine(directory, "recovery")));
        // 被锁文件也阻止了即时回滚；模拟下次启动时恢复。
        Transaction("Recover", Path.Combine(directory, "recovery"));
        Assert.AreEqual("old", File.ReadAllText(old));
        Assert.AreEqual("original", File.ReadAllText(locked));
    }
    [Test] public void UpdateBatchCommitsFilesAndManifest()
    {
        string stage = Path.Combine(directory, "stage");
        string target = Path.Combine(directory, "target");
        File.WriteAllText(stage, "new");
        Transaction("Commit", new[] { stage }, new[] { target }, Path.Combine(directory, "recovery"));
        Assert.AreEqual("new", File.ReadAllText(target));
        Transaction("Recover", Path.Combine(directory, "recovery"));
        Assert.AreEqual("new", File.ReadAllText(target));
    }
    [Test] public void SeekRestoresObjectsAbsentFromAdjacentSparseFrames()
    {
        var manager = Component<ReplayManager>();
        var a = Component<Transform>();
        var b = Component<Transform>();
        var frames = (List<RecordFrame>)typeof(ReplayManager).GetField("frames", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(manager);
        frames.Add(new RecordFrame(0) { snapshots = new List<ObjectSnapshot> {
            new ObjectSnapshot(a.gameObject.GetInstanceID(), Vector3.zero, Quaternion.identity, Vector3.one) { objectName = a.name },
            new ObjectSnapshot(b.gameObject.GetInstanceID(), Vector3.one, Quaternion.identity, Vector3.one) { objectName = b.name }
        } });
        frames.Add(new RecordFrame(1) { snapshots = new List<ObjectSnapshot> {
            new ObjectSnapshot(a.gameObject.GetInstanceID(), Vector3.right * 10, Quaternion.identity, Vector3.one) { objectName = a.name }
        } });
        frames.Add(new RecordFrame(2));
        var cache = (Dictionary<int, Transform>)typeof(ReplayManager).GetField("transformCache", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(manager);
        cache[a.gameObject.GetInstanceID()] = a; cache[b.gameObject.GetInstanceID()] = b;
        a.localPosition = b.localPosition = Vector3.one * 99;
        manager.Seek(1f);
        Assert.AreEqual(Vector3.right * 10, a.localPosition);
        Assert.AreEqual(Vector3.one, b.localPosition);
        manager.Seek(0f);
        Assert.AreEqual(Vector3.zero, a.localPosition);
    }
    [UnityTest] public IEnumerator CanceledDownloadCompletesWithTimeoutDisabled()
    {
        var downloader = Component<Downloader>();
        downloader.maxConcurrentDownloads = 0;
        downloader.defaultTimeout = 0;
        var pending = downloader.DownloadAsync("http://127.0.0.1:1/test", Path.Combine(directory, "download"));
        downloader.CancelAllDownloads();
        yield return Wait(pending);
        Assert.IsFalse(pending.Result);
    }
    [UnityTest] public IEnumerator DestroyedDownloaderCompletesPendingTask()
    {
        var downloader = Component<Downloader>();
        downloader.maxConcurrentDownloads = 0;
        downloader.defaultTimeout = 0;
        var pending = downloader.DownloadAsync("http://127.0.0.1:1/test", Path.Combine(directory, "download"));
        Object.DestroyImmediate(downloader.gameObject);
        yield return Wait(pending);
        Assert.IsFalse(pending.Result);
    }
    [UnityTest] public IEnumerator AssetInitializationFailureDoesNotReportReady()
    {
        var loader = Component<AssetsLoader>();
        LogAssert.Expect(LogType.Error, new Regex("资源关联数据缺少主包信息"));
        yield return Wait(loader.Init(AssetLoaddingMethod.Assetbundles, "", "", new ABSourcesRelated()));
        Assert.IsFalse(loader.IsInitialized);
        bool called = false;
        LogAssert.Expect(LogType.Error, new Regex("该资源不存在"));
        loader.LoadAsync<GameObject>("missing", value => { called = true; Assert.IsNull(value); });
        Assert.IsTrue(called, "失败也必须结束回调请求");
    }
}
