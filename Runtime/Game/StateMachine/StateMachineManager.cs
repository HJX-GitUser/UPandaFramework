using System;
using System.Collections.Generic;
using UnityEngine;

namespace UPandaGF.Gameplay.StateMachine
{
    /// <summary>分层状态机。活动叶状态与成功切换历史分开；状态 ID 在一台机器中唯一。</summary>
    public class StateMachineManager : MonoBehaviour
    {
        private IState rootState;
        private readonly Dictionary<string, IState> stateRegistry = new Dictionary<string, IState>();
        private readonly Stack<IState> stateStack = new Stack<IState>();
        private bool transitioning;
        private IState lastActiveLeaf;
        public string ActiveStatePath { get; private set; } = "None";
        public IState CurrentState => GetCurrentContext();
        public int HistoryCount => stateStack.Count;
        public event Action<string, string> OnStateChanged;

        private void Update()
        {
            rootState?.OnUpdate(Time.deltaTime);
            // 活动叶没变就不重建路径；避免每帧 List/HashSet/string 分配。
            if (!ReferenceEquals(GetCurrentContext(), lastActiveLeaf)) RefreshActivePath();
        }
        private void FixedUpdate() { rootState?.OnFixedUpdate(); }
        public void RegisterState(IState state, string parentStateID = "")
        {
            if (state == null || stateRegistry.ContainsKey(state.StateID)) return;
            if (string.IsNullOrEmpty(parentStateID))
            {
                if (rootState != null) { Debug.LogError("只能注册一个根状态。"); return; }
                state.Parent = null;
                rootState = state;
                stateRegistry.Add(state.StateID, state);
                state.OnEnter();
                RefreshActivePath();
                return;
            }
            if (!stateRegistry.TryGetValue(parentStateID, out var parent) || !(parent is BaseState baseParent))
            { Debug.LogError("父状态不存在或不是 BaseState：" + parentStateID); return; }
            baseParent.RegisterChild(state);
            stateRegistry.Add(state.StateID, state);
        }
        public bool SwitchState(string statePath)
        {
            if (transitioning || !TryResolve(statePath, out var target)) return false;
            return Transition(target, true);
        }
        private bool TryResolve(string path, out IState target)
        {
            target = null;
            if (string.IsNullOrWhiteSpace(path)) return false;
            var parts = path.Split('/');
            if (!stateRegistry.TryGetValue(parts[0], out target)) return false;
            // 支持唯一 ID、根路径及以任意已注册祖先起始的路径。
            for (int i = 1; i < parts.Length; i++)
            {
                if (!(target is BaseState parent) || !parent.children.TryGetValue(parts[i], out target)) return false;
            }
            return true;
        }
        private bool Transition(IState target, bool remember)
        {
            var previous = GetCurrentContext();
            if (ReferenceEquals(previous, target)) return true;
            var chain = Chain(target);
            if (chain.Count == 0 || !ReferenceEquals(chain[0], rootState)) return false;
            transitioning = true;
            string oldPath = ActiveStatePath;
            bool changed = false;
            try
            {
                // 无论 ID 还是多级路径，均由真实活动叶节点及其祖先决定是否允许。
                for (var current = previous; current != null; current = current.Parent)
                    if (!current.CanTransitionTo(target.StateID)) return false;
                changed = true;
                Activate(chain);
                RefreshActivePath();
                if (remember && previous != null) stateStack.Push(previous);
            }
            catch (Exception e)
            {
                // 路径及约束失败不会触发生命周期；用户生命周期抛错时尽力恢复活动结构。
                if (changed && previous != null)
                    try { Activate(Chain(previous)); } catch (Exception rollback) { Debug.LogException(rollback); }
                RefreshActivePath();
                Debug.LogException(e);
                return false;
            }
            finally { transitioning = false; }
            transitioning = true;
            try
            {
                if (OnStateChanged != null)
                    foreach (Action<string, string> listener in OnStateChanged.GetInvocationList())
                        try { listener(oldPath, ActiveStatePath); } catch (Exception e) { Debug.LogException(e); }
            }
            finally { transitioning = false; }
            return true;
        }
        private static List<IState> Chain(IState state)
        {
            var result = new List<IState>();
            var seen = new HashSet<IState>();
            while (state != null)
            {
                if (!seen.Add(state)) throw new InvalidOperationException("状态父链存在循环。");
                result.Add(state); state = state.Parent;
            }
            result.Reverse(); return result;
        }
        private static void Activate(List<IState> chain)
        {
            bool entered = false;
            for (int i = 0; i < chain.Count - 1; i++)
            {
                var parent = (BaseState)chain[i];
                if (!ReferenceEquals(parent.ActiveChild, chain[i + 1]))
                {
                    entered = true;
                    parent.SwitchToChild(chain[i + 1].StateID);
                }
            }
            if (!entered && chain[chain.Count - 1] is BaseState ancestor) ancestor.ClearActiveChild();
        }
        public bool GoBack()
        {
            if (transitioning || stateStack.Count == 0) return false;
            var target = stateStack.Peek();
            if (!Transition(target, false)) return false;
            stateStack.Pop();
            return true;
        }
        private IState GetCurrentContext()
        {
            var state = rootState;
            while (state is BaseState parent && parent.ActiveChild != null) state = parent.ActiveChild;
            return state;
        }
        private void RefreshActivePath()
        {
            if (rootState == null) { ActiveStatePath = "None"; lastActiveLeaf = null; return; }
            var current = GetCurrentContext();
            lastActiveLeaf = current;
            var names = new List<string>();
            foreach (var state in Chain(current)) names.Add(state.StateID);
            ActiveStatePath = string.Join("/", names);
        }
        public string GetStateInfo()
        {
            var text = new System.Text.StringBuilder($"当前状态路径: {ActiveStatePath}\n状态栈深度: {stateStack.Count}\n状态层级结构:\n");
            AppendTree(text, rootState, 0);
            return text.ToString();
        }
        private void AppendTree(System.Text.StringBuilder text, IState state, int depth)
        {
            if (state == null) return;
            text.Append(' ', depth * 2).Append(state.StateID);
            if (ReferenceEquals(state, CurrentState)) text.Append(" (Current)");
            text.AppendLine();
            if (state is BaseState parent)
                foreach (var child in parent.children.Values) AppendTree(text, child, depth + 1);
        }
        private void OnDestroy()
        {
            try { rootState?.OnExit(); }
            finally { rootState = null; stateRegistry.Clear(); stateStack.Clear(); OnStateChanged = null; ActiveStatePath = "None"; }
        }
    }
}
