using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public class ReporterRegressionTests
{
    private readonly List<GameObject> objects = new List<GameObject>();
    private readonly Dictionary<string, string> savedPreferences = new Dictionary<string, string>();
    private static readonly string[] boolKeys = { "collapse", "clearOnNewSceneLoaded", "showTime", "showScene", "showMemory", "showFps", "showLog", "showWarning", "showError", "showClearOnNewSceneLoadedButton", "showTimeButton", "showSceneButton", "showMemButton", "showFpsButton", "showSearchText", "showCopyButton", "showSaveButton" };
    [SetUp] public void SetUp()
    {
        savedPreferences.Clear();
        foreach (var key in boolKeys) savedPreferences[key] = PlayerPrefs.HasKey("Reporter_" + key) ? PlayerPrefs.GetInt("Reporter_" + key).ToString() : null;
        savedPreferences["filterText"] = PlayerPrefs.HasKey("Reporter_filterText") ? PlayerPrefs.GetString("Reporter_filterText") : null;
        savedPreferences["size"] = PlayerPrefs.HasKey("Reporter_size") ? PlayerPrefs.GetFloat("Reporter_size").ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
    }
    [TearDown] public void TearDown()
    {
        foreach (var go in objects) if (go != null) Object.DestroyImmediate(go);
        objects.Clear();
        foreach (var pair in savedPreferences)
        {
            string key = "Reporter_" + pair.Key;
            if (pair.Value == null) PlayerPrefs.DeleteKey(key);
            else if (pair.Key == "filterText") PlayerPrefs.SetString(key, pair.Value);
            else if (pair.Key == "size") PlayerPrefs.SetFloat(key, float.Parse(pair.Value, System.Globalization.CultureInfo.InvariantCulture));
            else PlayerPrefs.SetInt(key, int.Parse(pair.Value));
        }
        PlayerPrefs.Save();
    }
    private Reporter Create()
    {
        var go = new GameObject("Reporter regression"); objects.Add(go);
        var reporter = go.AddComponent<Reporter>();
        reporter.SetFilter(""); reporter.SetSeverityFilters(true, true, true); reporter.SetCollapse(false);
        reporter.ClearLogs(); return reporter;
    }
    private static void Call(Reporter reporter, string name, params object[] args)
    {
        typeof(Reporter).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(reporter, args);
    }
    private static object NewStore() { return Activator.CreateInstance(typeof(Reporter).Assembly.GetType("ReporterLogStore"), true); }
    private static object StoreCall(object store, string method, params object[] args) { return store.GetType().GetMethod(method).Invoke(store, args); }
    private static T Property<T>(object store, string name) { return (T)store.GetType().GetProperty(name).GetValue(store); }
    private static Reporter.Log Log(string message, Reporter._LogType type = Reporter._LogType.Log, string stack = "")
    {
        return new Reporter.Log { condition = message, stacktrace = stack, logType = type, sample = new Reporter.Sample { sceneName = "scene", fpsText = "60" } };
    }
    [Test] public void RetentionEvictsGroupsAndCountsTogether()
    {
        var store = NewStore();
        for (int i = 0; i < 100; i++) StoreCall(store, "Add", Log("message " + i), 5, 0L);
        Assert.AreEqual(5, Property<int>(store, "Count"));
        Assert.AreEqual(5, Property<int>(store, "GroupCount"));
        Assert.AreEqual(5, Property<int>(store, "LogCount"));
        var snapshot = (Reporter.Log[])StoreCall(store, "Snapshot");
        Assert.AreEqual("message 95", snapshot[0].condition);
    }
    [Test] public void CollapseCountOnlyIncludesRetainedRecords()
    {
        var store = NewStore();
        for (int i = 0; i < 100; i++) StoreCall(store, "Add", Log("repeat"), 5, 0L);
        var visible = new List<Reporter.Log>();
        StoreCall(store, "CopyVisible", visible, true, true, true, true, "");
        Assert.AreEqual(1, visible.Count); Assert.AreEqual(5, visible[0].count);
        StoreCall(store, "Clear");
        Assert.AreEqual(0, Property<int>(store, "GroupCount"));
        Assert.AreEqual(0L, Property<long>(store, "EstimatedBytes"));
    }
    [Test] public void SeverityIsPartOfTheCollapseKeyAndStackIsSearchable()
    {
        var store = NewStore();
        StoreCall(store, "Add", Log("same", Reporter._LogType.Log, "Frame.StackTarget"), 100, 0L);
        StoreCall(store, "Add", Log("same", Reporter._LogType.Error, "Frame.StackTarget"), 100, 0L);
        Assert.AreEqual(2, Property<int>(store, "GroupCount"));
        var visible = new List<Reporter.Log>();
        StoreCall(store, "CopyVisible", visible, true, false, true, true, "stacktarget");
        Assert.AreEqual(1, visible.Count); Assert.AreEqual(Reporter._LogType.Error, visible[0].logType);
    }
    [Test] public void MemoryLimitEvictsOldestInsteadOfClearingEverything()
    {
        var store = NewStore();
        for (int i = 0; i < 20; i++) StoreCall(store, "Add", Log("message" + i), 0, 600L);
        Assert.Greater(Property<int>(store, "Count"), 0);
        Assert.Less(Property<int>(store, "Count"), 20);
        Assert.LessOrEqual(Property<long>(store, "EstimatedBytes"), 600);
    }
    [Test] public void OpenAndInitializeAreIdempotentAndDisablingStopsCapture()
    {
        var reporter = Create();
        for (int i = 0; i < 5; i++) { reporter.Initialize(); reporter.ShowLogWindows(); }
        Assert.AreEqual(1, reporter.GetComponents<ReporterGUI>().Length);
        reporter.HideLogWindows(); Assert.IsFalse(reporter.GetComponent<ReporterGUI>().enabled);
        reporter.enabled = false;
        Call(reporter, "CaptureLogThread", "disabled", "", LogType.Log);
        Assert.AreEqual(0, reporter.PendingLogCount);
        reporter.enabled = true;
        Call(reporter, "CaptureLogThread", "enabled", "", LogType.Log);
        Assert.AreEqual(1, reporter.PendingLogCount);
        Object.DestroyImmediate(reporter.gameObject);
        Assert.IsTrue(Create().Initialized, "销毁后应允许创建新实例");
    }
    [UnityTest] public IEnumerator WorkerThreadBurstIsBoundedAndDrainedInBatches()
    {
        var reporter = Create(); reporter.maxQueuedLogs = 10; reporter.maxLogsPerFrame = 3; reporter.maxLogCount = 5;
        var task = Task.Run(() => {
            for (int i = 0; i < 100; i++) Call(reporter, "CaptureLogThread", "worker " + i, "", LogType.Log);
        });
        while (!task.IsCompleted) yield return null;
        if (task.IsFaulted) throw task.Exception;
        Assert.LessOrEqual(reporter.PendingLogCount, 10);
        Assert.Greater(reporter.DroppedLogCount, 0);
        Call(reporter, "Update");
        Assert.LessOrEqual(reporter.RetainedLogCount, 5);
        for (int i = 0; i < 10 && reporter.PendingLogCount > 0; i++) yield return null;
        Assert.AreEqual(0, reporter.PendingLogCount);
        Assert.AreEqual(5, reporter.RetainedLogCount);
        reporter.ClearLogs(); Assert.AreEqual(0, reporter.RetainedLogCount); Assert.AreEqual(0, reporter.PendingLogCount);
    }
    [Test] public void VisibleRangeIsBoundedByViewport()
    {
        object[] args = { 3000, 40000f, 400f, 40f, 0, 0 };
        typeof(Reporter).GetMethod("VisibleRange", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args);
        Assert.AreEqual(1000, args[4]); Assert.AreEqual(1012, args[5]);
    }
    [UnityTest] public IEnumerator VisibleReporterWorksWithoutAssignedImages()
    {
        var reporter = Create();
        Call(reporter, "CaptureLogThread", "Sample message\\nSecond line", "Stack.Frame()", LogType.Log);
        reporter.ShowLogWindows();
        for (int i = 0; i < 4; i++) yield return null;
        Assert.IsTrue(reporter.show); Assert.AreEqual(1, reporter.RetainedLogCount);
        reporter.HideLogWindows();
    }
    [Test] public void PendingQueueAlsoHasAByteBudget()
    {
        var reporter = Create(); reporter.maxQueuedLogs = 1000; reporter.maxQueuedMemoryMB = 1;
        var text = new string('x', 32000);
        for (int i = 0; i < 40; i++) Call(reporter, "CaptureLogThread", text, text, LogType.Log);
        Assert.Less(reporter.PendingLogCount, 40);
        long bytes = (long)typeof(Reporter).GetField("pendingBytes", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(reporter);
        Assert.LessOrEqual(bytes, 1024L * 1024L);
        Assert.Greater(reporter.DroppedLogCount, 0);
    }
}
