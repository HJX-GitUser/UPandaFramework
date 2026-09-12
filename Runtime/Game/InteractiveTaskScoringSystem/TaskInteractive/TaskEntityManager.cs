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
        private static int registeredCount = 0;   // 修复：判断是否真的注册过实体（避免销毁阶段无谓创建单例）

        /// <summary>是否注册过实体（静态判断，不会触发单例创建）</summary>
        public static bool HasRegistered => registeredCount > 0;
        public void Register(TaskEntityBase arg)
        {
            if (arg == null || arg.StepIDGroup == null || arg.StepIDGroup.Length == 0) return;   // 修复：空引用保护
            foreach (var item in arg.StepIDGroup)
            {
                if (string.IsNullOrEmpty(item)) continue;   // 修复：忽略未填写的空 ID
                    if (!entityDic.ContainsKey(item))
                {
                    entityDic.Add(item, arg);
                    registeredCount++;
                }
                else
                {
                    PLogger.LogError($"步骤ID「{item}」重复注册！\n新对象：{arg.name}\n已注册：{entityDic[item].name}");   // 修复：原信息打印的是数组名，且父物体为空时还会空引用
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
            registeredCount = 0;   // 修复：同步重置计数
        }
    }
}

