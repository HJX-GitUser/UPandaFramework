using System;
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
    public static class FileManager
    {
        public const string FolderName = "ReplayRecords";
        private const string Header = "UPANDA_REPLAY:2\n";
        private sealed class ComponentCodec
        {
            public Func<object, string> encode;
            public Func<string, object> decode;
        }
        [Serializable] private class StateBox<T> { public T value; }
        private static readonly Dictionary<string, ComponentCodec> codecs = new Dictionary<string, ComponentCodec>();
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
            string fullPath = null;
            string temporary = null;
            try
            {
                var file = ToSerializable(frames);
                file.version = 2;
                Validate(file);
                fullPath = Path.Combine(FolderPath, $"record_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.dat");
                temporary = fullPath + ".tmp";
                File.WriteAllText(temporary, Header + JsonUtility.ToJson(file), new UTF8Encoding(false));
                File.Move(temporary, fullPath);
                return fullPath;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ReplaySystem] 保存失败：" + e.Message);
                return null;
            }
            finally
            {
                if (temporary != null && File.Exists(temporary))
                    try { File.Delete(temporary); } catch (IOException) { }
            }
        }

        /// <summary>读取新版 JSON 或白名单旧版文件；损坏/版本不支持时返回 null。</summary>
        public static List<RecordFrame> Load(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                if (new FileInfo(path).Length > 128L * 1024 * 1024)
                    throw new InvalidDataException("回放文件超过 128 MiB 限制");
                SerializableRecordFile file;
                using (var stream = File.OpenRead(path))
                {
                    var prefix = new byte[Encoding.UTF8.GetByteCount(Header)];
                    int read = stream.Read(prefix, 0, prefix.Length);
                    stream.Position = 0;
                    if (read == prefix.Length && Encoding.UTF8.GetString(prefix) == Header)
                    {
                        using (var reader = new StreamReader(stream, Encoding.UTF8))
                            file = JsonUtility.FromJson<SerializableRecordFile>(reader.ReadToEnd().Substring(Header.Length));
                        if (file == null || file.version != 2) throw new InvalidDataException("不支持的回放版本");
                    }
                    else
                    {
                        bool legacy;
                        file = UPandaGF.VersionedDataSerializer.Deserialize<SerializableRecordFile>(
                            UPandaGF.VersionedDataSerializer.ReadFile(path, 128 * 1024 * 1024), "replay", out legacy);
                    }
                }
                Validate(file);
                return FromSerializable(file);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ReplaySystem] 读取失败：" + e.Message);
                return null;
            }
        }

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
                foreach (var snapshot in frame.snapshots)
                {
                    if (snapshot == null || (snapshot.animParamNames?.Length ?? 0) != (snapshot.animParamValues?.Length ?? 0)
                        || (snapshot.componentKeys?.Length ?? 0) != (snapshot.componentJson?.Length ?? 0))
                        throw new InvalidDataException("回放快照数组不匹配");
                    float[] values = { snapshot.px, snapshot.py, snapshot.pz, snapshot.rx, snapshot.ry,
                        snapshot.rz, snapshot.rw, snapshot.sx, snapshot.sy, snapshot.sz };
                    foreach (float value in values)
                        if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidDataException("回放坐标无效");
                }
            }
        }

        // ================= DTO 转换（可序列化层） =================

        private static SerializableRecordFile ToSerializable(List<RecordFrame> frames)
        {
            var file = new SerializableRecordFile { frames = new SerializableFrame[frames.Count] };
            for (int i = 0; i < frames.Count; i++)
            {
                var src = frames[i];
                var dst = new SerializableFrame { time = src.time, snapshots = new SerializableSnapshot[src.snapshots.Count] };
                for (int j = 0; j < src.snapshots.Count; j++)
                    dst.snapshots[j] = ToSerializable(src.snapshots[j]);
                file.frames[i] = dst;
            }
            return file;
        }

        private static SerializableSnapshot ToSerializable(ObjectSnapshot s)
        {
            var d = new SerializableSnapshot
            {
                instanceID = s.instanceID,
                objectName = s.objectName,
                objectPath = s.objectPath,
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
                int index = 0;
                foreach (var pair in s.componentData)
                {
                    ComponentCodec codec;
                    if (!codecs.TryGetValue(pair.Key, out codec))
                        throw new InvalidDataException("请先注册组件持久化类型：" + pair.Key);
                    d.componentKeys[index] = pair.Key;
                    d.componentJson[index++] = codec.encode(pair.Value);
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
                objectName = d.objectName, objectPath = d.objectPath
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
