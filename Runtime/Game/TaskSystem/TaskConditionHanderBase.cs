using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>
    /// 任务完成条件处理
    /// </summary>
    public abstract class TaskConditionHanderBase<T> where T : TaskConditionDataBase
    {
        /// <summary>
        /// 条件配置
        /// </summary>
        public T configData;
        /// <summary>
        /// 条件判断
        /// </summary>
        /// <param name="data">执行数据</param>
        /// <returns></returns>
        public abstract bool IsConditionMet(T data);
    }

    /// <summary>
    /// 任务条件数据
    /// </summary>
    [System.Serializable]
    public class TaskConditionDataBase
    {
        public string name;
        public string description;
    }
}

