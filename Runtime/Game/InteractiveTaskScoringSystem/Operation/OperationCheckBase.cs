using System;
using UnityEngine;
using UnityEngine.Events;

namespace UPandaGF.RunTime.InteractiveTaskScoringSystem
{
    public interface OperationStepCheck
    {
        /// <summary>
        /// 操作检查启动
        /// </summary>
        void OperationEnable();

        /// <summary>
        /// 检查操作是否正确
        /// </summary>
        /// <param name="arg"></param>
        /// <returns></returns>
        bool CheckOperation(TaskEntityBase arg);

        /// <summary>
        /// 操作引导
        /// </summary>
        void OperationInstructions();

        /// <summary>
        /// 操作执行
        /// </summary>
        /// <param name="callback"></param>
        void OpearationExecute(UnityAction callback);

        OperationPhase GetOperationPhase { get; }

        void OperationSkip();
    }

    /// <summary>
    /// 操作实体实现该接口后，由该操作自行判断满足条件
    /// </summary>
    public interface EntityOperationCheck
    {
        bool ConditionMet(TaskEntityBase arg);
    }

    public enum OperationPhase
    {
        Prepare,        // 准备
        TargetCheck,    // 目标检查阶段
        Execute,        // 执行阶段
        Complete        // 完成
    }

    /// <summary>
    /// 操作步骤，判断操作是否正确
    /// </summary>
    public class OperationCheckBase : MonoBehaviour, OperationStepCheck,GetUniTaskID
    {
        public string OperatingStepID;
        public OperationPhase operationPhase = OperationPhase.Prepare;
        private TaskEntityManager entityManager;
        // public OperatingStepData stepData;

        public TaskEntityBase TargetEntity;
        EntityOperationCheck OpCheck;
        public OperationPhase GetOperationPhase => operationPhase;

        public string GetID => OperatingStepID;

        public bool AutoExecute = false;


        private void Awake()
        {
            entityManager = TaskEntityManager.Instance;
        }

        private void Reset()
        {
            GetUniTaskID Tid = transform.parent.GetComponent<GetUniTaskID>();
            if (Tid != null)
                OperatingStepID = Tid.GetID + "-" + transform.GetSiblingIndex();
            else OperatingStepID = null;
        }

        public void Start()
        {
            if (TargetEntity == null)
            {
                TargetEntity = entityManager.FindEntity(OperatingStepID);
                if (TargetEntity == null)
                {
                    Debug.LogError($"{transform.parent.parent.name} : {transform.name} : 实体ID获取失败！");
                }
                OpCheck = TargetEntity.GetComponent<EntityOperationCheck>();
            }

        }



        /// <summary>
        /// 操作启动
        /// </summary>
        public virtual void OperationEnable()
        {
            operationPhase = OperationPhase.TargetCheck;
            if (string.IsNullOrEmpty(OperatingStepID) || AutoExecute)  // 目标检查阶段自动通过 直接完成
            {
                TargetEntity.OnSelect();
            }
            else
            {
                TargetEntity?.EnableInteractive();
            }
        }


        /// <summary>
        /// 操作执行
        /// </summary>
        /// <param name="callback"></param>
        public virtual void OpearationExecute(UnityAction callback)
        {
            if (operationPhase == OperationPhase.Execute || operationPhase == OperationPhase.Complete)
            {
                Debug.Log("操作执行中，或已完成");
                EventCenter.Instance.EventTrigger(new TaskTipsInfoEvent("操作执行中,请等待"));
                return;
            }
            operationPhase = OperationPhase.Execute;//目标检查通过，切换执行阶段
            TargetEntity.Execute(() =>
            {
                operationPhase = OperationPhase.Complete;
                callback?.Invoke();
            });
        }

        /// <summary>
        /// 操作检查
        /// </summary>
        /// <param name="arg">触发的任务实体对象</param>
        /// <returns></returns>
        public virtual bool CheckOperation(TaskEntityBase arg)
        {
            switch (operationPhase)
            {
                case OperationPhase.Prepare:
                    return false;
                case OperationPhase.TargetCheck:
                    if (AutoExecute) return true;
                    if (OpCheck == null)
                    {
                        if (arg != TargetEntity) return false;
                    }
                    else
                    {
                        return OpCheck.ConditionMet(arg);
                    }
                    break;
                case OperationPhase.Execute:
                    if (OpCheck != null)
                    {
                        return OpCheck.ConditionMet(arg);
                    }
                    break;
                case OperationPhase.Complete:
                    break;
            }
            return true;
        }

        public virtual void OperationInstructions()
        {
            if (operationPhase == OperationPhase.TargetCheck)
                TargetEntity.EnableGuide();
            else
            {
                Debug.LogWarning("任务步骤没有处于检测状态，不执行引导");
                if (operationPhase == OperationPhase.Execute)
                    EventCenter.Instance.EventTrigger(new TaskTipsInfoEvent("步骤执行中,请等待"));
            }
        }

        public void OperationSkip()
        {
            TargetEntity.Skip();
        }
    }
}


