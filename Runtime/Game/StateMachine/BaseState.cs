using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UPandaGF.StateMechine
{
    /// <summary>
    /// 状态接口
    /// </summary>
    public interface IState
    {
        /// <summary>
        /// 状态ID（用于状态切换和识别）
        /// </summary>
        string StateID { get; }

        /// <summary>
        /// 父状态（用于构建层级关系）
        /// </summary>
        IState Parent { get; set; }

        /// <summary>
        /// 进入状态
        /// </summary>
        void OnEnter();

        /// <summary>
        /// 退出状态
        /// </summary>
        void OnExit();

        /// <summary>
        /// 每帧更新
        /// </summary>
        void OnUpdate(float deltaTime);

        /// <summary>
        /// 固定更新（用于物理计算）
        /// </summary>
        void OnFixedUpdate();

        /// <summary>
        /// 能否切换到指定状态
        /// </summary>
        bool CanTransitionTo(string stateID);
    }

    /// <summary>
    /// 抽象状态基类
    /// </summary>
    public abstract class BaseState : IState
    {
        public abstract string StateID { get; }
        public IState Parent { get; set; }

        /// <summary>
        /// 层级深度（根状态为0，每增加一级子状态+1）
        /// </summary>
        public int Depth => Parent == null ? 0 : (Parent as BaseState)?.Depth + 1 ?? 1;

        /// <summary>
        /// 子状态映射表
        /// </summary>
        public Dictionary<string, IState> children = new Dictionary<string, IState>();

        /// <summary>
        /// 当前活跃的子状态
        /// </summary>
        protected IState activeChild = null;

        /// <summary>
        /// 默认子状态ID
        /// </summary>
        protected string defaultChildID = string.Empty;

        public virtual void OnEnter()
        {
            // 进入时激活默认子状态
            if (!string.IsNullOrEmpty(defaultChildID) && children.ContainsKey(defaultChildID))
            {
                SwitchToChild(defaultChildID);
            }
        }

        public virtual void OnExit()
        {
            // 退出时关闭所有子状态
            if (activeChild != null)
            {
                activeChild.OnExit();
                activeChild = null;
            }
        }

        public virtual void OnUpdate(float deltaTime)
        {
            // 更新当前活跃的子状态
            activeChild?.OnUpdate(deltaTime);
        }

        public virtual void OnFixedUpdate()
        {
            activeChild?.OnFixedUpdate();
        }

        public virtual bool CanTransitionTo(string stateID)
        {
            return true; // 默认允许切换到任何状态
        }

        /// <summary>
        /// 注册子状态
        /// </summary>
        public void RegisterChild(IState childState)
        {
            if (childState == null) return;

            childState.Parent = this;
            children[childState.StateID] = childState;
        }

        /// <summary>
        /// 切换到指定子状态
        /// </summary>
        public bool SwitchToChild(string childID)
        {
            if (!children.ContainsKey(childID)) return false;

            // 退出当前活跃子状态
            activeChild?.OnExit();

            // 激活新子状态
            activeChild = children[childID];
            activeChild.OnEnter();

            return true;
        }

        /// <summary>
        /// 设置默认子状态
        /// </summary>
        public void SetDefaultChild(string childID)
        {
            if (children.ContainsKey(childID))
                defaultChildID = childID;
        }

        /// <summary>
        /// 获取当前活跃子状态的完整路径
        /// </summary>
        public string GetActivePath()
        {
            var path = StateID;
            if (activeChild != null)
            {
                path += "/" + (activeChild as BaseState)?.GetActivePath() ?? activeChild.StateID;
            }
            return path;
        }
    }

}
