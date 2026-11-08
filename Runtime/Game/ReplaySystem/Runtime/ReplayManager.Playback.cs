using System;
using System.Collections.Generic;
using UnityEngine;

namespace UPandaGF.Gameplay.Replay
{
    public partial class ReplayManager
    {
        private struct TimedSnapshot { public float time; public ObjectSnapshot snapshot; public Dictionary<string, float> animator; }
        private sealed class Timeline
        {
            public readonly List<TimedSnapshot> points = new List<TimedSnapshot>();
            public int cursor = -1; public float lastTime = float.NegativeInfinity;
        }
        private readonly Dictionary<int, Timeline> timelines = new Dictionary<int, Timeline>();
        private readonly List<Timeline> playbackOrder = new List<Timeline>();
        private bool timelinesDirty = true;
        private void EnsureTimelines()
        {
            if (!timelinesDirty) return;
            timelines.Clear(); playbackOrder.Clear(); Statistics.indexedSnapshots = 0;
            foreach (var frame in frames) foreach (var snapshot in frame.snapshots)
            {
                Timeline track;
                if (!timelines.TryGetValue(snapshot.instanceID, out track)) { timelines.Add(snapshot.instanceID, track = new Timeline()); playbackOrder.Add(track); }
                var previous = track.points.Count > 0 ? track.points[track.points.Count - 1].animator : null;
                var complete = snapshot.animatorStateComplete ? snapshot.animatorParams : CompleteAnimator(previous, snapshot.animatorParams);
                track.points.Add(new TimedSnapshot { time = frame.time, snapshot = snapshot, animator = complete }); Statistics.indexedSnapshots++;
            }
            timelinesDirty = false;
        }
        private static Dictionary<string, float> CompleteAnimator(Dictionary<string, float> previous, Dictionary<string, float> current)
        {
            if (previous == null) return current;
            if (current == null || ReferenceEquals(previous, current)) return previous;
            bool same = true, full = true;
            foreach (var pair in previous) { float value; if (!current.TryGetValue(pair.Key, out value)) full = false; else if (value != pair.Value) same = false; }
            foreach (var pair in current) if (!previous.ContainsKey(pair.Key)) same = false;
            if (same) return previous;
            if (full) return current;
            var merged = new Dictionary<string, float>(previous); foreach (var pair in current) merged[pair.Key] = pair.Value; return merged;
        }
        private void ApplyAtTime(float time)
        {
            EnsureTimelines(); int epoch = generation;
            for (int i = 0; i < playbackOrder.Count; i++)
            {
                if (epoch != generation) return;
                var track = playbackOrder[i]; var points = track.points; int index = track.cursor;
                if (time >= track.lastTime) { while (index + 1 < points.Count && points[index + 1].time <= time) index++; }
                else
                {
                    int lo = 0, hi = points.Count - 1; index = -1;
                    while (lo <= hi) { int mid = (lo + hi) >> 1; if (points[mid].time <= time) { index = mid; lo = mid + 1; } else hi = mid - 1; }
                }
                track.cursor = index; track.lastTime = time; if (index < 0) continue;
                var a = points[index]; var tr = ResolveTransform(a.snapshot); if (tr == null) continue;
                tr.localPosition = a.snapshot.position; tr.localRotation = a.snapshot.rotation; tr.localScale = a.snapshot.localScale;
                if (index + 1 < points.Count)
                {
                    var b = points[index + 1]; float span = b.time - a.time, t = span > 0 ? Mathf.Clamp01((time - a.time) / span) : 0;
                    tr.localPosition = Vector3.Lerp(a.snapshot.position, b.snapshot.position, t);
                    tr.localRotation = Quaternion.Slerp(a.snapshot.rotation, b.snapshot.rotation, t);
                    tr.localScale = Vector3.Lerp(a.snapshot.localScale, b.snapshot.localScale, t);
                }
                if (a.animator != null)
                { var animator = tr.GetComponent<Animator>(); if (animator != null) foreach (var pair in a.animator) animator.SetFloat(pair.Key, pair.Value); }
                if (a.snapshot.componentData == null) continue;
                for (int j = 0; j < recordables.Count; j++)
                {
                    object value; var rec = recordables[j]; if (a.snapshot.TryGetComponentData(rec.Key, out value)) rec.Apply(tr.gameObject, value);
                    if (epoch != generation) return;
                }
            }
        }
        private Transform ResolveTransform(ObjectSnapshot snapshot)
        {
            Transform tr;
            if (transformCache.TryGetValue(snapshot.instanceID, out tr) && tr != null) return tr;
            if (!namesReady) RebuildNameMap();
            if ((!string.IsNullOrEmpty(snapshot.objectPath) && transformByPath.TryGetValue(snapshot.objectPath, out tr) && tr != null)
                || (!string.IsNullOrEmpty(snapshot.objectName) && transformByName.TryGetValue(snapshot.objectName, out tr) && tr != null))
            { transformCache[snapshot.instanceID] = tr; return tr; }
            return null;
        }
        private void SetRecordedRigidbodiesKinematic(bool kinematic)
        {
            if (!kinematic)
            { foreach (var pair in rbKinematicBackup) if (pair.Key != null) pair.Key.isKinematic = pair.Value; rbKinematicBackup.Clear(); return; }
            rbKinematicBackup.Clear();
            foreach (var track in playbackOrder)
            {
                var tr = ResolveTransform(track.points[0].snapshot); if (tr == null) tr = ResolveTransform(track.points[track.points.Count - 1].snapshot);
                if (tr == null) continue; var rb = tr.GetComponent<Rigidbody>();
                if (rb != null && !rbKinematicBackup.ContainsKey(rb)) { rbKinematicBackup[rb] = rb.isKinematic; rb.isKinematic = true; }
            }
        }
    }
}
