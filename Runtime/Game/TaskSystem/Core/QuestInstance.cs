using System;
using System.Collections.Generic;
using UnityEngine;

namespace UPandaGF.RunTime.TaskSystem
{
    /// <summary>
    /// 任务「运行时实例」：保存当前状态与各目标进度。
    /// 只读取 QuestDefinition（模板），绝不写回模板 —— 模板是多个存档共享的资产。
    /// </summary>
    public class QuestInstance
    {
        /// <summary>各目标的进度（顺序与 definition.objectives 一致）。</summary>
        private readonly List<QuestObjectiveProgress> objectives = new List<QuestObjectiveProgress>();

        // ------------------------------ 运行态字段 ------------------------------

        /// <summary>模板引用（只读）。</summary>
        public QuestDefinition definition;

        public QuestStatus status = QuestStatus.Locked;

        /// <summary>是否被 HUD 追踪（HUD 只显示被追踪的活跃任务）。</summary>
        public bool isTracked;

        /// <summary>接受顺序，HUD 排序用（越小越早接受）。</summary>
        public int acceptedOrder;

        /// <summary>提交次数（可重复任务会累加）。</summary>
        public int completionCount;

        public QuestInstance(QuestDefinition definition)
        {
            this.definition = definition;
            BuildObjectives();
        }

        // ---------------------- 便捷访问（全部转发到模板，避免调用方判空） ----------------------

        public string QuestId { get { return definition == null ? string.Empty : definition.GetQuestId(); } }
        public string Title { get { return definition == null ? "（空任务）" : definition.title; } }
        public string Description { get { return definition == null ? string.Empty : definition.description; } }
        public QuestType Type { get { return definition == null ? QuestType.Side : definition.questType; } }
        public QuestReward Reward { get { return definition == null ? null : definition.reward; } }
        public bool IsRepeatable { get { return definition != null && definition.isRepeatable; } }

        /// <summary>目标进度列表（只读用途；不要直接 Clear/Add）。</summary>
        public List<QuestObjectiveProgress> Objectives { get { return objectives; } }

        // ------------------------------- 状态查询 -------------------------------

        public bool IsActive { get { return status == QuestStatus.Active; } }

        /// <summary>是否已经走完流程（可提交或已提交）。</summary>
        public bool IsFinished
        {
            get { return status == QuestStatus.Completed || status == QuestStatus.TurnedIn; }
        }

        public int TotalObjectiveCount { get { return objectives.Count; } }

        public int CompletedObjectiveCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < objectives.Count; i++)
                {
                    if (objectives[i].IsCompleted) count++;
                }
                return count;
            }
        }

        /// <summary>所有目标进度平均值（0~1），HUD 进度条用。</summary>
        public float Progress01
        {
            get
            {
                if (objectives.Count == 0) return 1f;
                float sum = 0f;
                for (int i = 0; i < objectives.Count; i++) sum += objectives[i].Progress01;
                return sum / objectives.Count;
            }
        }

        public bool AreAllObjectivesCompleted
        {
            get
            {
                if (objectives.Count == 0) return true;
                for (int i = 0; i < objectives.Count; i++)
                {
                    if (!objectives[i].IsCompleted) return false;
                }
                return true;
            }
        }

        /// <summary>形如 "2/3"，给列表用。</summary>
        public string GetProgressSummary()
        {
            return CompletedObjectiveCount + "/" + TotalObjectiveCount;
        }

        // ============================ 以下仅由 QuestManager 调用 ============================

        /// <summary>按模板重建目标进度（实例创建 / 重置时用）。</summary>
        internal void BuildObjectives()
        {
            objectives.Clear();
            if (definition == null || definition.objectives == null) return;
            for (int i = 0; i < definition.objectives.Count; i++)
            {
                QuestObjective objective = definition.objectives[i];
                if (objective == null) continue;
                objectives.Add(objective.CreateProgress());
            }
        }

        /// <summary>清零所有目标进度（重新接受任务时调用）。</summary>
        internal void ResetProgress()
        {
            for (int i = 0; i < objectives.Count; i++) objectives[i].currentAmount = 0;
        }

        /// <summary>
        /// 推进目标进度：把所有「类型 + 目标 ID 匹配」且未完成的目标加 amount。
        /// changedOut 会收集本次真正发生变化的目标（QuestManager 用它派发 UI 事件，避免无谓刷新）。
        /// </summary>
        internal void ApplyProgress(ObjectiveType type, string targetId, int amount, List<QuestObjectiveProgress> changedOut)
        {
            if (amount <= 0) return;
            for (int i = 0; i < objectives.Count; i++)
            {
                QuestObjectiveProgress objective = objectives[i];
                if (objective.IsCompleted) continue;                            // 已达成的目标不再累加
                if (objective.type != type) continue;                           // 类型不符
                if (!IsTargetMatch(objective.targetId, targetId)) continue;     // 目标 ID 不符

                int before = objective.currentAmount;
                objective.currentAmount = Mathf.Min(objective.currentAmount + amount, objective.requiredAmount);
                if (objective.currentAmount != before && changedOut != null) changedOut.Add(objective);
            }
        }

        /// <summary>targetId 为空或 "*" 表示任意目标；否则不区分大小写比较。</summary>
        private static bool IsTargetMatch(string required, string actual)
        {
            if (string.IsNullOrEmpty(required) || required == "*") return true;
            return string.Equals(required, actual, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>导出存档记录。</summary>
        internal QuestSaveRecord ToSaveRecord()
        {
            QuestSaveRecord record = new QuestSaveRecord();
            record.questId = QuestId;
            record.status = status;
            record.isTracked = isTracked;
            record.acceptedOrder = acceptedOrder;
            record.completionCount = completionCount;
            for (int i = 0; i < objectives.Count; i++)
            {
                QuestObjectiveSaveRecord objectiveRecord = new QuestObjectiveSaveRecord();
                objectiveRecord.objectiveId = objectives[i].objectiveId;
                objectiveRecord.currentAmount = objectives[i].currentAmount;
                record.objectives.Add(objectiveRecord);
            }
            return record;
        }

        /// <summary>
        /// 套用存档记录。优先按 objectiveId 对齐；找不到时退化为按下标对齐（兼容改过 ID 的旧档）。
        /// </summary>
        internal void ApplySaveRecord(QuestSaveRecord record)
        {
            if (record == null) return;
            status = record.status;
            isTracked = record.isTracked;
            acceptedOrder = record.acceptedOrder;
            completionCount = record.completionCount;

            if (record.objectives == null) return;
            for (int i = 0; i < record.objectives.Count; i++)
            {
                QuestObjectiveSaveRecord objectiveRecord = record.objectives[i];
                QuestObjectiveProgress target = null;

                for (int j = 0; j < objectives.Count; j++)
                {
                    if (objectives[j].objectiveId == objectiveRecord.objectiveId) { target = objectives[j]; break; }
                }
                if (target == null && i < objectives.Count) target = objectives[i];
                if (target == null) continue;

                target.currentAmount = Mathf.Clamp(objectiveRecord.currentAmount, 0, target.requiredAmount);
            }
        }
    }
}
