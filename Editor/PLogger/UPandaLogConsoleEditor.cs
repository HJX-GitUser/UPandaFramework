using UnityEditor;
using UnityEngine;

namespace UPandaGF.GFEditor
{
    /// <summary>UPandaLogConsole 的中文 Inspector：容量、行为、运行状态与图标绑定说明。</summary>
    [CustomEditor(typeof(UPandaLogConsole))]
    public sealed class UPandaLogConsoleEditor : Editor
    {
        private SerializedProperty maxStoredEntries;
        private SerializedProperty maxStoredBytes;
        private SerializedProperty maxQueuedEntries;
        private SerializedProperty maxQueuedBytes;
        private SerializedProperty maxDrainPerFrame;
        private SerializedProperty maxMessageLength;
        private SerializedProperty openOnError;
        private SerializedProperty toggleKey;
        private SerializedProperty icons;
        private bool showIconReferences;

        private void OnEnable()
        {
            maxStoredEntries = serializedObject.FindProperty("maxStoredEntries");
            maxStoredBytes = serializedObject.FindProperty("maxStoredBytes");
            maxQueuedEntries = serializedObject.FindProperty("maxQueuedEntries");
            maxQueuedBytes = serializedObject.FindProperty("maxQueuedBytes");
            maxDrainPerFrame = serializedObject.FindProperty("maxDrainPerFrame");
            maxMessageLength = serializedObject.FindProperty("maxMessageLength");
            openOnError = serializedObject.FindProperty("openOnError");
            toggleKey = serializedObject.FindProperty("toggleKey");
            icons = serializedObject.FindProperty("icons");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var console = (UPandaLogConsole)target;

            EditorGUILayout.HelpBox(
                "运行时日志面板：后台线程只负责入队，主线程批量消费并绘制。\n" +
                "容量超过上限时会淘汰最旧日志或丢弃新日志；不会阻塞业务线程。",
                MessageType.Info);

            EditorGUILayout.LabelField("容量限制", EditorStyles.boldLabel);
            DrawProperty(maxStoredEntries, "最多保留条数", "主线程最多保留的日志条数；超过后淘汰最旧记录。");
            DrawProperty(maxStoredBytes, "最多保留内存（字节）", "主线程日志的估算内存上限；超过后淘汰最旧记录。默认 8 MB。");
            DrawProperty(maxQueuedEntries, "待处理队列条数上限", "尚未消费的日志队列条数上限；队列满时丢弃新日志并累计 DroppedCount。");
            DrawProperty(maxQueuedBytes, "待处理队列内存（字节）", "尚未消费的日志队列估算内存上限；队列满时丢弃新日志。默认 4 MB。");
            DrawProperty(maxDrainPerFrame, "每帧最多消费条数", "每帧从队列消费的条数；越大越及时，但单帧耗时越高。");
            DrawProperty(maxMessageLength, "单条消息最大字符数", "单条消息/堆栈的最大长度；超出部分会截断，避免异常长文本占满内存。");
            EditorGUILayout.HelpBox(
                "“最多保留”限制已经进入主线程存储的日志；“待处理队列”限制后台线程尚未消费的日志。\n" +
                "两条链路都有条数与内存上限，避免日志风暴拖垮主线程。",
                MessageType.None);

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("行为", EditorStyles.boldLabel);
            DrawProperty(openOnError, "错误时自动打开", "主线程消费到 Error / Exception / Assert 时自动显示面板。");
            DrawProperty(toggleKey, "显示/隐藏快捷键", "可选快捷键；设为 None 表示只通过 API 或左上角 FPS 按钮打开。");

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("外观", EditorStyles.boldLabel);
            bool bound = console.Icons != null && console.Icons.clearImage != null;
            EditorGUILayout.HelpBox(
                "图标与滚动条皮肤为项目自有资产，位于本模块的 Icons 目录。\n" +
                "编辑器创建控制台时会自动绑定；缺失时视图会退回纯文字，不影响日志捕获。\n" +
                "当前状态：" + (bound ? "已绑定" : "未绑定 / 不完整"),
                bound ? MessageType.Info : MessageType.Warning);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("重新绑定模块图标", GUILayout.Height(24f)))
                {
                    Undo.RecordObject(console, "Bind Log Console Icons");
                    LogConsoleIconLoader.Assign(console);
                    EditorUtility.SetDirty(console);
                    serializedObject.Update();
                }
                if (GUILayout.Button(bound ? "查看图标引用" : "展开图标引用", GUILayout.Height(24f)))
                    showIconReferences = !showIconReferences;
            }

            if (showIconReferences && icons != null)
            {
                EditorGUI.indentLevel++;
                DrawProperty(icons, "图标 / GUISkin 引用", "通常由创建流程自动绑定；手动调整时请保持文件位于 Icons 目录。", true);
                EditorGUI.indentLevel--;
            }

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(6f);
            DrawRuntime(console);
        }

        private void DrawRuntime(UPandaLogConsole console)
        {
            EditorGUILayout.LabelField("运行状态", EditorStyles.boldLabel);
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("进入 Play Mode 后可查看实时计数并操作面板。", MessageType.None);
                return;
            }

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.LabelField("面板可见", console.IsVisible ? "是" : "否");
                EditorGUILayout.LabelField("保留日志", console.RetainedCount.ToString());
                EditorGUILayout.LabelField("待处理队列", console.PendingCount.ToString());
                EditorGUILayout.LabelField("已丢弃", console.DroppedCount.ToString());
                EditorGUILayout.LabelField("级别计数", "Info " + console.InfoCount + " / Warn " + console.WarningCount + " / Error " + console.ErrorCount);
                EditorGUILayout.LabelField("暂停捕获", console.PauseCapture ? "是" : "否");
                EditorGUILayout.LabelField("自动跟随", console.FollowTail ? "是" : "否");
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(console.IsVisible ? "隐藏面板" : "显示面板", GUILayout.Height(24f)))
                {
                    if (console.IsVisible) console.Hide();
                    else console.Show();
                }
                if (GUILayout.Button("清空日志", GUILayout.Height(24f))) console.Clear();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("打开显示设置", GUILayout.Height(24f))) console.OpenSettings();
                if (GUILayout.Button("打开框架诊断", GUILayout.Height(24f))) console.OpenDiagnostics();
            }
        }

        private static void DrawProperty(SerializedProperty property, string label, string tooltip, bool includeChildren = false)
        {
            if (property == null)
            {
                EditorGUILayout.HelpBox("字段缺失：" + label + "（运行时脚本可能已改名）", MessageType.Error);
                return;
            }
            EditorGUILayout.PropertyField(property, new GUIContent(label, tooltip), includeChildren);
        }
    }
}
