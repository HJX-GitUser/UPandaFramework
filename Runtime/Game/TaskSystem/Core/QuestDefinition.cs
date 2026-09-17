using System.Collections.Generic;
using UnityEngine;

namespace UPandaGF.RunTime.TaskSystem
{
    /// <summary>
    /// 任务模板（ScriptableObject）。
    /// 存放在任意 Resources 目录下的 <see cref="ResourceFolder"/> 子目录中即可被 QuestManager 自动加载，
    /// 例如：Assets/xxx/Resources/Quests/Quest_KillWolves.asset。
    /// 运行时不修改本对象，进度全部记录在 QuestInstance 上。
    /// </summary>
    [CreateAssetMenu(fileName = "Quest_New", menuName = "UPandaGF/任务系统/任务定义 QuestDefinition", order = 0)]
    public class QuestDefinition : ScriptableObject
    {
        /// <summary>Resources 下的子目录名（QuestManager 会 LoadAll 这个目录）。</summary>
        public const string ResourceFolder = "Quests";

        [Header("基础信息")]
        [Tooltip("任务唯一 ID，用于存档与代码引用；留空则用资产名。")]
        public string questId = "";
        public string title = "新任务";
        [TextArea(2, 6)]
        public string description = "";
        public QuestType questType = QuestType.Side;

        [Header("流程控制")]
        [Tooltip("勾选后：前置满足时自动接受，无需玩家点接受。")]
        public bool autoStart = false;
        [Tooltip("勾选后：提交后可反复接受（日常任务）。")]
        public bool isRepeatable = false;
        [Tooltip("前置任务 ID 列表；这些任务处于「已完成/已提交」后，本任务才会变为可接受。")]
        public List<string> prerequisites = new List<string>();

        [Header("任务目标（全部达成才算完成）")]
        public List<QuestObjective> objectives = new List<QuestObjective>();

        [Header("奖励")]
        public QuestReward reward = new QuestReward();

        /// <summary>取有效 ID：优先 questId，为空时退化为资产名。</summary>
        public string GetQuestId()
        {
            return string.IsNullOrEmpty(questId) ? name : questId;
        }

        /// <summary>给编辑器工具 / 加载时做校验用；返回 false 表示该任务配置有误。</summary>
        public bool ValidateQuest(List<string> errors)
        {
            bool ok = true;
            if (string.IsNullOrEmpty(GetQuestId()))
            {
                ok = false;
                if (errors != null) errors.Add("questId 为空");
            }
            if (objectives == null || objectives.Count == 0)
            {
                ok = false;
                if (errors != null) errors.Add("没有任何任务目标");
            }
            else
            {
                for (int i = 0; i < objectives.Count; i++)
                {
                    QuestObjective objective = objectives[i];
                    if (objective == null)
                    {
                        ok = false;
                        if (errors != null) errors.Add("目标 " + i + " 为 null");
                        continue;
                    }
                    if (string.IsNullOrEmpty(objective.targetId) && objective.type != ObjectiveType.ReachLocation)
                    {
                        // ReachLocation 允许留空（表示只看类型），其它类型建议填 targetId
                        if (errors != null) errors.Add("目标 " + i + " 的 targetId 为空（将匹配任意目标）");
                    }
                    if (objective.requiredAmount <= 0)
                    {
                        ok = false;
                        if (errors != null) errors.Add("目标 " + i + " 的 requiredAmount 必须 ≥ 1");
                    }
                }
            }
            return ok;
        }

#if UNITY_EDITOR
        /// <summary>编辑器里改了资产名就自动补上 questId，避免手填遗漏。</summary>
        private void OnValidate()
        {
            if (string.IsNullOrEmpty(questId)) questId = name;
            if (reward == null) reward = new QuestReward();
            if (objectives == null) objectives = new List<QuestObjective>();
            if (prerequisites == null) prerequisites = new List<string>();
        }
#endif
    }
}
