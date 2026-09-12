using System;
using System.Collections.Generic;
using UnityEngine;


namespace UPandaGF.StateMechine
{
    /// <summary>
    /// 分层状态管理器
    /// </summary>
    public class StateMachineManager : MonoBehaviour
    {
        /// <summary>
        /// 根状态
        /// </summary>
        private IState rootState = null;

        /// <summary>
        /// 状态ID到状态的映射（用于快速查找）
        /// </summary>
        private Dictionary<string, IState> stateRegistry = new Dictionary<string, IState>();

        /// <summary>
        /// 状态栈（用于返回上一级状态）
        /// </summary>
        private Stack<IState> stateStack = new Stack<IState>();

        /// <summary>
        /// 当前活跃状态的完整路径
        /// </summary>
        public string ActiveStatePath { get; private set; } = "None";

        /// <summary>
        /// 状态改变事件
        /// </summary>
        public event Action<string, string> OnStateChanged;

        /// <summary>
        /// 路径是否已失效（状态切换后置为 true，需重新计算，避免每帧递归拼接字符串）
        /// </summary>
        private bool pathDirty = true;

        /// <summary>
        /// 多级切换记录：记录已切换的父状态及其原活跃子状态（用于失败回滚）
        /// </summary>
        private struct SwitchRecord
        {
            public BaseState parent;
            public IState oldChild;
        }

        void Update()
        {
            if (rootState != null)
            {
                rootState.OnUpdate(Time.deltaTime);
                // 仅当路径失效时才重新计算
                if (pathDirty)
                {
                    UpdateActivePath();
                    pathDirty = false;
                }
            }
        }

        void FixedUpdate()
        {
            rootState?.OnFixedUpdate();
        }

        /// <summary>
        /// 注册状态（自动构建层级关系）
        /// </summary>
        public void RegisterState(IState state, string parentStateID = "")
        {
            if (state == null) return;

            // 检查是否已注册
            if (stateRegistry.ContainsKey(state.StateID))
            {
                Debug.LogWarning($"状态 {state.StateID} 已注册");
                return;
            }

            // 添加到注册表
            stateRegistry[state.StateID] = state;

            // 设置父子关系
            if (!string.IsNullOrEmpty(parentStateID))
            {
                if (stateRegistry.TryGetValue(parentStateID, out var parentState))
                {
                    if (parentState is BaseState baseParent)
                    {
                        baseParent.RegisterChild(state);
                    }
                    else
                    {
                        // 父状态不是 BaseState，无法挂载子状态，回滚注册
                        stateRegistry.Remove(state.StateID);
                        Debug.LogError($"父状态 {parentStateID} 不是 BaseState，无法注册子状态 {state.StateID}");
                    }
                }
                else
                {
                    // 父状态未找到，回滚注册
                    stateRegistry.Remove(state.StateID);
                    Debug.LogError($"父状态 {parentStateID} 未找到，无法注册子状态 {state.StateID}");
                }
            }
            else
            {
                // 如果没有指定父状态，设置为根状态
                if (rootState == null)
                {
                    rootState = state;
                    rootState.OnEnter();
                    pathDirty = true;
                }
                else
                {
                    // 根状态已存在，回滚注册
                    stateRegistry.Remove(state.StateID);
                    Debug.LogError($"已存在根状态 {rootState.StateID}，无法设置新根状态 {state.StateID}");
                }
            }
        }

        /// <summary>
        /// 切换状态（支持层级路径）
        /// </summary>
        /// <param name="statePath">状态路径（例如："Movement/Run"）</param>
        public bool SwitchState(string statePath)
        {
            if (string.IsNullOrEmpty(statePath)) return false;

            string[] pathSegments = statePath.Split('/');

            if (pathSegments.Length == 1)
            {
                // 单级路径：直接切换根状态或当前上下文的子状态
                return SwitchToState(statePath);
            }

            // 多级路径：逐级切换，失败时回滚
            IState currentState = GetCurrentContext();
            if (currentState == null)
            {
                Debug.LogWarning("没有可用的状态上下文，无法切换");
                return false;
            }

            string oldPath = ActiveStatePath;

            // 把切换前的上下文压栈，便于 GoBack 返回
            stateStack.Push(currentState);

            // 记录已切换的父状态与其原活跃子状态（用于失败回滚）
            var switchRecord = new Stack<SwitchRecord>();

            for (int i = 0; i < pathSegments.Length; i++)
            {
                var baseState = currentState as BaseState;
                if (baseState == null)
                {
                    Rollback(switchRecord);
                    return false;
                }

                IState oldChild = baseState.ActiveChild;
                if (!baseState.SwitchToChild(pathSegments[i]))
                {
                    Rollback(switchRecord);
                    return false;
                }

                switchRecord.Push(new SwitchRecord { parent = baseState, oldChild = oldChild });
                currentState = baseState.ActiveChild;
            }

            // 更新路径并触发事件
            RefreshActivePath();
            OnStateChanged?.Invoke(oldPath, ActiveStatePath);
            return true;
        }

