using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UPandaGF.Gameplay.Replay;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace UPandaGF.Tests
{
using Object = UnityEngine.Object;
using ReplayFiles = UPandaGF.Gameplay.Replay.FileManager;

[PrebuildSetup(typeof(ReplayPerformanceFixtures))]
[PostBuildCleanup(typeof(ReplayPerformanceFixtures))]
public class ReplayPerformanceTests
{
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly List<GameObject> objects = new List<GameObject>();
    private readonly HashSet<string> files = new HashSet<string>();
    private string directory;
    [Serializable] public sealed class Payload { public string text; public int value; }
    private sealed class Recorder : IRecordableComponent, IReplayMemoryEstimator
    {
        public string Key => "ReplayPerformance";
        public bool accepts = true; public int value; public long bytes = 32; public Action callback;
        public bool CanHandle(GameObject go) => accepts;
        public object Capture(GameObject go) { callback?.Invoke(); return value; }
        public void Apply(GameObject go, object state) { value = (int)state; }
        public long EstimateBytes(object state) => bytes;
    }
    [SetUp] public void Setup()
    { directory = Path.Combine(Application.temporaryCachePath, "ReplayPerformanceTests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory); }
    [TearDown] public void Cleanup()
    {
        foreach (var go in objects) if (go != null) Object.DestroyImmediate(go); objects.Clear();
        foreach (string file in files) if (File.Exists(file)) File.Delete(file); files.Clear();
        if (Path.GetFullPath(directory).StartsWith(Path.GetFullPath(Path.Combine(Application.temporaryCachePath, "ReplayPerformanceTests")) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) Directory.Delete(directory, true);
    }
    private GameObject Go(string name = "ReplayTarget") { var go = new GameObject(name); objects.Add(go); return go; }
    private ReplayManager Manager(bool registered = true)
    {
        var manager = Go("ReplayManager").AddComponent<ReplayManager>(); manager.recording.targets = registered ? ReplayTargetMode.Registered : ReplayTargetMode.Scene;
        manager.recording.maximumDuration = 0; manager.recording.discoveryInterval = 10000; return manager;
    }
    private static List<RecordFrame> Frames(ReplayManager manager) { return (List<RecordFrame>)typeof(ReplayManager).GetField("frames", Private).GetValue(manager); }
    private static int CacheCount(ReplayManager manager, string name) { return ((ICollection)typeof(ReplayManager).GetField(name, Private).GetValue(manager)).Count; }
    private static void Capture(ReplayManager manager, float offset)
    { typeof(ReplayManager).GetMethod("CaptureAtTime", Private).Invoke(manager, new object[] { Frames(manager)[0].time + offset }); }
    private string Remember(string path) { Assert.IsNotNull(path); files.Add(path); return path; }
    private static IEnumerator Wait(Task task)
    {
        float deadline = Time.realtimeSinceStartup + 30; while (!task.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.IsTrue(task.IsCompleted, "异步文件操作未结束"); if (task.IsFaulted) throw task.Exception;
    }
    private static IEnumerator Canceled(Task task)
    {
        float deadline = Time.realtimeSinceStartup + 30; while (!task.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.IsTrue(task.IsCompleted); Assert.IsTrue(task.IsCanceled || (task.IsFaulted && task.Exception.GetBaseException() is OperationCanceledException));
    }
    private static List<RecordFrame> Data(int frameCount = 2, int targets = 1)
    {
        var result = new List<RecordFrame>(frameCount);
        for (int i = 0; i < frameCount; i++)
        {
            var frame = new RecordFrame(i, targets);
            for (int j = 0; j < targets; j++) frame.snapshots.Add(new ObjectSnapshot(j + 1, new Vector3(i, j, 0), Quaternion.identity, Vector3.one) { objectName = "target" + j, objectPath = "scene/" + j });
            result.Add(frame);
        }
        return result;
    }
    [Test] public void RegisteredTargetsAvoidFullSceneSearchAndDeduplicateChildren()
    {
        var manager = Manager(); var parent = Go("parent").transform; var child = Go("child").transform; child.SetParent(parent); Go("unrelated");
        manager.RegisterTarget(parent, true); manager.RegisterTarget(child); manager.StartRecording();
        Assert.AreEqual(2, Frames(manager)[0].snapshots.Count); Assert.AreEqual(0, manager.Statistics.sceneScans); Assert.AreEqual(2, manager.Statistics.trackedTargets);
    }
    [Test] public void RegisteredTargetCanBeRemovedAndNewTargetAddedDuringRecording()
    {
        var manager = Manager(); var first = Go("first").transform; var second = Go("second").transform; manager.RegisterTarget(first); manager.StartRecording();
        manager.UnregisterTarget(first); manager.RegisterTarget(second); Capture(manager, 1);
        Assert.AreEqual(1, manager.Statistics.trackedTargets); Assert.AreEqual(second.gameObject.GetInstanceID(), Frames(manager).Last().snapshots.Single().instanceID);
    }
    [Test] public void SceneDiscoveryIsPeriodicAndManualRefreshFindsSpawnedObject()
    {
        var manager = Manager(false); manager.StartRecording(); long scans = manager.Statistics.sceneScans; Go("spawned"); Capture(manager, 1);
        Assert.AreEqual(scans, manager.Statistics.sceneScans); manager.RefreshTargets(); Capture(manager, 2); Assert.AreEqual(scans + 1, manager.Statistics.sceneScans);
        Assert.IsTrue(Frames(manager).Last().snapshots.Any(s => s.objectName == "spawned"));
    }
    [Test] public void UiObjectsAreExcludedAndCanBeIncludedExplicitly()
    {
        var manager = Manager(); var canvas = Go("UI"); canvas.AddComponent<Canvas>(); var child = Go("child").transform; child.SetParent(canvas.transform); manager.RegisterTarget(canvas.transform, true);
        manager.StartRecording(); Assert.IsEmpty(Frames(manager)[0].snapshots); manager.StopRecording(); manager.excludeUI = false; manager.StartRecording(); Assert.AreEqual(2, Frames(manager)[0].snapshots.Count);
    }
    [Test] public void DestroyedRegisteredTargetIsPrunedWithoutHoldingCaptureState()
    {
        var manager = Manager(); var target = Go().transform; manager.RegisterTarget(target); manager.StartRecording(); Object.DestroyImmediate(target.gameObject); Capture(manager, 1); Capture(manager, 2);
        Assert.AreEqual(0, manager.Statistics.trackedTargets); Assert.AreEqual(0, CacheCount(manager, "lastSnapshots"));
    }
    [Test] public void StaticSamplingCoalescesFramesAndPreservesDuration()
    {
        var manager = Manager(); manager.RegisterTarget(Go().transform); manager.StartRecording(); for (int i = 1; i <= 1000; i++) Capture(manager, i * .01f);
        Assert.AreEqual(2, manager.FrameCount); Assert.That(manager.TotalDuration, Is.EqualTo(10).Within(.001)); Assert.AreEqual(1, manager.Statistics.createdSnapshots);
    }
    [Test] public void StationaryToMovingSeekKeepsHoldBeforeInterpolation()
    {
        var manager = Manager(); var tr = Go().transform; manager.RegisterTarget(tr); manager.StartRecording(); Capture(manager, 2); Capture(manager, 8); tr.localPosition = Vector3.right * 10; Capture(manager, 10); manager.StopRecording();
        manager.Seek(.5f); Assert.AreEqual(Vector3.zero, tr.localPosition); manager.Seek(.9f); Assert.That(tr.localPosition.x, Is.EqualTo(5).Within(.001)); manager.Seek(0); Assert.AreEqual(Vector3.zero, tr.localPosition);
    }
    [Test] public void FrameBudgetStopsBeforeIncompleteFrameIsAdded()
    {
        var manager = Manager(); var tr = Go().transform; manager.RegisterTarget(tr); manager.recording.maximumFrames = 2; manager.StartRecording(); tr.localPosition = Vector3.one; Capture(manager, 1); tr.localPosition = Vector3.one * 2; Capture(manager, 2);
        Assert.AreEqual(ReplayManager.State.Idle, manager.CurrentState); Assert.AreEqual(RecordingStopReason.FrameLimit, manager.LastRecordingStopReason); Assert.AreEqual(2, manager.FrameCount); Assert.AreEqual(Vector3.one, Frames(manager).Last().snapshots[0].position);
    }
    [Test] public void SnapshotBudgetDoesNotPublishPartialTargetSet()
    {
        var manager = Manager(); manager.RegisterTarget(Go("one").transform); manager.RegisterTarget(Go("two").transform); manager.recording.maximumSnapshots = 1; manager.StartRecording();
        Assert.AreEqual(RecordingStopReason.SnapshotLimit, manager.LastRecordingStopReason); Assert.AreEqual(0, manager.FrameCount); Assert.AreEqual(0, manager.Statistics.snapshotReferences);
    }
    [Test] public void CustomPayloadEstimatorParticipatesInMemoryBudget()
    {
        var manager = Manager(); manager.RegisterTarget(Go().transform); manager.RegisterRecordable(new Recorder { bytes = 2 * 1024 * 1024 }); manager.recording.maximumMemoryMB = 1; manager.StartRecording();
        Assert.AreEqual(RecordingStopReason.MemoryLimit, manager.LastRecordingStopReason); Assert.IsFalse(manager.HasData);
    }
    [Test] public void DurationBudgetStopsRecordingWithoutGrowingDataPastLimit()
    {
        var manager = Manager(); manager.recording.maximumDuration = 1; manager.RegisterTarget(Go().transform); manager.StartRecording(); Capture(manager, .5f); Capture(manager, 2);
        Assert.AreEqual(RecordingStopReason.DurationLimit, manager.LastRecordingStopReason); Assert.LessOrEqual(manager.TotalDuration, 1);
    }
    [Test] public void RestartClearsPreviousTimelineAndDisableReleasesCapture()
    {
        var manager = Manager(); manager.RegisterTarget(Go().transform); manager.StartRecording(); manager.StopRecording(); manager.Seek(0); Assert.Greater(CacheCount(manager, "timelines"), 0);
        manager.StartRecording(); Assert.AreEqual(0, CacheCount(manager, "timelines")); manager.enabled = false;
        Assert.AreEqual(0, CacheCount(manager, "lastSnapshots")); Assert.AreEqual(0, manager.Statistics.trackedTargets); Assert.IsTrue(manager.HasData);
    }
    [Test] public void ClearReleasesFramesIndexesAndCapacity()
    {
        var manager = Manager(); manager.RegisterTarget(Go().transform); manager.StartRecording(); manager.StopRecording(); manager.Seek(0); manager.ClearRecording();
        Assert.IsFalse(manager.HasData); Assert.AreEqual(0, Frames(manager).Capacity); Assert.AreEqual(0, CacheCount(manager, "timelines")); Assert.AreEqual(0, manager.Statistics.estimatedDataBytes);
    }
    [Test] public void ReentrantCaptureClearDoesNotAppendStaleFrame()
    {
        var manager = Manager(); manager.RegisterTarget(Go().transform); manager.RegisterRecordable(new Recorder { callback = manager.ClearRecording }); manager.StartRecording();
        Assert.IsFalse(manager.HasData); Assert.AreEqual(ReplayManager.State.Idle, manager.CurrentState);
    }
    [Test] public void RegisteredMetadataRefreshCapturesRenamingAndReparenting()
    {
        var manager = Manager(); var tr = Go("before").transform; manager.RegisterTarget(tr); manager.StartRecording(); string prior = Frames(manager)[0].snapshots[0].objectPath;
        tr.name = "after"; tr.SetParent(Go("new-parent").transform); manager.RefreshTargets(); Capture(manager, 1);
        Assert.AreEqual("after", Frames(manager).Last().snapshots[0].objectName); Assert.AreNotEqual(prior, Frames(manager).Last().snapshots[0].objectPath);
    }
    [Test] public void StaticCaptureAvoidsPerTargetAllocationBlocks()
    {
        var manager = Manager(); for (int i = 0; i < 200; i++) manager.RegisterTarget(Go("static" + i).transform); manager.StartRecording(); Capture(manager, 1);
        var method = typeof(ReplayManager).GetMethod("CaptureAtTime", Private); var action = (Action<float>)Delegate.CreateDelegate(typeof(Action<float>), manager, method);
        var recorder = UnityEngine.Profiling.Recorder.Get("GC.Alloc"); recorder.enabled = false; recorder.FilterToCurrentThread(); recorder.enabled = true;
        for (int i = 0; i < 100; i++) action(Frames(manager)[0].time + 2 + i);
        recorder.enabled = false; int blocks = recorder.sampleBlockCount; recorder.CollectFromAllThreads();
        Debug.Log("REPLAY_OPTIMIZED objects=200 samples=100 allocationBlocks=" + blocks); Assert.Less(blocks, 1000); Assert.AreEqual(0, manager.Statistics.sceneScans); Assert.AreEqual(200, manager.Statistics.createdSnapshots);
    }
    [Test] public void TimelineReferencesOriginalSnapshotsAndReverseSeekWorks()
    {
        var manager = Manager(); var tr = Go().transform; manager.RegisterTarget(tr); manager.StartRecording(); tr.localPosition = Vector3.right * 10; Capture(manager, 1); manager.StopRecording();
        var first = Frames(manager)[0].snapshots[0]; manager.Seek(.5f); Assert.That(tr.localPosition.x, Is.EqualTo(5).Within(.001)); manager.Seek(1); manager.Seek(0); Assert.AreEqual(Vector3.zero, tr.localPosition);
        var dictionary = (IDictionary)typeof(ReplayManager).GetField("timelines", Private).GetValue(manager); var timeline = dictionary[first.instanceID]; var points = (IList)timeline.GetType().GetField("points").GetValue(timeline);
        Assert.AreSame(first, points[0].GetType().GetField("snapshot").GetValue(points[0]));
    }
    [Test] public void RigidbodyAndGlobalPhysicsRestoreAfterPlaybackDisable()
    {
        bool physics = Physics.autoSimulation; var manager = Manager(); var tr = Go().transform; var rb = tr.gameObject.AddComponent<Rigidbody>(); manager.RegisterTarget(tr); manager.StartRecording(); manager.StopRecording(); manager.StartPlayback();
        Assert.IsTrue(rb.isKinematic); Assert.IsFalse(Physics.autoSimulation); manager.enabled = false; Assert.IsFalse(rb.isKinematic); Assert.AreEqual(physics, Physics.autoSimulation);
    }
    [UnityTest] public IEnumerator SamplingRateLimitsCaptureWhileMovementContinues()
    {
        var manager = Manager(); var tr = Go().transform; manager.RegisterTarget(tr); manager.recording.samplesPerSecond = 10; manager.StartRecording(); float end = Time.time + .26f;
        while (Time.time < end) { tr.localPosition += Vector3.one; yield return null; }
        Assert.LessOrEqual(manager.Statistics.samples, 4); Assert.GreaterOrEqual(manager.Statistics.samples, 2);
    }
    [Test] public void SynchronousSaveRemainsVersionTwoAndSupportsEscapedText()
    {
        ReplayFiles.RegisterComponentState<Payload>("ReplayPayload"); var data = Data(); data[0].snapshots[0].SetComponentData("ReplayPayload", new Payload { text = "括号 } ] 引号 \" 反斜线 \\ \n", value = 8 });
        string path = Remember(ReplayFiles.Save(data)); Assert.IsTrue(File.ReadAllText(path).StartsWith("UPANDA_REPLAY:2\n")); var loaded = ReplayFiles.Load(path);
        Assert.AreEqual(2, loaded.Count); Assert.AreEqual(8, ((Payload)loaded[0].snapshots[0].componentData["ReplayPayload"]).value);
    }
    [Test] public void ReaderAcceptsVersionAfterFramesAndUnknownRootProperties()
    {
        string path = Path.Combine(directory, "order.dat"); File.WriteAllText(path, "UPANDA_REPLAY:2\n{\"frames\":[{\"time\":0,\"snapshots\":[]}],\"future\":{\"text\":\"} ] \\\"\"},\"version\":2}"); Assert.AreEqual(1, ReplayFiles.Load(path).Count);
    }
    [TestCase("{\"version\":3,\"frames\":[]}")]
    [TestCase("{\"version\":2,\"frames\":[{\"time\":0,\"snapshots\":[]}]}garbage")]
    [TestCase("{\"version\":2,\"frames\":[{\"time\":2,\"snapshots\":[]},{\"time\":1,\"snapshots\":[]}]}")]
    [TestCase("{\"version\":2,\"frames\":[{\"time\":0,\"snapshots\":[]}")]
    [TestCase("{\"version\":2,\"frames\":null}")]
    [TestCase("{\"version\":2,\"version\":2,\"frames\":[]}")]
    public void MalformedOrUnsupportedStreamIsRejected(string json)
    { string path = Path.Combine(directory, "invalid.dat"); File.WriteAllText(path, "UPANDA_REPLAY:2\n" + json); Assert.IsNull(ReplayFiles.Load(path)); }
    [Test] public void InvalidSnapshotDoesNotPublishPartialFile()
    {
        var data = Data(); data[1].snapshots[0].position = new Vector3(float.NaN, 0, 0); var prior = new HashSet<string>(ReplayFiles.ListFiles());
        Assert.IsNull(ReplayFiles.Save(data)); CollectionAssert.AreEquivalent(prior, ReplayFiles.ListFiles());
    }
    [Test] public void LoadOverCurrentBudgetPreservesExistingRecording()
    {
        string path = Remember(ReplayFiles.Save(Data(3))); var manager = Manager(); manager.RegisterTarget(Go().transform); manager.StartRecording(); manager.StopRecording(); int old = manager.FrameCount;
        manager.recording.maximumFrames = 1; Assert.IsFalse(manager.Load(path)); Assert.AreEqual(old, manager.FrameCount); Assert.IsTrue(manager.HasData);
    }
    [UnityTest] public IEnumerator AsyncSaveFreezesPrefixAndAllowsFramesToAdvance()
    {
        var data = Data(400, 8); var task = ReplayFiles.SaveAsync(data); data[0].time = -50; data[0].snapshots.Add(new ObjectSnapshot(999, Vector3.one, Quaternion.identity, Vector3.one)); data.Add(new RecordFrame(900));
        int frames = 0; while (!task.IsCompleted) { frames++; yield return null; } yield return Wait(task); string path = Remember(task.Result);
        var load = ReplayFiles.LoadAsync(path); yield return Wait(load); Assert.AreEqual(400, load.Result.Count); Assert.AreEqual(0, load.Result[0].time); Assert.AreEqual(8, load.Result[0].snapshots.Count); Assert.Greater(frames, 0);
    }
    [UnityTest] public IEnumerator AsyncCancellationDeletesOnlyItsTemporaryFile()
    {
        var before = new HashSet<string>(Directory.GetFiles(ReplayFiles.FolderPath)); using (var cts = new CancellationTokenSource())
        {
            var task = ReplayFiles.SaveAsync(Data(500, 40), cts.Token); yield return null; cts.Cancel(); yield return Canceled(task);
            CollectionAssert.AreEquivalent(before, Directory.GetFiles(ReplayFiles.FolderPath));
        }
    }
    [UnityTest] public IEnumerator AsyncManagerSaveCoalescesAndClearCancels()
    {
        var manager = Manager(); Frames(manager).AddRange(Data(500, 20)); var a = manager.SaveAsync(); var b = manager.SaveAsync(); Assert.AreSame(a, b); Assert.IsTrue(manager.IsFileOperationRunning); manager.ClearRecording(); yield return Canceled(a); Assert.IsFalse(manager.HasData);
    }
    [UnityTest] public IEnumerator LatestAsyncLoadWinsAndOldReadCannotReplaceIt()
    {
        string first = Remember(ReplayFiles.Save(Data(200, 10))), second = Remember(ReplayFiles.Save(Data(2))); var manager = Manager(); var a = manager.LoadAsync(first); var b = manager.LoadAsync(second);
        yield return Wait(b); Assert.IsTrue(b.Result); Assert.AreEqual(2, manager.FrameCount); while (!a.IsCompleted) yield return null; Assert.IsTrue(a.IsCanceled || !a.Result);
    }
    [UnityTest] public IEnumerator DestroyedManagerCannotAdoptPendingRead()
    {
        string path = Remember(ReplayFiles.Save(Data(1000, 4))); var manager = Manager(); var pending = manager.LoadAsync(path); Object.DestroyImmediate(manager.gameObject);
        float end = Time.realtimeSinceStartup + 10; while (!pending.IsCompleted && Time.realtimeSinceStartup < end) yield return null; Assert.IsTrue(pending.IsCompleted); Assert.IsTrue(pending.IsCanceled || !pending.Result);
    }
    private Animator AnimatorTarget(ReplayManager manager)
    {
        var animator = Go("Animated").AddComponent<Animator>();
#if UNITY_EDITOR
        animator.runtimeAnimatorController = UnityEditor.AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ReplayPerformanceFixtures.ControllerPath);
#endif
        manager.RegisterTarget(animator.transform); return animator;
    }
    [Test] public void AnimatorValuesAreSharedWhenOnlyTransformChanges()
    {
        var manager = Manager(); var animator = AnimatorTarget(manager); animator.SetFloat("Speed", 2); manager.StartRecording(); var original = Frames(manager)[0].snapshots[0].animatorParams;
        animator.transform.localPosition = Vector3.one; Capture(manager, 1); Assert.AreSame(original, Frames(manager).Last().snapshots[0].animatorParams);
        animator.SetFloat("Speed", 4); Capture(manager, 2); Assert.AreNotSame(original, Frames(manager).Last().snapshots[0].animatorParams); manager.StopRecording();
        manager.Seek(0); Assert.AreEqual(2, animator.GetFloat("Speed")); manager.Seek(1); Assert.AreEqual(4, animator.GetFloat("Speed"));
    }
    [Test] public void LegacySparseAnimatorValuesSurviveForwardAndReverseSeek()
    {
        var manager = Manager(); var animator = AnimatorTarget(manager); int id = animator.gameObject.GetInstanceID(); var data = Frames(manager);
        data.Add(new RecordFrame(0) { snapshots = new List<ObjectSnapshot> { new ObjectSnapshot(id, Vector3.zero, Quaternion.identity, Vector3.one) { objectName = animator.name, animatorParams = new Dictionary<string, float> { { "Speed", 2 }, { "Weight", .4f } } } } });
        data.Add(new RecordFrame(1) { snapshots = new List<ObjectSnapshot> { new ObjectSnapshot(id, Vector3.one, Quaternion.identity, Vector3.one) { objectName = animator.name, animatorParams = new Dictionary<string, float> { { "Speed", 5 } } } } });
        manager.Seek(1); Assert.AreEqual(5, animator.GetFloat("Speed")); Assert.AreEqual(.4f, animator.GetFloat("Weight")); manager.Seek(0); Assert.AreEqual(2, animator.GetFloat("Speed"));
    }
    [Test] public void ControllerReplacementCapturesNewParameterNamesAndPersistsCompleteness()
    {
        var manager = Manager(); var animator = AnimatorTarget(manager); manager.StartRecording();
#if UNITY_EDITOR
        animator.runtimeAnimatorController = UnityEditor.AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(Path.GetDirectoryName(ReplayPerformanceFixtures.ControllerPath) + "/Alternate.controller");
#endif
        manager.RefreshTargets(); Capture(manager, 1); manager.StopRecording(); var latest = Frames(manager).Last().snapshots[0];
        Assert.IsTrue(latest.animatorStateComplete); CollectionAssert.AreEquivalent(new[] { "Weight" }, latest.animatorParams.Keys);
        string path = Remember(manager.Save()); var restored = ReplayFiles.Load(path); Assert.IsTrue(restored.Last().snapshots[0].animatorStateComplete);
    }
    [Test] public void StationaryRigidbodyUsesChangeRecorderWithoutRepeatedBoxing()
    {
        var manager = Manager(); var target = Go().AddComponent<Rigidbody>(); manager.RegisterTarget(target.transform); manager.StartRecording(); Capture(manager, 1);
        var method = typeof(ReplayManager).GetMethod("CaptureAtTime", Private); var capture = (Action<float>)Delegate.CreateDelegate(typeof(Action<float>), manager, method); float start = Frames(manager)[0].time;
        var recorder = UnityEngine.Profiling.Recorder.Get("GC.Alloc"); recorder.enabled = false; recorder.FilterToCurrentThread(); recorder.enabled = true;
        for (int i = 0; i < 100; i++) capture(start + 2 + i); recorder.enabled = false; int blocks = recorder.sampleBlockCount; recorder.CollectFromAllThreads(); Assert.Less(blocks, 20);
    }
    [UnityTest] public IEnumerator UiDestroyUnsubscribesExternalButtons()
    {
        var manager = Manager(); var button = Go("button").AddComponent<Button>(); var ui = Go("UI").AddComponent<ReplayUI>(); ui.manager = manager; ui.recordButton = button; yield return null;
        Object.DestroyImmediate(ui.gameObject); button.onClick.Invoke(); Assert.AreEqual(ReplayManager.State.Idle, manager.CurrentState);
    }
}

}