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
        public int order = 0;                   // P2：步骤执行顺序，越小越先执行；全为 0 时按场景节点层级顺序（兼容旧数据）
        public string tip;


        // 运行时状态（错误次数/得分/完成/跳过）已移到 TaskStepRuntime，不再写在配置资产上：
        // 这样重玩、多实例并行、存档都不会污染 ScriptableObject（见 Core/TaskStepRuntime.cs）

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

