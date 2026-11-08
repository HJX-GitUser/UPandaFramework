using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace UPandaGF.Gameplay.Replay
{
    public static partial class FileManager
    {
        private const long MaximumFileBytes = 128L * 1024 * 1024;
        private const int MaximumFrameCharacters = 16 * 1024 * 1024;
        private const int MaximumSnapshots = 1000000;
        private struct WriteOperation { public byte kind; public float time; public SerializableSnapshot snapshot; }
        private sealed class SaveView
        {
            public readonly RecordFrame[] frames;
            public readonly float[] times;
            public readonly int[] counts;
            public SaveView(List<RecordFrame> source)
            {
                if (source == null || source.Count == 0 || source.Count > 1000000) throw new InvalidDataException("无效回放帧数");
                frames = source.ToArray(); times = new float[frames.Length]; counts = new int[frames.Length];
                float prior = float.NegativeInfinity; long total = 0;
                for (int i = 0; i < frames.Length; i++)
                {
                    var frame = frames[i]; if (frame == null || frame.snapshots == null || !Finite(frame.time) || frame.time < prior) throw new InvalidDataException("无效回放帧");
                    times[i] = prior = frame.time; counts[i] = frame.snapshots.Count; total += counts[i];
                    if (total > MaximumSnapshots) throw new InvalidDataException("回放快照过多");
                }
            }
        }
        /// <summary>单文件版本 2 JSON，逐快照写入；临时文件完整关闭后才发布。</summary>
        private sealed class ReplayWriter : IDisposable
        {
            private readonly string target, temporary;
            private readonly CancellationToken token;
            private readonly FileStream stream;
            private readonly StreamWriter writer;
            private long bytes, frameBytes;
            private bool firstFrame = true, firstSnapshot, frameOpen, closed;
            public ReplayWriter(string folder, CancellationToken token)
            {
                this.token = token; token.ThrowIfCancellationRequested(); Directory.CreateDirectory(folder);
                target = Path.Combine(folder, $"record_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.dat"); temporary = target + ".tmp";
                stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                writer = new StreamWriter(stream, new UTF8Encoding(false), 16384, true);
                try { Write(Header + "{\"version\":2,\"frames\":["); }
                catch { Dispose(); throw; }
            }
            private void Write(string text)
            {
                token.ThrowIfCancellationRequested(); int count = Encoding.UTF8.GetByteCount(text);
                if (bytes + count > MaximumFileBytes || (frameOpen && frameBytes + count > MaximumFrameCharacters)) throw new InvalidDataException("回放文件或单帧超过容量限制");
                writer.Write(text); bytes += count; if (frameOpen) frameBytes += count;
            }
            public void StartFrame(float time)
            {
                if (!Finite(time)) throw new InvalidDataException("无效时间戳");
                if (!firstFrame) Write(","); firstFrame = false; frameOpen = true; frameBytes = 0; firstSnapshot = true;
                Write("{\"time\":" + time.ToString("R", CultureInfo.InvariantCulture) + ",\"snapshots\":[");
            }
            public void Snapshot(SerializableSnapshot snapshot)
            {
                // 与读取端共用结构与有限值检查；每次仅保留当前快照的 JSON。
                ValidateSnapshot(snapshot);
                if (!firstSnapshot) Write(","); firstSnapshot = false; Write(JsonUtility.ToJson(snapshot));
            }
            public void EndFrame() { Write("]}"); frameOpen = false; }
            public string Complete()
            { Write("]}"); writer.Flush(); stream.Flush(true); Close(); token.ThrowIfCancellationRequested(); File.Move(temporary, target); return target; }
            private void Close()
            { if (closed) return; closed = true; try { writer.Dispose(); } finally { stream.Dispose(); } }
            public void Dispose()
            { try { Close(); } finally { if (File.Exists(temporary)) File.Delete(temporary); } }
        }
        private static List<RecordFrame> ReadReplay(string path, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            using (var stream = File.OpenRead(path))
            {
                if (stream.Length <= 0 || stream.Length > MaximumFileBytes) throw new InvalidDataException("回放文件超过 128 MiB 限制或为空");
                byte[] prefix = Encoding.UTF8.GetBytes(Header); var actual = new byte[prefix.Length]; int read = stream.Read(actual, 0, actual.Length); stream.Position = 0;
                bool json = read == actual.Length; for (int i = 0; i < read && json; i++) json = prefix[i] == actual[i];
                if (!json)
                {
                    bool legacy; var data = UPandaGF.VersionedDataSerializer.Deserialize<SerializableRecordFile>(UPandaGF.VersionedDataSerializer.ReadFile(path, (int)MaximumFileBytes), "replay", out legacy);
                    token.ThrowIfCancellationRequested(); Validate(data); return FromSerializable(data);
                }
                using (var reader = new StreamReader(stream, new UTF8Encoding(false, true), false, 16384))
                {
                    for (int i = 0; i < Header.Length; i++) if (reader.Read() != Header[i]) throw new InvalidDataException("无效文件头");
                    return new ReplayJsonReader(reader, token).Read();
                }
            }
        }
        /// <summary>只拆分根对象/帧数组，帧内容交给 JsonUtility；字符串中的括号及转义保持原样。</summary>
        private sealed class ReplayJsonReader
        {
            private readonly TextReader reader; private readonly CancellationToken token;
            public ReplayJsonReader(TextReader reader, CancellationToken token) { this.reader = reader; this.token = token; }
            private void Space() { while (reader.Peek() >= 0 && char.IsWhiteSpace((char)reader.Peek())) reader.Read(); }
            private void Expect(char value) { Space(); if (reader.Read() != value) throw new InvalidDataException("无效回放 JSON 结构"); }
            private bool Take(char value) { Space(); if (reader.Peek() != value) return false; reader.Read(); return true; }
            [Serializable] private sealed class StringValue { public string value = string.Empty; }
            private string Key()
            {
                Space(); if (reader.Peek() != '"') throw new InvalidDataException("无效 JSON 字段名");
                string raw = Value(1024); return JsonUtility.FromJson<StringValue>("{\"value\":" + raw + "}").value;
            }
            private string Value(int maximum)
            {
                Space(); var text = new StringBuilder(); int depth = 0; bool quoted = false, escaped = false, container = false;
                while (true)
                {
                    if ((text.Length & 4095) == 0) token.ThrowIfCancellationRequested(); int next = reader.Peek(); if (next < 0) throw new EndOfStreamException("回放 JSON 截断"); char ch = (char)next;
                    if (text.Length > 0 && !quoted && depth == 0 && (ch == ',' || ch == '}' || ch == ']' || char.IsWhiteSpace(ch))) break;
                    reader.Read(); text.Append(ch); if (text.Length > maximum) throw new InvalidDataException("单帧 JSON 超过限制");
                    if (quoted) { if (escaped) escaped = false; else if (ch == '\\') escaped = true; else if (ch == '"') { quoted = false; if (!container && depth == 0) break; } continue; }
                    if (ch == '"') quoted = true;
                    else if (ch == '{' || ch == '[') { container = true; if (++depth > 128) throw new InvalidDataException("JSON 嵌套过深"); }
                    else if (ch == '}' || ch == ']') { if (--depth < 0) throw new InvalidDataException("JSON 括号无效"); if (depth == 0) break; }
                }
                return text.ToString();
            }
            public List<RecordFrame> Read()
            {
                Expect('{'); bool hasVersion = false, hasFrames = false; int version = 0; var frames = new List<RecordFrame>(); long snapshots = 0; float prior = float.NegativeInfinity;
                if (!Take('}')) do
                {
                    string key = Key(); Expect(':');
                    if (key == "version") { if (hasVersion || !int.TryParse(Value(32), NumberStyles.Integer, CultureInfo.InvariantCulture, out version)) throw new InvalidDataException("无效回放版本"); hasVersion = true; }
                    else if (key == "frames")
                    {
                        if (hasFrames) throw new InvalidDataException("重复帧数组"); hasFrames = true; Expect('[');
                        if (!Take(']'))
                        {
                            do
                            {
                                token.ThrowIfCancellationRequested(); Space(); if (reader.Peek() != '{') throw new InvalidDataException("回放帧不是对象");
                                var frame = JsonUtility.FromJson<SerializableFrame>(Value(MaximumFrameCharacters));
                                Validate(new SerializableRecordFile { frames = new[] { frame } });
                                if (frame.time < prior || frames.Count >= 1000000 || (snapshots += frame.snapshots.Length) > MaximumSnapshots) throw new InvalidDataException("时间戳未排序或数据量过大");
                                prior = frame.time; var restored = new RecordFrame(frame.time); foreach (var snapshot in frame.snapshots) { token.ThrowIfCancellationRequested(); restored.snapshots.Add(FromSerializable(snapshot)); } frames.Add(restored);
                            } while (Take(','));
                            Expect(']');
                        }
                    }
                    else Value(MaximumFrameCharacters);
                } while (Take(','));
                Expect('}'); Space(); if (reader.Peek() != -1 || !hasVersion || version != 2 || !hasFrames) throw new InvalidDataException("回放 JSON 版本/结尾无效");
                return frames;
            }
        }
    }
}
