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
            lastAnimValues.Clear();
            animatorParamNames.Clear();
            transformCache.Clear();
            RebuildNameMap();
            state = State.Recording;
            Debug.Log("[ReplaySystem] 开始录制");
        }

        public void StopRecording()
        {
            if (state != State.Recording) return;
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
            Debug.Log("[ReplaySystem] 已保存到：" + path);
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

                if (!transformChanged && animParams == null) continue; // 关键优化：无变化不记录

                var snap = new ObjectSnapshot(id, pos, rot, scl)
                {
                    objectName = go.name,
                    animatorParams = animParams
                };
                CaptureRecordables(go, snap);

                snapshotList.Add(snap);
                lastSnapshots[id] = snap;
                transformCache[id] = tr;
                transformByName[tr.name] = tr;
            }

            if (snapshotList.Count > 0)
                frames.Add(new RecordFrame(t) { snapshots = snapshotList });
        }

        private void ApplyAtTime(float time)
        {
            int idx = FindFrameIndex(time);
            if (idx < 0) return;
            int next = idx + 1;
            if (next >= frames.Count)
            {
                ApplyFrame(frames[idx]);
                return;
            }
            // 在相邻两帧之间插值（支持拖动进度条与变速播放的平滑还原）
            float span = frames[next].time - frames[idx].time;
            float t = span > 1e-5f ? Mathf.Clamp01((time - frames[idx].time) / span) : 0f;
            ApplyFrameBlend(frames[idx], frames[next], t);
        }

        private void ApplyFrame(RecordFrame frame)
        {
            for (int i = 0; i < frame.snapshots.Count; i++)
                ApplySnapshot(frame.snapshots[i]);
        }

        private void ApplyFrameBlend(RecordFrame a, RecordFrame b, float t)
        {
            var mapA = BuildIndex(a);
            var mapB = BuildIndex(b);

            foreach (var kv in mapA)
            {
                var tr = ResolveTransform(kv.Value);
                if (tr == null) continue;
                var sa = kv.Value;
                if (t > 0f && mapB.TryGetValue(kv.Key, out var sb))
                {
                    tr.localPosition = Vector3.Lerp(sa.position, sb.position, t);
                    tr.localRotation = Quaternion.Slerp(sa.rotation, sb.rotation, t);
                    tr.localScale = Vector3.Lerp(sa.localScale, sb.localScale, t);
                    var pick = t < 0.5f ? sa : sb;
                    ApplyAnimatorParams(tr, pick);
                    ApplyRecordables(tr.gameObject, pick);
                }
                else
                {
                    ApplySnapshot(sa);
                }
            }

            // 处理只在 b 中出现的（新增变化）物体
            foreach (var kv in mapB)
            {
                if (mapA.ContainsKey(kv.Key)) continue;
                ApplySnapshot(kv.Value);
            }
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

        private static Dictionary<int, ObjectSnapshot> BuildIndex(RecordFrame frame)
        {
            var map = new Dictionary<int, ObjectSnapshot>(frame.snapshots.Count);
            for (int i = 0; i < frame.snapshots.Count; i++)
                map[frame.snapshots[i].instanceID] = frame.snapshots[i];
            return map;
        }

        /// <summary>二分查找：返回最后一个时间戳 <= time 的帧索引</summary>
        private int FindFrameIndex(float time)
        {
            int lo = 0, hi = frames.Count - 1, ans = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (frames[mid].time <= time) { ans = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            return ans;
        }

        private Transform ResolveTransform(ObjectSnapshot snap)
        {
            if (transformCache.TryGetValue(snap.instanceID, out var tr) && tr != null) return tr;
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

        private void RebuildNameMap()
        {
            transformByName.Clear();
            var all = GetAllActiveTransforms();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == transform) continue;
                transformByName[all[i].name] = all[i];
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
