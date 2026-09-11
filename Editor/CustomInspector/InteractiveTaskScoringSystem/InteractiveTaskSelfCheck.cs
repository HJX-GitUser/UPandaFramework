using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace UPandaGF.RunTime.InteractiveTaskScoringSystem
{
    /// <summary>
    /// 纯逻辑自检（菜单：UPandaGF/交互任务评分系统/运行逻辑自检）。
    ///
    /// 背景：本项目未安装 com.unity.test-framework，无法用 NUnit/Test Runner；
    /// 因此把 P1 抽出的纯逻辑（评分策略 / 步骤运行状态 / 任务进度）直接在这里断言一遍，
    /// 一键就能验证核心规则没被改坏。等以后装了 Test Runner，这些断言可原样搬成单元测试。
    /// </summary>
    public static class InteractiveTaskSelfCheck
    {
        [MenuItem("UPandaGF/交互任务评分系统/运行逻辑自检", false, 22)]
        public static void Run()
        {
            int total = 0;
            List<string> fails = new List<string>();

            // ---------- 1. 评分策略 ----------
            IScorePolicy def = new DefaultScorePolicy();
            Check(() => def.CalcScore(10f, 0) == 10f, "默认策略：0 错 = 满分", fails, ref total);
            Check(() => def.CalcScore(10f, 1) == 5f, "默认策略：1 错 = 半分", fails, ref total);
            Check(() => def.CalcScore(10f, 2) == 0f, "默认策略：2 错 = 0 分", fails, ref total);
            Check(() => def.CalcScore(10f, 9) == 0f, "默认策略：多次错仍为 0 分", fails, ref total);
            Check(() => def.CalcScore(0f, 0) == 0f, "默认策略：基础分 0 时结果 0", fails, ref total);

            LinearPenaltyScorePolicy lin = new LinearPenaltyScorePolicy { penaltyPerError = 5f };
            Check(() => lin.CalcScore(10f, 0) == 10f, "线性策略：0 错 = 满分", fails, ref total);
            Check(() => lin.CalcScore(10f, 1) == 5f, "线性策略：1 错扣 5 分", fails, ref total);
            Check(() => lin.CalcScore(10f, 3) == 0f, "线性策略：扣到负数时钳制为 0", fails, ref total);

            // ---------- 2. 步骤运行状态 ----------
            TaskStepRuntime rt = new TaskStepRuntime("Step1", 10f);
            Check(() => rt.stepID == "Step1" && rt.score == 10f, "Runtime：初始分 = 基础分", fails, ref total);
            Check(() => rt.errors == 0 && !rt.isCompleted && !rt.isSkip, "Runtime：初始标记为未完成/未跳过", fails, ref total);
            rt.errors = 2;
            rt.isSkip = true;
            rt.isCompleted = true;
            rt.beginTime = 1f;
            rt.endTime = 3f;
            Check(() => rt.Duration == 2f, "Runtime：Duration = endTime - beginTime", fails, ref total);
            rt.Reset("Step1", 20f);
            Check(() => rt.errors == 0 && rt.score == 20f && !rt.isCompleted && !rt.isSkip && rt.Duration == 0f,
                "Runtime：Reset 清空错误/完成/跳过并恢复基础分", fails, ref total);

            // 可存档性：JsonUtility 往返（存档只需序列化 Runtime，不动配置资产）
            string json = JsonUtility.ToJson(rt);
            TaskStepRuntime back = JsonUtility.FromJson<TaskStepRuntime>(json);
            Check(() => back != null && back.stepID == "Step1" && back.score == 20f, "Runtime：JsonUtility 往返可用（支持存档）", fails, ref total);

            // ---------- 3. 任务进度 ----------
            TaskProgress p = new TaskProgress();
            p.Init(3);
            Check(() => p.stepCount == 3 && p.currentIndex == 0 && p.totalScore == 0f && !p.isFinished,
                "Progress：Init 回到第 0 步、总分 0、未完成", fails, ref total);
            Check(() => p.HasStep(0) && p.HasStep(2) && !p.HasStep(3) && !p.HasStep(-1), "Progress：HasStep 边界正确", fails, ref total);
            p.AddScore(10f);
            p.AddScore(0f);
            Check(() => p.totalScore == 10f, "Progress：AddScore 累加（含 0 分步骤）", fails, ref total);
            Check(() => p.MoveNext() && p.currentIndex == 1, "Progress：MoveNext 推进到第 1 步", fails, ref total);
            Check(() => p.MoveNext() && p.currentIndex == 2, "Progress：MoveNext 推进到第 2 步", fails, ref total);
            Check(() => !p.MoveNext() && p.currentIndex == 3 && p.isFinished, "Progress：最后一步后返回 false 并置为完成", fails, ref total);
            Check(() => !p.MoveNext() && p.currentIndex == 3, "Progress：已完成后再推进保持完成态（幂等）", fails, ref total);
            p.Init(1);
            Check(() => p.currentIndex == 0 && !p.isFinished && !p.MoveNext() && p.isFinished, "Progress：单步任务能正常完成", fails, ref total);
            p.Init(0);
            Check(() => p.isFinished && p.stepCount == 0, "Progress：0 步任务视为已完成（不会越界）", fails, ref total);

            // ---------- 结果 ----------
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"交互任务评分系统 · 逻辑自检：{total - fails.Count} / {total} 项通过");
            if (fails.Count > 0)
            {
                sb.AppendLine(new string('-', 40));
                foreach (string f in fails) sb.AppendLine("× " + f);
            }
            string report = sb.ToString();
            if (fails.Count == 0) Debug.Log("[交互任务自检] " + report);
            else Debug.LogError("[交互任务自检] " + report);
            EditorUtility.DisplayDialog("交互任务评分系统 · 逻辑自检", report, "确定");
        }

        private static void Check(System.Func<bool> assert, string name, List<string> fails, ref int total)
        {
            total++;
            bool ok;
            try { ok = assert(); }
            catch { ok = false; }
            if (!ok) fails.Add(name);
        }
    }
}
