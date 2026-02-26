using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//交互式任务评分系统
namespace UPandaGF.RunTime.InteractiveTaskScoringSystem
{

    /// <summary>
    /// 任务状态枚举
    /// </summary>
    public enum TaskState
    {
        Ready,          // 准备状态
        InProgress,     // 进行中
        StepComplete,   // 步骤完成
        TaskComplete,   // 任务完成
        TaskFailed      // 任务失败
    }

    /// <summary>
    /// 步骤状态枚举
    /// </summary>
    public enum StepState
    {
        Waiting,        // 等待开始
        InProgress,     // 进行中
        Error,          // 错误状态
        Complete        // 完成
    }

    /// <summary>
    /// 错误类型
    /// </summary>
    public enum ErrorType
    {
        WrongTool,      // 使用错误工具
        WrongObject,    // 点击错误对象
        WrongOrder,     // 顺序错误
        NoToolSelected, // 未选择工具
        Timeout         // 超时错误（可选）
    }
    public class TaskData
    {
        [System.Serializable]
        public class StepData
        {
            public string stepID;            // 步骤唯一标识
            public string stepName;          // 步骤名称
            public int targetObjectId;       // 目标对象id
            public int requiredToolId;       // 所需工具id
            public AnimationClip installAnimation;
            public float baseScore = 10f;    // 基础分数
            public int maxErrors = 3;        // 最大错误次数
            public int currentErrors = 0;    // 当前错误次数
            public float currentScore = 0f;  // 当前可得分数

            [Header("引导设置")]
            public string guideText;         // 引导文字
            public float guideDisplayTime = 3f; // 引导显示时间
            public AudioClip guideAudio;     // 引导语音

            public void Initialize()
            {
                currentErrors = 0;
                currentScore = baseScore;
            }

            public float RecordError()
            {
                if (currentErrors >= maxErrors)
                {
                    currentScore = 0f;
                    return 0f;
                }

                currentErrors++;
                float penalty = currentScore * 0.5f; // 扣当前分数的一半
                currentScore -= penalty;

                if (currentErrors >= maxErrors)
                {
                    currentScore = 0f; // 错误三次不得分
                }

                return penalty;
            }

            public float CompleteStep()
            {
                return currentScore; // 返回最终得分
            }
        }
    }
}

