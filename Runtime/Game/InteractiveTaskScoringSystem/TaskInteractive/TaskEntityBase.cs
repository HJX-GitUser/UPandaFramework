using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

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
        [SerializeField]
        private int id;
        public int Id => id;

        // 静态计数器，用于生成唯一ID
        // 使用 volatile 确保多线程安全（虽然Unity主线程单线程，但以防万一）
        private static volatile int _idCounter = 0;

        // 用于持久化的键值
        private const string ID_COUNTER_KEY = "UniqueId_Counter";

        // 标记是否已初始化，防止重复赋值
        [HideInInspector]
        public bool _isInitialized = false;

        protected float outlineWidth = 4;

        private void Reset()
        {
            GenerateId();
        }
        protected virtual void Awake()
        {
            TaskEntityManager.Instance.Register(this);
            //交互测试
            gameObject.AddComponent<TaskTriggerExample>();
        }

        /// <summary>
        /// 生成唯一ID的方法
        /// </summary>
        private void GenerateId()
        {
            if (_isInitialized) return;

#if UNITY_EDITOR
            // 编辑器模式：从持久化存储读取计数器
            if (!Application.isPlaying)
            {
                _idCounter = UnityEditor.EditorPrefs.GetInt(ID_COUNTER_KEY, 0);
            }
#endif

            // 原子操作生成新ID
            int newId = System.Threading.Interlocked.Increment(ref _idCounter);

            // 确保不会生成0（通常0表示无效ID）
            if (newId == 0) newId = System.Threading.Interlocked.Increment(ref _idCounter);

            id = newId;
            _isInitialized = true;

#if UNITY_EDITOR
            // 编辑器模式：保存计数器
            if (!Application.isPlaying)
            {
                UnityEditor.EditorPrefs.SetInt(ID_COUNTER_KEY, _idCounter);
                // 标记场景为脏，确保保存
                UnityEditor.EditorUtility.SetDirty(this);
            }
#endif

            Debug.Log($"[UniqueId] 生成新ID: {id} for {gameObject.name}", this);
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
    }
}

