using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
#if UNITY_EDITOR
using UnityEditor;
#endif
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

    public class TaskDataManager : MonoBehaviour, ITaskHost
    {
        private static TaskDataManager instance;
        public static TaskDataManager Instance => instance;

        //[Header("任务设置")]
        public TaskConfig taskSteps;
        public TaskStepBase[] taskStates;   // 用具体类型：ITask 不含 Runtime/ScorePolicy，且 Unity 无法序列化接口数组

        /// <summary>评分策略（可在代码里替换实现，例如难度分级；默认 0错满分/1错半分/2错0分）</summary>
        public IScorePolicy scorePolicy = new DefaultScorePolicy();

        /// <summary>取指定步骤的运行状态（HUD / 存档用）；下标非法时返回 null</summary>
        public TaskStepRuntime GetRuntime(int index)
        {
            if (taskStates == null || !progress.HasStep(index)) return null;
            return taskStates[index] != null ? taskStates[index].Runtime : null;
        }

        /// <summary>任务进度（纯 C#：下标/总分/推进），替代原先直接维护的 currentStepIndex / totalScore 字段</summary>
        private readonly TaskProgress progress = new TaskProgress();

        /// <summary>当前步骤下标（等于步骤总数表示全部完成）</summary>
        public int currentStepIndex { get { return progress.currentIndex; } }

        /// <summary>累计总分</summary>
        public float totalScore { get { return progress.totalScore; } }

        /// <summary>是否全部完成</summary>
        public bool isTaskFinished { get { return progress.isFinished; } }
        public TaskStepData currentTaskStep
        {
            get
            {
                if (taskStates == null || taskStates.Length == 0) taskStates = GetComponentsInChildren<TaskStepBase>();
                return taskStates.Length == 0 ? null : taskStates[Mathf.Clamp(currentStepIndex, 0, taskStates.Length - 1)].GetData;   // 修复：无步骤时返回 null（原写法空数组会越界）
            }
        }

        //[Header("评分系统")]
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
            // 修复：每次初始化都视为“重新开始”，进度复位见下方 progress.Init（各步骤状态在 TaskStepBase.Init 内复位）
            if (taskStates == null || taskStates.Length == 0) taskStates = GetComponentsInChildren<TaskStepBase>();
            if (taskSteps.stepsConfig.Length != taskStates.Length)
            {
                Debug.LogError("任务配置异常！配置数量不等");
                return;
            }
            progress.Init(taskStates.Length);   // 进度复位（原 totalScore / currentStepIndex 已并入 TaskProgress）
            for (int i = 0; i < taskStates.Length; i++)
            {
                //操作记录（分数/错误次数等运行时状态由 TaskStepBase.Init 复位）
                operationRecords[taskSteps.stepsConfig[i].stepID] = new List<OperationRecord>();
                //任务初始化
                taskStates[i].ScorePolicy = scorePolicy;   // 注入评分策略
                taskStates[i].Init(this);
            }

            // 修复：重置后总是从第 0 步开始（原条件 currentStepIndex == 0 会让“重玩”时任务不启动）
            if (taskStates.Length > 0)
            {
                taskStates[currentStepIndex].OnEnter();
                OnStepStarted?.Invoke(currentTaskStep);
                Debug.Log("任务开始");
            }
        }

        /// <summary>
        /// 重新开始任务：复位总分、步骤下标以及所有步骤/操作的运行时状态，并从第 0 步重新开始。
        /// （等价于再次调用 InitializeTask，只是语义更明确，便于按钮或流程逻辑调用）
        /// </summary>
        public void RestartTask()
        {
            InitializeTask();
        }

        /// <summary>
        /// 操作检查
        /// </summary>
        /// <param name="arg"></param>
        public void OperationCheck(TaskEntityBase arg)
        {
            //Debug.Log($"TaskDataManager_OperationCheck {currentStepIndex}:{taskStates.Length}");
            if (taskStates == null || taskStates.Length == 0)   // 修复：任务未成功初始化时忽略本次操作检查
            {
                Debug.LogWarning("任务未初始化或没有步骤，已忽略本次操作检查");
                return;
            }
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
            if (taskStates != null && currentStepIndex < taskStates.Length)   // 修复：空引用保护
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
            if (taskStates != null && currentStepIndex < taskStates.Length)   // 修复：空引用保护
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
        public void CompleteStep(TaskStepBase step)
        {
            if (step == null || step.Runtime == null) return;
            TaskStepRuntime rt = step.Runtime;
            if (!rt.isSkip) progress.AddScore(rt.score);   // 跳过的步骤不计入总分
            RecordOperation("Step_Complete", true, $"完成步骤 {rt.stepID}, 得分: {rt.score}");
            //关闭上一个任务
            if (progress.HasStep(progress.currentIndex))   // 修复：索引越界保护
                taskStates[progress.currentIndex].OnExit();
            OnStepCompleted?.Invoke(step.taskStepData);
            MoveToNextStep();
        }

        /// <summary>
        /// 下一个任务
        /// </summary>
        public void MoveToNextStep()
        {
            if (taskStates == null || taskStates.Length == 0) return;
            if (progress.MoveNext())
            {
                Debug.Log($"{progress.currentIndex}下一个任务");
                taskStates[progress.currentIndex].OnEnter();
                OnStepStarted?.Invoke(currentTaskStep);
            }
            else
            {
                // 任务完成（progress.currentIndex == stepCount 作为“全部完成”哨兵）
                Debug.Log("任务全部完成");
                EventCenter.Instance.EventTrigger(new TaskTipsInfoEvent("任务已全部完成"));
                OnTaskCompleted?.Invoke();
            }

        }

        void RecordOperation(string action, bool isCorrect, string details)
        {
            if (taskStates == null || !progress.HasStep(progress.currentIndex)) return;
            TaskStepBase step = taskStates[progress.currentIndex];
            TaskStepRuntime rt = step.Runtime;
            string runtimeStepID = rt != null ? rt.stepID : (step.taskStepData != null ? step.taskStepData.stepID : step.name);
            OperationRecord record = new OperationRecord
            {
                timestamp = DateTime.Now,
                stepID = runtimeStepID,
                action = action,
                isCorrect = isCorrect,
                details = details,
                score = rt != null ? rt.score : 0f,
                errorCount = rt != null ? rt.errors : 0
            };
            if (operationRecords == null) operationRecords = new Dictionary<string, List<OperationRecord>>();   // 修复：容错
            if (!operationRecords.ContainsKey(runtimeStepID)) operationRecords[runtimeStepID] = new List<OperationRecord>();
            operationRecords[runtimeStepID].Add(record);
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
            // 修复：仅在确实注册过实体时才清空，避免销毁阶段无谓创建单例
            if (TaskEntityManager.HasRegistered) TaskEntityManager.Instance.Clear();
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

