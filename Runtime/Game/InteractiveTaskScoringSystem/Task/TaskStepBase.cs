using System.Collections.Generic;
using UnityEngine;

namespace UPandaGF.RunTime.InteractiveTaskScoringSystem
{
    // 任务状态
    public enum TaskState
    {
        Ready,          // 准备状态
        InProgress,     // 进行中
        TaskComplete,   // 任务完成
    }

    public interface ITask
    {
        void Init(TaskDataManager arg);
        void OnEnter();
        void OnExit();

        void OperationCheck(TaskEntityBase arg);//操作检查

        void OperationInstructions(); //操作引导

        void SkipTask();//步骤跳过

        TaskStepData GetData { get; }
    }

    public interface GetUniTaskID
    {
        string GetID { get; }
    }


    /// <summary>
    /// 任务步骤
    /// </summary>
    public class TaskStepBase : MonoBehaviour, ITask, GetUniTaskID
    {
        private TaskDataManager taskDataManager;
        public TaskStepData taskStepData;
        public TaskState taskState;

        /// <summary>本步骤的运行时状态（纯 C#，替代原先写在 TaskStepData 上的字段）</summary>
        public TaskStepRuntime Runtime { get; private set; }

        /// <summary>评分策略（由 TaskDataManager 注入；默认 0错满分/1错半分/2错0分）</summary>
        public IScorePolicy ScorePolicy { get; set; } = new DefaultScorePolicy();

        // 兼容旧读取方式的只读属性（原为 public 字段，现已并入 Runtime）
        public float currentScore { get { return Runtime != null ? Runtime.score : 0f; } }
        public int erroTimes { get { return Runtime != null ? Runtime.errors : 0; } }

        private static readonly IScorePolicy DefaultPolicy = new DefaultScorePolicy();


        /// <summary>
        /// 要激活的实体
        /// </summary>
        public string[] EnableEntity;
        private List<TaskEntityBase> entitys;
        public OperationGroupBase operationGroup;

        public TaskStepData GetData => taskStepData;

        public string GetID => taskStepData.stepID;

        public virtual void Init(TaskDataManager arg)
        {
            taskDataManager = arg;
            taskState = TaskState.Ready;
            operationGroup = GetComponentInChildren<OperationGroupBase>();
            if (operationGroup == null)
            {
                Debug.LogWarning($"{taskStepData.stepID} 未配置操作检查组，该步骤进入后将直接完成");
                // 修复：原此处直接 return 会让下面的待激活实体解析不到，改为仅告警
            }
            if (operationGroup != null) operationGroup.Init(Submit);   // 修复：未配置检查组时跳过初始化，待激活实体仍会解析
            ResetStepState();   // 修复：重玩同一任务时复位本步骤与所有子操作的运行时状态
            entitys = new List<TaskEntityBase>();
            if (EnableEntity != null) foreach (string item in EnableEntity)   // 修复：未配置待激活实体时不空引用
            {
                TaskEntityBase taskEntity = TaskEntityManager.Instance.FindEntity(item);
                if (taskEntity != null) entitys.Add(taskEntity);
            }

        }

        /// <summary>步骤ID（配置缺失时退化为节点名，保证 Runtime 里总有可读标识）</summary>
        public string GetStepID()
        {
            return taskStepData != null ? taskStepData.stepID : name;
        }

        /// <summary>
        /// 复位本步骤与所有子操作的运行时状态。
        /// 修复原因：运行时状态寄生在 TaskStepData（ScriptableObject）上，
        /// 重玩同一任务时若不清理，会出现错误次数累加导致分数偏低、
        /// isSkip 残留让本步分数永远不计入总分、操作停留在 Complete 阶段使点击被忽略。
        /// </summary>
        private void ResetStepState()
        {
            float baseScore = taskStepData != null ? taskStepData.baseScore : 0f;
            string id = GetStepID();
            if (Runtime == null) Runtime = new TaskStepRuntime(id, baseScore);
            else Runtime.Reset(id, baseScore);
            Runtime.beginTime = Time.time;
            if (operationGroup != null) operationGroup.ResetOperation();
        }

        public virtual void OnEnter()
        {
            taskState = TaskState.InProgress;
            //Debug.Log("激活实体组");
            if (entitys != null)
            {
                foreach (var item in entitys)
                {
                    item.EnableInteractive();
                }
            }
            if (operationGroup == null)
            {
                Submit();
            }
            else
            {
                operationGroup.CheckEnable();
            }
        }

        public virtual void OnExit()
        {
            taskState = TaskState.TaskComplete;
            //Debug.Log("关闭实体组");
            //foreach (var item in entitys)
            //{
            //    item.DisableInteractive();
            //}
        }

        /// <summary>
        /// 操作步骤完成回调
        /// </summary>
        public virtual void Submit()
        {
            // 说明：Submit 会先把状态置为 TaskComplete 再调用本方法（保留判断以兼容外部直接调用）
            if (taskState == TaskState.TaskComplete) return;   // 修复：防止重复提交（跳过与执行回调叠加）导致步骤重复推进、重复计分
            taskState = TaskState.TaskComplete;
            //完成
            if (Runtime == null) Runtime = new TaskStepRuntime(GetStepID(), taskStepData != null ? taskStepData.baseScore : 0f);
            Runtime.score = HandleScore();
            Runtime.isCompleted = true;
            Runtime.endTime = Time.time;
            if (taskDataManager != null) taskDataManager.CompleteStep(this);
            else Debug.LogError($"{GetStepID()} 缺少 TaskDataManager，无法推进流程");
        }

        public void AddErroTimes()
        {
            if (Runtime == null) Runtime = new TaskStepRuntime(GetStepID(), taskStepData != null ? taskStepData.baseScore : 0f);
            Runtime.errors++;
        }

        public virtual float HandleScore()
        {
            // 说明：Submit 会先把状态置为 TaskComplete 再调用本方法（保留判断以兼容外部直接调用）
            if (taskState == TaskState.TaskComplete)
                // 评分规则：0 次错误=满分；1 次错误=半分；2 次及以上=0 分（isSkip 的步骤不计入总分）
            return (ScorePolicy != null ? ScorePolicy : DefaultPolicy).CalcScore(taskStepData != null ? taskStepData.baseScore : 0f, Runtime != null ? Runtime.errors : 0);
            else return 0;
        }

        public void OperationCheck(TaskEntityBase arg)
        {
            if (operationGroup == null)
            {
                Debug.Log($"任务id:{taskStepData.stepID} 没有任务检查组");
                return;
            }
            operationGroup.OperationCheck(arg, this);
        }

        public void OperationInstructions()
        {
            if (operationGroup == null) { Debug.LogWarning($"{taskStepData.stepID} 未配置操作检查组，无法执行引导"); return; }   // 修复：空引用保护
            operationGroup.OperationInstructions();
        }

        public void SkipTask()
        {
            if (operationGroup != null && operationGroup.operationPhase == OperationPhase.Execute)   // 修复：空引用保护
            {
                Debug.Log("步骤执行中，禁止跳过");
                EventCenter.Instance.EventTrigger(new TaskTipsInfoEvent("步骤执行中，禁止跳过!!!"));
                return;
            }
            else
                if (operationGroup != null) operationGroup.OperationSkip();   // 修复：空引用保护
            if (Runtime != null) Runtime.isSkip = true;
            Submit();
        }


    }
}



