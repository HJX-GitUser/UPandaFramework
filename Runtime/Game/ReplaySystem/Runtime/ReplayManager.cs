using System;
using System.Collections.Generic;
using UnityEngine;

namespace ReplaySystem
{
    /// <summary>
    /// 录制与回放主控制器。挂载到场景中的空物体上即可使用。
    /// </summary>
    public class ReplayManager : MonoBehaviour
    {
        public enum State { Idle, Recording, Playing, Paused }

        [Header("回放设置")]
        [Tooltip("回放速度倍率（0.1x ~ 5x）")]
        public float speed = 1f;

        [Tooltip("录制时是否排除 UI（Canvas 及其子物体）")]
        public bool excludeUI = true;

        [Header("保存设置")]
        [Tooltip("停止录制后是否自动保存到本地文件")]
        public bool autoSaveOnStop = false;

        // ============ 录制数据 ============
        private readonly List<RecordFrame> frames = new List<RecordFrame>();

        // ============ 录制缓存 ============
        private readonly Dictionary<int, ObjectSnapshot> lastSnapshots = new Dictionary<int, ObjectSnapshot>();
        private readonly Dictionary<int, float[]> lastAnimValues = new Dictionary<int, float[]>();
        private readonly Dictionary<int, string[]> animatorParamNames = new Dictionary<int, string[]>();

        // ============ 运行时对象索引 ============
        private readonly Dictionary<int, Transform> transformCache = new Dictionary<int, Transform>();        // instanceID -> Transform（同会话）
        private readonly Dictionary<string, Transform> transformByName = new Dictionary<string, Transform>(); // name -> Transform（跨会话回退）
        private readonly Dictionary<Rigidbody, bool> rbKinematicBackup = new Dictionary<Rigidbody, bool>();
        private readonly List<IRecordableComponent> recordables = new List<IRecordableComponent>();

        private struct TimedSnapshot
        {
            public float time;
            public ObjectSnapshot snapshot;
        }
        private readonly Dictionary<int, List<TimedSnapshot>> timelines = new Dictionary<int, List<TimedSnapshot>>();
        private readonly Dictionary<string, Transform> transformByPath = new Dictionary<string, Transform>();
        private readonly Dictionary<int, int> lastSnapshotFrame = new Dictionary<int, int>();
        private bool timelinesDirty = true;
        private State state = State.Idle;
        private float playbackStartTime;
        private float pausedElapsed;
        private float progress;
        private bool physicsWasAutoSimulation;
        private bool physicsWasAutoSyncTransforms;

        public State CurrentState => state;
        public float Speed => speed;
        public float Progress => progress;
        public int FrameCount => frames.Count;
        public bool HasData => frames.Count > 0;
        public float TotalDuration => frames.Count > 1 ? frames[frames.Count - 1].time - frames[0].time : 0f;

        void Awake()
        {
            // 默认注册 Rigidbody 速度扩展，演示 IRecordableComponent 用法
            RegisterRecordable(new RigidbodyVelocityRecorder());
        }

        void OnDisable()
        {
            StopPlaybackInternal();
        }

        void OnDestroy()
        {
            // 异常退出兜底：恢复物理与刚体状态
            StopPlaybackInternal();
        }

        // ================= 公开接口 =================

        public void RegisterRecordable(IRecordableComponent rec)
        {
            if (rec != null && !recordables.Contains(rec)) recordables.Add(rec);
        }

        public void StartRecording()
        {
            StopPlaybackInternal();
            frames.Clear();
            lastSnapshots.Clear();
            lastSnapshotFrame.Clear();
            lastAnimValues.Clear();
            animatorParamNames.Clear();
            transformCache.Clear();
            RebuildNameMap();
            timelinesDirty = true;
            state = State.Recording;
            RecordFrame();
            Debug.Log("[ReplaySystem] 开始录制");
        }

        public void StopRecording()
        {
            if (state != State.Recording) return;
            RecordFrame();
            state = State.Idle;
            Debug.Log($"[ReplaySystem] 停止录制：{frames.Count} 帧，时长 {TotalDuration:F2}s");
            if (autoSaveOnStop) Save();
        }

