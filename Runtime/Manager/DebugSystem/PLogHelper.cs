using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;

namespace UPandaGF
{
    public class LogData { public string log, trace; public LogType type; }
    /// <summary>桌面/移动端后台落盘；WebGL 或禁用后台选项时在 LateUpdate 有界排空。</summary>
    public class PLogHelper : MonoBehaviour
    {
        private static readonly Regex RichTextTag = new Regex(@"</?(color|b|i|size|material)(=[^>]*)?>");
        [Min(1)] public int maximumQueuedLogs = 4096;
        [Min(1)] public int logsPerFrame = 128;
        public bool useBackgroundThread = true;
        private const int MaximumText = 32768;
        private StreamWriter writer;
        private readonly ConcurrentQueue<LogData> queue = new ConcurrentQueue<LogData>();
        private readonly object lifecycle = new object(), writerLock = new object();
#if !UNITY_WEBGL || UNITY_EDITOR
        private readonly ManualResetEvent wake = new ManualResetEvent(false);
        private Thread thread;
#endif
        private volatile bool running;
        private bool cooperative, disposed;
        public bool IsRunning => running;
        public bool UsesBackgroundThread => running && !cooperative;
        public int PendingCount => queue.Count;
        public long DroppedCount { get; private set; }
        public string LogFilePath { get; private set; }
        public void InitLogFileModule(string savePath, string logfineName)
        {
            if (disposed) return;
            string path;
            try { path = Path.GetFullPath(Path.Combine(savePath, logfineName)); }
            catch (Exception e) { Debug.LogError("[PLogger] 日志路径无效：" + e.Message); return; }
            if (running && LogFilePath == path) return;
            StopSafely();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)); writer = new StreamWriter(path);
                writer.WriteLine($"=== 日志开始 {DateTime.Now:yyyy-MM-dd HH:mm:ss} 平台 {Application.platform} 版本 {Application.version} ==="); writer.Flush(); LogFilePath = path;
                cooperative = !useBackgroundThread;
#if UNITY_WEBGL && !UNITY_EDITOR
                cooperative = true;
#endif
                lock (lifecycle) { running = true; Application.logMessageReceivedThreaded += Receive; }
#if !UNITY_WEBGL || UNITY_EDITOR
                if (!cooperative) { thread = new Thread(FileLogThread) { IsBackground = true, Name = "PLogFileWriter" }; thread.Start(); }
#endif
            }
            catch (Exception e) { StopSafely(); Debug.LogError("[PLogger] 日志文件初始化失败：" + e.Message); }
        }
        private void LateUpdate()
        {
            if (!running || !cooperative) return;
            try { Drain(Math.Max(1, logsPerFrame)); }
            catch (Exception e) { StopSafely(); Debug.LogWarning("[PLogger] 日志写入停止：" + e.Message); }
        }
        public void FileLogThread()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            try { while (running) { wake.WaitOne(200); wake.Reset(); if (running) Drain(int.MaxValue); } }
            catch (Exception e) { Debug.LogWarning("[PLogger] 日志写线程停止：" + e.Message); }
            finally { running = false; }
#endif
        }
        private void Drain(int maximum)
        {
            lock (writerLock)
            {
                if (writer == null) return;
                LogData data; int count = 0;
                while (count++ < maximum && queue.TryDequeue(out data))
                {
                    string label = data.type == LogType.Warning ? "Warning >>> " : data.type == LogType.Log ? "Log >>> " : "Error >>> ";
                    writer.Write(label); writer.WriteLine(Strip(data.log)); writer.WriteLine(data.trace); writer.WriteLine();
                }
                writer.Flush();
            }
        }
        private void Receive(string condition, string trace, LogType type)
        {
            lock (lifecycle)
            {
                if (!running) return;
                if (queue.Count >= Math.Max(1, maximumQueuedLogs)) { DroppedCount++; return; }
                queue.Enqueue(new LogData { log = DateTime.Now.ToString("yyyy:MM:dd HH:mm:ss") + " " + Limit(condition), trace = Limit(trace), type = type });
#if !UNITY_WEBGL || UNITY_EDITOR
                if (!cooperative) wake.Set();
#endif
            }
        }
        public void StopSafely()
        {
            lock (lifecycle)
            {
                Application.logMessageReceivedThreaded -= Receive; running = false;
#if !UNITY_WEBGL || UNITY_EDITOR
                if (!disposed) wake.Set();
#endif
            }
#if !UNITY_WEBGL || UNITY_EDITOR
            if (thread != null && thread.IsAlive && thread != Thread.CurrentThread) thread.Join(500);
            thread = null;
#endif
            try { Drain(int.MaxValue); }
            catch (Exception) { }
            lock (writerLock) { try { writer?.Dispose(); } catch (Exception) { } writer = null; }
#if UNITY_WEBGL && !UNITY_EDITOR
            if (LogFilePath != null) _ = FlushBrowser();
#endif
        }
#if UNITY_WEBGL && !UNITY_EDITOR
        private static async System.Threading.Tasks.Task FlushBrowser()
        { try { await PlatformStorage.FlushAsync(); } catch (Exception e) { Debug.LogWarning("[PLogger] 浏览器日志尚未持久化：" + e.Message); } }
#endif
        private static string Limit(string text) => text == null || text.Length <= MaximumText ? text : text.Substring(0, MaximumText) + "…";
        private static string Strip(string text) => string.IsNullOrEmpty(text) || text.IndexOf('<') < 0 ? text : RichTextTag.Replace(text, string.Empty);
        private void OnDisable() { StopSafely(); }
        private void OnApplicationQuit() { StopSafely(); }
        private void OnDestroy()
        {
            StopSafely(); disposed = true;
#if !UNITY_WEBGL || UNITY_EDITOR
            wake.Dispose();
#endif
        }
    }
}
