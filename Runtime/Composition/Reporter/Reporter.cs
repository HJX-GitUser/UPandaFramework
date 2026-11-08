    using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace UPandaGF
{

[System.Serializable]
public class Images
{
    public Texture2D clearImage;
    public Texture2D collapseImage;
    public Texture2D clearOnNewSceneImage;
    public Texture2D showTimeImage;
    public Texture2D showSceneImage;
    public Texture2D userImage;
    public Texture2D showMemoryImage;
    public Texture2D softwareImage;
    public Texture2D dateImage;
    public Texture2D showFpsImage;
    public Texture2D infoImage;
    public Texture2D saveLogsImage;
    public Texture2D searchImage;
    public Texture2D copyImage;
    public Texture2D closeImage;

    public Texture2D buildFromImage;
    public Texture2D systemInfoImage;
    public Texture2D graphicsInfoImage;
    public Texture2D backImage;

    public Texture2D logImage;
    public Texture2D warningImage;
    public Texture2D errorImage;

    public Texture2D barImage;
    public Texture2D button_activeImage;
    public Texture2D even_logImage;
    public Texture2D odd_logImage;
    public Texture2D selectedImage;

    public GUISkin reporterScrollerSkin;
}

/// <summary>运行时日志入口：后台线程只入队，主线程分批处理，绘制位于 Reporter.View.cs。</summary>
public partial class Reporter : MonoBehaviour
{
    public enum _LogType { Assert = LogType.Assert, Error = LogType.Error, Exception = LogType.Exception, Log = LogType.Log, Warning = LogType.Warning }
    public class Sample
    {
        public float time;
        public byte loadedScene;
        public float memory, fps;
        public string fpsText;
        public string sceneName;
        public static float MemSize() { return 32; }
        public string GetSceneName() { return string.IsNullOrEmpty(sceneName) ? "Scene " + loadedScene : sceneName; }
    }
    public class Log
    {
        public int count = 1;
        public _LogType logType;
        public string condition, stacktrace;
        public int sampleId;
        public Sample sample;
        internal string preview;
        public Log CreateCopy() { return (Log)MemberwiseClone(); }
        public float GetMemoryUsage() { return 160 + 2 * ((condition?.Length ?? 0) + (stacktrace?.Length ?? 0)); }
    }

    public Images images = new Images();
    public bool show;
    public Vector2 size = new Vector2(32, 32);
    [Tooltip("保留日志估算内存上限(MiB),0 表示不限；采用淘汰最旧记录而非整体清空。")]
    public float maxSize = 20;
    public int maxLogCount = 3000;
    public int numOfCircleToShow = 1; // 保留旧场景字段；显式 ShowLogWindows 仍为主要入口。
    public string UserData = "";
    public float fps;
    public string fpsText = "--";
    [NonSerialized] public bool Initialized;
    [Min(1)] public int maxQueuedLogs = 2048;
    [Min(1)] public int maxQueuedMemoryMB = 4;
    [Min(1)] public int maxLogsPerFrame = 256;
    [Min(256)] public int maxMessageLength = 32768;

    private static Reporter active;
    [ThreadStatic] private static bool notifyingObserver;
    private readonly ReporterLogStore store = new ReporterLogStore();
    private readonly Queue<Log> pending = new Queue<Log>();
    private readonly object pendingGate = new object();
    private readonly List<Log> frameBatch = new List<Log>();
    private readonly List<Log> currentLog = new List<Log>();
    private readonly System.Diagnostics.Stopwatch clock = new System.Diagnostics.Stopwatch();
    private bool subscribed, ownsInstance, visibleDirty = true, paused, followTail = true;
    private int visibleVersion = -1, droppedCount, nextSampleId, clearGeneration;
    private volatile bool accepting;
    private long pendingBytes;
    private float captureTimeOffset, nextMetricsAt, metricStart;
    private int metricFrames;
    private volatile float cachedFps, cachedMemory;
    private volatile string currentScene = "";
    private string buildDate = "未提供构建信息", logDate, statusMessage = "";
    private float statusUntil;
    private ReporterGUI renderer;
    private bool collapse, showTime, showScene, showMemory, showFps;
    private bool showLog = true, showWarning = true, showError = true;
    private bool clearOnNewSceneLoaded;
    private bool showClearOnNewSceneLoadedButton = true, showTimeButton = true, showSceneButton = true;
    private bool showMemButton = true, showFpsButton = true, showSearchText = true, showCopyButton = true, showSaveButton = true;
    private string filterText = "";
    private enum ReportView { None, Logs, Settings, Diagnostics }
    private ReportView currentView = ReportView.Logs;
    private Log selectedLog;
    public float TotalMemUsage => store.EstimatedBytes / (1024f * 1024f);
    public int RetainedLogCount => store.Count;
    public int DroppedLogCount => Volatile.Read(ref droppedCount);
    public int PendingLogCount { get { lock (pendingGate) return pending.Count; } }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession() { active = null; }
    private void Awake() { Initialize(); }
    private void OnEnable() { if (Initialized && ownsInstance) Subscribe(); }
    private void OnDisable()
    {
        Unsubscribe();
        if (renderer != null) renderer.enabled = false;
    }
    private void OnDestroy()
    {
        Unsubscribe();
        if (active == this) active = null;
        DisposeView();
    }
    public void Initialize()
    {
        if (Initialized && ownsInstance) return;
        if (active != null && active != this) { Destroy(gameObject); return; }
        active = this;
        ownsInstance = true;
        if (images == null) images = new Images();
        SendMessage("OnPreStart", SendMessageOptions.DontRequireReceiver);
        ReadPreferences();
        currentScene = SceneManager.GetActiveScene().name;
        logDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        captureTimeOffset = Time.realtimeSinceStartup;
        clock.Restart();
        metricStart = Time.realtimeSinceStartup;
        DontDestroyOnLoad(gameObject);
        Initialized = true;
        renderer = GetComponent<ReporterGUI>();
        if (renderer == null) renderer = gameObject.AddComponent<ReporterGUI>();
        renderer.enabled = show && isActiveAndEnabled;
        if (isActiveAndEnabled) Subscribe();
    }
    private void Subscribe()
    {
        if (subscribed) return;
        accepting = true;
        Application.logMessageReceivedThreaded += CaptureLogThread;
        SceneManager.sceneLoaded += SceneLoaded;
        subscribed = true;
    }
    private void Unsubscribe()
    {
        accepting = false;
        if (!subscribed) return;
        Application.logMessageReceivedThreaded -= CaptureLogThread;
        SceneManager.sceneLoaded -= SceneLoaded;
        subscribed = false;
    }
    private void Start() { if (ownsInstance) StartCoroutine(ReadBuildInfo()); }
    public void ShowLogWindows()
    {
        if (!Initialized) Initialize();
        if (!ownsInstance) return;
        bool changed = !show;
        show = true;
        currentView = ReportView.Logs;
        if (renderer != null) renderer.enabled = isActiveAndEnabled;
        if (changed) SendMessage("OnShowReporter", SendMessageOptions.DontRequireReceiver);
    }
    public void HideLogWindows()
    {
        if (!show) return;
        show = false;
        if (renderer != null) renderer.enabled = false;
        WritePreferences();
        SendMessage("OnHideReporter", SendMessageOptions.DontRequireReceiver);
    }
    public void ClearLogs()
    {
        clearGeneration++;
        lock (pendingGate) { pending.Clear(); pendingBytes = 0; }
        store.Clear();
        currentLog.Clear();
        selectedLog = detailLog = null;
        detailText = cachedDetailText = "";
        scrollPosition = detailScroll = Vector2.zero;
        Interlocked.Exchange(ref droppedCount, 0);
        visibleDirty = true;
        // 不强制 GC.Collect：清屏不应造成全局 GC 停顿。
    }
    public void SetFilter(string value) { filterText = value ?? ""; visibleDirty = true; }
    public void SetCollapse(bool value) { collapse = value; visibleDirty = true; }
    public void SetSeverityFilters(bool logs, bool warnings, bool errors)
    {
        showLog = logs; showWarning = warnings; showError = errors; visibleDirty = true;
    }
    private void Update()
    {
        if (!ownsInstance || !Initialized) return;
        if (renderer != null) renderer.enabled = show && isActiveAndEnabled;
        UpdateMetrics();
        if (!paused) DrainPending();
        long budget = maxSize > 0 ? (long)(maxSize * 1024 * 1024) : 0;
        store.Trim(maxLogCount, budget);
        RefreshVisible();
    }
    private void UpdateMetrics()
    {
        metricFrames++;
        float now = Time.realtimeSinceStartup;
        if (now < nextMetricsAt) return;
        float elapsed = now - metricStart;
        if (elapsed > 0) fps = metricFrames / elapsed;
        fpsText = fps.ToString("0.0");
        cachedFps = fps;
        cachedMemory = GC.GetTotalMemory(false) / (1024f * 1024f);
        nextMetricsAt = now + .25f;
        metricStart = now;
        metricFrames = 0;
    }
    private void CaptureLogThread(string condition, string stacktrace, LogType type)
    {
        if (!accepting || notifyingObserver) return;
        int limit = Math.Max(256, maxMessageLength);
        condition = Limit(condition, limit);
        stacktrace = Limit(stacktrace, limit);
        var log = new Log { condition = condition, stacktrace = stacktrace, logType = (_LogType)type,
            sample = new Sample { time = captureTimeOffset + (float)clock.Elapsed.TotalSeconds,
                sceneName = currentScene, fps = cachedFps, memory = cachedMemory } };
        lock (pendingGate)
        {
            if (!accepting) return;
            long bytes = PendingBytes(log);
            long budget = Math.Max(1, maxQueuedMemoryMB) * 1024L * 1024L;
            if (bytes > budget) { Interlocked.Increment(ref droppedCount); return; }
            while (pending.Count > 0 && (pending.Count >= Math.Max(1, maxQueuedLogs) || pendingBytes + bytes > budget))
            {
                pendingBytes -= PendingBytes(pending.Dequeue());
                Interlocked.Increment(ref droppedCount);
            }
            pending.Enqueue(log);
            pendingBytes += bytes;
        }
    }
    private static long PendingBytes(Log log) { return 160L + 2L * (log.condition.Length + log.stacktrace.Length); }
    private static string Limit(string value, int max)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Length <= max ? value : value.Substring(0, max) + " …[truncated]";
    }
    private void DrainPending()
    {
        frameBatch.Clear();
        lock (pendingGate)
        {
            int count = Math.Min(pending.Count, Math.Max(1, maxLogsPerFrame));
            for (int i = 0; i < count; i++)
            {
                var log = pending.Dequeue(); pendingBytes -= PendingBytes(log); frameBatch.Add(log);
            }
        }
        int generation = clearGeneration;
        foreach (var log in frameBatch)
        {
            if (generation != clearGeneration || !ownsInstance || this == null) break;
            log.sampleId = nextSampleId++;
            log.sample.fpsText = log.sample.fps.ToString("0.0");
            int cr = log.condition.IndexOf('\r'), lf = log.condition.IndexOf('\n');
            int newline = cr < 0 ? lf : lf < 0 ? cr : Math.Min(cr, lf);
            log.preview = newline >= 0 ? log.condition.Substring(0, newline) + " …" : log.condition;
            store.Add(log, maxLogCount, maxSize > 0 ? (long)(maxSize * 1024 * 1024) : 0);
            // 防止接收方在 OnLog 内写日志/抛异常形成自反馈。其它线程的正常日志不受影响。
            notifyingObserver = true;
            try { SendMessage("OnLog", log, SendMessageOptions.DontRequireReceiver); }
            catch (Exception e) { SetStatus("OnLog 接收方异常：" + e.Message); }
            finally { notifyingObserver = false; }
        }
        frameBatch.Clear();
    }
    private void RefreshVisible()
    {
        if (!visibleDirty && visibleVersion == store.Version) return;
        store.CopyVisible(currentLog, collapse, showLog, showWarning, showError, filterText);
        if (selectedLog != null && !currentLog.Contains(selectedLog))
        {
            var group = store.GroupFor(selectedLog);
            selectedLog = group != null && currentLog.Contains(group) ? group : null;
        }
        visibleVersion = store.Version;
        visibleDirty = false;
    }
    private void SceneLoaded(Scene scene, LoadSceneMode mode)
    {
        currentScene = SceneManager.GetActiveScene().name;
        if (clearOnNewSceneLoaded) ClearLogs();
    }
    private IEnumerator ReadBuildInfo()
    {
        string path = Path.Combine(Application.streamingAssetsPath, "build_info");
        if (!path.Contains("://") && !File.Exists(path)) yield break;
        string url = path.Contains("://") ? path : new Uri(path).AbsoluteUri;
        using (var request = UnityWebRequest.Get(url))
        {
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success) buildDate = request.downloadHandler.text;
        }
    }
    private void SaveLogsToDevice()
    {
        string path = Path.Combine(Application.persistentDataPath, "logs.txt");
        string temporary = path + ".tmp";
        try
        {
            using (var writer = new StreamWriter(temporary, false, new System.Text.UTF8Encoding(false)))
                foreach (var log in store.Snapshot())
                {
                    writer.WriteLine("[{0:0.000}] [{1}] [{2}]", log.sample.time, log.logType, log.sample.GetSceneName());
                    writer.WriteLine(log.condition);
                    writer.WriteLine(log.stacktrace);
                    writer.WriteLine();
                }
            UPandaGF.FileUpdateTransaction.Commit(new[] { temporary }, new[] { path }, path + ".recovery");
            SetStatus("已保存：" + path);
        }
        catch (Exception e) { SetStatus("保存失败：" + e.Message); }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } }
    }
    private void SetStatus(string message) { statusMessage = message; statusUntil = Time.realtimeSinceStartup + 6; }
    private void ReadPreferences()
    {
        collapse = Pref("collapse", false); clearOnNewSceneLoaded = Pref("clearOnNewSceneLoaded", false);
        showTime = Pref("showTime", true); showScene = Pref("showScene", false);
        showMemory = Pref("showMemory", false); showFps = Pref("showFps", false);
        showLog = Pref("showLog", true); showWarning = Pref("showWarning", true); showError = Pref("showError", true);
        showClearOnNewSceneLoadedButton = Pref("showClearOnNewSceneLoadedButton", true);
        showTimeButton = Pref("showTimeButton", true); showSceneButton = Pref("showSceneButton", true);
        showMemButton = Pref("showMemButton", true); showFpsButton = Pref("showFpsButton", true);
        showSearchText = Pref("showSearchText", true); showCopyButton = Pref("showCopyButton", true); showSaveButton = Pref("showSaveButton", true);
        filterText = PlayerPrefs.GetString("Reporter_filterText", "");
        panelOpacity = Mathf.Clamp(PlayerPrefs.GetFloat("Reporter_opacity", 1f), .2f, 1f);
        float scale = Mathf.Clamp(PlayerPrefs.GetFloat("Reporter_size", size.x), 16, 64);
        size = new Vector2(scale, scale);
    }
    private static bool Pref(string name, bool fallback) { return PlayerPrefs.GetInt("Reporter_" + name, fallback ? 1 : 0) != 0; }
    private void WritePreferences()
    {
        if (!ownsInstance) return;
        SavePref("collapse", collapse); SavePref("clearOnNewSceneLoaded", clearOnNewSceneLoaded);
        SavePref("showTime", showTime); SavePref("showScene", showScene); SavePref("showMemory", showMemory); SavePref("showFps", showFps);
        SavePref("showLog", showLog); SavePref("showWarning", showWarning); SavePref("showError", showError);
        SavePref("showClearOnNewSceneLoadedButton", showClearOnNewSceneLoadedButton);
        SavePref("showTimeButton", showTimeButton); SavePref("showSceneButton", showSceneButton); SavePref("showMemButton", showMemButton); SavePref("showFpsButton", showFpsButton);
        SavePref("showSearchText", showSearchText); SavePref("showCopyButton", showCopyButton); SavePref("showSaveButton", showSaveButton);
        PlayerPrefs.SetString("Reporter_filterText", filterText); PlayerPrefs.SetFloat("Reporter_size", size.x);
        PlayerPrefs.SetFloat("Reporter_opacity", panelOpacity);
        PlayerPrefs.Save();
    }
    private static void SavePref(string name, bool value) { PlayerPrefs.SetInt("Reporter_" + name, value ? 1 : 0); }
    private void OnApplicationQuit() { WritePreferences(); }
}

}