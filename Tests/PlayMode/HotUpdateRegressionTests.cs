using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UPandaGF;
using Object = UnityEngine.Object;

public class HotUpdateRegressionTests
{
    private string directory;
    private readonly List<HotUpdateStorage> stores = new List<HotUpdateStorage>();
    private readonly List<GameObject> objects = new List<GameObject>();
    private LogConfig logging;
    private const string Root = "AssetBundles/Test";
    [SetUp] public void Setup()
    {
        logging = PLogger.cfg; PLogger.cfg = new LogConfig();
        directory = Path.Combine(Application.temporaryCachePath, "UPandaHotUpdate", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
    }
    [TearDown] public void Cleanup()
    {
        Time.timeScale = 1;
        foreach (var go in objects) if (go != null) Object.DestroyImmediate(go); objects.Clear();
        foreach (var store in stores) store.DisposeAsync().GetAwaiter().GetResult(); stores.Clear();
        PLogger.cfg = logging;
        if (Path.GetFullPath(directory).StartsWith(Path.GetFullPath(Path.Combine(Application.temporaryCachePath, "UPandaHotUpdate")) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) Directory.Delete(directory, true);
    }
    private static T Result<T>(Task<T> task) { return task.GetAwaiter().GetResult(); }
    private static void Done(Task task) { task.GetAwaiter().GetResult(); }
    private HotUpdateStorage Open(HotUpdateSettings options = null, CancellationToken token = default, Func<string, long?> space = null, string root = Root)
    {
        var store = Result(HotUpdateStorage.OpenAsync(directory, root, options ?? new HotUpdateSettings { minimumFreeSpaceMB = 0, retainedManifestCount = 1 }, token, space)); stores.Add(store); return store;
    }
    private static string Md5(byte[] bytes) { using (var md5 = MD5.Create()) return BitConverter.ToString(md5.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    private static ABSourcesRelated Manifest(params (string name, byte[] bytes)[] files)
    {
        var data = new ABSourcesRelated { mainBundleInfo = new AssetBundleLoadInfo { bundleName = "Main", loadPath = ABLoadPath.StreamingAssetsPath } };
        foreach (var file in files) data.bundleInfo.Add(file.name, new AssetBundleLoadInfo { bundleName = file.name, size = file.bytes.LongLength, md5 = Md5(file.bytes), loadPath = ABLoadPath.PersistentDataPath }); return data;
    }
    private HotUpdatePlan Prepare(HotUpdateStorage store, ABSourcesRelated data, ABSourcesRelated bundled = null)
    {
        Done(store.BeginStageAsync()); File.WriteAllBytes(store.StagedManifestPath, AssetReferenceSerializer.Encode(data));
        return Result(store.PrepareAsync(null, bundled ?? Manifest()));
    }
    private void Commit(params (string name, byte[] bytes)[] files) { CommitWith(new HotUpdateSettings { minimumFreeSpaceMB = 0, retainedManifestCount = 1 }, Manifest(), files); }
    private HotUpdateStatistics CommitWith(HotUpdateSettings options, ABSourcesRelated bundled, params (string name, byte[] bytes)[] files)
    {
        var store = Open(options); var plan = Prepare(store, Manifest(files), bundled);
        foreach (var item in plan.Downloads) File.WriteAllBytes(item.StagedPath, files.First(file => file.name == item.Name).bytes);
        Done(store.CommitAsync(plan)); Done(store.DisposeAsync()); return store.Statistics;
    }
    private string Bundle(string name) { return Path.Combine(directory, Root, name); }
    private void Write(string path, byte[] bytes) { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, bytes); }
    private static byte[] Bytes(int count, byte value = 7) { var bytes = new byte[count]; for (int i = 0; i < bytes.Length; i++) bytes[i] = value; return bytes; }
    private static IEnumerator Wait(Task task)
    {
        float end = Time.realtimeSinceStartup + 20; while (!task.IsCompleted && Time.realtimeSinceStartup < end) yield return null;
        Assert.IsTrue(task.IsCompleted, "异步工作未结束"); if (task.IsFaulted) throw task.Exception;
    }
    [Test] public void NewVersionCommitsBundlesLedgerAndManifestTogether()
    {
        var data = Bytes(100); Commit(("one", data));
        CollectionAssert.AreEqual(data, File.ReadAllBytes(Bundle("one")));
        var store = Open(); Assert.AreEqual(Md5(data), Result(store.ReadLocalAsync(null, true)).bundleInfo["one"].md5);
        Assert.IsFalse(store.RecoveryPending); Assert.AreEqual(1, Directory.GetFiles(Path.Combine(directory, "AssetBundles/.update-state"), "ledger.data", SearchOption.AllDirectories).Length);
    }
    [Test] public void ValidExistingFileDoesNotDownloadOrBecomeOwned()
    {
        var bytes = Bytes(12); Write(Bundle("foreign"), bytes);
        var store = Open(); var plan = Prepare(store, Manifest(("foreign", bytes))); Assert.IsEmpty(plan.Downloads); Done(store.CommitAsync(plan)); Done(store.DisposeAsync());
        Commit(); Assert.IsTrue(File.Exists(Bundle("foreign")), "原有未知归属文件不能被接管后删除");
    }
    [Test] public void MissingAndSameLengthCorruptFilesDownloadAgain()
    {
        var bytes = Bytes(12); Commit(("one", bytes), ("two", bytes)); File.Delete(Bundle("one")); File.WriteAllBytes(Bundle("two"), Bytes(12, 9));
        var store = Open(); Assert.AreEqual(2, Prepare(store, Manifest(("one", bytes), ("two", bytes))).Downloads.Count);
    }
    [Test] public void MatchingFileIsHashedOnWorker()
    {
        var bytes = Bytes(1024); Write(Bundle("one"), bytes); var store = Open(); Prepare(store, Manifest(("one", bytes)));
        Assert.AreNotEqual(Thread.CurrentThread.ManagedThreadId, store.Statistics.ioThreadId); Assert.AreEqual(bytes.Length, store.Statistics.verifiedBytes);
    }
    [Test] public void ObsoleteOwnedBundleIsCollectedAndUnknownFileSurvives()
    {
        Commit(("old", Bytes(40))); Write(Bundle("user-file"), Bytes(3)); var stats = CommitWith(new HotUpdateSettings { minimumFreeSpaceMB = 0, retainedManifestCount = 1 }, Manifest(), ("new", Bytes(50)));
        Assert.IsFalse(File.Exists(Bundle("old"))); Assert.IsTrue(File.Exists(Bundle("user-file"))); Assert.AreEqual(40, stats.reclaimedBytes); Assert.AreEqual(1, stats.removedBundles);
    }
    [Test] public void BundledPersistentReferenceProtectsOldOwnedBundle()
    {
        var bytes = Bytes(10); Commit(("old", bytes)); CommitWith(new HotUpdateSettings { minimumFreeSpaceMB = 0, retainedManifestCount = 1 }, Manifest(("old", bytes)), ("new", Bytes(20)));
        Assert.IsTrue(File.Exists(Bundle("old")));
    }
    [Test] public void LastTwoManifestReferencesExpireOnThirdVersion()
    {
        var options = new HotUpdateSettings { minimumFreeSpaceMB = 0, retainedManifestCount = 2 };
        CommitWith(options, Manifest(), ("one", Bytes(10))); CommitWith(options, Manifest(), ("two", Bytes(20))); Assert.IsTrue(File.Exists(Bundle("one")));
        CommitWith(options, Manifest(), ("three", Bytes(30))); Assert.IsFalse(File.Exists(Bundle("one"))); Assert.IsTrue(File.Exists(Bundle("two")));
    }
    [Test] public void ExternallyChangedOwnedFileIsNotDeleted()
    {
        Commit(("old", Bytes(10))); File.WriteAllBytes(Bundle("old"), Bytes(10, 9)); Commit(); Assert.AreEqual(9, File.ReadAllBytes(Bundle("old"))[0]);
        Commit(); Assert.IsTrue(File.Exists(Bundle("old")));
    }
    [Test] public void MissingBundledManifestDisablesGarbageCollection()
    {
        Commit(("old", Bytes(10))); var store = Open(); Done(store.BeginStageAsync()); File.WriteAllBytes(store.StagedManifestPath, AssetReferenceSerializer.Encode(Manifest()));
        var plan = Result(store.PrepareAsync(null, null)); Done(store.CommitAsync(plan)); Assert.IsTrue(File.Exists(Bundle("old"))); Assert.IsTrue(store.Statistics.garbageCollectionSkipped);
    }
    [Test] public void DisabledCollectionRetainsOwnedFiles()
    {
        Commit(("old", Bytes(10))); CommitWith(new HotUpdateSettings { minimumFreeSpaceMB = 0, retainedManifestCount = 1, collectUnusedBundles = false }, Manifest()); Assert.IsTrue(File.Exists(Bundle("old")));
    }
    [Test] public void CorruptLedgerDisablesCollectionWithoutDeletingFiles()
    {
        Commit(("old", Bytes(10))); var ledger = Directory.GetFiles(Path.Combine(directory, "AssetBundles/.update-state"), "ledger.data", SearchOption.AllDirectories).Single(); File.WriteAllText(ledger, "corrupt");
        var stats = CommitWith(new HotUpdateSettings { minimumFreeSpaceMB = 0, retainedManifestCount = 1 }, Manifest()); Assert.IsTrue(stats.garbageCollectionSkipped); Assert.IsTrue(File.Exists(Bundle("old")));
    }
    [Test] public void CurrentVersionSurvivesFailedNextCommitAndPreCommitGc()
    {
        var old = Bytes(12); Commit(("old", old)); var store = Open(); var plan = Prepare(store, Manifest(("new", Bytes(20)))); File.WriteAllBytes(plan.Downloads[0].StagedPath, Bytes(20, 9));
        Assert.Throws<InvalidDataException>(() => Done(store.CommitAsync(plan))); CollectionAssert.AreEqual(old, File.ReadAllBytes(Bundle("old"))); Assert.IsNotNull(Result(store.ReadLocalAsync(null, true)));
    }
    [Test] public void ChangedStagedManifestIsRejectedBeforeAnyTargetWrites()
    {
        Commit(("old", Bytes(12))); var store = Open(); var plan = Prepare(store, Manifest(("new", Bytes(20)))); File.WriteAllBytes(plan.Downloads[0].StagedPath, Bytes(20)); File.WriteAllBytes(store.StagedManifestPath, AssetReferenceSerializer.Encode(Manifest()));
        Assert.Throws<InvalidDataException>(() => Done(store.CommitAsync(plan))); Assert.IsFalse(File.Exists(Bundle("new"))); Assert.IsNotNull(Result(store.ReadLocalAsync(null, true)));
    }
    [Test] public void StagingBudgetRejectsBeforeBundleDownloads()
    {
        var store = Open(new HotUpdateSettings { minimumFreeSpaceMB = 0, maximumStagingMB = 1 });
        Assert.Throws<IOException>(() => Prepare(store, Manifest(("large", Bytes(1024 * 1024))))); Assert.IsFalse(File.Exists(Bundle("large")));
    }
    [Test] public void CacheBudgetDoesNotEvictProtectedCurrentFiles()
    {
        Commit(("old", Bytes(700000))); var store = Open(new HotUpdateSettings { minimumFreeSpaceMB = 0, maximumManagedCacheMB = 1 });
        Assert.Throws<IOException>(() => Prepare(store, Manifest(("new", Bytes(700000))))); Assert.IsTrue(File.Exists(Bundle("old")));
    }
    [Test] public void DeviceSpaceBudgetIncludesBackupAndTargetGrowth()
    {
        var store = Open(new HotUpdateSettings { minimumFreeSpaceMB = 1 }, space: _ => 1024 * 1024 + 100);
        Assert.Throws<IOException>(() => Prepare(store, Manifest(("new", Bytes(1000)))));
    }
    [Test] public void SpaceIsCheckedAgainBeforeCommit()
    {
        long free = 20 * 1024 * 1024; var store = Open(new HotUpdateSettings { minimumFreeSpaceMB = 1 }, space: _ => free);
        var plan = Prepare(store, Manifest(("new", Bytes(1000)))); File.WriteAllBytes(plan.Downloads[0].StagedPath, Bytes(1000)); free = 1024 * 1024;
        Assert.Throws<IOException>(() => Done(store.CommitAsync(plan))); Assert.IsFalse(File.Exists(Bundle("new")));
    }
    [Test] public void UnknownDeviceSpaceFailsWithReserveAndWorksWhenProbeDisabled()
    {
        var store = Open(new HotUpdateSettings { minimumFreeSpaceMB = 1 }, space: _ => null); Assert.Throws<IOException>(() => Done(store.BeginStageAsync())); Done(store.DisposeAsync());
        store = Open(space: _ => null); Prepare(store, Manifest());
    }
    [Test] public void MissingSizeCannotCreateUnboundedHotUpdate()
    {
        var manifest = Manifest(("new", Bytes(10))); manifest.bundleInfo["new"].size = 0;
        Assert.Throws<InvalidDataException>(() => Prepare(Open(), manifest));
    }
    [Test] public void ConcurrentSessionIsRefusedAndCanOpenAfterClose()
    {
        var store = Open(); Assert.Throws<IOException>(() => Open()); Done(store.DisposeAsync()); Open();
    }
    [Test] public void StageCleanupRequiresValidOwnerMarker()
    {
        var store = Open(); Done(store.BeginStageAsync()); string abandoned = store.StagePath; byte[] marker;
        // 保存标记需在释放独占所有权之后。
        string root = Path.GetDirectoryName(abandoned); Done(store.DisposeAsync()); Directory.CreateDirectory(abandoned);
        string rootId = Path.GetFileName(root); marker = System.Text.Encoding.UTF8.GetBytes("UPANDA_HOTUPDATE_STAGE:1\n" + rootId);
        Write(Path.Combine(abandoned, ".owner"), marker); Write(Path.Combine(abandoned, "partial"), Bytes(10));
        string unknown = Path.Combine(root, "unknown"); Write(Path.Combine(unknown, "user"), Bytes(10));
        string legacy = Path.Combine(directory, "AssetBundles/.update-stage/legacy"); Write(Path.Combine(legacy, "user"), Bytes(10));
        Open(); Assert.IsFalse(Directory.Exists(abandoned)); Assert.IsTrue(Directory.Exists(unknown)); Assert.IsTrue(Directory.Exists(legacy));
    }
    [Test] public void CancellationClosesStageWithoutUnityContinuation()
    {
        using (var cts = new CancellationTokenSource())
        {
            var store = Open(token: cts.Token); Done(store.BeginStageAsync()); string path = store.StagePath; Write(Path.Combine(path, "partial"), Bytes(10));
            cts.Cancel(); Done(store.DisposeAsync()); Assert.IsFalse(Directory.Exists(path)); Open();
        }
    }
    [TestCase("../outside")][TestCase("/absolute")][TestCase("AssetBundles/../../outside")]
    public void InvalidResourceRootIsRefused(string root) { Assert.Throws<InvalidDataException>(() => Open(root: root)); }
    [TestCase(".update-recovery/journal")][TestCase(".update-stage")][TestCase(".update-state/ledger")][TestCase(".update.lock")][TestCase("assetData.assetref")]
    public void ControlFilesCannotBeBundleTargets(string name)
    { Assert.Throws<InvalidDataException>(() => Prepare(Open(root: "AssetBundles"), Manifest((name, Bytes(10))))); }
    [Test] public void UnsafeJournalRejectsAllTargetsBeforeRecovery()
    {
        string recovery = Path.Combine(directory, "AssetBundles/.update-recovery"), first = Bundle("one"); Write(first, Bytes(5, 1)); Directory.CreateDirectory(recovery);
        Write(Path.Combine(recovery, "0.bak"), Bytes(5, 2));
        using (var writer = new BinaryWriter(File.Create(Path.Combine(recovery, "journal")))) { writer.Write(2); writer.Write(first); writer.Write(true); writer.Write(Path.Combine(directory, "outside")); writer.Write(false); }
        Assert.Throws<InvalidDataException>(() => Open()); Assert.AreEqual(1, File.ReadAllBytes(first)[0]); Assert.IsTrue(File.Exists(Path.Combine(recovery, "journal")));
    }
    [Test] public void CancellationDuringCommitRestoresCompleteOldVersion()
    {
        string a = Bundle("one"), b = Bundle("two"), sa = Path.Combine(directory, "staged-one"), sb = Path.Combine(directory, "staged-two"); Write(a, Bytes(3, 1)); Write(b, Bytes(4, 2)); Write(sa, Bytes(6, 3)); Write(sb, Bytes(7, 4));
        var type = typeof(UPGameRoot).Assembly.GetType("UPandaGF.FileUpdateTransaction");
        using (var cts = new CancellationTokenSource())
        {
            long copied = 0; var error = Assert.Throws<TargetInvocationException>(() => type.GetMethod("CommitCancelable").Invoke(null, new object[] { new[] { sa, sb }, new[] { a, b }, Path.Combine(directory, "recovery"), cts.Token, null, (Action<long>)(n => { copied += n; if (copied > 7) cts.Cancel(); }) }));
            Assert.IsInstanceOf<OperationCanceledException>(error.InnerException); CollectionAssert.AreEqual(Bytes(3, 1), File.ReadAllBytes(a)); CollectionAssert.AreEqual(Bytes(4, 2), File.ReadAllBytes(b));
        }
    }
    [Test] public void LegacyConfigReceivesDiskManagementDefaults()
    {
        var config = UPGameRootConfigFile.FromJson("{\"enableAssetUpdate\":true}"); Assert.AreEqual(64, config.hotUpdate.minimumFreeSpaceMB); Assert.AreEqual(2, config.hotUpdate.retainedManifestCount); Assert.IsTrue(config.hotUpdate.collectUnusedBundles);
        config = UPGameRootConfigFile.FromJson("{\"hotUpdate\":{\"maximumStagingMB\":10}}"); Assert.AreEqual(64, config.hotUpdate.minimumFreeSpaceMB); Assert.AreEqual(10, config.hotUpdate.maximumStagingMB);
    }
    [Test] public void CappedHandlerRejectsChunkAndResumeBeforeExceedingBudget()
    {
        string path = Path.Combine(directory, "cap");
        using (var handler = new UPandaGF.DownloadHandlerFile(path, false, null, 10))
        { Assert.IsTrue(Feed(handler, Bytes(8))); Assert.IsFalse(Feed(handler, Bytes(8))); Assert.IsTrue(handler.WriteFailed); handler.Close(); Assert.AreEqual(8, new FileInfo(path).Length); }
        using (var handler = new UPandaGF.DownloadHandlerFile(path, true, () => true, 10))
        { Assert.IsFalse(Feed(handler, Bytes(4))); handler.Close(); Assert.AreEqual(8, new FileInfo(path).Length); }
    }
    private static bool Feed(UPandaGF.DownloadHandlerFile handler, byte[] bytes)
    { return (bool)typeof(UPandaGF.DownloadHandlerFile).GetMethod("ReceiveData", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(handler, new object[] { bytes, bytes.Length }); }
    private Downloader DownloadComponent()
    { var go = new GameObject("HotUpdateDownloader"); objects.Add(go); return go.AddComponent<Downloader>(); }
    [UnityTest] public IEnumerator NativeDownloadCannotWriteBeyondCap()
    {
        using (var server = new CoordinationHttpServer())
        {
            var downloader = DownloadComponent(); var item = new DownloadItem { url = server.Url + "/ok", savePath = Path.Combine(directory, "cap"), maximumBytes = 3 };
            PLogger.cfg.openError = false; downloader.AddBatchDownloads(new List<DownloadItem> { item });
            while (item.state == DownloadState.Downloading || item.state == DownloadState.Waiting) yield return null;
            Assert.AreEqual(DownloadState.Failed, item.state); Assert.IsTrue(!File.Exists(item.savePath) || new FileInfo(item.savePath).Length <= 3); Assert.IsNull(item.request);
        }
    }
    [UnityTest] public IEnumerator DisablingDownloaderSettlesAwaitersAndClosesFile()
    {
        using (var server = new CoordinationHttpServer())
        {
            var downloader = DownloadComponent(); var task = downloader.DownloadAsync(server.Url + "/slow", Path.Combine(directory, "partial"));
            yield return null; downloader.gameObject.SetActive(false); yield return Wait(task); Assert.IsFalse(task.Result); Assert.AreEqual(0, downloader.ActiveCount);
            downloader.gameObject.SetActive(true); var next = downloader.DownloadAsync(server.Url + "/ok", Path.Combine(directory, "next")); yield return Wait(next); Assert.IsTrue(next.Result);
        }
    }
    [UnityTest] public IEnumerator DisableFromStartCallbackDoesNotUseDisposedNativeRequest()
    {
        var downloader = DownloadComponent(); downloader.OnDownloadStart += _ => downloader.enabled = false;
        var task = downloader.DownloadAsync("http://127.0.0.1:1", Path.Combine(directory, "partial")); yield return Wait(task); Assert.IsFalse(task.Result);
    }
    [UnityTest] public IEnumerator RetryCancellationDoesNotDependOnTimeScale()
    {
        using (var server = new CoordinationHttpServer())
        {
            var downloader = DownloadComponent(); downloader.defaultRetryCount = 1; downloader.retryDelay = 30;
            var item = new DownloadItem { url = server.Url + "/fail", savePath = Path.Combine(directory, "retry") }; Time.timeScale = 0;
            downloader.AddBatchDownloads(new List<DownloadItem> { item }); float end = Time.realtimeSinceStartup + 5;
            while ((int)typeof(DownloadItem).GetField("attempt", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(item) < 2 && Time.realtimeSinceStartup < end) yield return null;
            downloader.CancelDownload(item); end = Time.realtimeSinceStartup + 1;
            while (item.state != DownloadState.Canceled && Time.realtimeSinceStartup < end) yield return null;
            Assert.AreEqual(DownloadState.Canceled, item.state); Assert.IsNull(item.request);
        }
    }
    [UnityTest] public IEnumerator FileMaintenanceGateDefersNewBundleLoadWithoutBlockingMainThread()
    {
        var type = typeof(UPGameRoot).Assembly.GetType("UPandaGF.HotUpdateFileGate"); var writer = (IDisposable)type.GetMethod("TryMaintenance").Invoke(null, null);
        Assert.IsNotNull(writer); var pending = (Task<IDisposable>)type.GetMethod("EnterLoad").Invoke(null, null);
        try { Assert.IsFalse(pending.IsCompleted); yield return null; Assert.IsFalse(pending.IsCompleted); }
        finally { writer.Dispose(); }
        yield return Wait(pending); Assert.IsNull(type.GetMethod("TryMaintenance").Invoke(null, null)); pending.Result.Dispose();
        ((IDisposable)type.GetMethod("TryMaintenance").Invoke(null, null)).Dispose();
    }
    [UnityTest] public IEnumerator LargeFileHashAllowsUnityFramesToAdvance()
    {
        var bytes = Bytes(16 * 1024 * 1024); var manifest = Manifest(("large", bytes)); Write(Bundle("large"), bytes); var store = Open(); Done(store.BeginStageAsync()); File.WriteAllBytes(store.StagedManifestPath, AssetReferenceSerializer.Encode(manifest));
        int thread = Thread.CurrentThread.ManagedThreadId; var task = store.PrepareAsync(null, Manifest()); int frames = 0;
        while (!task.IsCompleted) { frames++; yield return null; }
        Assert.IsFalse(task.IsFaulted, task.Exception?.ToString()); Assert.Greater(frames, 0); Assert.AreNotEqual(thread, store.Statistics.ioThreadId); Assert.IsEmpty(task.Result.Downloads);
        Debug.Log("HOTUPDATE_BACKGROUND_HASH bytes=" + bytes.Length + " frames=" + frames + " ms=" + store.Statistics.verifyMilliseconds);
    }
}
