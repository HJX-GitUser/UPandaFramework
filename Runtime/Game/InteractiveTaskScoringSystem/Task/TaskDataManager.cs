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

        /// <summary>P2：是否记录动作级回放日志（默认开启；关闭后不再追加，已有日志仍在）</summary>
        public bool enableReplayLog = true;

        /// <summary>P2：动作级回放日志（菜单「回放复盘查看器」可查看/导出，SaveReplayLog() 落盘）</summary>
        public ReplayLog Replay { get; private set; }

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
            SortStepsByOrder();   // P2：按 TaskStepData.order 稳定排序（order 全为 0 时保持节点层级顺序，兼容旧数据）
            progress.Init(taskStates.Length);   // 进度复位（原 totalScore / currentStepIndex 已并入 TaskProgress）
            Replay = new ReplayLog(taskSteps.name, Time.time);   // P2：本次任务的回放日志
            if (enableReplayLog) Replay.Add(Time.time, null, "Task_Start", true, "任务开始，共 " + taskStates.Length + " 步");
            for (int i = 0; i < taskStates.Length; i++)
            {
                if (taskStates[i] == null) continue;
                //操作记录：键取步骤自身绑定的 stepID（步骤顺序改由 order 决定，不再依赖配置数组下标）
                string initStepID = taskStates[i].taskStepData != null ? taskStates[i].taskStepData.stepID : null;
                if (!string.IsNullOrEmpty(initStepID)) operationRecords[initStepID] = new List<OperationRecord>();
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
            if (Replay != null) Replay.Add(Time.time, null, "Task_Restart", true, "重新开始任务");   // P2：动作级日志
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
                if (Replay != null)
                {
                    Replay.totalScore = progress.totalScore;
                    Replay.stepCount = progress.stepCount;
                    Replay.Add(Time.time, null, "Task_Complete", true, "全部步骤完成，总分 " + progress.totalScore);
                }
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
            if (enableReplayLog && Replay != null)   // P2：同一条记录也写入动作级日志
                Replay.Add(Time.time, runtimeStepID, action, isCorrect, details, record.score, record.errorCount);
        }

        /// <summary>
        /// P2：记录一条动作级日志（由 TaskStepBase 调用）。
        /// enableReplayLog = false 时直接忽略；日志会顺带带上该步骤当前的得分与错误次数。
        /// </summary>
        public void RecordStepAction(string stepID, string action, bool isCorrect, string details)
        {
            if (!enableReplayLog) return;
            if (Replay == null) Replay = new ReplayLog(taskSteps != null ? taskSteps.name : name, Time.time);
            TaskStepRuntime rt = FindRuntime(stepID);
            Replay.Add(Time.time, stepID, action, isCorrect, details, rt != null ? rt.score : 0f, rt != null ? rt.errors : 0);
        }

        /// <summary>按 stepID 找步骤运行状态（写日志用；找不到返回 null）</summary>
        private TaskStepRuntime FindRuntime(string stepID)
        {
            if (string.IsNullOrEmpty(stepID) || taskStates == null) return null;
            for (int i = 0; i < taskStates.Length; i++)
            {
                if (taskStates[i] == null) continue;
                TaskStepRuntime rt = taskStates[i].Runtime;
                if (rt != null && rt.stepID == stepID) return rt;
            }
            return null;
        }

        /// <summary>
        /// P2：把回放日志写成 JSON（默认落到 StreamingAssets，文件名可指定）。
        /// 返回写入的完整路径；没有日志或写失败返回 null。
        /// </summary>
        public string SaveReplayLog(string fileName = null)
        {
            if (Replay == null) return null;
            Replay.totalScore = progress.totalScore;
            Replay.stepCount = progress.stepCount;
            if (string.IsNullOrEmpty(fileName))
                fileName = "replay_" + (taskSteps != null ? taskSteps.name : name) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json";
            string path = Path.Combine(Application.streamingAssetsPath, fileName);
            try
            {
                File.WriteAllText(path, JsonUtility.ToJson(Replay, true));
                Debug.Log("[交互任务] 回放日志已保存：" + path);
                return path;
            }
            catch (Exception e)
            {
                Debug.LogError("[交互任务] 回放日志保存失败：" + e.Message);
                return null;
            }
        }

        /// <summary>P2：从 JSON 文件读取回放日志（查看器/复盘用）</summary>
        public static ReplayLog LoadReplayLog(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try { return JsonUtility.FromJson<ReplayLog>(File.ReadAllText(path)); }
            catch (Exception e) { Debug.LogError("[交互任务] 回放日志读取失败：" + e.Message); return null; }
        }

        /// <summary>
        /// P2：按 TaskStepData.order 做「稳定排序」——order 小者先执行；order 相同（含全部为 0 的旧数据）
        /// 保持场景节点层级顺序。这样步骤执行顺序不再被子节点排列顺序绑架。
        /// </summary>
        private void SortStepsByOrder()
        {
            if (taskStates == null || taskStates.Length < 2) return;
            int[] indexs = new int[taskStates.Length];
            for (int i = 0; i < indexs.Length; i++) indexs[i] = i;
            Array.Sort(indexs, (a, b) =>
            {
                int oa = StepOrderOf(taskStates[a]);
                int ob = StepOrderOf(taskStates[b]);
                return oa != ob ? oa.CompareTo(ob) : a.CompareTo(b);   // order 相同则用原下标兜底 → 稳定排序
            });
            TaskStepBase[] sorted = new TaskStepBase[taskStates.Length];
            for (int i = 0; i < sorted.Length; i++) sorted[i] = taskStates[indexs[i]];
            taskStates = sorted;
        }

        private static int StepOrderOf(TaskStepBase step)
        {
            return step != null && step.taskStepData != null ? step.taskStepData.order : 0;
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

