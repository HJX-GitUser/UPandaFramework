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
        public int completeCount = 0;   // 需要完成的子操作数量（<=0 表示“全部完成”；运行时不会改写本字段）

        private int executeCount;           // 已执行完成的数量
        private int runtimeCompleteCount;   // 运行时生效的完成数量（修复：不再改写配置字段 completeCount）
        public override void CheckEnable()
        {
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
                // 修复：若已无处于目标检查阶段的子步骤（都在播放动画），本次点击不判为操作错误
                if (!HasTargetCheckStep()) return;
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
            // 修复：改用运行时字段，不再改写配置字段（原写法会把设计值 0 在运行时变成 OperationCount，并可能被序列化回场景）
            runtimeCompleteCount = completeCount <= 0 ? OperationCount : completeCount;
            // 修复：原 if (completeCount == 0) 分支为不可达代码，已删除（现改用运行时字段 runtimeCompleteCount）
            foreach (var item in ChildStep)
            {
                if (item != null) item.OperationEnable();   // 修复：null 元素保护
            }
        }
        public override bool CheckOperation(TaskEntityBase arg)
        {
            bool checkRight = false;
            foreach (var item in ChildStep)
            {
                if (item != null && item.CheckOperation(arg) && item.GetOperationPhase == OperationPhase.TargetCheck)   // 修复：null 元素保护
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
            if (executeCount >= runtimeCompleteCount)
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
                if (item != null) item.OperationInstructions();   // 修复：null 元素保护
            }
        }


    }
}


