using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace UPandaGF.RunTime.InteractiveTaskScoringSystem
{
    /// <summary>
    /// 回放复盘查看器（菜单：UPandaGF/交互任务评分系统/回放复盘查看器）。
    ///
    /// 定位：P2 的“回放”是**可视化复盘**，不是把交互重新演一遍——
    ///   · 时间轴：按 ReplayLog 的 offset 串起每一次动作（进入/错误/跳过/完成）；
    ///   · 定位：点某一条就能在场景里选中对应的 TaskStepBase 节点；
    ///   · 播放：拖时间轴或点播放，会自动按时间顺序高亮当前步骤（只做定位，不改游戏状态）。
    ///
    /// 数据来源两类：
    ///   ① 「载入当前会话日志」= 直接读场景里 TaskDataManager.Replay（运行中/刚跑完最方便）；
    ///   ② 从 StreamingAssets 下的 JSON 文件载入（TaskDataManager.SaveReplayLog() 落盘的文件）。
    /// </summary>
    public class InteractiveTaskReplayWindow : EditorWindow
    {
        private ReplayLog log;
        private string source = "";
        private Vector2 listScroll;

        private string[] files = new string[0];
        private int fileIndex = -1;

        private string filterStepID = "";
        private bool onlyError;

        // 时间轴回放
        private bool playing;
        private bool autoLocate = true;
        private float cursor;
        private double lastTick;
        private int selectedEntry = -1;

        [MenuItem("UPandaGF/Runtime/交互任务评分系统/回放复盘查看器")]
        public static void Open()
        {
            InteractiveTaskReplayWindow win = GetWindow<InteractiveTaskReplayWindow>("回放复盘");
            win.minSize = new Vector2(620f, 380f);
            win.RefreshFileList();
        }

        private void OnEnable()
        {
            RefreshFileList();
        }

        private void Update()
        {
            if (!playing || log == null) return;
            if (lastTick <= 0d) { lastTick = EditorApplication.timeSinceStartup; return; }
            double now = EditorApplication.timeSinceStartup;
            cursor += (float)(now - lastTick);
            lastTick = now;
            if (cursor > log.Duration)
            {
                cursor = log.Duration;
                playing = false;
            }
            if (autoLocate) LocateByCursor();
            Repaint();
        }

        private void OnGUI()
        {
            DrawToolbar();
            EditorGUILayout.Space();
            DrawSummary();
            EditorGUILayout.Space();
            DrawTimeline();
            EditorGUILayout.Space();
            DrawFooter();
        }

        // ---------------------------------------------------------------- 工具栏
        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("载入当前会话日志", EditorStyles.toolbarButton, GUILayout.Width(140f)))
                    LoadFromScene();

                GUILayout.Space(6f);
                if (GUILayout.Button("刷新文件", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                    RefreshFileList();

                GUILayout.Space(6f);
                if (files.Length > 0)
                {
                    int newIndex = EditorGUILayout.Popup(fileIndex < 0 ? 0 : fileIndex, files, EditorStyles.toolbarPopup, GUILayout.Width(260f));
                    if (newIndex != fileIndex)
                    {
                        fileIndex = newIndex;
                        LoadFromFile(files[fileIndex]);
                    }
                }
                else
                {
                    GUILayout.Label("StreamingAssets 下没有 json 日志", EditorStyles.miniLabel);
                }

                GUILayout.FlexibleSpace();
                if (GUILayout.Button("导出 CSV", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                    ExportCsv();
                if (GUILayout.Button("复制时间轴", EditorStyles.toolbarButton, GUILayout.Width(80f)))
                    CopyTimeline();
            }
        }

        // ---------------------------------------------------------------- 摘要
        private void DrawSummary()
        {
            if (log == null)
            {
                EditorGUILayout.HelpBox(
                    "还没有载入日志。\n" +
                    "· 运行场景后点「载入当前会话日志」可直接查看本次记录；\n" +
                    "· 或在运行时调用 TaskDataManager.SaveReplayLog() 落盘，再从上面的下拉框载入。",
                    MessageType.Info);
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField($"任务：{log.taskName}    创建：{log.createTime}    来源：{source}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    $"记录 {log.Count} 条    错误 {log.ErrorCount} 次    耗时 {log.Duration:F2}s    总分 {log.totalScore:F1}    步骤数 {log.stepCount}");

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("只看步骤ID", GUILayout.Width(70f));
                    filterStepID = EditorGUILayout.TextField(filterStepID, GUILayout.Width(160f));
                    onlyError = EditorGUILayout.ToggleLeft("只看错误动作", onlyError, GUILayout.Width(110f));
                    if (GUILayout.Button("清空筛选", GUILayout.Width(80f))) { filterStepID = ""; onlyError = false; }
                }

                // 时间轴播放
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(playing ? "暂停" : "播放", GUILayout.Width(60f)))
                    {
                        playing = !playing;
                        lastTick = 0d;
                    }
                    if (GUILayout.Button("回到开头", GUILayout.Width(70f))) { cursor = 0f; selectedEntry = -1; }
                    float max = log.Duration > 0f ? log.Duration : 0.001f;
                    float newCursor = EditorGUILayout.Slider(cursor, 0f, max);
                    if (!Mathf.Approximately(newCursor, cursor)) { cursor = newCursor; LocateByCursor(); }
                    autoLocate = EditorGUILayout.ToggleLeft("播放时自动定位", autoLocate, GUILayout.Width(120f));
                }
            }
        }

        // ---------------------------------------------------------------- 时间轴列表
        private void DrawTimeline()
        {
            if (log == null || log.entries == null) return;

            EditorGUILayout.LabelField($"时间轴（{ShownCount()} 条）", EditorStyles.boldLabel);
            listScroll = EditorGUILayout.BeginScrollView(listScroll, GUILayout.MinHeight(140f));

            HashSet<string> stepIDs = new HashSet<string>();
            for (int i = 0; i < log.entries.Count; i++)
            {
                ReplayEntry e = log.entries[i];
                if (e == null) continue;
                if (!PassesFilter(e)) continue;
                if (!string.IsNullOrEmpty(e.stepID)) stepIDs.Add(e.stepID);

                bool active = Mathf.Abs(e.offset - cursor) <= 0.5f && (i == LastEntryBeforeCursor());
                using (new EditorGUILayout.HorizontalScope(active ? EditorStyles.helpBox : GUIStyle.none))
                {
                    Color old = GUI.color;
                    if (!e.isCorrect) GUI.color = new Color(1f, 0.55f, 0.55f);
                    else if (active) GUI.color = new Color(0.6f, 1f, 0.6f);

                    GUILayout.Label($"[+{e.offset,7:F2}s]", EditorStyles.miniLabel, GUILayout.Width(80f));
                    GUILayout.Label(string.IsNullOrEmpty(e.stepID) ? "-" : e.stepID, GUILayout.Width(110f));
                    GUILayout.Label(e.action, GUILayout.Width(120f));
                    GUILayout.Label($"得分 {e.score,6:F1}  错误 {e.errors}", GUILayout.Width(150f));
                    GUILayout.Label(e.details, EditorStyles.miniLabel);
                    GUI.color = old;

                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("定位", EditorStyles.miniButton, GUILayout.Width(46f)))
                    {
                        selectedEntry = i;
                        cursor = e.offset;
                        LocateStep(e.stepID);
                    }
                }
            }

            EditorGUILayout.EndScrollView();

            if (stepIDs.Count > 0)
            {
                EditorGUILayout.LabelField("包含的步骤", EditorStyles.miniBoldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    foreach (string id in stepIDs)
                        if (GUILayout.Button(id, EditorStyles.miniButton, GUILayout.Width(90f)))
                            LocateStep(id);
                }
            }
        }

        private void DrawFooter()
        {
            if (log == null) return;
            if (selectedEntry >= 0 && selectedEntry < log.entries.Count && log.entries[selectedEntry] != null)
            {
                ReplayEntry e = log.entries[selectedEntry];
                EditorGUILayout.LabelField($"选中：{e.stepID} / {e.action} / {e.details}");
            }
            EditorGUILayout.LabelField("提示：本窗口只做复盘定位，不会修改任何运行时状态。", EditorStyles.miniLabel);
        }

        // ---------------------------------------------------------------- 数据
        private void LoadFromScene()
        {
            TaskDataManager mgr = null;
            TaskDataManager[] managers = FindObjectsOfType<TaskDataManager>(true);
            if (managers != null && managers.Length > 0) mgr = managers[0];
            if (mgr == null) { EditorUtility.DisplayDialog("回放复盘", "场景里没有 TaskDataManager。", "确定"); return; }
            if (mgr.Replay == null) { EditorUtility.DisplayDialog("回放复盘", "还没有日志：请先运行场景完成任务，或关闭了 enableReplayLog。", "确定"); return; }

            log = mgr.Replay;
            source = "当前会话（内存）";
            cursor = 0f;
            selectedEntry = -1;
            Repaint();
        }

        private void LoadFromFile(string fileName)
        {
            string path = Path.Combine(Application.streamingAssetsPath, fileName);
            ReplayLog loaded = TaskDataManager.LoadReplayLog(path);
            if (loaded == null) { EditorUtility.DisplayDialog("回放复盘", "载入失败：" + path, "确定"); return; }
            log = loaded;
            source = fileName;
            cursor = 0f;
            selectedEntry = -1;
            Repaint();
        }

        private void RefreshFileList()
        {
            List<string> result = new List<string>();
            string dir = Application.streamingAssetsPath;
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                string[] all = Directory.GetFiles(dir, "*.json");
                for (int i = 0; i < all.Length; i++)
                {
                    string name = Path.GetFileName(all[i]);
                    if (name.StartsWith("replay_") || name.Contains("replay")) result.Add(name);
                }
            }
            files = result.ToArray();
            if (fileIndex >= files.Length) fileIndex = -1;
        }

        private bool PassesFilter(ReplayEntry e)
        {
            if (e == null) return false;
            if (onlyError && e.isCorrect) return false;
            if (!string.IsNullOrEmpty(filterStepID) && e.stepID != filterStepID) return false;
            return true;
        }

        private int ShownCount()
        {
            if (log == null || log.entries == null) return 0;
            int n = 0;
            for (int i = 0; i < log.entries.Count; i++) if (PassesFilter(log.entries[i])) n++;
            return n;
        }

        private int LastEntryBeforeCursor()
        {
            if (log == null || log.entries == null) return -1;
            int found = -1;
            for (int i = 0; i < log.entries.Count; i++)
            {
                ReplayEntry e = log.entries[i];
                if (e == null) continue;
                if (e.offset <= cursor) found = i; else break;
            }
            return found;
        }

        private void LocateByCursor()
        {
            int index = LastEntryBeforeCursor();
            if (index < 0) return;
            selectedEntry = index;
            LocateStep(log.entries[index].stepID, false);
        }

        /// <summary>在场景里选中对应步骤节点（不做任何状态修改）</summary>
        private void LocateStep(string stepID, bool focus = false)
        {
            if (string.IsNullOrEmpty(stepID)) return;
            TaskStepBase[] steps = FindObjectsOfType<TaskStepBase>(true);
            for (int i = 0; i < steps.Length; i++)
            {
                TaskStepBase step = steps[i];
                if (step == null || step.GetStepID() != stepID) continue;
                if (focus) EditorGUIUtility.PingObject(step.gameObject);
                Selection.activeGameObject = step.gameObject;
                SceneView.RepaintAll();
                break;
            }
        }

        private void ExportCsv()
        {
            if (log == null) return;
            string path = EditorUtility.SaveFilePanel("导出回放 CSV", Application.streamingAssetsPath,
                "replay_" + log.taskName + ".csv", "csv");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                File.WriteAllText(path, log.ToCsv(), new UTF8Encoding(true));
                Debug.Log($"[交互任务] 回放 CSV 已导出：{path}");
                EditorUtility.RevealInFinder(path);
            }
            catch (System.Exception e)
            {
                EditorUtility.DisplayDialog("回放复盘", "导出失败：" + e.Message, "确定");
            }
        }

        private void CopyTimeline()
        {
            if (log == null) return;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(log.Summary());
            foreach (string line in log.BuildTimeline()) sb.AppendLine(line);
            EditorGUIUtility.systemCopyBuffer = sb.ToString();
            Debug.Log("[交互任务] 回放时间轴已复制到剪贴板");
        }
    }
}
