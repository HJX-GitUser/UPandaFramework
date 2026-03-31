using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;


namespace UPandaGF.RunTime.InteractiveTaskScoringSystem
{
    /// <summary>
    /// 操作监听组
    /// </summary>
    public abstract class OperationGroupBase : MonoBehaviour, OperationStepCheck, GetUniTaskID
    {
        public string OperatingStepID;
        /// <summary>
        /// 任务状态
        /// </summary>
        public OperationPhase operationPhase;
        /// <summary>
        /// 操作集合
        /// </summary>
        public OperationStepCheck[] ChildStep;
        /// <summary>
        /// 操作数量
        /// </summary>
        public int OperationCount => ChildStep == null ? 0 : ChildStep.Length;

        public OperationPhase GetOperationPhase => operationPhase;

        public string GetID => OperatingStepID;

        /// <summary>
        /// 操作完成事件
        /// </summary>
        public UnityAction OperationComplete;

        private void Reset()
        {
            GetUniTaskID Tid = transform.parent.GetComponent<GetUniTaskID>();
            if (Tid != null)
                OperatingStepID = Tid.GetID + "-" + transform.GetSiblingIndex();
            else OperatingStepID = null;
        }

        /// <summary>
        /// 操作组初始化
        /// </summary>
        /// <param name="completeEvent">操作组结束完成回调</param>
        public virtual void Init(UnityAction completeEvent)
        {
            //Debug.Log("操作组初始化");
            OperationComplete = completeEvent;
        }

        private void InitChildStep()
        {
            //只获取第一层级子对象的步骤
            List<OperationStepCheck> tempSteps = new List<OperationStepCheck>();
            for (int i = 0; i < transform.childCount; i++)
            {
                OperationStepCheck _interface = transform.GetChild(i).GetComponent<OperationStepCheck>();
                if (_interface != null) tempSteps.Add(_interface);
            }
            ChildStep = tempSteps.ToArray();
        }

        /// <summary>
        /// 操作检查
        /// </summary>
        /// <param name="arg"></param>
        /// <param name="taskStep"></param>
        public abstract void OperationCheck(TaskEntityBase arg, TaskStepBase taskStep);

        /// <summary>
        /// 操作检查启动
        /// </summary>
        public abstract void CheckEnable();

        /// <summary>
        /// 操作引导
        /// </summary>
        public abstract void OperationInstructions();

        public virtual void OperationEnable()
        {
            InitChildStep();
            if (OperationCount == 0)
            {
                operationPhase = OperationPhase.Complete;
                OperationComplete?.Invoke();
                TaskStepData parentData = transform.GetComponentInParent<TaskStepBase>().taskStepData;
                Debug.LogWarning($"操作组任务检查步骤未配置,该操作直接完成,操作组位于：{parentData.stepID}下");
                return;
            }
        }

        public abstract bool CheckOperation(TaskEntityBase arg);

        public abstract void OpearationExecute(UnityAction callback);

        public virtual void OperationSkip()
        {
            foreach (var item in ChildStep)
            {
                if (item.GetOperationPhase != OperationPhase.Complete)
                    item.OperationSkip();
            }
        }
    }
}

