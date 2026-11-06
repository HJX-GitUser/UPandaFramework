using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Runtime.Serialization;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ReplaySystem
{
    /// <summary>
    /// 录制文件的保存/加载逻辑。
    /// 保存格式：版本化 JSON（旧版二进制文件只读兼容），文件存放于 Application.persistentDataPath/ReplayRecords/。
    /// </summary>
    public static partial class FileManager
    {
        public const string FolderName = "ReplayRecords";
        private const string Header = "UPANDA_REPLAY:2\n";
        private sealed class ComponentCodec
        {
            public Func<object, string> encode;
            public Func<string, object> decode;
        }
        [Serializable] private class StateBox<T> { public T value; }
        private static readonly ConcurrentDictionary<string, ComponentCodec> codecs = new ConcurrentDictionary<string, ComponentCodec>();
        static FileManager() { RegisterComponentState<Vector3>("RigidbodyVelocity"); }

        /// <summary>自定义组件持久化需注册明确的数据类型；不从文件解析 CLR 类型名。</summary>
        public static void RegisterComponentState<T>(string key)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("Component key required");
            codecs[key] = new ComponentCodec {
                encode = value => JsonUtility.ToJson(new StateBox<T> { value = (T)value }),
                decode = json => JsonUtility.FromJson<StateBox<T>>(json).value
            };
        }

        /// <summary>录制文件所在目录（不存在则自动创建）</summary>
        public static string FolderPath
        {
            get
            {
                string path = Path.Combine(Application.persistentDataPath, FolderName);
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                return path;
            }
        }

        /// <summary>保存录制数据，文件名带时间戳，返回完整路径</summary>
        public static string Save(List<RecordFrame> frames)
        {
            ReplayWriter writer = null;
            try
            {
                var view = new SaveView(frames); writer = new ReplayWriter(FolderPath, CancellationToken.None);
                for (int i = 0; i < view.frames.Length; i++)
                {
                    writer.StartFrame(view.times[i]);
                    for (int j = 0; j < view.counts[i]; j++) writer.Snapshot(ToSerializable(view.frames[i].snapshots[j]));
                    writer.EndFrame();
                }
                return writer.Complete();
            }
            catch (Exception e) { Debug.LogWarning("[ReplaySystem] 保存失败：" + e.Message); return null; }
            finally { writer?.Dispose(); }
        }
        /// <summary>保存调用时的帧前缀；DTO 按 128 个快照分块，Unity 状态只在调用线程编码。</summary>
        public static async Task<string> SaveAsync(List<RecordFrame> frames, CancellationToken token = default)
        {
            ReplayWriter writer = null;
            try
            {
                token.ThrowIfCancellationRequested(); var view = new SaveView(frames); string folder = Path.Combine(Application.persistentDataPath, FolderName);
                writer = await Background(() => new ReplayWriter(folder, token));
                int frameIndex = 0, snapshotIndex = -1;
                while (frameIndex < view.frames.Length)
                {
                    token.ThrowIfCancellationRequested();
                    var batch = new List<WriteOperation>(256); int snapshots = 0; long characters = 0; var slice = System.Diagnostics.Stopwatch.StartNew();
                    do
                    {
                        if (snapshotIndex < 0) { batch.Add(new WriteOperation { kind = 0, time = view.times[frameIndex] }); snapshotIndex = 0; }
                        if (snapshotIndex < view.counts[frameIndex])
                        {
                            var snapshot = ToSerializable(view.frames[frameIndex].snapshots[snapshotIndex++]);
                            batch.Add(new WriteOperation { kind = 1, snapshot = snapshot }); snapshots++;
                            characters += 256 + (snapshot.objectName?.Length ?? 0) + (snapshot.objectPath?.Length ?? 0);
                            if (snapshot.componentJson != null) foreach (string json in snapshot.componentJson) characters += json.Length;
                        }
                        if (snapshotIndex == view.counts[frameIndex]) { batch.Add(new WriteOperation { kind = 2 }); snapshotIndex = -1; frameIndex++; }
                    } while (frameIndex < view.frames.Length && snapshots < 128 && batch.Count < 512 && characters < 1024 * 1024 && slice.ElapsedMilliseconds < 4);
                    await Background(() => { foreach (var operation in batch) { if (operation.kind == 0) writer.StartFrame(operation.time); else if (operation.kind == 1) writer.Snapshot(operation.snapshot); else writer.EndFrame(); } return true; });
                    await Task.Yield();
                }
                string published = await Background(writer.Complete);
                try { await UPandaGF.PlatformStorage.FlushAsync(); }
                catch { if (File.Exists(published)) File.Delete(published); throw; }
                return published;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) { Debug.LogWarning("[ReplaySystem] 保存失败：" + e.Message); return null; }
            finally { if (writer != null) await Background(() => { writer.Dispose(); return true; }); }
        }
        public static List<RecordFrame> Load(string path)
        {
            try { return ReadReplay(path, CancellationToken.None); }
            catch (Exception e) { Debug.LogWarning("[ReplaySystem] 读取失败：" + e.Message); return null; }
        }
        public static async Task<List<RecordFrame>> LoadAsync(string path, CancellationToken token = default)
        {
            try { return await Background(() => ReadReplay(path, token)); }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) { Debug.LogWarning("[ReplaySystem] 读取失败：" + e.Message); return null; }
        }
        private static Task<T> Background<T>(Func<T> action)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return Inline(action);
