using System.Collections.Generic;
using UnityEngine;

namespace UPandaGF.RunTime.InteractiveTaskScoringSystem
{
    /// <summary>
    /// 任务交互对象管理器
    /// </summary>
    public class TaskEntityManager : LazySingletonBase<TaskEntityManager>
    {
        private Dictionary<string, TaskEntityBase> entityDic = new Dictionary<string, TaskEntityBase>();
        public void Register(TaskEntityBase arg)
        {
            if (arg.StepIDGroup.Length == 0) return;
            foreach (var item in arg.StepIDGroup)
            {
                if (!entityDic.ContainsKey(item))
                {
                    entityDic.Add(item, arg);
                }
                else
                {
                    PLogger.LogError($"{arg.StepIDGroup}重复注册！注册对象：{arg.transform.parent.name}/{arg.transform.name}\n已注册对象：{entityDic[item].transform.parent.name}/{entityDic[item].transform.name}");
                }
            }
            
        }

        public TaskEntityBase FindEntity(string id)
        {
            TaskEntityBase arg = entityDic.ContainsKey(id) ? entityDic[id] : null;
            if (arg == null) Debug.LogError($"id{id} 查找失败！");
            return arg;
        }

        public void Clear()
        {
            entityDic.Clear();
            Debug.Log("任务实体已清空");
        }
    }
}

