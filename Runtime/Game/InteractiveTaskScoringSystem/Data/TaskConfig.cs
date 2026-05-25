using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UPandaGF.RunTime.InteractiveTaskScoringSystem
{

    [System.Serializable]
    public class TaskStepData
    {
        public string stepID;                   // 步骤id
        public string description;              // 描述
        public float baseScore = 10f;           // 基础分数
        public string tip;

        [NonSerialized] public int currentErrors = 0;       // 错误次数
        [NonSerialized] public float currentScore = 0;      // 得分
        [NonSerialized] public bool isCompleted = false;    // 已完成
        [NonSerialized] public bool isSkip = false;         // 是否跳过

        public TaskStepData[] childrenStep;
    }

    [CreateAssetMenu(fileName = "TaskConfig", menuName = "UPandaGF/InteractiveTaskScoringSystem/任务配置")]
    [System.Serializable]
    public class TaskConfig : ScriptableObject
    {
        public TaskStepData[] stepsConfig;
    }

    [System.Serializable]
    public class TaskConfigJsonData
    {
        public TaskStepData[] stepsConfig;
    }
}

