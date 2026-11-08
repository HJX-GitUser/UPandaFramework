using System;
using System.Collections.Generic;
using UnityEngine;

namespace UPandaGF.Gameplay.Replay
{
    public partial class ReplayManager
    {
        private sealed class Registration { public Transform root; public bool children; }
        private sealed class CaptureTarget
        {
            public Transform transform; public int id; public string name, path;
            public Animator animator; public RuntimeAnimatorController controller; public bool animatorDirty;
            public string[] names = Array.Empty<string>(); public int[] hashes = Array.Empty<int>(); public float[] values = Array.Empty<float>();
            public readonly Dictionary<string, object> components = new Dictionary<string, object>();
        }
        private readonly Dictionary<int, Registration> registrations = new Dictionary<int, Registration>();
        private readonly Dictionary<int, CaptureTarget> captureTargets = new Dictionary<int, CaptureTarget>();
        private readonly List<CaptureTarget> captureOrder = new List<CaptureTarget>();
        private readonly List<Transform> discovered = new List<Transform>();
        private readonly HashSet<int> seenTargets = new HashSet<int>();
        private readonly List<ObjectSnapshot> changed = new List<ObjectSnapshot>(), holds = new List<ObjectSnapshot>();
        private readonly List<int> removed = new List<int>();
        private bool targetsDirty = true, capturing;
        private float nextDiscovery, lastSampleTime, recordingStarted;
        public void RegisterTarget(Transform target, bool includeChildren = false)
        {
            if (target == null || destroyed) return;
            registrations[target.GetInstanceID()] = new Registration { root = target, children = includeChildren }; targetsDirty = true;
        }
        public void UnregisterTarget(Transform target)
        { if (target != null && registrations.Remove(target.GetInstanceID())) targetsDirty = true; }
        /// <summary>立即请求下一次采样刷新目标、组件和层级路径；注册模式不会扫描全场景。</summary>
        public void RefreshTargets() { targetsDirty = true; }
        private Transform[] RegisteredTransforms()
        {
            discovered.Clear();
            foreach (var pair in registrations)
            {
                var registration = pair.Value; if (registration.root == null) continue;
                if (registration.children) registration.root.GetComponentsInChildren<Transform>(false, discoveredChildren);
                else { discovered.Add(registration.root); continue; }
                discovered.AddRange(discoveredChildren); discoveredChildren.Clear();
            }
            return discovered.ToArray();
        }
        private readonly List<Transform> discoveredChildren = new List<Transform>();
        private void DiscoverTargets()
        {
            var all = recording.targets == ReplayTargetMode.Registered ? RegisteredTransforms() : GetAllActiveTransforms(); seenTargets.Clear();
            foreach (var tr in all)
            {
                if (tr == null || tr == transform || !tr.gameObject.activeInHierarchy || (excludeUI && tr.GetComponentInParent<Canvas>() != null)) continue;
                int id = tr.gameObject.GetInstanceID(); if (!seenTargets.Add(id)) continue;
                CaptureTarget target;
                if (!captureTargets.TryGetValue(id, out target)) captureTargets.Add(id, target = new CaptureTarget { transform = tr, id = id });
                target.name = tr.name; target.path = HierarchyPath(tr); var animator = tr.GetComponent<Animator>();
                var controller = animator != null ? animator.runtimeAnimatorController : null;
                if (target.animator != animator || target.controller != controller)
                {
                    target.animator = animator; target.controller = controller; target.animatorDirty = true;
                    var names = new List<string>(); var hashes = new List<int>();
                    if (animator != null) foreach (var parameter in animator.parameters) if (parameter.type == AnimatorControllerParameterType.Float) { names.Add(parameter.name); hashes.Add(parameter.nameHash); }
                    target.names = names.ToArray(); target.hashes = hashes.ToArray(); target.values = new float[target.names.Length];
                }
                transformCache[id] = tr;
            }
            removed.Clear(); foreach (var pair in captureTargets) if (!seenTargets.Contains(pair.Key)) removed.Add(pair.Key);
            foreach (int id in removed) { captureTargets.Remove(id); lastSnapshots.Remove(id); lastSnapshotFrame.Remove(id); transformCache.Remove(id); }
            captureOrder.Clear(); captureOrder.AddRange(captureTargets.Values); Statistics.trackedTargets = captureOrder.Count;
            targetsDirty = false; nextDiscovery = Time.realtimeSinceStartup + Mathf.Max(0, recording.discoveryInterval);
        }
        private void ReleaseCapture()
        {
            lastSnapshots.Clear(); lastSnapshotFrame.Clear(); captureTargets.Clear(); captureOrder.Clear(); changed.Clear(); holds.Clear();
            discovered.Clear(); discoveredChildren.Clear(); removed.Clear(); seenTargets.Clear(); Statistics.trackedTargets = 0;
        }
        private void CaptureAtTime(float time)
        {
            if (state != State.Recording || capturing || time < lastSampleTime) return;
            if (recording.maximumDuration > 0 && time - recordingStarted > recording.maximumDuration) { FinishRecording(RecordingStopReason.DurationLimit); return; }
            capturing = true; int epoch = generation;
            try
            {
                if (targetsDirty || Time.realtimeSinceStartup >= nextDiscovery) DiscoverTargets();
                lastSampleTime = time; Statistics.samples++; changed.Clear(); holds.Clear(); long additional = 0;
                for (int i = 0; i < captureOrder.Count; i++)
                {
                    if (epoch != generation || state != State.Recording) return;
                    var target = captureOrder[i]; var tr = target.transform;
                    if (tr == null || !tr.gameObject.activeInHierarchy) { targetsDirty = true; continue; }
                    Statistics.visitedTargets++; ObjectSnapshot previous; lastSnapshots.TryGetValue(target.id, out previous);
                    Vector3 position = tr.localPosition, scale = tr.localScale; Quaternion rotation = tr.localRotation;
                    Dictionary<string, float> animator = previous?.animatorParams; bool animatorChanged = previous == null || target.animatorDirty;
                    if (target.animator != null)
                    {
                        if ((animator?.Count ?? 0) != target.names.Length) animatorChanged = true;
                        for (int j = 0; j < target.names.Length; j++) { float value = target.animator.GetFloat(target.hashes[j]); if (!Mathf.Approximately(value, target.values[j])) animatorChanged = true; target.values[j] = value; }
                        if (animatorChanged) { animator = target.names.Length > 0 ? new Dictionary<string, float>(target.names.Length) : null; for (int j = 0; j < target.names.Length; j++) animator[target.names[j]] = target.values[j]; }
                    }
                    else if (animator != null) { animator = null; animatorChanged = true; }
                    target.animatorDirty = false;
                    var components = target.components; components.Clear();
                    for (int j = 0; j < recordables.Count; j++)
                    {
                        var rec = recordables[j]; bool handles = rec.CanHandle(tr.gameObject);
                        if (epoch != generation || state != State.Recording) return;
                        if (!handles) continue;
                        object prior = null; previous?.componentData?.TryGetValue(rec.Key, out prior); object value;
                        var fast = rec as IReplayChangeRecorder;
                        if (fast != null) { if (!fast.TryCapture(tr.gameObject, prior, out value)) value = prior; }
                        else value = rec.Capture(tr.gameObject);
                        if (epoch != generation || state != State.Recording) return;
                        components[rec.Key] = value;
                    }
                    if (previous != null && previous.position == position && previous.rotation == rotation && previous.localScale == scale
                        && previous.objectName == target.name && previous.objectPath == target.path && !animatorChanged && ComponentsEqual(previous.componentData, components)) continue;
                    var snapshot = new ObjectSnapshot(target.id, position, rotation, scale) { objectName = target.name, objectPath = target.path, animatorParams = animator, animatorStateComplete = true,
                        componentData = components.Count > 0 ? new Dictionary<string, object>(components) : null };
                    long cost = EstimateSnapshot(snapshot); additional = checked(additional + cost + 8); Statistics.createdSnapshots++; changed.Add(snapshot);
                    int frameIndex;
                    if (previous != null && frames.Count > 0 && lastSnapshotFrame.TryGetValue(target.id, out frameIndex) && frameIndex < frames.Count - 1) { holds.Add(previous); additional += 8; }
                }
                if (epoch != generation || state != State.Recording) return;
                // 静止时只更新一个末尾空帧，保留时间跨度与运动前的保持锚点。
                if (changed.Count == 0 && frames.Count > 0 && frames[frames.Count - 1].snapshots.Count == 0)
                { frames[frames.Count - 1].time = time; return; }
                if (changed.Count == 0 && frames.Count > 0 && frames[frames.Count - 1].time == time) return;
                additional += 96;
                var limit = CheckBudget(additional, changed.Count + holds.Count);
                if (limit != RecordingStopReason.None) { FinishRecording(limit); return; }
                if (holds.Count > 0) frames[frames.Count - 1].snapshots.AddRange(holds);
                var frame = new RecordFrame(time, changed.Count); frame.snapshots.AddRange(changed);
                foreach (var snapshot in changed) { lastSnapshots[snapshot.instanceID] = snapshot; lastSnapshotFrame[snapshot.instanceID] = frames.Count; }
                frames.Add(frame); Statistics.estimatedDataBytes += additional; Statistics.snapshotReferences += changed.Count + holds.Count;
                timelinesDirty = true;
            }
            catch (Exception e) { if (epoch == generation) { FinishRecording(RecordingStopReason.CaptureError); Debug.LogException(e); } }
            finally { capturing = false; changed.Clear(); holds.Clear(); }
        }
        private RecordingStopReason CheckBudget(long additional, int snapshots)
        {
            if (recording.maximumFrames > 0 && frames.Count >= recording.maximumFrames) return RecordingStopReason.FrameLimit;
            if (recording.maximumSnapshots > 0 && Statistics.snapshotReferences + snapshots > recording.maximumSnapshots) return RecordingStopReason.SnapshotLimit;
            if (recording.maximumMemoryMB > 0 && Statistics.estimatedDataBytes + additional > (long)recording.maximumMemoryMB * 1024 * 1024) return RecordingStopReason.MemoryLimit;
            return RecordingStopReason.None;
        }
        private long EstimateSnapshot(ObjectSnapshot snapshot)
        {
            long bytes = 192 + 2L * ((snapshot.objectName?.Length ?? 0) + (snapshot.objectPath?.Length ?? 0));
            if (snapshot.animatorParams != null) foreach (var pair in snapshot.animatorParams) bytes += 96 + 2L * pair.Key.Length;
            if (snapshot.componentData != null) foreach (var pair in snapshot.componentData)
            {
                long payload = pair.Value is string text ? 24L + text.Length * 2L : 256;
                foreach (var recorder in recordables) if (recorder.Key == pair.Key && recorder is IReplayMemoryEstimator estimator) { payload = Math.Max(0, estimator.EstimateBytes(pair.Value)); break; }
                bytes = checked(bytes + 96 + pair.Key.Length * 2L + payload);
            }
            return bytes;
        }
        private static bool ComponentsEqual(Dictionary<string, object> a, Dictionary<string, object> b)
        {
            if ((a?.Count ?? 0) != b.Count) return false; if (a == null) return true;
            foreach (var pair in a) { object value; if (!b.TryGetValue(pair.Key, out value) || !Equals(pair.Value, value)) return false; } return true;
        }
    }
}
