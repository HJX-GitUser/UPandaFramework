using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UPandaGF
{
    /// <summary>
    /// 运行时日志控制台：后台线程只负责入队，主线程批量消费并以 IMGUI 绘制。
    /// <para>图标使用项目自有资产（可选，缺失时退回纯文字）；容量、搜索、折叠与导出均由本模块自己维护。</para>
    /// </summary>
    [AddComponentMenu("UPandaGF/日志控制台")]
    [DisallowMultipleComponent]
    public sealed partial class UPandaLogConsole : MonoBehaviour
    {
        private struct QueuedLog
        {
            public string Message;
            public string Stack;
            public LogConsoleLevel Level;
            public DateTime TimeUtc;
            public long EstimatedBytes;
        }

        [Header("容量")]
        [Tooltip("主线程最多保留的日志条数；超过后从最旧记录开始淘汰。")]
        [SerializeField, Min(64)] private int maxStoredEntries = 2000;
        [Tooltip("主线程日志的估算内存上限（字节）。超过后从最旧记录开始淘汰。")]
        [SerializeField, Min(1024)] private long maxStoredBytes = 8L * 1024 * 1024;
        [Tooltip("尚未被主线程消费的日志队列条数上限；队列满时丢弃新日志并累计 DroppedCount。")]
        [SerializeField, Min(16)] private int maxQueuedEntries = 2048;
        [Tooltip("尚未被主线程消费的日志队列估算内存上限（字节）；队列满时丢弃新日志。")]
        [SerializeField, Min(1024)] private long maxQueuedBytes = 4L * 1024 * 1024;
        [Tooltip("每帧最多从队列消费多少条日志；数值越大越及时，但单帧耗时越高。")]
        [SerializeField, Min(1)] private int maxDrainPerFrame = 256;
        [Tooltip("单条日志消息/堆栈的最大字符数；超出部分截断，避免异常长文本占满内存。")]
        [SerializeField, Min(256)] private int maxMessageLength = 32768;

        [Header("行为")]
        [Tooltip("主线程消费到 Error / Exception / Assert 时自动打开日志面板。")]
        [SerializeField] private bool openOnError = true;
        [Tooltip("可选的显示/隐藏快捷键；None 表示只通过 API 或 FPS 按钮打开。")]
        [SerializeField] private KeyCode toggleKey = KeyCode.None;

        [Header("外观")]
        [Tooltip("项目自有图标与滚动条皮肤；由 LogConsoleIconLoader 在创建控制台时自动绑定。")]
        [SerializeField] private LogConsoleIcons icons = new LogConsoleIcons();

        /// <summary>图标与滚动条皮肤；为 null 时视图退回纯文字。</summary>
        public LogConsoleIcons Icons
        {
            get { return icons; }
            set { icons = value ?? new LogConsoleIcons(); }
        }

        private readonly Queue<QueuedLog> queue = new Queue<QueuedLog>();
        private readonly object queueGate = new object();
        private readonly List<QueuedLog> drainBuffer = new List<QueuedLog>();
        private LogConsoleStore store;
        private bool subscribed;
        private bool initialized;
        private bool visible;
        private int dropped;
        private long queuedBytes;
        private float fps;
        private float memoryMb;
        private float frameWindow;
        private int frameCount;
        private string sceneName = string.Empty;
        private DateTime startedAt;

        /// <summary>最多保留条数。</summary>
        public int MaxStoredEntries
        {
            get { return maxStoredEntries; }
            set
            {
                maxStoredEntries = Math.Max(1, value);
                if (store != null) store.MaxEntries = maxStoredEntries;
            }
        }

        /// <summary>最多保留的估算字节数（0 表示不限制）。</summary>
        public long MaxStoredBytes
        {
            get { return maxStoredBytes; }
            set
            {
                maxStoredBytes = Math.Max(0, value);
                if (store != null) store.MaxBytes = maxStoredBytes;
            }
        }

        public int MaxQueuedEntries
        {
            get { return maxQueuedEntries; }
            set { maxQueuedEntries = Math.Max(1, value); }
        }

        public long MaxQueuedBytes
        {
            get { return maxQueuedBytes; }
            set { maxQueuedBytes = Math.Max(0, value); }
        }

        public int MaxDrainPerFrame
        {
            get { return maxDrainPerFrame; }
            set { maxDrainPerFrame = Math.Max(1, value); }
        }

        public bool OpenOnError
        {
            get { return openOnError; }
            set { openOnError = value; }
        }

        public KeyCode ToggleKey
        {
            get { return toggleKey; }
            set { toggleKey = value; }
        }

        public bool IsVisible { get { return visible; } }
        public int RetainedCount { get { return store != null ? store.Count : 0; } }
        public int InfoCount { get { return store != null ? store.InfoCount : 0; } }
        public int WarningCount { get { return store != null ? store.WarningCount : 0; } }
        public int ErrorCount { get { return store != null ? store.ErrorCount : 0; } }
        public float Fps { get { return fps; } }
        public float MemoryMb { get { return memoryMb; } }
        public string SceneName { get { return sceneName; } }

        public int PendingCount
        {
            get { lock (queueGate) return queue.Count; }
        }

        public int DroppedCount
        {
            get { lock (queueGate) return dropped; }
        }

        private void Awake()
        {
            EnsureStore();
            LoadPreferences();
            startedAt = DateTime.Now;
            sceneName = SceneManager.GetActiveScene().name;
        }

        private void OnEnable()
        {
            EnsureStore();
            Subscribe();
        }

        private void OnDisable()
        {
            SavePreferences();
            Unsubscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        /// <summary>幂等初始化；外部可在创建后立即调用，也可以等待 OnEnable。</summary>
        public void Initialize()
        {
            EnsureStore();
            LoadPreferences();
            initialized = true;
            Subscribe();
        }

        private void EnsureStore()
        {
            if (store == null)
            {
                store = new LogConsoleStore(maxStoredEntries, maxStoredBytes);
                initialized = true;
            }
            else
            {
                store.MaxEntries = maxStoredEntries;
                store.MaxBytes = maxStoredBytes;
            }
        }

        private void Subscribe()
        {
            if (subscribed || !isActiveAndEnabled || !Application.isPlaying) return;
            Application.logMessageReceivedThreaded += OnUnityLog;
            SceneManager.sceneLoaded += OnSceneLoaded;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed) return;
            Application.logMessageReceivedThreaded -= OnUnityLog;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            subscribed = false;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            sceneName = scene.name;
            if (clearOnSceneLoad) Clear();
        }

        /// <summary>Unity 的线程日志回调：只做有界入队，不触碰 Unity 对象。</summary>
        private void OnUnityLog(string condition, string stackTrace, LogType type)
        {
            Capture(condition, stackTrace, ToLevel(type));
        }

        /// <summary>线程安全地追加一条日志；队列超过条数/字节上限时丢弃并计数。</summary>
        public void Capture(string message, string stackTrace, LogType type)
        {
            Capture(message, stackTrace, ToLevel(type));
        }

        /// <summary>线程安全地追加一条日志。</summary>
        public void Capture(string message, string stackTrace, LogConsoleLevel level)
        {
            message = Trim(message);
            stackTrace = Trim(stackTrace);
            long size = 96L + 2L * (message.Length + stackTrace.Length);
            lock (queueGate)
            {
                if (queue.Count >= maxQueuedEntries || (maxQueuedBytes > 0 && queuedBytes + size > maxQueuedBytes))
                {
                    dropped++;
                    return;
                }
                queue.Enqueue(new QueuedLog
                {
                    Message = message,
                    Stack = stackTrace,
                    Level = level,
                    TimeUtc = DateTime.UtcNow,
                    EstimatedBytes = size,
                });
                queuedBytes += size;
            }
        }

        private string Trim(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length <= maxMessageLength ? value : value.Substring(0, maxMessageLength);
        }

        private static LogConsoleLevel ToLevel(LogType type)
        {
            if (type == LogType.Warning) return LogConsoleLevel.Warning;
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) return LogConsoleLevel.Error;
            return LogConsoleLevel.Info;
        }

        private void Update()
        {
            if (!initialized) return;
            UpdateMetrics();
            if (!pauseCapture) ProcessPending(maxDrainPerFrame);
            if (toggleKey != KeyCode.None && Input.GetKeyDown(toggleKey)) Toggle();
        }

        private void UpdateMetrics()
        {
            frameCount++;
            frameWindow += Time.unscaledDeltaTime;
            if (frameWindow < 0.5f) return;
            fps = frameCount / frameWindow;
            memoryMb = GC.GetTotalMemory(false) / (1024f * 1024f);
            frameCount = 0;
            frameWindow = 0f;
        }

        /// <summary>
        /// 主线程消费队列；<paramref name="budget"/> 小于 0 时使用 <see cref="MaxDrainPerFrame"/>。
        /// 返回本次写入存储的条数。
        /// </summary>
        public int ProcessPending(int budget = -1)
        {
            if (store == null) EnsureStore();
            int limit = budget < 0 ? maxDrainPerFrame : Math.Max(0, budget);
            if (limit <= 0) return 0;

            drainBuffer.Clear();
            lock (queueGate)
            {
                while (drainBuffer.Count < limit && queue.Count > 0)
                {
                    QueuedLog item = queue.Dequeue();
                    queuedBytes -= item.EstimatedBytes;
                    if (queuedBytes < 0) queuedBytes = 0;
                    drainBuffer.Add(item);
                }
            }
            if (drainBuffer.Count == 0) return 0;

            string currentScene = SceneManager.GetActiveScene().name;
            float currentFps = fps;
            for (int i = 0; i < drainBuffer.Count; i++)
            {
                QueuedLog queued = drainBuffer[i];
                store.Add(new LogConsoleEntry
                {
                    Message = queued.Message,
                    StackTrace = queued.Stack,
                    Level = queued.Level,
                    TimestampUtc = queued.TimeUtc,
                    SceneName = currentScene,
                    Fps = currentFps,
                    MemoryMb = memoryMb,
                });
                if (queued.Level == LogConsoleLevel.Error && openOnError) visible = true;
            }
            sceneName = currentScene;
            int processed = drainBuffer.Count;
            drainBuffer.Clear();
            return processed;
        }

        public void Show()
        {
            EnsureStore();
            Initialize();
            page = ConsolePage.Logs;
            visible = true;
        }

        public void Hide()
        {
            SavePreferences();
            visible = false;
        }

        public void Toggle()
        {
            if (visible) Hide(); else Show();
        }

        public void Clear()
        {
            lock (queueGate)
            {
                queue.Clear();
                queuedBytes = 0;
                dropped = 0;
            }
            if (store != null) store.Clear();
            ResetViewAfterClear();
        }

        /// <summary>返回当前筛选条件下的可见记录；折叠模式返回副本。</summary>
        public List<LogConsoleEntry> SnapshotVisible()
        {
            EnsureStore();
            var result = new List<LogConsoleEntry>();
            store.CopyVisible(result, collapse, showInfo, showWarning, showError, filter);
            return result;
        }

        /// <summary>复制当前可见日志到系统剪贴板。</summary>
        public void CopyVisible()
        {
            List<LogConsoleEntry> entries = SnapshotVisible();
            if (entries.Count == 0) return;
            GUIUtility.systemCopyBuffer = FormatEntries(entries);
        }

        /// <summary>导出当前可见日志；返回写入的文件路径。</summary>
        public string SaveVisible()
        {
            List<LogConsoleEntry> entries = SnapshotVisible();
            string directory = Path.Combine(Application.persistentDataPath, "Logs");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "upanda-log-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
            File.WriteAllText(path, FormatEntries(entries), new UTF8Encoding(false));
            return path;
        }

        private static string FormatEntries(List<LogConsoleEntry> entries)
        {
            var builder = new StringBuilder();
            for (int i = 0; i < entries.Count; i++)
            {
                LogConsoleEntry entry = entries[i];
                builder.Append('[').Append(entry.TimestampUtc.ToLocalTime().ToString("HH:mm:ss"))
                    .Append("] [").Append(entry.Level).Append("] ")
                    .Append(entry.Message);
                if (entry.RepeatCount > 1) builder.Append("  x").Append(entry.RepeatCount);
                builder.AppendLine();
                if (!string.IsNullOrEmpty(entry.StackTrace)) builder.AppendLine(entry.StackTrace);
            }
            return builder.ToString();
        }

        partial void ResetViewAfterClear();
    }
}
