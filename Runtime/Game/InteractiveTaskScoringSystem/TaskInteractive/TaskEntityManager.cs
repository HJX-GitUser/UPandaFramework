using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UPandaGF;

namespace UPandaGF.RunTime.InteractiveTaskScoringSystem
{
    /// <summary>
    /// 任务交互对象管理器
    /// </summary>
    public class TaskEntityManager : LazySingletonBase<TaskEntityManager>
    {
        private Dictionary<int, TaskEntityBase> entityDic = new Dictionary<int, TaskEntityBase>();
        public void Register(TaskEntityBase arg)
        {
            if (!entityDic.ContainsKey(arg.Id))
            {
                entityDic.Add(arg.Id, arg);
            }
            else
            {
                PLogger.LogError($"{arg.Id}重复注册！注册对象：{arg.transform.parent.name}/{arg.transform.name}\n已注册对象：{entityDic[arg.Id].transform.parent.name}/{entityDic[arg.Id].transform.name}");
            }
        }

        public TaskEntityBase FindEntity(int id)
        {
            TaskEntityBase arg = entityDic.ContainsKey(id) ? entityDic[id] : null;
            if (arg == null && id != 0) Debug.LogError($"id{id} 查找失败！");
            return arg;
        }

        public void Clear()
        {
            entityDic.Clear();
            Debug.Log("任务实体已清空");
        }
    }
}