        /// <summary>保存录制数据到本地，返回文件路径（无数据返回 null）</summary>
        public string Save()
        {
            if (frames.Count == 0)
            {
                Debug.LogWarning("[ReplaySystem] 没有可保存的录制数据");
                return null;
            }
            string path = FileManager.Save(frames);
            if (path != null) Debug.Log("[ReplaySystem] 已保存到：" + path);
            return path;
        }

        /// <summary>从指定路径加载录制文件到内存，准备回放</summary>
        public bool Load(string path)
        {
            var data = FileManager.Load(path);
            if (data == null || data.Count == 0)
            {
                Debug.LogWarning("[ReplaySystem] 加载失败：" + path);
                return false;
            }
            StopPlaybackInternal();
            frames.Clear();
            frames.AddRange(data);
            timelinesDirty = true;
            frames.Sort((a, b) => a.time.CompareTo(b.time));
            transformCache.Clear();
            RebuildNameMap();
            progress = 0f;
            state = State.Idle;
            Debug.Log($"[ReplaySystem] 已加载 {frames.Count} 帧");
            return true;
        }

        public void StartPlayback()
        {
            if (frames.Count == 0)
            {
                Debug.LogWarning("[ReplaySystem] 没有可回放的录制数据");
                return;
            }
            StopPlaybackInternal();
            RebuildNameMap();

            // 冻结物理 + 将记录物体设为 Kinematic，防止物理引擎干扰位置还原
            physicsWasAutoSimulation = Physics.autoSimulation;
            physicsWasAutoSyncTransforms = Physics.autoSyncTransforms;
            Physics.autoSimulation = false;
            Physics.autoSyncTransforms = false;
            SetRecordedRigidbodiesKinematic(true);

            playbackStartTime = Time.time;
            pausedElapsed = 0f;
            progress = 0f;
            state = State.Playing;
            Debug.Log("[ReplaySystem] 开始回放");
        }

        public void PausePlayback()
        {
            if (state != State.Playing) return;
            pausedElapsed = (Time.time - playbackStartTime) * speed;
            state = State.Paused;
        }

        public void ResumePlayback()
        {
            if (state != State.Paused) return;
            playbackStartTime = Time.time - pausedElapsed / Mathf.Max(speed, 0.0001f);
            state = State.Playing;
        }

        public void StopPlayback()
        {
            if (state != State.Playing && state != State.Paused) return;
            StopPlaybackInternal();
            Debug.Log("[ReplaySystem] 停止回放");
        }

        /// <summary>停止：录制或回放均可</summary>
        public void Stop()
        {
            if (state == State.Recording) { StopRecording(); return; }
            StopPlayback();
        }

        public void SetSpeed(float s)
        {
            if (state == State.Playing)
            {
                // 保持当前进度不变，只改变速率
                float elapsed = (Time.time - playbackStartTime) * speed;
                speed = Mathf.Clamp(s, 0.1f, 5f);
                playbackStartTime = Time.time - elapsed / speed;
            }
            else
            {
                speed = Mathf.Clamp(s, 0.1f, 5f);
            }
        }

        /// <summary>跳转到录制数据的任意时间点（normalizedTime 范围 0~1），恢复所有物体状态</summary>
        public void Seek(float normalizedTime)
        {
            if (frames.Count == 0) return;
            normalizedTime = Mathf.Clamp01(normalizedTime);
            progress = normalizedTime;
            float time = frames[0].time + normalizedTime * TotalDuration;
            ApplyAtTime(time);

            float elapsed = normalizedTime * TotalDuration;
            if (state == State.Playing)
                playbackStartTime = Time.time - elapsed / Mathf.Max(speed, 0.0001f);
            else if (state == State.Paused)
                pausedElapsed = elapsed;
        }

        // ================= 内部实现 =================

