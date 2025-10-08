using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace UPandaGF
{
    // 任务状态枚举
    public enum TaskStatus
    {
        Inactive,
        InProgress,
        Completed,
        Failed
    }

    /// <summary>
    /// 任务类
    /// </summary>
    public class Task
    {
        public TaskData taskData;
        /// <summary>
        /// 任务类型
        /// </summary>
        public string taskType;
        public TaskStatus status;
        public TaskConditionHanderBase<TaskConditionDataBase> taskCondition;
    }
}


