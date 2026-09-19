using System;
using UnityEngine;

namespace UPandaGF.RunTime.TaskSystem
{
    /// <summary>
    /// 任务目标「定义」：由策划在 QuestDefinition（ScriptableObject）里配置，运行时只读。
    /// </summary>
    [Serializable]
    public class QuestObjective
    {
        [Tooltip("目标 ID，唯一即可（留空时自动用 类型_targetId 生成），存档按它对齐。")]
        public string objectiveId = "";

        [Tooltip("目标类型：击杀 / 收集 / 到达 / 对话。")]
        public ObjectiveType type = ObjectiveType.Kill;

        [Tooltip("Kill = 敌人ID；Collect = 物品ID；ReachLocation = 地点ID；TalkToNPC = NPC ID。填 * 表示任意目标都算。")]
        public string targetId = "";

        [Tooltip("需要达成的数量，至少为 1。")]
        public int requiredAmount = 1;

        [TextArea(1, 3)]
        [Tooltip("给玩家看的描述，留空则用「类型 目标 (0/需求)」自动生成。")]
        public string description = "";

        /// <summary>由定义创建一个运行进度对象（拷贝必要字段，便于独立存档）。</summary>
        public QuestObjectiveProgress CreateProgress()
        {
            QuestObjectiveProgress progress = new QuestObjectiveProgress();
            progress.objectiveId = GetObjectiveId();
            progress.type = type;
            progress.targetId = targetId;
            progress.requiredAmount = Mathf.Max(1, requiredAmount);
            progress.description = description;
            progress.currentAmount = 0;
            return progress;
        }

        public string GetObjectiveId()
        {
            if (!string.IsNullOrEmpty(objectiveId)) return objectiveId;
            return type + "_" + targetId;
        }
    }

    /// <summary>
    /// 目标「运行进度」：挂在 QuestInstance 上，是唯一会被存档写入的对象。
    /// 这里冗余保存了 type/targetId/requiredAmount，是为了读档时不必查定义也能还原 UI。
    /// </summary>
    [Serializable]
    public class QuestObjectiveProgress
    {
        public string objectiveId;
        public ObjectiveType type;
        public string targetId;
        public int requiredAmount = 1;
        public int currentAmount;
        public string description;

        public bool IsCompleted
        {
            get { return currentAmount >= requiredAmount; }
        }

        /// <summary>0~1 的完成度，供进度条使用。</summary>
        public float Progress01
        {
            get { return requiredAmount <= 0 ? 1f : Mathf.Clamp01((float)currentAmount / requiredAmount); }
        }

        public string GetProgressText()
        {
            return currentAmount + "/" + requiredAmount;
        }

        /// <summary>UI 显示的整行文本。</summary>
        public string GetDisplayText()
        {
            if (!string.IsNullOrEmpty(description)) return description;
            return QuestText.GetObjectiveTypeName(type) + " " + targetId + " (" + GetProgressText() + ")";
        }
    }
}