#else
            return Task.Run(action);
#endif
        }
#if UNITY_WEBGL && !UNITY_EDITOR
        private static async Task<T> Inline<T>(Func<T> action) { await Task.Yield(); return action(); }
#endif

        private static void Validate(SerializableRecordFile file)
        {
            if (file == null || file.frames == null || file.frames.Length > 1000000)
                throw new InvalidDataException("无效回放帧数据");
            float previous = float.NegativeInfinity;
            foreach (var frame in file.frames)
            {
                if (frame == null || frame.snapshots == null || float.IsNaN(frame.time)
                    || float.IsInfinity(frame.time) || frame.time < previous)
                    throw new InvalidDataException("回放时间戳无效或未排序");
                previous = frame.time;
                foreach (var snapshot in frame.snapshots) ValidateSnapshot(snapshot);
            }
        }
        private static void ValidateSnapshot(SerializableSnapshot snapshot)
        {
            if (snapshot == null || (snapshot.animParamNames?.Length ?? 0) != (snapshot.animParamValues?.Length ?? 0)
                || (snapshot.componentKeys?.Length ?? 0) != (snapshot.componentJson?.Length ?? 0)) throw new InvalidDataException("回放快照数组不匹配");
            if (!Finite(snapshot.px) || !Finite(snapshot.py) || !Finite(snapshot.pz) || !Finite(snapshot.rx) || !Finite(snapshot.ry)
                || !Finite(snapshot.rz) || !Finite(snapshot.rw) || !Finite(snapshot.sx) || !Finite(snapshot.sy) || !Finite(snapshot.sz)) throw new InvalidDataException("回放坐标无效");
            if (snapshot.animParamNames != null) for (int i = 0; i < snapshot.animParamNames.Length; i++)
                if (string.IsNullOrEmpty(snapshot.animParamNames[i]) || !Finite(snapshot.animParamValues[i])) throw new InvalidDataException("回放动画参数无效");
            if (snapshot.componentKeys != null) for (int i = 0; i < snapshot.componentKeys.Length; i++)
                if (string.IsNullOrEmpty(snapshot.componentKeys[i]) || snapshot.componentJson[i] == null) throw new InvalidDataException("回放组件数据无效");
        }

        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }

        // ================= DTO 转换（可序列化层） =================

        private static SerializableSnapshot ToSerializable(ObjectSnapshot s)
        {
            var d = new SerializableSnapshot
            {
                instanceID = s.instanceID,
                objectName = s.objectName,
                objectPath = s.objectPath, animatorStateComplete = s.animatorStateComplete,
                px = s.position.x, py = s.position.y, pz = s.position.z,
                rx = s.rotation.x, ry = s.rotation.y, rz = s.rotation.z, rw = s.rotation.w,
                sx = s.localScale.x, sy = s.localScale.y, sz = s.localScale.z
            };
            // Animator 参数转成“名称数组 + 数值数组”两个平行数组
            if (s.animatorParams != null && s.animatorParams.Count > 0)
            {
                d.animParamNames = new string[s.animatorParams.Count];
                d.animParamValues = new float[s.animatorParams.Count];
                int k = 0;
                foreach (var kv in s.animatorParams)
                {
                    d.animParamNames[k] = kv.Key;
                    d.animParamValues[k] = kv.Value;
                    k++;
                }
            }
            if (s.componentData != null && s.componentData.Count > 0)
            {
                d.componentKeys = new string[s.componentData.Count];
                d.componentJson = new string[s.componentData.Count];
                int index = 0; long characters = 0;
                foreach (var pair in s.componentData)
                {
                    ComponentCodec codec;
                    if (!codecs.TryGetValue(pair.Key, out codec))
                        throw new InvalidDataException("请先注册组件持久化类型：" + pair.Key);
                    d.componentKeys[index] = pair.Key;
                    string encoded = codec.encode(pair.Value); characters += encoded.Length;
                    if (characters > 1024 * 1024) throw new InvalidDataException("单快照组件 JSON 超过 1 Mi 字符限制");
                    d.componentJson[index++] = encoded;
                }
            }
            return d;
        }

        private static List<RecordFrame> FromSerializable(SerializableRecordFile file)
        {
            var frames = new List<RecordFrame>(file.frames.Length);
            for (int i = 0; i < file.frames.Length; i++)
            {
                var src = file.frames[i];
                var frame = new RecordFrame(src.time);
                for (int j = 0; j < src.snapshots.Length; j++)
                    frame.snapshots.Add(FromSerializable(src.snapshots[j]));
                frames.Add(frame);
            }
            return frames;
        }

        private static ObjectSnapshot FromSerializable(SerializableSnapshot d)
        {
            var s = new ObjectSnapshot(
                d.instanceID,
                new Vector3(d.px, d.py, d.pz),
                new Quaternion(d.rx, d.ry, d.rz, d.rw),
                new Vector3(d.sx, d.sy, d.sz))
            {
                objectName = d.objectName, objectPath = d.objectPath, animatorStateComplete = d.animatorStateComplete
            };
            if (d.animParamNames != null && d.animParamNames.Length > 0)
            {
                s.animatorParams = new Dictionary<string, float>(d.animParamNames.Length);
                for (int k = 0; k < d.animParamNames.Length; k++)
                    s.animatorParams[d.animParamNames[k]] = d.animParamValues[k];
            }
            if (d.componentKeys != null)
                for (int i = 0; i < d.componentKeys.Length; i++)
                {
                    ComponentCodec codec;
                    if (!codecs.TryGetValue(d.componentKeys[i], out codec))
                        throw new InvalidDataException("未注册组件持久化类型：" + d.componentKeys[i]);
                    s.SetComponentData(d.componentKeys[i], codec.decode(d.componentJson[i]));
                }
            return s;
        }

        /// <summary>列出目录下所有 .dat 文件</summary>
        public static string[] ListFiles()
        {
            string path = FolderPath;
            if (!Directory.Exists(path)) return new string[0];
            return Directory.GetFiles(path, "*.dat");
        }

        /// <summary>获取最近一次录制的文件路径（无文件返回 null）</summary>
        public static string LoadLatest()
        {
            var files = ListFiles();
            if (files.Length == 0) return null;
            Array.Sort(files);
            return files[files.Length - 1];
        }

        /// <summary>
        /// 打开文件选择对话框（仅编辑器下生效）。运行时返回 null，
        /// 此时可改用 NativeFilePicker 等插件，或直接调用 Load(path) / LoadLatest()。
        /// </summary>
        public static string OpenFileBrowser()
        {
#if UNITY_EDITOR
            return EditorUtility.OpenFilePanel("选择回放文件", FolderPath, "dat");
#else
            return null;
#endif
        }
    }

    /// <summary>可序列化的录制文件根（纯 .NET [Serializable] 类型，用于历史数据兼容与当前 JSON 持久化）</summary>
    [Serializable]
    public class SerializableRecordFile
    {
        [System.Runtime.Serialization.OptionalField] public int version;
        public SerializableFrame[] frames;
    }

    /// <summary>可序列化的一帧数据</summary>
    [Serializable]
    public class SerializableFrame
    {
        public float time;
        public SerializableSnapshot[] snapshots;
    }

    /// <summary>
    /// 可序列化的物体快照。Vector3/Quaternion 拆成 float 字段存储，
    /// Animator 参数拆成“名称数组 + 数值数组”两个平行数组。
    /// </summary>
    [Serializable]
    public class SerializableSnapshot
    {
        public int instanceID;
        public string objectName;
        [System.Runtime.Serialization.OptionalField] public string objectPath;
        [System.Runtime.Serialization.OptionalField] public bool animatorStateComplete;

        // position
        public float px, py, pz;
        // rotation（四元数）
        public float rx, ry, rz, rw;
        // localScale
        public float sx, sy, sz;

        // Animator float 参数（平行数组）
        [System.Runtime.Serialization.OptionalField] public string[] componentKeys;
        [System.Runtime.Serialization.OptionalField] public string[] componentJson;
        public string[] animParamNames;
        public float[] animParamValues;
    }
}
