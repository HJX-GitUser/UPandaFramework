using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UPandaGF;
using UPandaGF.Gameplay.StateMachine;

namespace UPandaGF.Tests
{

public class P0StartupRegressionTests
{
    private readonly Dictionary<string, byte[]> originalConfigs = new Dictionary<string, byte[]>();
    [SetUp] public void ConfigureStartup()
    {
        if (!Application.isEditor) Assert.Ignore("P0 startup fixtures require Editor resource loading.");
        string directory = Path.Combine(Application.streamingAssetsPath, "Data");
        Directory.CreateDirectory(directory);
        foreach (var name in new[] { "GameRootConfig.json", "LogConfig.json" })
        {
            string path = Path.Combine(directory, name);
            originalConfigs[path] = File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        File.WriteAllText(Path.Combine(directory, "GameRootConfig.json"), UPGameRootConfigFile.ToJson(new UPGameRootConfig()));
        File.WriteAllText(Path.Combine(directory, "LogConfig.json"), JsonUtility.ToJson(new LogConfig()));
    }
    [TearDown] public void RestoreStartup()
    {
        foreach (var pair in originalConfigs)
            if (pair.Value == null) File.Delete(pair.Key); else File.WriteAllBytes(pair.Key, pair.Value);
        originalConfigs.Clear();
    }

    private static IEnumerator Await(Task task)
    {
        float until = Time.realtimeSinceStartup + 15f;
        while (!task.IsCompleted && Time.realtimeSinceStartup < until) yield return null;
        Assert.IsTrue(task.IsCompleted, "任务未在 15 秒内终止");
    }
    [UnityTest] public IEnumerator StartupRunsOnceAndClosesInReverseOrder()
    {
        var calls = new List<string>(); var lifecycle = new StartupLifecycle();
        foreach (var id in new[] { "A", "B", "C" })
        {
            string name = id;
            lifecycle.Add(name, ct => { calls.Add("+" + name); return Task.CompletedTask; },
                () => { calls.Add("-" + name); return Task.CompletedTask; });
        }
        var start = lifecycle.InitializeAsync(); Assert.AreSame(start, lifecycle.InitializeAsync());
        yield return Await(start); Assert.IsFalse(start.IsFaulted); Assert.AreEqual(StartupState.Ready, lifecycle.State);
        var stop = lifecycle.ShutdownAsync(); Assert.AreSame(stop, lifecycle.ShutdownAsync());
        yield return Await(stop); Assert.IsFalse(stop.IsFaulted);
        CollectionAssert.AreEqual(new[] { "+A", "+B", "+C", "-C", "-B", "-A" }, calls);
        Assert.AreEqual(StartupState.Stopped, lifecycle.State);
    }
    [UnityTest] public IEnumerator FailedStepAlsoRollsBackAndLaterStepsNeverRun()
    {
        var calls = new List<string>(); var lifecycle = new StartupLifecycle();
        lifecycle.Add("A", ct => Task.CompletedTask, () => { calls.Add("A"); return Task.CompletedTask; });
        lifecycle.Add("B", ct => throw new InvalidOperationException("probe"), () => { calls.Add("B"); return Task.CompletedTask; });
        lifecycle.Add("C", ct => { Assert.Fail("失败后不应继续初始化"); return Task.CompletedTask; });
        var start = lifecycle.InitializeAsync(); yield return Await(start);
        Assert.IsTrue(start.IsFaulted); Assert.AreEqual(StartupState.Failed, lifecycle.State);
        CollectionAssert.AreEqual(new[] { "B", "A" }, calls);
        Assert.AreSame(start, lifecycle.InitializeAsync());
        yield return Await(lifecycle.ShutdownAsync()); Assert.AreEqual(2, calls.Count);
    }
    [UnityTest] public IEnumerator ShutdownDuringInitializationCancelsAndRollsBack()
    {
        var lifecycle = new StartupLifecycle(); var entered = new TaskCompletionSource<bool>(); int stops = 0;
        lifecycle.Add("Pending", async ct => { entered.SetResult(true); await Task.Delay(Timeout.Infinite, ct); },
            () => { stops++; return Task.CompletedTask; });
        var start = lifecycle.InitializeAsync(); yield return Await(entered.Task);
        var stop = lifecycle.ShutdownAsync(); yield return Await(stop);
        Assert.IsTrue(start.IsCanceled); Assert.AreEqual(1, stops); Assert.AreEqual(StartupState.Stopped, lifecycle.State);
    }
    [UnityTest] public IEnumerator WaiterCancellationDoesNotCancelSharedStartup()
    {
        var lifecycle = new StartupLifecycle(); var release = new TaskCompletionSource<bool>();
        lifecycle.Add("Pending", ct => release.Task);
        var start = lifecycle.InitializeAsync(); using (var cts = new CancellationTokenSource())
        {
            var waiter = StartupLifecycle.WaitAsync(start, cts.Token); cts.Cancel(); yield return Await(waiter);
            Assert.IsTrue(waiter.IsCanceled); Assert.IsFalse(start.IsCompleted);
        }
        release.SetResult(true); yield return Await(start); Assert.AreEqual(StartupState.Ready, lifecycle.State);
        yield return Await(lifecycle.ShutdownAsync());
    }
    [UnityTest] public IEnumerator CleanupFailureDoesNotSkipEarlierServices()
    {
        int cleaned = 0; var lifecycle = new StartupLifecycle();
        lifecycle.Add("A", ct => Task.CompletedTask, () => { cleaned++; return Task.CompletedTask; });
        lifecycle.Add("B", ct => Task.CompletedTask, () => throw new InvalidOperationException("cleanup"));
        yield return Await(lifecycle.InitializeAsync()); var stop = lifecycle.ShutdownAsync(); yield return Await(stop);
        Assert.IsTrue(stop.IsFaulted); Assert.AreEqual(1, cleaned); Assert.AreEqual(StartupState.Stopped, lifecycle.State);
    }
    [UnityTest] public IEnumerator RootReadyCanBeAwaitedLateAndShutdownRejectsAccess()
    {
        var go = new GameObject("P0Root"); var root = go.AddComponent<UPGameRoot>();
        try
        {
            Assert.Throws<InvalidOperationException>(() => root.GetAssetsLoader());
            var start = root.InitializeAsync(); yield return Await(start);
            Assert.IsFalse(start.IsFaulted, start.Exception?.ToString()); Assert.IsTrue(root.IsInited);
            var late = root.WaitUntilReadyAsync(); yield return Await(late); Assert.IsFalse(late.IsFaulted);
            var loader = (AssetsLoader)root.GetAssetsLoader();
            var stop = root.ShutdownAsync(); yield return Await(stop);
            Assert.IsFalse(stop.IsFaulted, stop.Exception?.ToString()); Assert.IsFalse(loader.IsInitialized);
            Assert.Throws<InvalidOperationException>(() => root.GetAssetsLoader());
            var stoppedWaiter = root.WaitUntilReadyAsync(); yield return Await(stoppedWaiter);
            Assert.IsTrue(stoppedWaiter.IsFaulted);
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }
    [UnityTest] public IEnumerator DestroyingRootDuringStartupSettlesWaiters()
    {
        var go = new GameObject("P0DestroyedRoot"); var root = go.AddComponent<UPGameRoot>();
        var start = root.InitializeAsync(); UnityEngine.Object.DestroyImmediate(go);
        yield return Await(start); Assert.IsTrue(start.IsCanceled);
    }
}

public class P0StateMachineRegressionTests
{
    private class State : BaseState
    {
        private readonly string id; public bool Allow = true; public int Enters, Exits;
        public State(string id) { this.id = id; }
        public override string StateID => id;
        public override bool CanTransitionTo(string target) => Allow;
        public override void OnEnter() { Enters++; base.OnEnter(); }
        public override void OnExit() { Exits++; base.OnExit(); }
    }
    private GameObject go; private StateMachineManager machine;
    private State root, a, b, a1, a2, b1;
    [SetUp] public void SetUp()
    {
        go = new GameObject("P0StateMachine"); machine = go.AddComponent<StateMachineManager>();
        root = new State("Root"); a = new State("A"); b = new State("B");
        a1 = new State("A1"); a2 = new State("A2"); b1 = new State("B1");
        machine.RegisterState(root); machine.RegisterState(a, "Root"); machine.RegisterState(b, "Root");
        machine.RegisterState(a1, "A"); machine.RegisterState(a2, "A"); machine.RegisterState(b1, "B");
    }
    [TearDown] public void TearDown() { UnityEngine.Object.DestroyImmediate(go); }
    [Test] public void CrossBranchActivatesAncestorsAndHistoryRestoresLeaf()
    {
        Assert.IsTrue(machine.SwitchState("A/A1")); Assert.IsTrue(machine.SwitchState("B1"));
        Assert.AreEqual("Root/B/B1", machine.ActiveStatePath); Assert.AreSame(b1, machine.CurrentState);
        Assert.IsTrue(machine.GoBack()); Assert.AreEqual("Root/A/A1", machine.ActiveStatePath);
        Assert.IsTrue(machine.GoBack()); Assert.AreEqual("Root", machine.ActiveStatePath);
        Assert.IsFalse(machine.GoBack()); Assert.AreEqual(0, machine.HistoryCount);
    }
    [Test] public void IdAndPathBothRespectActualLeafGuard()
    {
        machine.SwitchState("A/A1"); a1.Allow = false;
        Assert.IsFalse(machine.SwitchState("B1")); Assert.IsFalse(machine.SwitchState("Root/B/B1"));
        Assert.AreEqual("Root/A/A1", machine.ActiveStatePath); Assert.AreEqual(1, machine.HistoryCount);
    }
    [Test] public void AncestorGuardIsAlsoChecked()
    {
        machine.SwitchState("A/A1"); a.Allow = false;
        Assert.IsFalse(machine.SwitchState("A2")); Assert.AreSame(a1, machine.CurrentState);
    }
    [Test] public void InvalidDeepPathHasNoLifecycleSideEffects()
    {
        machine.SwitchState("A/A1"); int enters = b.Enters, exits = a.Exits;
        Assert.IsFalse(machine.SwitchState("B/Missing"));
        Assert.AreEqual(enters, b.Enters); Assert.AreEqual(exits, a.Exits); Assert.AreEqual(1, machine.HistoryCount);
    }
    [Test] public void FailedBackKeepsHistoryAndSameStateDoesNotReenter()
    {
        machine.SwitchState("A1"); machine.SwitchState("A2"); a2.Allow = false;
        Assert.IsFalse(machine.GoBack()); Assert.AreEqual(2, machine.HistoryCount);
        int enters = a2.Enters; Assert.IsTrue(machine.SwitchState("A2")); Assert.AreEqual(enters, a2.Enters);
        a2.Allow = true; Assert.IsTrue(machine.GoBack()); Assert.AreSame(a1, machine.CurrentState);
    }
    [Test] public void NotificationCannotReenterAndCorruptBackHistory()
    {
        machine.SwitchState("A1"); machine.SwitchState("B1");
        bool accepted = true;
        machine.OnStateChanged += (from, to) => accepted = machine.SwitchState("A2");
        Assert.IsTrue(machine.GoBack()); Assert.IsFalse(accepted);
        Assert.AreSame(a1, machine.CurrentState); Assert.AreEqual(1, machine.HistoryCount);
    }
    [Test] public void DefaultChildIsEnteredWhenActivatingNewBranch()
    {
        a.SetDefaultChild("A1"); Assert.IsTrue(machine.SwitchState("A"));
        Assert.AreEqual("Root/A/A1", machine.ActiveStatePath);
        Assert.IsTrue(machine.GoBack()); Assert.AreEqual("Root", machine.ActiveStatePath);
    }
    [Test] public void DestroyExitsActiveHierarchy()
    {
        machine.SwitchState("B/B1"); UnityEngine.Object.DestroyImmediate(go);
        Assert.AreEqual(1, root.Exits); Assert.AreEqual(1, b.Exits); Assert.AreEqual(1, b1.Exits);
    }
}

}