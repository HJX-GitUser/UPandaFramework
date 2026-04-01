using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

public class LogListenerManager : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] public GameObject logPanel;
    [SerializeField] public Text logText;  // 使用标准Text组件
    [SerializeField] public ScrollRect logScrollRect;
    [SerializeField] public InputField filterInput;
    [SerializeField] public Toggle autoScrollToggle;
    [SerializeField] public Button clearButton;
    [SerializeField] public Button saveButton;
    [SerializeField] public Button closeButton;
    [SerializeField] public Text statusText;
    [SerializeField] public Button copyButton;
    [SerializeField] public Button exportButton;

    [Header("Filter Toggles")]
    [SerializeField] public Toggle infoToggle;
    [SerializeField] public Toggle warningToggle;
    [SerializeField] public Toggle errorToggle;
    [SerializeField] public Toggle exceptionToggle;

    [Header("Display Settings")]
    [SerializeField] public int maxLogLines = 1000;
    [SerializeField] public int maxDisplayLines = 500;  // 实际显示的行数
    [SerializeField] public float updateInterval = 0.1f;
    [SerializeField] public bool showOnError = true;
    [SerializeField] public bool autoStart = true;
    [SerializeField] public string[] filterKeywords = new string[0];
    [SerializeField] public Font logFont;
    [SerializeField] public int fontSize = 14;

    [Header("Colors")]
    [SerializeField] public Color infoColor = Color.white;
    [SerializeField] public Color warningColor = Color.yellow;
    [SerializeField] public Color errorColor = Color.red;
    [SerializeField] public Color exceptionColor = new Color(1f, 0.5f, 0f);
    [SerializeField] public Color timestampColor = new Color(0.6f, 0.8f, 1f, 0.8f);

    [Header("UI Settings")]
    [SerializeField] public KeyCode toggleKey = KeyCode.F12;
    [SerializeField] public bool ctrlRequired = true;
    [SerializeField] public float panelOpacity = 0.95f;

    private Queue<LogEntry> logQueue = new Queue<LogEntry>();
    private List<LogEntry> logEntries = new List<LogEntry>();
    private StringBuilder logContent = new StringBuilder();
    private Thread logThread;
    private bool isListening = false;
    private string logFilePath;
    private FileStream fileStream;
    private StreamReader streamReader;
    private int currentLineCount = 0;
    private float lastUpdateTime = 0f;
    private bool needsUpdate = false;
    private System.DateTime lastLogTime = System.DateTime.Now;

    // 日志类型过滤器
    private bool showInfo = true;
    private bool showWarning = true;
    private bool showError = true;
    private bool showException = true;

    public CanvasGroup canvasGroup;

    private void SetEnable(bool isEnable)
    {
        //if(canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        //canvasGroup.alpha = isEnable? 1 : 0;
        //canvasGroup.interactable = isEnable;
        //canvasGroup.blocksRaycasts = isEnable;
        gameObject.SetActive(isEnable);
    }

    // 日志结构
    private class LogEntry
    {
        public string text;
        public string rawText;
        public string stackInfo;
        public LogType type;
        public System.DateTime time;
        public bool isColored = false;
    }

    private static LogListenerManager instance;
    public static LogListenerManager Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindObjectOfType<LogListenerManager>();
                if (instance == null)
                {
                    GameObject obj = new GameObject("LogListenerManager");
                    instance = obj.AddComponent<LogListenerManager>();
                }
            }
            return instance;
        }
    }

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Initialize();
    }

    private void Start()
    {
        SetupUIEvents();

        if (autoStart)
        {
            AutoDetectLogFile();

            if (!string.IsNullOrEmpty(logFilePath) && File.Exists(logFilePath))
            {
                StartListening();
            }
        }

        // 初始化日志字体
        if (logFont != null && logText != null)
        {
            logText.font = logFont;
        }

        UpdateStatus("日志监听器已初始化");
        SetEnable(false);
    }

    private void Initialize()
    {
        Application.logMessageReceived += HandleUnityLog;

        // 获取日志文件路径
        logFilePath = GetLogFilePath();

        // 确保日志目录存在
        string logDir = Path.GetDirectoryName(logFilePath);
        if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
        {
            Directory.CreateDirectory(logDir);
        }

        // 写入初始化日志
        WriteInitialLog();
    }

    private void SetupUIEvents()
    {
        if (clearButton != null)
        {
            clearButton.onClick.RemoveAllListeners();
            clearButton.onClick.AddListener(ClearLogs);
        }

        if (saveButton != null)
        {
            saveButton.onClick.RemoveAllListeners();
            saveButton.onClick.AddListener(SaveLogs);
        }

        if (copyButton != null)
        {
            copyButton.onClick.RemoveAllListeners();
            copyButton.onClick.AddListener(CopyLogsToClipboard);
        }

        if (exportButton != null)
        {
            exportButton.onClick.RemoveAllListeners();
            exportButton.onClick.AddListener(ExportLogs);
        }

        if (closeButton != null)
        {
            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(TogglePanel);
        }

        if (filterInput != null)
        {
            filterInput.onValueChanged.RemoveAllListeners();
            filterInput.onValueChanged.AddListener(OnFilterChanged);
        }

        if (infoToggle != null)
        {
            infoToggle.onValueChanged.RemoveAllListeners();
            infoToggle.onValueChanged.AddListener(value => {
                showInfo = value;
                needsUpdate = true;
            });
        }

        if (warningToggle != null)
        {
            warningToggle.onValueChanged.RemoveAllListeners();
            warningToggle.onValueChanged.AddListener(value => {
                showWarning = value;
                needsUpdate = true;
            });
        }

        if (errorToggle != null)
        {
            errorToggle.onValueChanged.RemoveAllListeners();
            errorToggle.onValueChanged.AddListener(value => {
                showError = value;
                needsUpdate = true;
            });
        }

        if (exceptionToggle != null)
        {
            exceptionToggle.onValueChanged.RemoveAllListeners();
            exceptionToggle.onValueChanged.AddListener(value => {
                showException = value;
                needsUpdate = true;
            });
        }

        if (autoScrollToggle != null && autoScrollToggle.isOn && logScrollRect != null)
        {
            logScrollRect.onValueChanged.AddListener((Vector2 pos) => {
                autoScrollToggle.isOn = Mathf.Approximately(pos.y, 0f);
            });
        }
    }

    private void Update()
    {
        // 快捷键监听
        bool togglePressed = false;

        if (ctrlRequired)
        {
            togglePressed = (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                          && Input.GetKeyDown(toggleKey);
        }
        else
        {
            togglePressed = Input.GetKeyDown(toggleKey);
        }

        if (togglePressed)
        {
            TogglePanel();
        }

        // 定期更新UI
        if (Time.time - lastUpdateTime > updateInterval && needsUpdate)
        {
            UpdateLogDisplay();
            lastUpdateTime = Time.time;
            needsUpdate = false;
        }

        // 更新状态显示
        if (Time.time - lastUpdateTime > 1f)
        {
            UpdateStatusDisplay();
        }
    }

    public void StartListening()
    {
        if (isListening || string.IsNullOrEmpty(logFilePath))
            return;

        try
        {
            if (!File.Exists(logFilePath))
            {
                // 创建日志文件
                File.WriteAllText(logFilePath, $"=== Log Started at {System.DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n");
            }

            isListening = true;

            // 启动监听线程
            logThread = new Thread(ReadLogFile);
            logThread.IsBackground = true;
            logThread.Start();

            UpdateStatus("正在监听日志...");
            AddLog("开始监听日志文件: " + logFilePath, LogType.Log);
        }
        catch (System.Exception e)
        {
            AddLog($"启动监听失败: {e.Message}", LogType.Error);
        }
    }

    public void StopListening()
    {
        isListening = false;

        if (logThread != null && logThread.IsAlive)
        {
            logThread.Join(1000);
        }

        if (fileStream != null)
        {
            fileStream.Close();
            fileStream = null;
        }

        UpdateStatus("已停止监听");
        AddLog("停止监听日志", LogType.Log);
    }

    private void ReadLogFile()
    {
        try
        {
            fileStream = new FileStream(logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            streamReader = new StreamReader(fileStream, Encoding.UTF8);

            // 读取现有内容
            string content = streamReader.ReadToEnd();
            if (!string.IsNullOrEmpty(content))
            {
                lock (logQueue)
                {
                    var lines = content.Split('\n');
                    foreach (var line in lines)
                    {
                        if (!string.IsNullOrEmpty(line.Trim()))
                        {
                            logQueue.Enqueue(new LogEntry
                            {
                                text = line,
                                rawText = line,
                                type = ParseLogType(line),
                                time = System.DateTime.Now
                            });
                        }
                    }
                }
                needsUpdate = true;
            }

            // 实时监听新内容
            while (isListening)
            {
                Thread.Sleep(50);

                if (!streamReader.EndOfStream)
                {
                    string newContent = streamReader.ReadToEnd();
                    if (!string.IsNullOrEmpty(newContent))
                    {
                        lock (logQueue)
                        {
                            var lines = newContent.Split('\n');
                            foreach (var line in lines)
                            {
                                if (!string.IsNullOrEmpty(line.Trim()))
                                {
                                    logQueue.Enqueue(new LogEntry
                                    {
                                        text = line,
                                        rawText = line,
                                        type = ParseLogType(line),
                                        time = System.DateTime.Now
                                    });
                                }
                            }
                        }
                        needsUpdate = true;
                    }
                }
            }
        }
        catch (System.Exception e)
        {
            AddLog($"[监听错误] {e.Message}", LogType.Exception);
        }
    }

    private LogType ParseLogType(string line)
    {
        if (line.Contains("[ERROR]") || line.Contains("Error:"))
            return LogType.Error;
        else if (line.Contains("[WARN]") || line.Contains("Warning:"))
            return LogType.Warning;
        else if (line.Contains("[EXCEPTION]") || line.Contains("Exception:"))
            return LogType.Exception;
        else
            return LogType.Log;
    }

    private void HandleUnityLog(string logString, string stackTrace, LogType type)
    {
        string formattedLog = FormatLog(logString, type);

        lock (logQueue)
        {
            logQueue.Enqueue(new LogEntry
            {
                text = formattedLog,
                rawText = logString,
                stackInfo = stackTrace,
                type = type,
                time = System.DateTime.Now
            });
        }

        needsUpdate = true;
        lastLogTime = System.DateTime.Now;

        // 错误时自动显示面板
        if (showOnError && (type == LogType.Error || type == LogType.Exception))
        {
            if (logPanel != null && !logPanel.activeSelf)
            {
                logPanel.SetActive(true);
                AddLog($"检测到错误，自动显示日志面板", LogType.Warning);
            }
        }
    }

    private string FormatLog(string message, LogType type)
    {
        string timestamp = System.DateTime.Now.ToString("HH:mm:ss.fff");
        string typeStr = GetLogTypeString(type);

        return $"[<color=#{ColorUtility.ToHtmlStringRGBA(timestampColor)}>{timestamp}</color>] [{typeStr}] {message}";
    }

    private string GetLogTypeString(LogType type)
    {
        switch (type)
        {
            case LogType.Log: return "INFO";
            case LogType.Warning: return "WARN";
            case LogType.Error: return "ERROR";
            case LogType.Exception: return "EXCEPTION";
            case LogType.Assert: return "ASSERT";
            default: return "UNKNOWN";
        }
    }

    private void UpdateLogDisplay()
    {
        if (logText == null) return;

        // 处理队列中的新日志
        List<LogEntry> newEntries = new List<LogEntry>();
        lock (logQueue)
        {
            while (logQueue.Count > 0)
            {
                newEntries.Add(logQueue.Dequeue());
            }
        }

        if (newEntries.Count > 0)
        {
            // 过滤并添加新日志
            foreach (var entry in newEntries)
            {
                if (ShouldDisplayEntry(entry))
                {
                    logEntries.Add(entry);
                    currentLineCount++;
                }
            }

            // 限制日志数量
            if (logEntries.Count > maxLogLines)
            {
                int removeCount = logEntries.Count - maxLogLines;
                logEntries.RemoveRange(0, removeCount);
            }

            // 构建显示文本
            logContent.Clear();
            int displayCount = Mathf.Min(logEntries.Count, maxDisplayLines);
            int startIndex = Mathf.Max(0, logEntries.Count - displayCount);

            for (int i = startIndex; i < logEntries.Count; i++)
            {
                var entry = logEntries[i];
                logContent.AppendLine(ApplyColor(entry));
            }

            // 更新UI
            logText.text = logContent.ToString();

            // 自动滚动
            if (autoScrollToggle != null && autoScrollToggle.isOn && logScrollRect != null)
            {
                Canvas.ForceUpdateCanvases();
                logScrollRect.verticalNormalizedPosition = 0f;
            }
        }
    }

    private bool ShouldDisplayEntry(LogEntry entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.text))
            return false;

        // 日志类型过滤
        switch (entry.type)
        {
            case LogType.Log: if (!showInfo) return false; break;
            case LogType.Warning: if (!showWarning) return false; break;
            case LogType.Error: if (!showError) return false; break;
            case LogType.Exception: if (!showException) return false; break;
        }

        // 关键词过滤
        if (filterInput != null && !string.IsNullOrEmpty(filterInput.text))
        {
            if (entry.text.IndexOf(filterInput.text, System.StringComparison.OrdinalIgnoreCase) < 0 &&
                entry.rawText.IndexOf(filterInput.text, System.StringComparison.OrdinalIgnoreCase) < 0)
                return false;
        }

        // 内置关键词过滤
        if (filterKeywords != null && filterKeywords.Length > 0 && !string.IsNullOrEmpty(filterInput.text))
        {
            bool hasKeyword = false;
            foreach (var keyword in filterKeywords)
            {
                if (!string.IsNullOrEmpty(keyword) &&
                    (entry.text.IndexOf(keyword, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                     entry.rawText.IndexOf(keyword, System.StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    hasKeyword = true;
                    break;
                }
            }
            if (!hasKeyword) return false;
        }

        return true;
    }

    private string ApplyColor(LogEntry entry)
    {
        if (entry.isColored)
            return entry.text;

        string coloredText = entry.text;
        switch (entry.type)
        {
            case LogType.Error:
                coloredText = $"<color=#{ColorUtility.ToHtmlStringRGB(errorColor)}>{entry.text}</color>\n{entry.stackInfo}";
                break;
            case LogType.Warning:
                coloredText = $"<color=#{ColorUtility.ToHtmlStringRGB(warningColor)}>{entry.text}</color>\n{entry.stackInfo}";
                break;
            case LogType.Exception:
                coloredText = $"<color=#{ColorUtility.ToHtmlStringRGB(exceptionColor)}>{entry.text}</color>\n{entry.stackInfo}";
                break;
            case LogType.Log:
                coloredText = $"<color=#{ColorUtility.ToHtmlStringRGB(infoColor)}>{entry.text}</color>\n{entry.stackInfo}";
                break;
        }

        entry.isColored = true;
        entry.text = coloredText;
        return coloredText;
    }

    public void AddLog(string message, LogType type = LogType.Log)
    {
        string formattedLog = FormatLog(message, type);

        lock (logQueue)
        {
            logQueue.Enqueue(new LogEntry
            {
                text = formattedLog,
                rawText = message,
                type = type,
                time = System.DateTime.Now
            });
        }

        needsUpdate = true;
        lastLogTime = System.DateTime.Now;
    }

    public void ClearLogs()
    {
        lock (logQueue)
        {
            logQueue.Clear();
        }

        logEntries.Clear();
        logContent.Clear();
        currentLineCount = 0;

        if (logText != null)
            logText.text = "";

        AddLog("日志已清空", LogType.Log);
    }

    public void SaveLogs()
    {
        try
        {
            string logDir = Path.Combine(Application.persistentDataPath, "SavedLogs");
            if (!Directory.Exists(logDir))
            {
                Directory.CreateDirectory(logDir);
            }

            string savePath = Path.Combine(logDir, $"log_{System.DateTime.Now:yyyyMMdd_HHmmss}.txt");

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"=== Log Export at {System.DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
            sb.AppendLine($"Total Entries: {logEntries.Count}");
            sb.AppendLine("=".PadRight(50, '='));

            foreach (var entry in logEntries)
            {
                string typeStr = GetLogTypeString(entry.type);
                string timestamp = entry.time.ToString("yyyy-MM-dd HH:mm:ss.fff");
                sb.AppendLine($"[{timestamp}] [{typeStr}] {entry.rawText}");
            }

            File.WriteAllText(savePath, sb.ToString());
            AddLog($"日志已保存到: {savePath}", LogType.Log);

            // 在编辑器中打开文件夹
#if UNITY_EDITOR
            UnityEditor.EditorUtility.RevealInFinder(savePath);
#endif
        }
        catch (System.Exception e)
        {
            AddLog($"保存日志失败: {e.Message}", LogType.Error);
        }
    }

    public void CopyLogsToClipboard()
    {
        try
        {
            StringBuilder sb = new StringBuilder();
            foreach (var entry in logEntries)
            {
                string typeStr = GetLogTypeString(entry.type);
                string timestamp = entry.time.ToString("HH:mm:ss");
                sb.AppendLine($"[{timestamp}] [{typeStr}] {entry.rawText}");
            }

            GUIUtility.systemCopyBuffer = sb.ToString();
            AddLog("日志已复制到剪贴板", LogType.Log);
        }
        catch (System.Exception e)
        {
            AddLog($"复制失败: {e.Message}", LogType.Error);
        }
    }

    public void ExportLogs()
    {
        SaveLogs(); // 目前与保存功能相同
    }

    public void TogglePanel()
    {
        if (logPanel != null)
        {
            bool newState = !gameObject.activeSelf;
            SetEnable(newState);
            if (newState)
            {
                needsUpdate = true;
                AddLog("日志面板已打开", LogType.Log);
            }
            else
            {
                AddLog("日志面板已关闭", LogType.Log);
            }
        }
    }

    private void UpdateStatusDisplay()
    {
        if (statusText != null)
        {
            string status = isListening ? "监听中" : "已停止";
            string timeStr = lastLogTime.ToString("HH:mm:ss");
            statusText.text = $"状态: {status} | 日志: {currentLineCount} | 最后: {timeStr}";
        }
    }

    private void UpdateStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
    }

    public void SetLogFilePath(string path)
    {
        StopListening();
        logFilePath = path;
        StartListening();
    }

    private void AutoDetectLogFile()
    {
        string detectedPath = GetLogFilePath();

        if (File.Exists(detectedPath))
        {
            logFilePath = detectedPath;
            UpdateStatus($"找到日志文件: {Path.GetFileName(detectedPath)}");
        }
        else
        {
            // 尝试其他可能的位置
            string[] possiblePaths =
            {
                Path.Combine(Application.persistentDataPath, "Player.log"),
                Path.Combine(Application.dataPath, "../Logs/Player.log"),
                Path.Combine(Application.dataPath, "../output_log.txt"),
                Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                          Application.companyName,
                          Application.productName,
                          "Player.log")
            };

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    logFilePath = path;
                    UpdateStatus($"找到日志文件: {Path.GetFileName(path)}");
                    return;
                }
            }

            UpdateStatus("未找到日志文件，将使用默认位置");
        }
    }

    private string GetLogFilePath()
    {
#if UNITY_EDITOR
        return Path.Combine(Application.dataPath, "../Logs/Player.log");
#elif UNITY_STANDALONE_WIN
            return Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                               Application.companyName,
                               Application.productName,
                               "Player.log");
