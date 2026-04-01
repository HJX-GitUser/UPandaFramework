using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace UPandaGF.RunTime.InteractiveTaskScoringSystem
{
    /// <summary>
    /// 交互触发接口
    /// </summary>
    public interface InteractiveTrigger
    {
        void OnEnter();
        void OnExit();
        void OnStay();
        void OnSelect();

        void OnSelectExit();

    }

    /// <summary>
    /// 任务交互对象
    /// </summary>
    public abstract class TaskEntityBase : MonoBehaviour, InteractiveTrigger
    {
        public string[] StepIDGroup;

        protected float outlineWidth = 4;


        protected virtual void Awake()
        {
            TaskEntityManager.Instance.Register(this);
            //交互测试
            gameObject.AddComponent<TaskTriggerExample>();
        }


        /// <summary>
        /// 执行动作,任务检查通过后调用
        /// </summary>
        /// <param name="callback"></param>
        public abstract void Execute(UnityAction callback);

        /// <summary>
        /// 激活交互
        /// </summary>
        public abstract void EnableInteractive();

        /// <summary>
        /// 关闭交互
        /// </summary>
        public abstract void DisableInteractive();

        /// <summary>
        /// 启动引导
        /// </summary>
        public abstract void EnableGuide();


        #region 交互触发接口
        public abstract void OnEnter();

        public abstract void OnExit();

        public abstract void OnStay();

        public virtual void OnSelect()
        {
            //Debug.Log($"{id} OnSelect");
            TaskDataManager.Instance.OperationCheck(this);
        }

        public abstract void OnSelectExit();

        public abstract void Skip();

        #endregion

        public bool IsPointerOverUI()
        {
            // EventSystem.current.IsPointerOverGameObject() 是 Unity 内置方法
            // 注意：这个方法在触摸屏上可能有一些特殊情况
            return EventSystem.current != null &&
                   EventSystem.current.IsPointerOverGameObject();
        }
    }
}

