using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;

namespace UPandaGF
{
    public class LogData
    {
        public string log;
        public string trace;
        public LogType type;
    }

    /// <summary>
    /// 日志落盘模块：订阅 <c>Application.logMessageReceivedThreaded</c>（拿到的是**所有** Debug.Log，
    /// 不只是 PLogger 发出的），入无锁队列，由**后台线程**写文件，避免主线程做磁盘 IO。
    ///
    /// 由 <see cref="PLogger.InitLog"/> 在 cfg.logSave = true 时创建（挂在 DontDestroyOnLoad 的 LogHelper 节点上）。
    /// </summary>
    public class PLogHelper : MonoBehaviour
    {
        /// <summary>只剥离 PLogger/Unity 支持的富文本标签，避免误伤日志正文里的 &lt; &gt;</summary>
        private static readonly Regex RichTextTag = new Regex(@"</?(color|b|i|size|material)(=[^>]*)?>", RegexOptions.Compiled);

        /// <summary>写线程唤醒超时：即使没人 Set 也能周期性醒来检查退出标志</summary>
        private const int WakeTimeoutMs = 200;
        /// <summary>停止时等待写线程收尾的最长时间</summary>
        private const int StopJoinTimeoutMs = 500;

        private StreamWriter mStreamWriter;
        private readonly ConcurrentQueue<LogData> mConCurrentQueue = new ConcurrentQueue<LogData>();
        private readonly ManualResetEvent mManualRestEvent = new ManualResetEvent(false);
        private readonly object mWriterLock = new object();
        private volatile bool mThreadRuning = false;
        private Thread mFileThread;

        private string mNowTime { get { return DateTime.Now.ToString("yyyy:MM:dd HH:mm:ss"); } }

        /// <summary>写线程是否在运行</summary>
        public bool IsRunning { get { return mThreadRuning; } }

        /// <summary>当前日志文件完整路径（失败时为 null）</summary>
        public string LogFilePath { get; private set; }

        /// <summary>队列中待写入的条数（诊断用）</summary>
        public int PendingCount { get { return mConCurrentQueue.Count; } }

        public void InitLogFileModule(string savePath, string logfineName)
        {
            // 修复：原实现没有异常保护，目录不可写 / 文件被占用会让异常冒泡回 PLogger.InitLog 中断初始化
            try
            {
                if (!Directory.Exists(savePath))
                {
                    Directory.CreateDirectory(savePath);
                }
                LogFilePath = Path.Combine(savePath, logfineName);
                mStreamWriter = new StreamWriter(LogFilePath);
                mStreamWriter.AutoFlush = false;
                mStreamWriter.WriteLine($"=== 日志开始 {DateTime.Now:yyyy-MM-dd HH:mm:ss}  平台 {Application.platform}  版本 {Application.version} ===");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"[PLogger] 日志文件创建失败，已跳过文件落盘：{SavePathOf(savePath, logfineName)}  {e.Message}");
                mStreamWriter = null;
                return;
            }

            Application.logMessageReceivedThreaded += OnLogMessageReceivedThreaded;
            mThreadRuning = true;

            mFileThread = new Thread(FileLogThread);
            mFileThread.IsBackground = true;   // 修复：非后台线程会在停止 Play / 退出时阻碍进程收尾
            mFileThread.Name = "PLogFileWriter";
            mFileThread.Start();
        }

        public void FileLogThread()
        {
            try
            {
                while (mThreadRuning)
                {
                    // 修复：原实现用无超时的 WaitOne + 退出时只 Reset（Reset 不会唤醒线程）→ 线程可能永远醒不来
                    mManualRestEvent.WaitOne(WakeTimeoutMs);
                    if (!mThreadRuning) break;
                    DrainQueue();
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"[PLogger] 日志写线程异常退出：{e.Message}");
            }
            finally
            {
                FlushQuiet();
            }
        }

        /// <summary>把队列里剩余日志一次写完（锁内批量写，避免每条都抢锁）</summary>
        private void DrainQueue()
        {
            LogData data;
            bool any = false;
            lock (mWriterLock)
            {
                if (mStreamWriter == null) return;
                while (mConCurrentQueue.TryDequeue(out data))
                {
                    any = true;
                    if (data == null) continue;
                    switch (data.type)
                    {
                        case LogType.Warning:
                            mStreamWriter.Write("Warning >>> ");
                            break;
                        case LogType.Error:
                        case LogType.Exception:
                        case LogType.Assert:
                            mStreamWriter.Write("Error >>> ");
                            break;
                        default:
                            mStreamWriter.Write("Log >>> ");
                            break;
                    }
                    // 修复：写文件时剥掉 <color=xxx> 之类的富文本标签（控制台需要，文本文件里只会变成噪音）
                    mStreamWriter.WriteLine(StripRichText(data.log));
                    mStreamWriter.WriteLine(data.trace);
                    mStreamWriter.Write("\r\n");
                }
                if (any) mStreamWriter.Flush();
            }
        }

        private void FlushQuiet()
        {
            lock (mWriterLock)
            {
                if (mStreamWriter == null) return;
                try { mStreamWriter.Flush(); }
                catch (Exception) { }
            }
        }

        /// <summary>
        /// 安全停止：唤醒写线程 → 等待收尾 → 写完剩余日志 → 关闭文件。
        /// 必须在销毁/退出时调用，替代原来"直接 Close + 置 null"的做法（那是竞态）。
        /// </summary>
        public void StopSafely()
        {
            Application.logMessageReceivedThreaded -= OnLogMessageReceivedThreaded;
            mThreadRuning = false;
            mManualRestEvent.Set();   // 唤醒写线程（原先只 Reset，线程会一直阻塞在 WaitOne 上）

            if (mFileThread != null && mFileThread.IsAlive)
            {
                try { mFileThread.Join(StopJoinTimeoutMs); }
                catch (Exception) { }
            }
            mFileThread = null;

            DrainQueue();   // 收尾：把剩余日志写完（此时写线程已退出，不会再抢锁）

            lock (mWriterLock)
            {
                if (mStreamWriter == null) return;
                try
                {
                    mStreamWriter.Flush();
                    mStreamWriter.Close();
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogError($"[PLogger] 关闭日志文件失败：{e.Message}");
                }
                mStreamWriter = null;
            }
        }

        private void OnApplicationQuit()
        {
            StopSafely();
        }

        private void OnDestroy()
        {
            StopSafely();
        }

        private void OnLogMessageReceivedThreaded(string condition, string stackTrace, LogType type)
        {
            if (!mThreadRuning) return;   // 收尾阶段不再入队，避免写入已关闭的流
            mConCurrentQueue.Enqueue(new LogData { log = mNowTime + " " + condition, trace = stackTrace, type = type });
            mManualRestEvent.Set();
        }

        private static string StripRichText(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('<') < 0) return text;
            return RichTextTag.Replace(text, string.Empty);
        }

        private static string SavePathOf(string savePath, string fileName)
        {
            try { return Path.Combine(savePath, fileName); }
            catch (Exception) { return savePath + "/" + fileName; }
        }
    }
}
