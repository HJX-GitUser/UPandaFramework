using UnityEngine;
using UnityEngine.Events;

namespace UPandaGF.RunTime.InteractiveTaskScoringSystem
{
    /// <summary>
    /// 串联操作监听组
    /// </summary>
    public class SeriesOperationGroup : OperationGroupBase
    {
        /// <summary>
        /// 当前操作步骤
        /// </summary>
        private int currentIndex;


        public override void OperationCheck(TaskEntityBase arg, TaskStepBase taskStep)
        {
            if (OperationCount == 0)
            {
                Debug.LogWarning($"任务id:{taskStep.taskStepData.stepID} 串联操作组任务检查步骤未配置！！！");
                return;
            }
            if (currentIndex < OperationCount)
            {
                if (ChildStep[currentIndex] == null || ChildStep[currentIndex].GetOperationPhase != OperationPhase.TargetCheck) return;   // 修复：子步骤已在执行或已完成时，本次点击不判为操作错误
                if (CheckOperation(arg))
                {
                    OpearationExecute(() =>
                    {
                        Debug.Log($"{taskStep.taskStepData.stepID}操作完成！！！");
                        //EventCenter.Instance.EventTrigger(new TaskTipsInfoEvent($"<color=yellow>{taskStep.taskStepData.stepID}操作完成</color>"));
                    });
                }
                else
                {
                    EventCenter.Instance.EventTrigger(new TaskTipsInfoEvent("操作错误!!!"));
                    taskStep.AddErroTimes();
                }
            }
        }

        public override void CheckEnable()
        {
            OperationEnable();
        }

        public override void OperationInstructions()
        {
            if (ChildStep == null || currentIndex < 0 || currentIndex >= OperationCount) return;   // 修复：越界保护
            ChildStep[currentIndex].OperationInstructions();
        }

        public override void OperationEnable()
        {
            base.OperationEnable();
            if (OperationCount == 0) return;
            currentIndex = 0;
            if (ChildStep[currentIndex] != null) ChildStep[currentIndex].OperationEnable();   // 修复：空子步骤保护
            operationPhase = OperationPhase.TargetCheck;
        }

        public override bool CheckOperation(TaskEntityBase arg)
        {
            return ChildStep[currentIndex] != null && ChildStep[currentIndex].CheckOperation(arg);   // 修复：空子步骤保护
        }

        public override void OpearationExecute(UnityAction callback)
        {
            operationPhase = OperationPhase.Execute;
            ChildStep[currentIndex].OpearationExecute(() =>
            {
                currentIndex++;
                if (currentIndex < OperationCount)
                {
                    operationPhase = OperationPhase.TargetCheck;
                    if (ChildStep[currentIndex] != null) ChildStep[currentIndex].OperationEnable();   // 修复：空子步骤保护
                }
                else
                {
                    Debug.Log("串联任务结束");
                    operationPhase = OperationPhase.Complete;
                    OperationComplete?.Invoke();
                    callback?.Invoke();
                }
            });
        }
    }
}