#elif UNITY_STANDALONE_OSX
            return Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Personal),
                               "Library/Logs/Unity",
                               Application.productName,
                               "Player.log");
#elif UNITY_ANDROID
            return Path.Combine(Application.persistentDataPath, "Player.log");
#elif UNITY_IOS
            return Path.Combine(Application.persistentDataPath, "Player.log");
#else
            return Path.Combine(Application.persistentDataPath, "Player.log");
#endif
    }

    private void WriteInitialLog()
    {
        string initialLog = $"\n=== Log Listener Started at {System.DateTime.Now:yyyy-MM-dd HH:mm:ss} ===";
        initialLog += $"\nUnity Version: {Application.unityVersion}";
        initialLog += $"\nPlatform: {Application.platform}";
        initialLog += $"\nProduct: {Application.productName}";
        initialLog += $"\nCompany: {Application.companyName}";
        initialLog += $"\nLog File: {logFilePath}";
        initialLog += $"\n========================================\n";

        AddLog(initialLog, LogType.Log);
    }

    private void OnFilterChanged(string filter)
    {
        needsUpdate = true;
    }

    private void OnDestroy()
    {
        StopListening();
        Application.logMessageReceived -= HandleUnityLog;
    }

    // 公开接口
    public void ShowPanel() => TogglePanel();
    public void HidePanel() { if (logPanel != null) logPanel.SetActive(false); }
    public bool IsPanelVisible => logPanel != null && logPanel.activeSelf;
    public int LogCount => currentLineCount;
    public string CurrentLogPath => logFilePath;

    // 静态方法，方便从其他地方调用
    public static void Log(string message) => Instance.AddLog(message, LogType.Log);
    public static void LogWarning(string message) => Instance.AddLog(message, LogType.Warning);
    public static void LogError(string message) => Instance.AddLog(message, LogType.Error);
    public static void LogException(string message) => Instance.AddLog(message, LogType.Exception);
}