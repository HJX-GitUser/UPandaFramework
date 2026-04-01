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
                    Debug.Log("操作错误！！！");
                    Debug.Log(arg.gameObject.name);
                    EventCenter.Instance.EventTrigger(new TaskTipsInfoEvent("操作错误!!!"));
                    taskStep.AddErroTimes();
                }
            }
        }

        public override void CheckEnable()
        {
            Debug.Log("SeriesOperationGroup 检查启动" + transform.name);
            OperationEnable();
        }

        public override void OperationInstructions()
        {
            ChildStep[currentIndex].OperationInstructions();
        }

        public override void OperationEnable()
        {
            base.OperationEnable();
            if (OperationCount == 0) return;
            currentIndex = 0;
            ChildStep[currentIndex].OperationEnable();
            operationPhase = OperationPhase.TargetCheck;
        }

        public override bool CheckOperation(TaskEntityBase arg)
        {
            return ChildStep[currentIndex].CheckOperation(arg);
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
                    ChildStep[currentIndex].OperationEnable();
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


