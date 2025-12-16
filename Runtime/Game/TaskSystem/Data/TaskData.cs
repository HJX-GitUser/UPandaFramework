using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UPandaGF.RunTime.TaskSystem
{
    /// <summary>
    /// 任务数据
    /// </summary>
    [System.Serializable]
    public class TaskData
    {
        public string taskID;
        public string taskName;
        public string taskDescription;
        public TaskReward taskReward;
    }
}