        void LateUpdate()
        {
            // 在 LateUpdate 录制：此时脚本与 Animator 已更新完 Transform，可捕获最终姿态
            if (state != State.Recording) return;
            RecordFrame();
        }

        void Update()
        {
            if (state != State.Playing) return;
            if (frames.Count == 0) return;

            float total = TotalDuration;
            if (total <= 0f)
            {
                ApplyFrame(frames[0]);
                progress = 1f;
                StopPlaybackInternal();
                return;
            }

            float elapsed = (Time.time - playbackStartTime) * speed;
            if (elapsed >= total)
            {
                elapsed = total;
                progress = 1f;
                ApplyAtTime(frames[0].time + elapsed);
                StopPlaybackInternal();
                Debug.Log("[ReplaySystem] 回放结束");
                return;
            }

            progress = elapsed / total;
            ApplyAtTime(frames[0].time + elapsed);
        }

        private void RecordFrame()
        {
            float t = Time.time;
            var snapshotList = new List<ObjectSnapshot>();
            var all = GetAllActiveTransforms();

            for (int i = 0; i < all.Length; i++)
            {
                var tr = all[i];
                if (!ShouldRecord(tr)) continue;
                var go = tr.gameObject;
                int id = go.GetInstanceID();

                Vector3 pos = tr.localPosition;
                Quaternion rot = tr.localRotation;
                Vector3 scl = tr.localScale;

                // 1. Transform 变化检测（与上一帧比较）
                lastSnapshots.TryGetValue(id, out var prev);
                bool transformChanged = prev == null ||
                    prev.position != pos || prev.rotation != rot || prev.localScale != scl;

                // 2. Animator float 参数（仅当参数变化时才写入 animatorParams）
                Dictionary<string, float> animParams = null;
                var animator = go.GetComponent<Animator>();
                if (animator != null)
                {
                    var names = GetAnimatorFloatNames(animator);
                    if (names.Length > 0)
                    {
                        var values = new float[names.Length];
                        for (int j = 0; j < names.Length; j++) values[j] = animator.GetFloat(names[j]);

                        bool animChanged = false;
                        if (lastAnimValues.TryGetValue(id, out var prevVals) && prevVals.Length == values.Length)
                        {
                            for (int j = 0; j < values.Length; j++)
                                if (!Mathf.Approximately(prevVals[j], values[j])) { animChanged = true; break; }
                        }
                        else animChanged = true;

                        lastAnimValues[id] = values;
                        if (animChanged)
                        {
                            animParams = new Dictionary<string, float>(names.Length);
                            for (int j = 0; j < names.Length; j++) animParams[names[j]] = values[j];
                        }
                    }
                }

                var snap = new ObjectSnapshot(id, pos, rot, scl)
                {
                    objectName = go.name,
                    objectPath = HierarchyPath(tr),
                    animatorParams = animParams
                };
                CaptureRecordables(go, snap);
                bool componentChanged = !ComponentStatesEqual(prev?.componentData, snap.componentData);
                if (!transformChanged && animParams == null && !componentChanged) continue;
                // Transform 单独变化时也保留完整 Animator 状态，确保 Seek 不依赖先前的播放顺序。
                if (animParams == null && prev != null) snap.animatorParams = prev.animatorParams;

                // 长时间静止后首次变化，在前一帧补一个保持状态，避免跨越整段静止期插值。
                int lastFrame;
                if (prev != null && frames.Count > 0 && lastSnapshotFrame.TryGetValue(id, out lastFrame)
                    && lastFrame < frames.Count - 1)
                    frames[frames.Count - 1].snapshots.Add(prev);
                lastSnapshotFrame[id] = frames.Count;
                snapshotList.Add(snap);
                lastSnapshots[id] = snap;
                transformCache[id] = tr;

            }

            if (frames.Count > 0 && frames[frames.Count - 1].time == t && snapshotList.Count == 0) return;
            frames.Add(new RecordFrame(t) { snapshots = snapshotList });
            timelinesDirty = true;
        }

