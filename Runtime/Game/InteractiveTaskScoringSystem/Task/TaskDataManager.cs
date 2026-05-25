using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;

namespace UPandaGF.RunTime.InteractiveTaskScoringSystem
{
    /// <summary>
    /// 操作记录
    /// </summary>
    [System.Serializable]
    public class OperationRecord
    {
        public DateTime timestamp;  // 时间
        public string stepID;       // 步骤id
        public string action;       // 
        public bool isCorrect;      // 是否正确
        public string details;      // 
        public float score;         // 分数
        public int errorCount;      // 错误次数
    }

    /// <summary>
    /// 任务结果
    /// </summary>
    [System.Serializable]
    public class TaskResult
    {
        public DateTime completionTime;
        public float totalScore;
        public int stepsCompleted;
    }

    public class TaskDataManager : MonoBehaviour
    {
        private static TaskDataManager instance;
        public static TaskDataManager Instance => instance;

        //[Header("任务设置")]
        public TaskConfig taskSteps;
        public int currentStepIndex = 0;
        public ITask[] taskStates;
        public TaskStepData currentTaskStep
        {
            get
            {
                if (taskStates == null) taskStates = GetComponentsInChildren<TaskStepBase>();
                return taskStates[Mathf.Clamp(currentStepIndex, 0, taskStates.Length - 1)].GetData;
            }
        }

        //[Header("评分系统")]
        public float totalScore = 0f;
        private Dictionary<string, List<OperationRecord>> operationRecords;

        public UnityAction<TaskStepData> OnStepStarted;
        public UnityAction<TaskStepData> OnStepCompleted;
        public UnityAction OnTaskCompleted;


        //[Header("起始任务")]
        //public int startIndex = 0;

        //public UnityAction<float> OnScoreUpdated;
        private void Awake()
        {
            instance = this;
        }
        private void Start()
        {
            InitializeTask();
        }
        /// <summary>
        /// 任务初始化
        /// </summary>
        public void InitializeTask()
        {
            if (taskSteps == null)
            {
                Debug.LogError("任务配置缺失！！！");
                return;
            }
            operationRecords = new Dictionary<string, List<OperationRecord>>();
            if (taskStates == null) taskStates = GetComponentsInChildren<TaskStepBase>();
            if (taskSteps.stepsConfig.Length != taskStates.Length)
            {
                Debug.LogError("任务配置异常！配置数量不等");
                return;
            }
            for (int i = 0; i < taskStates.Length; i++)
            {
                //分数初始化
                taskSteps.stepsConfig[i].currentScore = taskSteps.stepsConfig[i].baseScore;
                //操作记录
                operationRecords[taskSteps.stepsConfig[i].stepID] = new List<OperationRecord>();
                //任务初始化
                taskStates[i].Init(this);
            }

            if (currentStepIndex == 0)
            {
                taskStates[currentStepIndex].OnEnter();
                OnStepStarted?.Invoke(currentTaskStep);
                Debug.Log("任务开始");
            }
        }

        /// <summary>
        /// 操作检查
        /// </summary>
        /// <param name="arg"></param>
        public void OperationCheck(TaskEntityBase arg)
        {
            //Debug.Log($"TaskDataManager_OperationCheck {currentStepIndex}:{taskStates.Length}");
            if (currentStepIndex >= taskStates.Length)
            {
                Debug.Log("所有任务都已完成");
                EventCenter.Instance.EventTrigger(new TaskTipsInfoEvent("所有任务都已完成"));
                return;
            }
            taskStates[currentStepIndex].OperationCheck(arg);
        }

        /// <summary>
        /// 操作引导
        /// </summary>
        public void OperationInstructions()
        {
            if (currentStepIndex < taskStates.Length)
                taskStates[currentStepIndex].OperationInstructions();
            else
            {
                Debug.Log("步骤已全部完成");
                EventCenter.Instance.EventTrigger(new TaskTipsInfoEvent("步骤已全部完成"));
            }
        }

        /// <summary>
        /// 任务跳过
        /// </summary>
        public void SkipTask()
        {
            if (currentStepIndex < taskStates.Length)
            {
                taskStates[currentStepIndex].SkipTask();
            }
            else
            {
                Debug.Log("任务已全部完成");
                EventCenter.Instance.EventTrigger(new TaskTipsInfoEvent("任务已全部完成"));
            }
        }

        /// <summary>
        /// 完成任务步骤
        /// </summary>
        /// <param name="step"></param>
        public void CompleteStep(TaskStepData step)
        {
            step.isCompleted = true;
            if (!step.isSkip)
                totalScore += step.currentScore;
            //uiManager.ShowFeedback("步骤完成！", Color.green);
            //uiManager.UpdateTotalScore(totalScore);
            RecordOperation("Step_Complete", true, $"完成步骤 {step.stepID}, 得分: {step.currentScore}");
            //关闭上一个任务
            taskStates[currentStepIndex].OnExit();
            OnStepCompleted?.Invoke(step);
            MoveToNextStep();
        }

        /// <summary>
        /// 下一个任务
        /// </summary>
        public void MoveToNextStep()
        {
            Debug.Log($"{currentStepIndex}下一个任务");
            if (currentStepIndex < taskSteps.stepsConfig.Length - 1)
            {
                currentStepIndex++;
                taskStates[currentStepIndex].OnEnter();
                OnStepStarted?.Invoke(currentTaskStep);
            }
            else
            {
                // 任务完成
                Debug.Log("任务全部完成");
                EventCenter.Instance.EventTrigger(new TaskTipsInfoEvent("任务已全部完成"));
                currentStepIndex = taskStates.Length;
                OnTaskCompleted?.Invoke();
            }

        }

        void RecordOperation(string action, bool isCorrect, string details)
        {
            TaskStepData currentStep = taskSteps.stepsConfig[currentStepIndex];
            OperationRecord record = new OperationRecord
            {
                timestamp = DateTime.Now,
                stepID = currentStep.stepID,
                action = action,
                isCorrect = isCorrect,
                details = details,
                score = currentStep.currentScore,
                errorCount = currentStep.currentErrors
            };
            operationRecords[currentStep.stepID].Add(record);
        }

        public List<OperationRecord> GetReplayData()
        {
            List<OperationRecord> allRecords = new List<OperationRecord>();
            foreach (var records in operationRecords.Values)
            {
                allRecords.AddRange(records);
            }
            return allRecords;
        }

        private void OnDestroy()
        {
            TaskEntityManager.Instance.Clear();
        }

#if UNITY_EDITOR

        [ContextMenu("SaveConfigToJson")]
        private void SaveConfigToJson()
        {
            string json = JsonUtility.ToJson(taskSteps, true);
            Debug.Log(json);
            string path = Application.streamingAssetsPath + "/taskSteps.json";
            File.WriteAllText(path, json);
            AssetDatabase.Refresh();
        }

        [ContextMenu("SetConfigFormJson")]
        private void JsonSetConfig()
        {
            string path = Application.streamingAssetsPath + "/taskSteps.json";
            if (!File.Exists(path))
            {
                Debug.Log($"path is null:{path}");
                return;
            }
            string json = File.ReadAllText(path);
            Debug.Log(json);
            TaskConfigJsonData arg = JsonUtility.FromJson<TaskConfigJsonData>(json);
            Debug.Log(arg.stepsConfig.Length);
            taskSteps.stepsConfig = arg.stepsConfig;
        }
#endif
    }


    public class TaskTipsInfoEvent : EventArgBase
    {
        public string info;
        public TaskTipsInfoEvent(string arg)
        {
            info = arg;
        }
    }
}

