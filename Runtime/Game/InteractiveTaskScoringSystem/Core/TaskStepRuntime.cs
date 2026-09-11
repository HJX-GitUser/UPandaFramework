using System;

namespace UPandaGF.RunTime.InteractiveTaskScoringSystem
{
    /// <summary>
    /// 单个步骤的“运行状态”（纯 C# 类，与配置资产完全分离）。
    ///
    /// 为什么分离：原先错误次数/得分/完成/跳过 这 4 个状态写在 <see cref="TaskStepData"/>（ScriptableObject）上，
    /// 会带来三个问题：
    ///   1) 重玩同一任务时状态残留（错误累加、isSkip 残留导致分数不计入总分）；
    ///   2) 同一份配置资产无法并行跑多个任务实例；
    ///   3) 无法干净地做存档/回放（状态与只读配置混在一起）。
    /// 现在这些状态由本类承载：每个步骤每次 <c>Init</c> 都会创建/复位一份，配置资产始终只读。
    /// </summary>
    [Serializable]
    public class TaskStepRuntime
    {
        public string stepID;       // 对应配置里的 stepID（便于存档/回放对齐）
        public int errors;          // 本步错误次数
        public float score;         // 本步得分
        public bool isCompleted;    // 是否已完成
        public bool isSkip;         // 是否被跳过（跳过的步骤不计入总分）
        public float beginTime;     // 进入步骤的时间（Time.time）
        public float endTime;       // 完成/离开步骤的时间

        public TaskStepRuntime() { }

        public TaskStepRuntime(string stepID, float baseScore)
        {
            Reset(stepID, baseScore);
        }

        /// <summary>复位为“未开始”状态；初始分 = 基础分（与原行为一致）</summary>
        public void Reset(string stepID, float baseScore)
        {
            this.stepID = stepID;
            errors = 0;
            score = baseScore;
            isCompleted = false;
            isSkip = false;
            beginTime = 0f;
            endTime = 0f;
        }

        /// <summary>该步骤耗时（秒）；未完成时为 0</summary>
        public float Duration
        {
            get { return endTime > beginTime ? endTime - beginTime : 0f; }
        }

        public override string ToString()
        {
            return $"{stepID} score={score} errors={errors} completed={isCompleted} skip={isSkip}";
        }
    }
}
