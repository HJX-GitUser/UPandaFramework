using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UPandaGF
{
    public enum TaskGroupType
    {
        Chain,//任务链
        Parallel,//并行任务
        PreTask//前置
    }
    public abstract class TaskGroupBase
    {
        public TaskGroupType taskGroupType;
        public List<Task> tasks;

        public abstract void InitTask(List<Task> arg);



    }

    public class TaskChainGroup : TaskGroupBase
    {
        private int currentIndex;

        public override void InitTask(List<Task> arg)
        {
            currentIndex = 0;
            for (int i = 1; i < tasks.Count; i++)
            {
                tasks[currentIndex].status = TaskStatus.Inactive;
            }
            tasks[currentIndex].status = TaskStatus.InProgress;
        }

        public void UpdataTask(TaskConditionDataBase arg)
        {
            Task task = tasks[currentIndex];
            if (task.taskCondition.IsConditionMet(arg))
            {
                task.status = TaskStatus.Completed;
                currentIndex++;
                if (currentIndex < tasks.Count)
                {
                    tasks[currentIndex].status = TaskStatus.Inactive;
                }
                else
                {
                    CompleteTask();
                }
            }
        }

        public void CompleteTask()
        {
            PLoger.Log("Complete");
        }

    }
}
