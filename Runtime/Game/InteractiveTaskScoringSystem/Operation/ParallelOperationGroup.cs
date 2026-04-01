using UnityEngine;
using UnityEngine.Events;


namespace UPandaGF.RunTime.InteractiveTaskScoringSystem
{
    /// <summary>
    /// 并联操作监听组
    /// </summary>
    public class ParallelOperationGroup : OperationGroupBase
    {
        [Header("需要执行的操作数量")]
        public int completeCount = 0;

        private int executeCount; // 已执行完成的数量
        public override void CheckEnable()
        {
            Debug.Log("ParallelOperationGroup 检查启动");
            OperationEnable();
        }



        public override void OperationCheck(TaskEntityBase arg, TaskStepBase taskStep)
        {
            if (OperationCount == 0)
            {
                Debug.LogWarning($"任务id:{taskStep.taskStepData.stepID} 并联操作组任务检查步骤未配置！！！");
                return;
            }

            if (CheckOperation(arg))
            {
                OpearationExecute(() =>
                {
                    Debug.Log($"{taskStep.taskStepData.stepID}操作完成！！！");
                    //EventCenter.Instance.EventTrigger(new TaskTipsInfoEvent($"<color=green>{taskStep.taskStepData.stepID}操作完成</color>"));
                });
            }
            else
            {
                Debug.Log("操作错误！！！");
                EventCenter.Instance.EventTrigger(new TaskTipsInfoEvent("<color=red>操作错误!!!</color>"));
                taskStep.AddErroTimes();
            }
        }


        public override void OperationEnable()
        {
            base.OperationEnable();
            if (OperationCount == 0) return;
            operationPhase = OperationPhase.TargetCheck;
            executeCount = 0;
            if (OperationCount != 0 && completeCount <= 0)
            {
                completeCount = OperationCount;
            }
            if (completeCount == 0)
            {
                OperationComplete?.Invoke();
            }
            foreach (var item in ChildStep)
            {
                item.OperationEnable();
            }
        }
        public override bool CheckOperation(TaskEntityBase arg)
        {
            bool checkRight = false;
            foreach (var item in ChildStep)
            {
                if (item.CheckOperation(arg) && item.GetOperationPhase == OperationPhase.TargetCheck)
                {
                    checkRight = true;
                    item.OpearationExecute(null);
                    break;
                }
            }
            return checkRight;
        }

        public override void OpearationExecute(UnityAction callback)
        {
            executeCount++;
            if (executeCount >= completeCount)
            {
                operationPhase = OperationPhase.Complete;
                OperationComplete?.Invoke();
                callback?.Invoke();
            }
        }
        public override void OperationInstructions()
        {
            foreach (var item in ChildStep)
            {
                item.OperationInstructions();
            }
        }

        
    }
}


