using System;
using System.Collections.Generic;

namespace UPandaGF.RunTime.TaskSystem
{
    /// <summary>单个目标的存档记录。</summary>
    [Serializable]
    public class QuestObjectiveSaveRecord
    {
        public string objectiveId;
        public int currentAmount;
    }

    /// <summary>单个任务的存档记录。</summary>
    [Serializable]
    public class QuestSaveRecord
    {
        public string questId;
        public QuestStatus status;
        public bool isTracked;
        public int acceptedOrder;
        public int completionCount;
        public List<QuestObjectiveSaveRecord> objectives = new List<QuestObjectiveSaveRecord>();
    }

    /// <summary>
    /// 整份存档（JsonUtility 可序列化：只用公共字段 + List，不用 Dictionary）。
    /// 通过 QuestManager.SaveToJson / LoadFromJson 读写。
    /// </summary>
    [Serializable]
    public class QuestSaveData
    {
        /// <summary>存档格式版本，将来结构变化时用于兼容旧档。</summary>
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        public string savedAt = "";
        public List<QuestSaveRecord> quests = new List<QuestSaveRecord>();
    }
}
