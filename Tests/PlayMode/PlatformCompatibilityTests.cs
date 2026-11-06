using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UPandaGF;
using Object = UnityEngine.Object;

public class PlatformCompatibilityTests
{
    private string directory;
    private readonly List<GameObject> objects = new List<GameObject>();
    [SetUp] public void Setup() { directory = Path.Combine(Application.temporaryCachePath, "UPandaPlatformTests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory); }
    [TearDown] public void Cleanup()
    {
        foreach (var go in objects) if (go != null) Object.DestroyImmediate(go); objects.Clear();
        if (Path.GetFullPath(directory).StartsWith(Path.GetFullPath(Path.Combine(Application.temporaryCachePath, "UPandaPlatformTests")) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) Directory.Delete(directory, true);
    }
    [TestCase("https://example.com/StreamingAssets", "https://example.com/StreamingAssets/Probe/%E7%A9%BA%20%E6%A0%BC/%E5%86%85%E5%AE%B9%231.txt")]
    [TestCase("jar:file:///data/base.apk!/assets", "jar:file:///data/base.apk!/assets/Probe/%E7%A9%BA%20%E6%A0%BC/%E5%86%85%E5%AE%B9%231.txt")]
    public void UrlPathsEscapeRawNamesAndNeverInsertWindowsSeparators(string root, string expected)
    { Assert.AreEqual(expected, AssetLocation.Combine(root, @"Probe\空 格\内容#1.txt")); }
    [Test] public void UrlPathsTreatLiteralPercentAsFileName()
    { Assert.AreEqual("https://host/data/100%25%2B%23.txt", AssetLocation.Combine("https://host/data/", "/100%+#.txt")); }
    [Test] public void LocalPathsKeepRawFileNames()
    { Assert.AreEqual(Path.Combine(directory, "空 格", "内容#1.txt"), AssetLocation.Combine(directory, "/空 格/内容#1.txt")); }
    [TestCase("../escape")][TestCase("a/../../escape")][TestCase("a:b")][TestCase("a/./b")]
    public void RelativePathsRejectTraversalAndDriveReplacement(string path)
    { Assert.Throws<ArgumentException>(() => AssetLocation.Combine("https://host/root", path)); }
    [Test] public void AssetBundlePathHelpersShareUrlSafeComposition()
    {
        Assert.AreEqual("jar:file:///base.apk!/assets/Bundles/a%23b", ABLoadMgr.CombineLocalPath("jar:file:///base.apk!/assets", "Bundles", "a#b"));
        Assert.AreEqual("https://host/Bundles/a%20b", ABLoadMgr.CombineUrl("https://host/", "Bundles/", "a b"));
    }
    [Test] public void FileUriEscapesSpecialCharactersExactlyOnce()
    {
        string raw = Path.Combine(directory, "空 格#%.txt"); string uri = AssetLocation.AsUrl(raw);
        Assert.AreEqual(Path.GetFullPath(raw), Path.GetFullPath(new Uri(uri).LocalPath)); Assert.AreEqual(uri, AssetLocation.AsUrl(uri));
    }
    [Test] public void DeviceProbeWorksForExistingAndNotYetCreatedDirectories()
    {
        Assert.Greater(PlatformStorage.AvailableDiskSpace(directory), 0);
        Assert.Greater(PlatformStorage.AvailableDiskSpace(Path.Combine(directory, "not-created")), 0);
    }
    private PLogHelper Logger()
    { var go = new GameObject("PlatformLogger"); objects.Add(go); var logger = go.AddComponent<PLogHelper>(); logger.useBackgroundThread = false; return logger; }
    [Test] public void CooperativeLoggerBoundsQueueAndTracksDroppedMessages()
    {
        var logger = Logger(); logger.maximumQueuedLogs = 2; logger.InitLogFileModule(directory, "log.txt");
        for (int i = 0; i < 8; i++) Debug.Log("PLATFORM_QUEUE_" + i);
        Assert.LessOrEqual(logger.PendingCount, 2); Assert.GreaterOrEqual(logger.DroppedCount, 6); Assert.IsFalse(logger.UsesBackgroundThread);
        logger.StopSafely(); Assert.AreEqual(0, logger.PendingCount); Assert.IsTrue(File.ReadAllText(logger.LogFilePath).Contains("PLATFORM_QUEUE_0"));
    }
    [Test] public void LoggerRepeatedInitKeepsExistingSessionAndClosesHandle()
    {
        var logger = Logger(); logger.InitLogFileModule(directory, "log.txt"); Debug.Log("PLATFORM_SESSION_MARKER"); logger.InitLogFileModule(directory, "log.txt"); logger.StopSafely(); logger.StopSafely();
        Assert.IsTrue(File.ReadAllText(logger.LogFilePath).Contains("PLATFORM_SESSION_MARKER"));
        using (File.Open(logger.LogFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
    }
    [UnityTest] public IEnumerator CooperativeLoggerDrainsAtMostConfiguredBatchEachFrame()
    {
        var logger = Logger(); logger.logsPerFrame = 2; logger.InitLogFileModule(directory, "log.txt");
        for (int i = 0; i < 8; i++) Debug.Log("PLATFORM_BATCH_" + i);
        int before = logger.PendingCount; typeof(PLogHelper).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(logger, null);
        Assert.AreEqual(before - 2, logger.PendingCount); yield return null; logger.StopSafely(); Assert.IsFalse(logger.IsRunning);
    }
    [Test] public void CooperativeLogStripsFormattingAndDisableStopsWriter()
    {
        var logger = Logger(); logger.InitLogFileModule(directory, "log.txt"); Debug.Log("<color=red>PLATFORM_RICH</color>"); logger.enabled = false;
        string text = File.ReadAllText(logger.LogFilePath); Assert.IsTrue(text.Contains("PLATFORM_RICH")); Assert.IsFalse(text.Contains("<color=red>")); Assert.IsFalse(logger.IsRunning);
    }
    [Test] public void ThreadedLogStopsBeforeDestroyDisposesWakeHandle()
    {
        var logger = Logger(); logger.useBackgroundThread = true; logger.InitLogFileModule(directory, "log.txt"); Debug.Log("PLATFORM_THREAD");
        string file = logger.LogFilePath; Object.DestroyImmediate(logger.gameObject);
        Assert.IsTrue(File.ReadAllText(file).Contains("PLATFORM_THREAD")); using (File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
    }
    [UnityTest] public IEnumerator PortableDelayUsesRealTimeAndCanCancel()
    {
        float previous = Time.timeScale; Time.timeScale = 0;
        try
        {
            var wait = PlatformTasks.Delay(TimeSpan.FromMilliseconds(25)); float until = Time.realtimeSinceStartup + 2;
            while (!wait.IsCompleted && Time.realtimeSinceStartup < until) yield return null;
            Assert.IsTrue(wait.IsCompleted); Assert.IsFalse(wait.IsFaulted);
            using (var cts = new System.Threading.CancellationTokenSource())
            { var canceled = PlatformTasks.Delay(TimeSpan.FromSeconds(30), cts.Token); cts.Cancel(); while (!canceled.IsCompleted) yield return null; Assert.IsTrue(canceled.IsCanceled); }
        }
        finally { Time.timeScale = previous; }
    }
    [UnityTest] public IEnumerator DesktopSpaceProbeAndFlushComplete()
    {
        var probe = PlatformStorage.AvailableSpaceAsync(directory); while (!probe.IsCompleted) yield return null;
        Assert.Greater(probe.Result, 0); var flush = PlatformStorage.FlushAsync(); while (!flush.IsCompleted) yield return null; Assert.IsFalse(flush.IsFaulted);
    }
}