        /// <summary>
        /// 切换到指定状态
        /// </summary>
        private bool SwitchToState(string stateID)
        {
            if (!stateRegistry.TryGetValue(stateID, out var targetState)) return false;

            // 检查是否可以切换
            if (!CanTransitionTo(targetState)) return false;

            // 记录旧状态
            var oldPath = ActiveStatePath;

            // 处理状态栈（如果新状态是当前状态的子状态，压栈）
            var currentState = GetCurrentContext();
            if (currentState != null && targetState.Parent == currentState)
            {
                stateStack.Push(currentState);
            }

            // 切换状态
            if (targetState.Parent == null)
            {
                // 切换根状态：清空旧状态栈（已失效），退出旧根并进入新根
                stateStack.Clear();
                rootState?.OnExit();
                rootState = targetState;
                rootState.OnEnter();
            }
            else
            {
                // 通过父状态切换
                if (targetState.Parent is BaseState parentBase)
                {
                    if (!parentBase.SwitchToChild(stateID)) return false;
                }
                else
                {
                    return false;
                }
            }

            // 更新路径并触发事件
            RefreshActivePath();
            OnStateChanged?.Invoke(oldPath, ActiveStatePath);

            return true;
        }

        /// <summary>
        /// 返回上一级状态
        /// </summary>
        public bool GoBack()
        {
            if (stateStack.Count == 0) return false;

            var previousState = stateStack.Pop();
            return SwitchToState(previousState.StateID);
        }

        /// <summary>
        /// 获取当前上下文状态（状态栈顶或根状态）
        /// </summary>
        private IState GetCurrentContext()
        {
            return stateStack.Count > 0 ? stateStack.Peek() : rootState;
        }

        /// <summary>
        /// 检查是否可以切换到目标状态
        /// </summary>
        private bool CanTransitionTo(IState targetState)
        {
            var currentState = GetCurrentContext();
            if (currentState == null) return true;

            // 沿层级链向上，逐级检查当前状态是否允许切换到目标状态
            var temp = currentState;
            while (temp != null)
            {
                if (!temp.CanTransitionTo(targetState.StateID))
                    return false;

                // 到达目标状态的父层级后停止（更高层级无需检查）
                if (temp.Parent == null || temp.Parent == targetState.Parent)
                    break;

                temp = temp.Parent;
            }

            return true;
        }

        /// <summary>
        /// 更新当前活跃路径
        /// </summary>
        private void UpdateActivePath()
        {
            if (rootState is BaseState baseRoot)
            {
                ActiveStatePath = baseRoot.GetActivePath();
            }
            else
            {
                ActiveStatePath = rootState?.StateID ?? "None";
            }
        }

        /// <summary>
        /// 更新活跃路径并标记为已刷新
        /// </summary>
        private void RefreshActivePath()
        {
            UpdateActivePath();
            pathDirty = false;
        }

        /// <summary>
        /// 回滚多级切换：逐级恢复原活跃子状态，并弹出切换前压入的状态栈
        /// </summary>
        private void Rollback(Stack<SwitchRecord> record)
        {
            // 逆序恢复各级原活跃子状态
            while (record.Count > 0)
            {
                var r = record.Pop();
                if (r.oldChild != null)
                {
                    r.parent.SwitchToChild(r.oldChild.StateID);
                }
                else
                {
                    r.parent.ClearActiveChild();
                }
            }

            // 弹出多级切换前压入的状态栈
            if (stateStack.Count > 0) stateStack.Pop();
        }

        /// <summary>
        /// 获取状态信息（用于调试）
        /// </summary>
        public string GetStateInfo()
        {
            var info = $"当前状态路径: {ActiveStatePath}\n";
            info += $"状态栈深度: {stateStack.Count}\n";

            if (rootState is BaseState baseRoot)
            {
                info += "状态层级结构:\n";
                info += PrintStateTree(baseRoot, 0);
            }

            return info;
        }

        private string PrintStateTree(BaseState state, int indent)
        {
            var prefix = new string(' ', indent * 2);
            var result = $"{prefix}[{state.StateID}]";

            if (state == (rootState as BaseState))
                result += " (Root)";

            if (state == (GetCurrentContext() as BaseState))
                result += " (Current)";

            result += "\n";

            foreach (var child in state.children.Values)
            {
                if (child is BaseState childBase)
                {
                    result += PrintStateTree(childBase, indent + 1);
                }
            }

            return result;
        }
    }
}
