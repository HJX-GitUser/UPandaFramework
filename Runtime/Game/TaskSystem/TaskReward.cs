using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UPandaGF.RunTime.TaskSystem
{
    // 定义任务奖励类型的枚举
    public enum TaskRewardType
    {
        None,//无
        Gold,//钱
        Item,//物品
        Experience,//经验
        Score//分数
    }

    /// <summary>
    /// 任务奖励
    /// </summary>
    [System.Serializable]
    public class TaskReward
    {
        public TaskRewardType rewardName;
        public string value;
    }
}

