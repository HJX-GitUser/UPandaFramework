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
        public float currentScore = 0;
        public int erroTimes = 0;


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
                Debug.LogWarning($"{taskStepData.stepID} ");
                return;
            }
            operationGroup.Init(Submit);
            entitys = new List<TaskEntityBase>();
            foreach (string item in EnableEntity)
            {
                TaskEntityBase taskEntity = TaskEntityManager.Instance.FindEntity(item);
                if (taskEntity != null) entitys.Add(taskEntity);
            }

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
            taskState = TaskState.TaskComplete;
            //完成
            taskStepData.currentScore = HandleScore();
            currentScore = taskStepData.currentScore;
            taskDataManager.CompleteStep(taskStepData);
        }

        public void AddErroTimes()
        {
            taskStepData.currentErrors++;
            erroTimes = taskStepData.currentErrors;
        }

        public virtual float HandleScore()
        {
            if (taskState == TaskState.TaskComplete)
                return taskStepData.currentErrors == 0 ? taskStepData.baseScore : (taskStepData.currentErrors == 1 ? taskStepData.baseScore / 2 : 0);
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
            operationGroup.OperationInstructions();
        }

        public void SkipTask()
        {
            if (operationGroup.operationPhase == OperationPhase.Execute)
            {
                Debug.Log("步骤执行中，禁止跳过");
                EventCenter.Instance.EventTrigger(new TaskTipsInfoEvent("步骤执行中，禁止跳过!!!"));
                return;
            }
            else
                operationGroup.OperationSkip();
            taskStepData.isSkip = true;
            Submit();
        }


    }
}



