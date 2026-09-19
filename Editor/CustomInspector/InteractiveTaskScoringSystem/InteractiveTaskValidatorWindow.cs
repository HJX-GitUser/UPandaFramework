using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UPandaGF.RunTime.InteractiveTaskScoringSystem
{
    /// <summary>
    /// 任务流程校验器窗口（P0 工具）：
    /// 一键体检当前场景的任务配置，问题按 错误/警告/提示 分级，
    /// 点击条目可定位到对应对象，可自动修复项直接点「修复」或在工具栏「一键修复」。
    /// </summary>
    public class InteractiveTaskValidatorWindow : EditorWindow
    {
        private List<TaskIssue> issues = new List<TaskIssue>();
        private int errorCount;
        private int warningCount;
        private bool hasResult;
        private string summary = "点击「校验当前场景」开始体检";
        private Vector2 scroll;

        [MenuItem("UPandaGF/Runtime/交互任务评分系统/任务流程校验器")]
        public static void Open()
        {
            InteractiveTaskValidatorWindow window = GetWindow<InteractiveTaskValidatorWindow>("交互任务校验");
            window.minSize = new Vector2(560, 340);
            window.Validate();
        }

        private void Validate()
        {
            issues = InteractiveTaskValidator.ValidateScene(out errorCount, out warningCount);
            hasResult = true;
            summary = $"错误 {errorCount} · 警告 {warningCount} · 提示 {InteractiveTaskValidator.Count(issues, TaskIssueLevel.Info)}";
            Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("校验当前场景", EditorStyles.toolbarButton, GUILayout.Width(100)))
                Validate();
            if (GUILayout.Button("一键修复（ID→属性）", EditorStyles.toolbarButton, GUILayout.Width(150)))
            {
                summary = InteractiveTaskValidator.OneClickFix();
                issues = InteractiveTaskValidator.ValidateScene(out errorCount, out warningCount);
                hasResult = true;
            }
            if (GUILayout.Button("复制报告", EditorStyles.toolbarButton, GUILayout.Width(80)))
            {
                EditorGUIUtility.systemCopyBuffer = InteractiveTaskValidator.BuildReport(issues, errorCount, warningCount);
                Debug.Log("[交互任务校验] 报告已复制到剪贴板");
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            MessageType summaryType = MessageType.None;
            if (hasResult && errorCount > 0) summaryType = MessageType.Error;
            else if (hasResult && warningCount > 0) summaryType = MessageType.Warning;
            else if (hasResult) summaryType = MessageType.Info;
            EditorGUILayout.HelpBox(hasResult ? summary : "点击工具栏「校验当前场景」开始体检", summaryType);
            EditorGUILayout.LabelField("说明：错误 = 运行时会失败或流程失效；警告 = 能跑但行为不符预期；提示 = 可忽略。点击条目可定位对象。", EditorStyles.miniLabel);
            EditorGUILayout.Space();

            scroll = EditorGUILayout.BeginScrollView(scroll);
            for (int i = 0; i < issues.Count; i++)
            {
                TaskIssue issue = issues[i];
                EditorGUILayout.BeginHorizontal();

                Color old = GUI.color;
                GUI.color = LevelColor(issue.level);
                GUILayout.Label(InteractiveTaskValidator.LevelText(issue.level), EditorStyles.boldLabel, GUILayout.Width(36));
                GUI.color = old;

                if (GUILayout.Button(issue.message, EditorStyles.label))
                    Select(issue.context);

                if (issue.fix != null)
                {
                    string label = string.IsNullOrEmpty(issue.fixLabel) ? "修复" : issue.fixLabel;
                    if (GUILayout.Button(label, GUILayout.Width(70)))
                    {
                        issue.fix();
                        Validate();
                        GUIUtility.ExitGUI();   // 列表内容已变化，本帧结束布局
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            if (hasResult && issues.Count == 0)
                EditorGUILayout.HelpBox("没有发现问题，配置是自洽的。", MessageType.Info);
            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUILayout.LabelField($"共 {issues.Count} 条", EditorStyles.miniLabel, GUILayout.Width(80));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private static void Select(UnityEngine.Object target)
        {
            if (target == null) return;
            Selection.activeObject = target;
            EditorGUIUtility.PingObject(target);
        }

        private static Color LevelColor(TaskIssueLevel level)
        {
            switch (level)
            {
                case TaskIssueLevel.Error: return new Color(1f, 0.45f, 0.4f);
                case TaskIssueLevel.Warning: return new Color(1f, 0.8f, 0.35f);
                default: return new Color(0.65f, 0.8f, 1f);
            }
        }
    }
}
