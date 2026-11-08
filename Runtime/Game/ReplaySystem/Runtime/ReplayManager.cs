using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace UPandaGF.Gameplay.Replay
{
    /// <summary>有界录制、目标发现与回放控制器；公开入口在 Unity 主线程调用。</summary>
    public partial class ReplayManager : MonoBehaviour
    {
        public enum State { Idle, Recording, Playing, Paused }
        [Header("回放设置")]
        public float speed = 1f;
        public bool excludeUI = true;
        [Header("录制设置")]
        public ReplayRecordingSettings recording = new ReplayRecordingSettings();
        [Header("保存设置")]
        public bool autoSaveOnStop;
        private readonly List<RecordFrame> frames = new List<RecordFrame>();
        private readonly Dictionary<int, ObjectSnapshot> lastSnapshots = new Dictionary<int, ObjectSnapshot>();
        private readonly Dictionary<int, int> lastSnapshotFrame = new Dictionary<int, int>();
        private readonly Dictionary<int, Transform> transformCache = new Dictionary<int, Transform>();
        private readonly Dictionary<string, Transform> transformByName = new Dictionary<string, Transform>();
        private readonly Dictionary<string, Transform> transformByPath = new Dictionary<string, Transform>();
        private readonly Dictionary<Rigidbody, bool> rbKinematicBackup = new Dictionary<Rigidbody, bool>();
        private readonly List<IRecordableComponent> recordables = new List<IRecordableComponent>();
        private State state;
        private int generation;
        private bool destroyed, namesReady;
        private float playbackStartTime, pausedElapsed, progress;
        private bool physicsWasAutoSimulation, physicsWasAutoSyncTransforms;
        public State CurrentState => state;
        public float Speed => speed;
        public float Progress => progress;
        public int FrameCount => frames.Count;
        public bool HasData => frames.Count > 0;
        public float TotalDuration => frames.Count > 1 ? frames[frames.Count - 1].time - frames[0].time : 0f;
        public ReplayStatistics Statistics { get; private set; } = new ReplayStatistics();
        public RecordingStopReason LastRecordingStopReason { get; private set; }
        private void Awake() { RegisterRecordable(new RigidbodyVelocityRecorder()); }
        private void OnDisable()
        {
            generation++;
            if (state == State.Recording) FinishRecording(RecordingStopReason.Disabled);
            StopPlaybackInternal(); ReleaseCapture();
        }
        private void OnDestroy() { destroyed = true; ClearRecording(); registrations.Clear(); recordables.Clear(); }
        public void RegisterRecordable(IRecordableComponent rec)
        { if (rec != null && !recordables.Contains(rec)) { recordables.Add(rec); targetsDirty = true; } }
        public void UnregisterRecordable(IRecordableComponent rec)
        { if (recordables.Remove(rec)) targetsDirty = true; }
        public void StartRecording()
        {
            if (destroyed || !isActiveAndEnabled) return;
            ClearRecording(); if (recording == null) recording = new ReplayRecordingSettings();
            Statistics = new ReplayStatistics(); LastRecordingStopReason = RecordingStopReason.None;
            recordingStarted = Time.time; lastSampleTime = float.NegativeInfinity; targetsDirty = true;
            state = State.Recording; CaptureAtTime(Time.time);
        }
        public void StopRecording()
        {
            if (state != State.Recording) return;
            CaptureAtTime(Time.time);
            if (state == State.Recording) FinishRecording(RecordingStopReason.User);
        }
        private void FinishRecording(RecordingStopReason reason)
        {
            state = State.Idle; generation++; LastRecordingStopReason = reason; ReleaseCapture();
            if (autoSaveOnStop && !destroyed && reason != RecordingStopReason.Disabled) _ = AutoSave();
        }
        private async Task AutoSave() { try { await SaveAsync(); } catch (OperationCanceledException) { } }
        public void ClearRecording()
        {
            generation++; CancelFileOperations(); StopPlaybackInternal(); state = State.Idle;
            frames.Clear(); frames.Capacity = 0; ReleaseCapture(); timelines.Clear(); playbackOrder.Clear();
            transformCache.Clear(); transformByName.Clear(); transformByPath.Clear(); namesReady = false; timelinesDirty = true;
            lastSnapshots.TrimExcess(); lastSnapshotFrame.TrimExcess(); transformCache.TrimExcess(); transformByName.TrimExcess(); transformByPath.TrimExcess();
            captureTargets.TrimExcess(); captureOrder.Capacity = changed.Capacity = holds.Capacity = 0; timelines.TrimExcess(); playbackOrder.Capacity = 0;
            Statistics.estimatedDataBytes = Statistics.snapshotReferences = 0; Statistics.indexedSnapshots = 0; progress = 0;
        }
        private void LateUpdate() { if (state == State.Recording) RecordFrame(); }
        private void RecordFrame()
        {
            float time = Time.time;
            int rate = Math.Max(0, recording.samplesPerSecond);
            if (time <= lastSampleTime || (rate > 0 && time - lastSampleTime < 1f / rate)) return;
            CaptureAtTime(time);
        }
        public void Stop() { if (state == State.Recording) StopRecording(); else StopPlayback(); }
        public void SetSpeed(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return;
            float elapsed = state == State.Playing ? (Time.time - playbackStartTime) * speed : 0;
            speed = Mathf.Clamp(value, .1f, 5f);
            if (state == State.Playing) playbackStartTime = Time.time - elapsed / speed;
        }
        public void StartPlayback()
        {
            if (!HasData || destroyed || !isActiveAndEnabled) return;
            if (state == State.Recording) StopRecording();
            StopPlaybackInternal(); RebuildNameMap(); EnsureTimelines();
            physicsWasAutoSimulation = Physics.autoSimulation; physicsWasAutoSyncTransforms = Physics.autoSyncTransforms;
            Physics.autoSimulation = false; Physics.autoSyncTransforms = false;
            SetRecordedRigidbodiesKinematic(true); playbackStartTime = Time.time; pausedElapsed = progress = 0; state = State.Playing;
        }
        public void PausePlayback() { if (state == State.Playing) { pausedElapsed = (Time.time - playbackStartTime) * speed; state = State.Paused; } }
        public void ResumePlayback() { if (state == State.Paused) { playbackStartTime = Time.time - pausedElapsed / Mathf.Max(speed, .0001f); state = State.Playing; } }
        public void StopPlayback() { generation++; StopPlaybackInternal(); }
        private void StopPlaybackInternal()
        {
            if (state == State.Playing || state == State.Paused)
            { Physics.autoSimulation = physicsWasAutoSimulation; Physics.autoSyncTransforms = physicsWasAutoSyncTransforms; SetRecordedRigidbodiesKinematic(false); }
            if (state != State.Recording) state = State.Idle;
        }
        public void Seek(float normalizedTime)
        {
            if (!HasData || state == State.Recording || float.IsNaN(normalizedTime)) return;
            progress = Mathf.Clamp01(normalizedTime); ApplyAtTime(frames[0].time + progress * TotalDuration);
            if (state == State.Playing) playbackStartTime = Time.time - progress * TotalDuration / Mathf.Max(speed, .0001f);
            else if (state == State.Paused) pausedElapsed = progress * TotalDuration;
        }
        private void Update()
        {
            if (state != State.Playing || !HasData) return;
            float elapsed = Mathf.Max(0, (Time.time - playbackStartTime) * speed), duration = TotalDuration;
            progress = duration > 0 ? Mathf.Clamp01(elapsed / duration) : 1;
            ApplyAtTime(frames[0].time + Math.Min(elapsed, duration));
            if (progress >= 1) StopPlaybackInternal();
        }
        private void RebuildNameMap()
        {
            transformByName.Clear(); transformByPath.Clear(); var ambiguous = new HashSet<string>();
            var all = recording != null && recording.targets == ReplayTargetMode.Registered ? RegisteredTransforms() : GetAllActiveTransforms();
            foreach (var tr in all)
            {
                if (tr == null || tr == transform) continue;
                transformByPath[HierarchyPath(tr)] = tr; string name = tr.name;
                if (ambiguous.Contains(name)) continue;
                if (transformByName.ContainsKey(name)) { transformByName.Remove(name); ambiguous.Add(name); } else transformByName[name] = tr;
            }
            namesReady = true;
        }
        private static string HierarchyPath(Transform tr)
        { string path = string.Empty; for (var node = tr; node != null; node = node.parent) path = "/" + node.GetSiblingIndex() + ":" + node.name + path; return tr.gameObject.scene.name + path; }
        private Transform[] GetAllActiveTransforms()
        {
            Statistics.sceneScans++;
#if UNITY_2022_3_OR_NEWER
            return UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None);
#else
            return UnityEngine.Object.FindObjectsOfType<Transform>();
#endif
        }
    }
}
