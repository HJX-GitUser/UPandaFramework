using System.Collections.Generic;
using System.IO;
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
        [MenuItem("UPandaGF/Runtime/交互任务评分系统/运行逻辑自检")]
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

            // ---------- 4. 难度分级 / 超时扣分（P2） ----------
            DifficultyScorePolicy easy = DifficultyScorePolicy.Create(TaskDifficulty.Easy);
            Check(() => easy.CalcScore(10f, 0) == 10f, "难度 Easy：0 错满分", fails, ref total);
            Check(() => easy.CalcScore(10f, 1) == 9f, "难度 Easy：1 错扣 10%", fails, ref total);
            Check(() => easy.CalcScore(10f, 9) == 7f, "难度 Easy：超出系数表后取最后一项（保底 70%）", fails, ref total);

            DifficultyScorePolicy normal = DifficultyScorePolicy.Create(TaskDifficulty.Normal);
            Check(() => normal.CalcScore(10f, 0) == 10f && normal.CalcScore(10f, 1) == 5f && normal.CalcScore(10f, 2) == 0f,
                "难度 Normal：与默认策略三档一致", fails, ref total);

            DifficultyScorePolicy hard = DifficultyScorePolicy.Create(TaskDifficulty.Hard);
            Check(() => hard.CalcScore(10f, 0) == 10f && hard.CalcScore(10f, 1) == 0f, "难度 Hard：错一次即 0 分", fails, ref total);
            Check(() => DifficultyScorePolicy.GetRateTable(TaskDifficulty.Easy).Length == 4
                && DifficultyScorePolicy.GetRateTable(TaskDifficulty.Normal).Length == 3
                && DifficultyScorePolicy.GetRateTable(TaskDifficulty.Hard)[0] == 1f,
                "难度：系数表按难度返回（且为副本）", fails, ref total);

            DifficultyScorePolicy timed = DifficultyScorePolicy.Create(TaskDifficulty.Normal, 10f, 2f);
            ScoreContext inTime = new ScoreContext { baseScore = 10f, errors = 0, duration = 8f };
            ScoreContext overTime = new ScoreContext { baseScore = 10f, errors = 0, duration = 13f };
            ScoreContext wayOver = new ScoreContext { baseScore = 10f, errors = 1, duration = 100f };
            Check(() => timed.CalcScore(inTime) == 10f, "超时扣分：未超时不扣", fails, ref total);
            Check(() => timed.CalcScore(overTime) == 4f, "超时扣分：超过 3 秒每秒扣 2 分", fails, ref total);
            Check(() => timed.CalcScore(wayOver) == 0f, "超时扣分：扣成负数时钳制为 0", fails, ref total);
            Check(() => !string.IsNullOrEmpty(timed.Description) && timed.Description.Contains("Normal"),
                "难度：Description 可读（供 UI 展示）", fails, ref total);

            IScorePolicy asBase = timed;   // 向后兼容：增强策略仍能当普通策略用
            Check(() => asBase.CalcScore(10f, 1) == 5f, "难度策略：旧签名（无耗时）等价 duration = 0", fails, ref total);

            // ---------- 5. 回放日志（P2） ----------
            ReplayLog rl = new ReplayLog("DemoTask", 5f);
            Check(() => rl.Count == 0 && rl.taskName == "DemoTask" && !string.IsNullOrEmpty(rl.createTime),
                "回放：新建日志为空且带任务名/创建时间", fails, ref total);
            rl.Add(5.5f, "Step1", "Step_Enter", true, "进入步骤", 10f, 0);
            rl.Add(6.0f, "Step1", "Step_Error", false, "第 1 次错误", 10f, 1);
            rl.Add(8.5f, "Step1", "Step_Complete", true, "完成步骤", 5f, 1);
            Check(() => rl.Count == 3, "回放：Add 追加记录", fails, ref total);
            Check(() => Mathf.Abs(rl.entries[2].offset - 3.5f) < 0.0001f, "回放：offset = 动作时间 - 任务开始时间", fails, ref total);
            Check(() => Mathf.Abs(rl.Duration - 3.5f) < 0.0001f, "回放：Duration = 最后一条 - 开始时间", fails, ref total);
            Check(() => rl.ErrorCount == 1, "回放：ErrorCount 统计 Step_Error", fails, ref total);
            Check(() => rl.FilterByStep("Step1").Count == 3 && rl.FilterByStep("StepX").Count == 0, "回放：按步骤筛选", fails, ref total);
            Check(() => rl.BuildTimeline().Length == 3, "回放：时间轴行数 = 记录数", fails, ref total);
            Check(() => rl.ToCsv().Split('\n').Length >= 4, "回放：CSV 含表头 + 3 行数据", fails, ref total);

            string rlJson = JsonUtility.ToJson(rl, true);
            ReplayLog rlBack = JsonUtility.FromJson<ReplayLog>(rlJson);
            Check(() => rlBack != null && rlBack.Count == 3 && rlBack.entries[1].action == "Step_Error" && !rlBack.entries[1].isCorrect,
                "回放：JsonUtility 往返保持记录与字段", fails, ref total);

            string rlPath = Path.Combine(Path.GetTempPath(), "itsa_selfcheck_replay.json");
            File.WriteAllText(rlPath, rlJson);
            ReplayLog rlLoaded = TaskDataManager.LoadReplayLog(rlPath);
            Check(() => rlLoaded != null && rlLoaded.Count == 3 && rlLoaded.taskName == "DemoTask",
                "回放：LoadReplayLog 能从文件读回（落盘/复盘链路可用）", fails, ref total);
            if (File.Exists(rlPath)) File.Delete(rlPath);

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
