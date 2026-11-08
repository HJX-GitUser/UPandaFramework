using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace UPandaGF.Tests
{
    /// <summary>日志控制台回归：容量淘汰、折叠/搜索、线程队列与显隐 API。</summary>
    public class LogConsoleRegressionTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in objects) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            objects.Clear();
        }

        private static LogConsoleEntry Entry(string message, LogConsoleLevel level = LogConsoleLevel.Info, string stack = "")
        {
            return new LogConsoleEntry
            {
                Message = message,
                StackTrace = stack,
                Level = level,
                TimestampUtc = DateTime.UtcNow,
                SceneName = "test",
            };
        }

        [Test]
        public void StoreEvictsOldestByCountAndByteBudget()
        {
            var store = new LogConsoleStore(5, 0);
            for (int i = 0; i < 100; i++) store.Add(Entry("message " + i));
            Assert.AreEqual(5, store.Count);
            Assert.AreEqual(5, store.InfoCount);
            Assert.AreEqual("message 95", store.Snapshot()[0].Message);

            var bytes = new LogConsoleStore(1000, 600);
            for (int i = 0; i < 20; i++) bytes.Add(Entry("message" + i));
            Assert.Greater(bytes.Count, 0);
            Assert.Less(bytes.Count, 20);
            Assert.LessOrEqual(bytes.EstimatedBytes, 600);
        }

        [Test]
        public void CollapseGroupsBySeverityAndMessage()
        {
            var store = new LogConsoleStore();
            for (int i = 0; i < 100; i++) store.Add(Entry("repeat"));
            store.Add(Entry("repeat", LogConsoleLevel.Error, "Frame.StackTarget"));

            var visible = new List<LogConsoleEntry>();
            store.CopyVisible(visible, true, true, false, true, "");
            Assert.AreEqual(2, visible.Count);
            Assert.AreEqual(100, visible.Find(x => x.Level == LogConsoleLevel.Info).RepeatCount);
            Assert.AreEqual(1, visible.Find(x => x.Level == LogConsoleLevel.Error).RepeatCount);

            store.CopyVisible(visible, true, true, false, true, "stacktarget");
            Assert.AreEqual(1, visible.Count);
            Assert.AreEqual(LogConsoleLevel.Error, visible[0].Level);

            store.Clear();
            Assert.AreEqual(0, store.Count);
            Assert.AreEqual(0L, store.EstimatedBytes);
            Assert.AreEqual(0, store.InfoCount);
        }

        [Test]
        public void SeverityAndSearchFilterVisibleEntries()
        {
            var store = new LogConsoleStore();
            store.Add(Entry("info"));
            store.Add(Entry("warn", LogConsoleLevel.Warning));
            store.Add(Entry("error", LogConsoleLevel.Error, "stack"));

            var visible = new List<LogConsoleEntry>();
            store.CopyVisible(visible, false, true, false, false, "");
            Assert.AreEqual(1, visible.Count);
            store.CopyVisible(visible, false, false, true, true, "");
            Assert.AreEqual(2, visible.Count);
            store.CopyVisible(visible, false, true, true, true, "err");
            Assert.AreEqual(1, visible.Count);
        }

        [Test]
        public void VisibleRangeIsBoundedByViewportAndOverscan()
        {
            int first, last;
            LogConsoleStore.GetVisibleRange(3000, 40000f, 40f, 400f, out first, out last);
            Assert.AreEqual(998, first);
            Assert.AreEqual(1012, last);

            LogConsoleStore.GetVisibleRange(0, 0f, 40f, 400f, out first, out last);
            Assert.AreEqual(0, first);
            Assert.AreEqual(0, last);
        }

        private UPandaLogConsole CreateConsole()
        {
            var go = new GameObject("LogConsoleTest");
            objects.Add(go);
            var console = go.AddComponent<UPandaLogConsole>();
            console.enabled = false; // 测试期间不接管 Unity 自身日志
            console.Clear();
            return console;
        }

        [UnityTest]
        public IEnumerator WorkerBurstIsBoundedAndDrainedInBatches()
        {
            var console = CreateConsole();
            console.MaxQueuedEntries = 10;
            console.MaxStoredEntries = 5;
            console.MaxDrainPerFrame = 3;

            Task task = Task.Run(() =>
            {
                for (int i = 0; i < 100; i++) console.Capture("worker " + i, "", LogConsoleLevel.Info);
            });
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted) throw task.Exception;

            Assert.LessOrEqual(console.PendingCount, 10);
            Assert.Greater(console.DroppedCount, 0);

            console.ProcessPending(3);
            Assert.LessOrEqual(console.RetainedCount, 5);
            while (console.PendingCount > 0) console.ProcessPending(3);
            Assert.AreEqual(0, console.PendingCount);
            Assert.AreEqual(5, console.RetainedCount);

            console.Clear();
            Assert.AreEqual(0, console.RetainedCount);
            Assert.AreEqual(0, console.PendingCount);
        }

        [Test]
        public void PendingQueueAlsoHasAByteBudget()
        {
            var console = CreateConsole();
            console.MaxQueuedEntries = 1000;
            console.MaxQueuedBytes = 1024L * 1024L;
            string text = new string('x', 32000);
            for (int i = 0; i < 40; i++) console.Capture(text, text, LogConsoleLevel.Info);

            Assert.Less(console.PendingCount, 40);
            Assert.Greater(console.DroppedCount, 0);
        }

        [Test]
        public void ShowHideClearAndFilterWork()
        {
            var console = CreateConsole();
            console.Capture("hello", "", LogConsoleLevel.Info);
            console.ProcessPending(10);
            Assert.AreEqual(1, console.RetainedCount);

            console.Show();
            Assert.IsTrue(console.IsVisible);
            Assert.AreEqual(1, console.SnapshotVisible().Count);

            console.SetFilter("missing");
            Assert.AreEqual(0, console.SnapshotVisible().Count);
            console.SetFilter("hello");
            Assert.AreEqual(1, console.SnapshotVisible().Count);

            console.Hide();
            Assert.IsFalse(console.IsVisible);

            console.Clear();
            Assert.AreEqual(0, console.RetainedCount);
        }

        [UnityTest]
        public IEnumerator VisibleConsoleDrawsGuiWithoutExternalAssets()
        {
            var console = CreateConsole();
            console.enabled = true;
            console.Show();
            console.SetFilter("drawn message");
            console.Capture("drawn message", "Frame.Stack", LogConsoleLevel.Info);
            console.ProcessPending(10);
            for (int i = 0; i < 3; i++) yield return null;
            Assert.IsTrue(console.IsVisible);
            Assert.AreEqual(1, console.SnapshotVisible().Count);
            console.Hide();
            console.enabled = false;
        }

        [UnityTest]
        public IEnumerator SettingsAndDiagnosticsPagesRenderWithoutErrors()
        {
            var console = CreateConsole();
            console.enabled = true;
            console.Show();
            console.ShowScene = true;
            console.ShowFps = true;
            console.PauseCapture = true;
            console.PanelScale = 1.15f;
            console.OpenSettings();
            yield return null;
            yield return null;
            console.OpenDiagnostics();
            yield return null;
            yield return null;
            Assert.IsTrue(console.IsVisible);
            Assert.IsTrue(console.ShowScene);
            Assert.IsTrue(console.ShowFps);
            Assert.IsTrue(console.PauseCapture);
            Assert.AreEqual(1.15f, console.PanelScale, 0.001f);

            console.ShowScene = false;
            console.ShowFps = false;
            console.PauseCapture = false;
            console.PanelScale = 1f;
            console.Hide();
            console.enabled = false;
        }
        [Test]
        public void ErrorOpensConsoleOnlyWhenEnabled()
        {
            var console = CreateConsole();
            console.OpenOnError = true;
            console.Capture("boom", "", LogConsoleLevel.Error);
            console.ProcessPending(10);
            Assert.IsTrue(console.IsVisible);

            console.Hide();
            console.OpenOnError = false;
            console.Capture("boom2", "", LogConsoleLevel.Error);
            console.ProcessPending(10);
            Assert.IsFalse(console.IsVisible);
        }
    }
}

