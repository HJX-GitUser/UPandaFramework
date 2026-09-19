using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UPandaGF.EditorTools
{
    /// <summary>
    /// 日志系统诊断窗口（菜单：UPandaGF/日志系统/日志系统诊断）。
    ///
    /// 用来回答排查日志时最耗时的三个问题：
    ///   1) 日志开关到底开了没？—— OPEN_PLOG 宏状态、PLogger.IsInitialized、cfg 各开关（面板上可直接改，运行时立即生效）
    ///   2) 日志文件写哪去了、有没有在写？—— 完整路径、文件大小、PLogHelper 是否存活、待写条数
    ///   3) 日志链路通不通？—— 一键发测试日志 + 实时看窗口收到的最近日志
    /// </summary>
    public class PLoggerDiagnosticsWindow : EditorWindow
    {
        private const int MaxRecent = 100;
        private const string MenuPath = "UPandaGF/日志系统/日志系统诊断";

        private Vector2 scroll;
        private readonly List<string> recent = new List<string>();
        private bool listening;

        [MenuItem(MenuPath, false, 20)]
        public static void Open()
        {
            PLoggerDiagnosticsWindow win = GetWindow<PLoggerDiagnosticsWindow>("日志诊断");
            win.minSize = new Vector2(580f, 420f);
        }

        private void OnEnable()
        {
            if (listening) return;
            Application.logMessageReceived += OnLogReceived;
            listening = true;
        }

        private void OnDisable()
        {
            if (!listening) return;
            Application.logMessageReceived -= OnLogReceived;
            listening = false;
        }

        private void OnLogReceived(string condition, string stackTrace, LogType type)
        {
            recent.Add($"[{type}] {DateTime.Now:HH:mm:ss.fff}  {condition}");
            if (recent.Count > MaxRecent) recent.RemoveRange(0, recent.Count - MaxRecent);
            Repaint();
        }

        private void OnGUI()
        {
            DrawToolbar();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawSwitchSection();
            DrawConfigSection();
            DrawFileSection();
            DrawRecentSection();
            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("发送测试日志", EditorStyles.toolbarButton, GUILayout.Width(100f)))
                {
                    PLogger.Log("[诊断] Log 测试");
                    PLogger.LogWarning("[诊断] Warning 测试");
                    PLogger.LogError("[诊断] Error 测试");
                    PLogger.LogException("[诊断] Exception 测试");
                    PLogger.LogFormat("[诊断] Format 测试 HP={0}", 42);
                }
                if (GUILayout.Button("日志自检", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                    PLoggerSelfCheck.Run();
                if (GUILayout.Button("清空列表", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                    recent.Clear();

                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField($"最近 {recent.Count} 条（上限 {MaxRecent}）", EditorStyles.miniLabel);
            }
        }

        private void DrawSwitchSection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("开关状态", EditorStyles.boldLabel);
#if OPEN_PLOG
                EditorGUILayout.LabelField("OPEN_PLOG 宏：已定义（日志调用参与编译）");
#else
                EditorGUILayout.HelpBox("当前未定义 OPEN_PLOG 宏 —— 所有 PLogger 调用都已在编译期被整体剔除，日志一定不会输出。\n" +
                    "需要日志请执行菜单：UPandaGF -> 日志系统 -> 启动日志（会为 Standalone/iOS/Android/WebGL 四个平台组添加宏）。", MessageType.Warning);
#endif
                EditorGUILayout.LabelField($"配置是否已初始化：{PLogger.IsInitialized}（false = 尚未读到 StreamingAssets/Data/LogConfig.json，正在用默认配置）");
            }
        }

        private void DrawConfigSection()
        {
            LogConfig cfg = PLogger.cfg;
            if (cfg == null)
            {
                EditorGUILayout.HelpBox("PLogger.cfg 为 null（不应发生，请检查是否被外部代码置空）。", MessageType.Error);
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("运行时开关（改动立即生效）", EditorStyles.boldLabel);
                cfg.openLog = EditorGUILayout.Toggle("总开关 openLog", cfg.openLog);
                EditorGUI.indentLevel++;
                cfg.openWarning = EditorGUILayout.Toggle("输出 Warning", cfg.openWarning);
                cfg.openError = EditorGUILayout.Toggle("输出 Error / Exception", cfg.openError);
                EditorGUI.indentLevel--;

                cfg.addHeadFix = EditorGUILayout.Toggle("添加前缀 addHeadFix", cfg.addHeadFix);
                if (cfg.addHeadFix) cfg.logHeadFix = EditorGUILayout.TextField("前缀 logHeadFix", cfg.logHeadFix);
                cfg.openTime = EditorGUILayout.Toggle("显示时间 openTime", cfg.openTime);
                cfg.showThreadID = EditorGUILayout.Toggle("显示线程号 showThreadID", cfg.showThreadID);
                cfg.maxLogLength = EditorGUILayout.IntField("单条最大长度 maxLogLength（0=不限）", cfg.maxLogLength);
                cfg.logSave = EditorGUILayout.Toggle("落盘 logSave（下次初始化生效）", cfg.logSave);
                cfg.saveOverwrite = EditorGUILayout.Toggle("覆盖写入 saveOverwrite", cfg.saveOverwrite);
                cfg.logFileSavePath = EditorGUILayout.TextField("相对路径 logFileSavePath", cfg.logFileSavePath);
            }
        }

        private void DrawFileSection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("文件落盘状态", EditorStyles.boldLabel);

                PLogHelper helper = FindObjectOfType<PLogHelper>();
                if (helper == null)
                {
                    EditorGUILayout.LabelField(Application.isPlaying
                        ? "未找到 PLogHelper：当前 cfg.logSave 为 false，或文件模块初始化失败（看 Console）"
                        : "未找到 PLogHelper（Play 模式下才会创建）", EditorStyles.miniLabel);
                }
                else
                {
                    EditorGUILayout.LabelField($"PLogHelper：运行中={helper.IsRunning}  待写条数={helper.PendingCount}  文件={helper.LogFilePath}", EditorStyles.miniLabel);
                }

                string dir = PLogger.cfg != null ? PLogger.cfg.LogFileSavePath : Application.persistentDataPath;
                string file = PLogger.cfg != null ? Path.Combine(dir, PLogger.cfg.LogFileName) : dir;
                EditorGUILayout.LabelField("目录：" + dir, EditorStyles.miniLabel);
                if (Directory.Exists(dir))
                {
                    string[] files = Directory.GetFiles(dir, "*.log");
                    EditorGUILayout.LabelField($"目录下 .log 文件 {files.Length} 个" + (File.Exists(file) ? $"，本次文件名已存在（{new FileInfo(file).Length / 1024} KB）" : ""), EditorStyles.miniLabel);
                }
                else
                {
                    EditorGUILayout.LabelField("目录尚不存在（开启 logSave 后首次初始化会创建）", EditorStyles.miniLabel);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("打开日志目录", GUILayout.Width(110f)))
                    {
                        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                        EditorUtility.RevealInFinder(dir);
                    }
                    if (GUILayout.Button("仅清空本次日志文件", GUILayout.Width(150f)))
                    {
                        if (File.Exists(file))
                        {
                            File.WriteAllText(file, "");
                            Debug.Log("已清空日志文件：" + file);
                        }
                    }
                }
            }
        }

        private void DrawRecentSection()
        {
            EditorGUILayout.LabelField("窗口收到的最近日志（含编辑器自身的日志）", EditorStyles.boldLabel);
            if (recent.Count == 0)
            {
                EditorGUILayout.LabelField("（暂无：点上方「发送测试日志」验证链路）", EditorStyles.miniLabel);
                return;
            }
            for (int i = recent.Count - 1; i >= 0; i--)
                EditorGUILayout.LabelField(recent[i], EditorStyles.miniLabel);
        }
    }
}
