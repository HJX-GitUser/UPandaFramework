using System.IO;
using System.Text;
using UnityEngine;

namespace UPandaGF.RunTime.InteractiveTaskScoringSystem
{
    /// <summary>
    /// 示例：任务 HUD（演示用 OnGUI，正式项目请换成 UGUI/TMP）。
    /// 1) 订阅 TaskDataManager 的步骤事件（开始/完成/全部完成）显示进度与总分；
    /// 2) 订阅 EventCenter 的 TaskTipsInfoEvent 显示模块内部发出的提示（如"操作错误!!!"）；
    /// 3) 提供快捷键：G = 步骤引导，K = 跳过当前步骤。
    /// </summary>
    public class DemoTaskHud : MonoBehaviour
    {
        private string tipsText = "";
        private float tipsHideTime;
        private string lastEventLog = "";
        private GUIStyle boxStyle;
        private GUIStyle labelStyle;

        private void Start()
        {
            if (TaskDataManager.Instance == null)
            {
                Debug.LogError("[示例] 场景中缺少 TaskDataManager，HUD 无法工作");
                return;
            }
            TaskDataManager.Instance.OnStepStarted += HandleStepStarted;
            TaskDataManager.Instance.OnStepCompleted += HandleStepCompleted;
            TaskDataManager.Instance.OnTaskCompleted += HandleTaskCompleted;
            EventCenter.Instance.AddEventListener<TaskTipsInfoEvent>(HandleTips);
        }

        private void OnDestroy()
        {
            if (TaskDataManager.Instance != null)
            {
                TaskDataManager.Instance.OnStepStarted -= HandleStepStarted;
                TaskDataManager.Instance.OnStepCompleted -= HandleStepCompleted;
                TaskDataManager.Instance.OnTaskCompleted -= HandleTaskCompleted;
            }
            EventCenter.Instance.RemoveEventListener<TaskTipsInfoEvent>(HandleTips);
        }

        private void Update()
        {
            if (TaskDataManager.Instance == null) return;
            if (Input.GetKeyDown(KeyCode.G)) TaskDataManager.Instance.OperationInstructions();
            if (Input.GetKeyDown(KeyCode.K)) TaskDataManager.Instance.SkipTask();
            if (Input.GetKeyDown(KeyCode.S)) SaveReplay();
        }

        private void HandleStepStarted(TaskStepData step)
        {
            if (step == null) return;
            lastEventLog = $"步骤开始：{step.stepID}（基础分 {step.baseScore}）";
            ShowTips($"新步骤：{step.description}");
        }

        private void HandleStepCompleted(TaskStepData step)
        {
            if (step == null || TaskDataManager.Instance == null) return;
            // 运行状态已从配置资产移到 TaskStepRuntime（OnStepCompleted 触发时下标仍指向刚完成的步骤）
            TaskStepRuntime rt = TaskDataManager.Instance.GetRuntime(TaskDataManager.Instance.currentStepIndex);
            lastEventLog = rt != null
                ? $"步骤完成：{rt.stepID}，本步得分 {rt.score}，错误 {rt.errors} 次"
                : $"步骤完成：{step.stepID}";
        }

        private void HandleTaskCompleted()
        {
            lastEventLog = "全部步骤完成，任务结束";
        }

        private void HandleTips(TaskTipsInfoEvent evt)
        {
            if (evt == null) return;
            ShowTips(evt.info);
        }

        private void ShowTips(string text)
        {
            tipsText = text;
            tipsHideTime = Time.time + 3f;
        }

        /// <summary>
        /// P2：保存动作级回放日志（走 TaskDataManager.SaveReplayLog，默认落到 StreamingAssets）。
        /// 保存后可用菜单「UPandaGF/交互任务评分系统/回放复盘查看器」载入复盘。
        /// </summary>
        private void SaveReplay()
        {
            string path = TaskDataManager.Instance.SaveReplayLog();
            if (string.IsNullOrEmpty(path))
            {
                ShowTips("回放日志保存失败（没有日志或写入被拒）");
                return;
            }
            ShowTips("回放日志已保存：" + Path.GetFileName(path));
            Debug.Log("[示例] 回放日志路径：" + path);
        }

        private void OnGUI()
        {
            if (boxStyle == null)
            {
                boxStyle = new GUIStyle(GUI.skin.box) { fontSize = 16, alignment = TextAnchor.UpperLeft, richText = true };
                labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, richText = true, wordWrap = true };
            }

            var mgr = TaskDataManager.Instance;
            StringBuilder sb = new StringBuilder();
            if (mgr == null)
            {
                sb.AppendLine("未找到 TaskDataManager");
            }
            else
            {
                sb.AppendLine($"总分：{mgr.totalScore:F1}");
                if (mgr.scorePolicy != null) sb.AppendLine($"评分策略：{mgr.scorePolicy.Description}");
                TaskStepData cur = mgr.currentTaskStep;
                TaskStepRuntime rt = mgr.GetRuntime(mgr.currentStepIndex);
                if (cur != null)
                {
                    sb.AppendLine($"当前步骤：{cur.stepID}  {cur.description}");
                    sb.AppendLine(rt != null
                        ? $"本步得分：{rt.score:F1} / {cur.baseScore}   错误次数：{rt.errors}"
                        : $"本步基础分：{cur.baseScore}");
                    sb.AppendLine($"已完成：{CountCompleted(mgr)} / {CountTotal(mgr)}");
                }
                else
                {
                    sb.AppendLine("全部步骤已完成");
                }
            }
            sb.AppendLine("-----------------------------");
            sb.AppendLine("左键点击方块完成操作");
            sb.AppendLine("G = 步骤引导   K = 跳过当前步骤   S = 保存回放日志");
            if (!string.IsNullOrEmpty(lastEventLog)) sb.AppendLine("事件：" + lastEventLog);
            if (Time.time < tipsHideTime) sb.AppendLine("提示：" + tipsText);

            GUI.Box(new Rect(10, 10, 520, 250), GUIContent.none, boxStyle);
            GUI.Label(new Rect(22, 18, 496, 234), sb.ToString(), labelStyle);
        }

        private static int CountTotal(TaskDataManager mgr)
        {
            TaskStepBase[] steps = mgr.GetComponentsInChildren<TaskStepBase>();
            return steps == null ? 0 : steps.Length;
        }

        private static int CountCompleted(TaskDataManager mgr)
        {
            TaskStepBase[] steps = mgr.GetComponentsInChildren<TaskStepBase>();
            if (steps == null) return 0;
            int n = 0;
            foreach (var s in steps)
            {
                if (s != null && s.Runtime != null && s.Runtime.isCompleted) n++;
            }
            return n;
        }
    }
}
