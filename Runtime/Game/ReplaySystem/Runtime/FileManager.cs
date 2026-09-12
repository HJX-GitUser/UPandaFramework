using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ReplaySystem
{
    /// <summary>
    /// 录制文件的保存/加载逻辑。
    /// 保存格式：二进制序列化（BinaryFormatter），文件存放于 Application.persistentDataPath/ReplayRecords/。
    /// </summary>
    public static class FileManager
    {
        public const string FolderName = "ReplayRecords";

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
            string fileName = $"record_{DateTime.Now:yyyyMMdd_HHmmss}.dat";
            string fullPath = Path.Combine(FolderPath, fileName);

            // 关键：Vector3/Quaternion 不是 .NET [Serializable]，不能直接交给 BinaryFormatter。
            // 先转换为纯可序列化的 DTO（用 float 存坐标），再做二进制序列化。
            var file = ToSerializable(frames);

#pragma warning disable SYSLIB0011 // BinaryFormatter 在新版 .NET 中标记为过时，但 Unity 2021.3 仍可用
            using (var fs = new FileStream(fullPath, FileMode.Create))
            {
                var formatter = new BinaryFormatter();
                formatter.Serialize(fs, file);
            }
#pragma warning restore SYSLIB0011
            return fullPath;
        }

        /// <summary>从指定路径加载录制数据（失败返回 null）</summary>
        public static List<RecordFrame> Load(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
#pragma warning disable SYSLIB0011
            using (var fs = new FileStream(path, FileMode.Open))
            {
                var formatter = new BinaryFormatter();
                var file = formatter.Deserialize(fs) as SerializableRecordFile;
                return file != null ? FromSerializable(file) : null;
            }
#pragma warning restore SYSLIB0011
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
            // 注意：componentData（扩展组件数据，如 Rigidbody.velocity）值为 object 类型，
            // 无法安全序列化，故保存时忽略。扩展数据仅在同会话内回放生效。
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
                objectName = d.objectName
            };
            if (d.animParamNames != null && d.animParamNames.Length > 0)
            {
                s.animatorParams = new Dictionary<string, float>(d.animParamNames.Length);
                for (int k = 0; k < d.animParamNames.Length; k++)
                    s.animatorParams[d.animParamNames[k]] = d.animParamValues[k];
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

    /// <summary>可序列化的录制文件根（纯 .NET [Serializable] 类型，供 BinaryFormatter 使用）</summary>
    [Serializable]
    public class SerializableRecordFile
    {
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

        // position
        public float px, py, pz;
        // rotation（四元数）
        public float rx, ry, rz, rw;
        // localScale
        public float sx, sy, sz;

        // Animator float 参数（平行数组）
        public string[] animParamNames;
        public float[] animParamValues;
    }
}