        private static bool ComponentStatesEqual(Dictionary<string, object> a, Dictionary<string, object> b)
        {
            if ((a?.Count ?? 0) != (b?.Count ?? 0)) return false;
            if (a == null || a.Count == 0) return true;
            foreach (var pair in a)
            {
                object value;
                if (!b.TryGetValue(pair.Key, out value) || !Equals(pair.Value, value)) return false;
            }
            return true;
        }

        private void EnsureTimelines()
        {
            if (!timelinesDirty) return;
            timelines.Clear();
            foreach (var frame in frames)
                foreach (var snapshot in frame.snapshots)
                {
                    List<TimedSnapshot> track;
                    if (!timelines.TryGetValue(snapshot.instanceID, out track))
                        timelines.Add(snapshot.instanceID, track = new List<TimedSnapshot>());
                    // 兼容旧文件中稀疏 Animator 参数，生成一次完整状态索引。
                    var complete = new ObjectSnapshot(snapshot.instanceID, snapshot.position, snapshot.rotation, snapshot.localScale) {
                        objectName = snapshot.objectName, objectPath = snapshot.objectPath, componentData = snapshot.componentData
                    };
                    var previous = track.Count > 0 ? track[track.Count - 1].snapshot.animatorParams : null;
                    if (previous != null) complete.animatorParams = new Dictionary<string, float>(previous);
                    if (snapshot.animatorParams != null)
                    {
                        if (complete.animatorParams == null) complete.animatorParams = new Dictionary<string, float>();
                        foreach (var pair in snapshot.animatorParams) complete.animatorParams[pair.Key] = pair.Value;
                    }
                    track.Add(new TimedSnapshot { time = frame.time, snapshot = complete });
                }
            timelinesDirty = false;
        }

        private void ApplyAtTime(float time)
        {
            EnsureTimelines();
            foreach (var track in timelines.Values)
            {
                int lo = 0, hi = track.Count - 1, index = -1;
                while (lo <= hi)
                {
                    int mid = (lo + hi) >> 1;
                    if (track[mid].time <= time) { index = mid; lo = mid + 1; }
                    else hi = mid - 1;
                }
                if (index < 0) continue;
                var a = track[index];
                ApplySnapshot(a.snapshot);
                if (index + 1 >= track.Count) continue;
                var b = track[index + 1];
                var tr = ResolveTransform(a.snapshot);
                if (tr == null) continue;
                float span = b.time - a.time;
                float t = span > 0f ? Mathf.Clamp01((time - a.time) / span) : 0f;
                tr.localPosition = Vector3.Lerp(a.snapshot.position, b.snapshot.position, t);
                tr.localRotation = Quaternion.Slerp(a.snapshot.rotation, b.snapshot.rotation, t);
                tr.localScale = Vector3.Lerp(a.snapshot.localScale, b.snapshot.localScale, t);
            }
        }

        private void ApplyFrame(RecordFrame frame)
        {
            for (int i = 0; i < frame.snapshots.Count; i++) ApplySnapshot(frame.snapshots[i]);
        }

        private void ApplySnapshot(ObjectSnapshot snap)
        {
            var tr = ResolveTransform(snap);
            if (tr == null) return;
            tr.localPosition = snap.position;
            tr.localRotation = snap.rotation;
            tr.localScale = snap.localScale;
            ApplyAnimatorParams(tr, snap);
            ApplyRecordables(tr.gameObject, snap);
        }

        private void ApplyAnimatorParams(Transform tr, ObjectSnapshot snap)
        {
            if (snap.animatorParams == null || snap.animatorParams.Count == 0) return;
            var animator = tr.GetComponent<Animator>();
            if (animator == null) return;
            foreach (var kv in snap.animatorParams)
                animator.SetFloat(kv.Key, kv.Value);
        }

        private Transform ResolveTransform(ObjectSnapshot snap)
        {
            if (transformCache.TryGetValue(snap.instanceID, out var tr) && tr != null) return tr;
            if (!string.IsNullOrEmpty(snap.objectPath) && transformByPath.TryGetValue(snap.objectPath, out tr) && tr != null) return tr;
            if (!string.IsNullOrEmpty(snap.objectName) && transformByName.TryGetValue(snap.objectName, out tr) && tr != null) return tr;
            return null;
        }

        private string[] GetAnimatorFloatNames(Animator animator)
        {
            int id = animator.gameObject.GetInstanceID();
            if (!animatorParamNames.TryGetValue(id, out var names))
            {
                var ps = animator.parameters;
                var list = new List<string>();
                for (int i = 0; i < ps.Length; i++)
                    if (ps[i].type == AnimatorControllerParameterType.Float)
                        list.Add(ps[i].name);
                names = list.ToArray();
                animatorParamNames[id] = names;
            }
            return names;
        }

        private void SetRecordedRigidbodiesKinematic(bool kinematic)
        {
            if (kinematic)
            {
                rbKinematicBackup.Clear();
                var seen = new HashSet<Transform>();
                for (int i = 0; i < frames.Count; i++)
                {
                    var list = frames[i].snapshots;
                    for (int j = 0; j < list.Count; j++)
                    {
                        var tr = ResolveTransform(list[j]);
                        if (tr == null || seen.Contains(tr)) continue;
                        seen.Add(tr);
                        var rb = tr.GetComponent<Rigidbody>();
                        if (rb != null)
                        {
                            rbKinematicBackup[rb] = rb.isKinematic;
                            rb.isKinematic = true;
                        }
                    }
                }
            }
            else
            {
                foreach (var kv in rbKinematicBackup)
                    if (kv.Key != null) kv.Key.isKinematic = kv.Value;
                rbKinematicBackup.Clear();
            }
        }

        private void CaptureRecordables(GameObject go, ObjectSnapshot snap)
        {
            for (int i = 0; i < recordables.Count; i++)
            {
                var rec = recordables[i];
                if (rec != null && rec.CanHandle(go))
                    snap.SetComponentData(rec.Key, rec.Capture(go));
            }
        }

        private void ApplyRecordables(GameObject go, ObjectSnapshot snap)
        {
            if (snap.componentData == null) return;
            for (int i = 0; i < recordables.Count; i++)
            {
                var rec = recordables[i];
                if (rec != null && snap.TryGetComponentData(rec.Key, out var state))
                    rec.Apply(go, state);
            }
        }

        private static string HierarchyPath(Transform tr)
        {
            string path = string.Empty;
            for (var node = tr; node != null; node = node.parent)
                path = "/" + node.GetSiblingIndex() + ":" + node.name + path;
            return tr.gameObject.scene.name + path;
        }

        private void RebuildNameMap()
        {
            transformByName.Clear();
            transformByPath.Clear();
            var ambiguous = new HashSet<string>();
            var all = GetAllActiveTransforms();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == transform) continue;
                transformByPath[HierarchyPath(all[i])] = all[i];
                string name = all[i].name;
                if (ambiguous.Contains(name)) continue;
                if (transformByName.ContainsKey(name)) { transformByName.Remove(name); ambiguous.Add(name); }
                else transformByName[name] = all[i];
            }
        }

        private bool ShouldRecord(Transform tr)
        {
            if (tr == transform) return false;                                     // 排除管理器自身
            if (excludeUI && tr.GetComponentInParent<Canvas>() != null) return false; // 排除 UI
            return true;
        }

        private void StopPlaybackInternal()
        {
            if (state != State.Playing && state != State.Paused) { state = State.Idle; return; }
            Physics.autoSimulation = physicsWasAutoSimulation;
            Physics.autoSyncTransforms = physicsWasAutoSyncTransforms;
            SetRecordedRigidbodiesKinematic(false);
            state = State.Idle;
        }

        /// <summary>获取场景中所有激活的 Transform（按 Unity 版本适配，避免弃用告警）</summary>
        private static Transform[] GetAllActiveTransforms()
        {
#if UNITY_2022_3_OR_NEWER
            return UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None);
#else
            return UnityEngine.Object.FindObjectsOfType<Transform>();
#endif
        }
    }
}
