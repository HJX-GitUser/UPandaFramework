/* ==========================================================================================
 * InputService —— 动作映射 + 上下文栈 + 运行时改键
 *
 * 【职责】
 *   按“动作名 + 上下文”查找绑定，再通过 IInputBackend 读取真实设备。
 *   绑定表由 UPGameRootConfig.inputBindings 提供默认值，业务可通过 Bind/Unbind/TryRebind 覆盖。
 *
 * 【上下文栈规则】
 *   PushContext("UI") 会把 UI 压栈；从栈顶往下找：遇到同名上下文→允许；
 *   遇到 Block=true 的条目→拒绝（阻止继续往下找）。
 *   所以“打开菜单后 Gameplay 失效、UI 仍然响应”是天然行为。
 *
 * 【暂停与输入】Gameplay 上下文在暂停时直接失效；UI 上下文不受影响。
 *
 * 【性能】绑定表为“上下文 → 动作”两级字典 + 有序快照，每帧查询不拼接字符串。
 *
 * 【运行时改键】TryRebind 做冲突/保留键校验；TryCaptureKey 捕获下一键；
 *   ExportOverrides/ApplyOverrides 配合 InputBindingOverlay 做玩家覆盖层持久化。
 * ========================================================================================== */

using System;
using System.Collections.Generic;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>
    /// 动作映射与上下文栈（实现 <see cref="IInputService"/>）。
    /// <para>暂停时屏蔽 Gameplay，UI 仍可响应。</para>
    /// <para>绑定表内部使用“上下文 → 动作”两级字典，查询不再产生字符串分配；
    /// 同时维护一份按注册顺序的绑定快照，供 <see cref="GetBindings"/> 与设置 UI 使用。</para>
    /// <para>非线程安全：仅在 Unity 主线程使用。</para>
    /// </summary>
    public sealed class InputService : IInputService, IDisposable
    {
        /// <summary>上下文栈里的一项：Name 是上下文名，Block 表示“到此为止，不继续往下找”。</summary>
        private sealed class Context { public string Name; public bool Block; }
        /// <summary>绑定表：第一级 context，第二级 action。</summary>
        private readonly Dictionary<string, Dictionary<string, InputBinding>> bindingsByContext =
            new Dictionary<string, Dictionary<string, InputBinding>>(StringComparer.Ordinal);
        /// <summary>按注册顺序保存的绑定快照（元素与字典中的实例相同）。</summary>
        private readonly List<InputBinding> orderedBindings = new List<InputBinding>();
        /// <summary>构造时的默认绑定表（供重置/导出覆盖层使用）。</summary>
        private readonly List<InputBinding> defaultBindings = new List<InputBinding>();
        /// <summary>上下文栈（栈顶 = 最后 Push 的）。</summary>
        private readonly List<Context> contexts = new List<Context>();
        private readonly IInputBackend backend;
        private readonly IPauseService pause;
        private bool disposed;

        /// <inheritdoc />
        public event Action BindingsChanged;

        /// <param name="bindings">为 null 时用 <see cref="InputBinding.Defaults"/></param>
        public InputService(IInputBackend backend, IPauseService pause, IEnumerable<InputBinding> bindings = null)
        {
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend)); this.pause = pause;
            foreach (var binding in bindings ?? InputBinding.Defaults())
            {
                var copy = binding.Clone();
                defaultBindings.Add(copy);
                BindInternal(copy);
            }
        }
        /// <summary>
        /// 注册/覆盖一条绑定（低层 API，不查冲突；设置 UI 请用 <see cref="TryRebind"/>）。
        /// <para>拷贝语义：内部保存 <see cref="InputBinding.Clone"/> 出来的副本，调用方后续改对象
        /// 不会影响已注册绑定（设置系统每次重绑都应重新 Bind 一份）。</para>
        /// </summary>
        public void Bind(InputBinding binding)
        {
            if (disposed) throw new ObjectDisposedException(nameof(InputService));
            BindInternal(binding);
            RaiseBindingsChanged();
        }
        private void BindInternal(InputBinding binding)
        {
            if (binding == null || string.IsNullOrWhiteSpace(binding.action) || string.IsNullOrWhiteSpace(binding.context))
                throw new ArgumentException("输入动作和上下文不能为空。");

            var copy = binding.Clone();
            if (!bindingsByContext.TryGetValue(copy.context, out var actions))
            {
                actions = new Dictionary<string, InputBinding>(StringComparer.Ordinal);
                bindingsByContext[copy.context] = actions;
            }
            if (actions.TryGetValue(copy.action, out var existing))
            {
                int index = orderedBindings.IndexOf(existing);
                if (index >= 0) orderedBindings[index] = copy;
            }
            else
            {
                orderedBindings.Add(copy);
            }
            actions[copy.action] = copy;
        }
        /// <summary>移除一条绑定；不存在返回 false。已释放后调用抛 <see cref="ObjectDisposedException"/>。</summary>
        public bool Unbind(string action, string context = InputContexts.Gameplay)
        {
            if (disposed) throw new ObjectDisposedException(nameof(InputService));
            if (string.IsNullOrWhiteSpace(action) || string.IsNullOrWhiteSpace(context)) return false;
            if (!bindingsByContext.TryGetValue(context, out var actions) || !actions.TryGetValue(action, out var binding)) return false;

            actions.Remove(action);
            orderedBindings.Remove(binding);
            if (actions.Count == 0) bindingsByContext.Remove(context);
            RaiseBindingsChanged();
            return true;
        }
        /// <summary>返回当前绑定快照（拷贝、按注册顺序）。Dispose 后返回空列表。</summary>
        public IReadOnlyList<InputBinding> GetBindings()
        {
            if (disposed) return Array.Empty<InputBinding>();
            var result = new List<InputBinding>(orderedBindings.Count);
            for (int i = 0; i < orderedBindings.Count; i++) result.Add(orderedBindings[i].Clone());
            return result;
        }
        /// <summary>返回构造时的默认绑定快照（拷贝）。</summary>
        public IReadOnlyList<InputBinding> GetDefaultBindings()
        {
            var result = new List<InputBinding>(defaultBindings.Count);
            for (int i = 0; i < defaultBindings.Count; i++) result.Add(defaultBindings[i].Clone());
            return result;
        }
        /// <summary>
        /// 设置层改键：校验保留键与同上下文冲突后应用。
        /// <para>保留键默认 F1–F12；如需允许调试键可传 allowReservedKey=true。
        /// 与现有绑定相同（幂等）返回 Success。</para>
        /// </summary>
        public InputRebindResult TryRebind(InputBinding binding, bool allowReservedKey = false)
        {
            if (disposed) throw new ObjectDisposedException(nameof(InputService));
            if (!IsValidBinding(binding)) return InputRebindResult.InvalidBinding;
            if (!allowReservedKey)
            {
                if (binding.positive != KeyCode.None && InputRebindRules.IsReserved(binding.positive))
                    return InputRebindResult.ReservedKey;
                if (binding.negative != KeyCode.None && InputRebindRules.IsReserved(binding.negative))
                    return InputRebindResult.ReservedKey;
            }
            var normalized = binding.Clone();
            normalized.context = string.IsNullOrWhiteSpace(normalized.context) ? InputContexts.Gameplay : normalized.context;
            if (InputRebindRules.ConflictsWithAny(normalized, orderedBindings, out _)) return InputRebindResult.Conflict;

            BindInternal(normalized);
            RaiseBindingsChanged();
            return InputRebindResult.Success;
        }
        /// <summary>捕获本帧按下的下一个可映射键（无输入返回 false）。</summary>
        public bool TryCaptureKey(out KeyCode key)
        {
            if (disposed) { key = KeyCode.None; return false; }
            return backend.TryCaptureKey(out key);
        }
        /// <summary>恢复默认绑定表（保留上下文栈）；返回是否执行了恢复。</summary>
        public bool ResetToDefaults()
        {
            if (disposed) throw new ObjectDisposedException(nameof(InputService));
            if (defaultBindings.Count == 0) return false;
            bindingsByContext.Clear();
            orderedBindings.Clear();
            for (int i = 0; i < defaultBindings.Count; i++) BindInternal(defaultBindings[i]);
            RaiseBindingsChanged();
            return true;
        }
        /// <summary>导出相对默认表的覆盖项（含新增动作）；未修改的默认项不导出。</summary>
        public InputBinding[] ExportOverrides()
        {
            var defaults = new Dictionary<string, InputBinding>(StringComparer.Ordinal);
            for (int i = 0; i < defaultBindings.Count; i++)
                defaults[Key(defaultBindings[i].action, defaultBindings[i].context)] = defaultBindings[i];

            var result = new List<InputBinding>();
            for (int i = 0; i < orderedBindings.Count; i++)
            {
                var current = orderedBindings[i];
                if (!defaults.TryGetValue(Key(current.action, current.context), out var fallback) || !BindingEquals(current, fallback))
                    result.Add(current.Clone());
            }
            return result.ToArray();
        }
        /// <summary>应用玩家覆盖层（逐条 Bind，不查冲突；用于启动恢复设置）。</summary>
        public void ApplyOverrides(IEnumerable<InputBinding> overrides)
        {
            if (disposed) throw new ObjectDisposedException(nameof(InputService));
            if (overrides == null) return;
            foreach (var binding in overrides)
            {
                if (binding == null) continue;
                BindInternal(binding);
            }
            RaiseBindingsChanged();
        }
        /// <summary>
        /// 当前是否允许某上下文：
        /// <para>1) 已释放 → 否；Gameplay 且处于暂停 → 否（但 UI 不受影响）。</para>
        /// <para>2) 从栈顶往下扫：遇到同名上下文→允许；遇到 Block→拒绝；扫到底→默认允许。</para>
        /// </summary>
        private bool Allowed(string context)
        {
            if (disposed || (context == InputContexts.Gameplay && pause != null && pause.IsPaused)) return false;
            for (int i = contexts.Count - 1; i >= 0; i--)
            {
                if (contexts[i].Name == context) return true;
                if (contexts[i].Block) return false;
            }
            return true;
        }
        /// <summary>找到可用绑定；上下文被屏蔽或未注册时返回 null（调用方都当 0/false 处理）。</summary>
        private InputBinding Find(string action, string context)
        {
            if (!Allowed(context)) return null;
            return bindingsByContext.TryGetValue(context, out var actions) && actions.TryGetValue(action, out var binding)
                ? binding
                : null;
        }
        /// <summary>读轴值：有 axis 用轴；否则 positive/negative 相减得到 -1/0/1。</summary>
        public float GetAxis(string action, string context = InputContexts.Gameplay)
        {
            var binding = Find(action, context); if (binding == null) return 0;
            if (!string.IsNullOrEmpty(binding.axis)) return backend.Axis(binding.axis);
            return (binding.positive != KeyCode.None && backend.Held(binding.positive) ? 1 : 0) - (binding.negative != KeyCode.None && backend.Held(binding.negative) ? 1 : 0);
        }
        /// <summary>按键是否按住（只看 positive）。</summary>
        public bool GetButton(string action, string context = InputContexts.Gameplay)
        { var b = Find(action, context); return b != null && b.positive != KeyCode.None && backend.Held(b.positive); }
        /// <summary>按键是否在本帧按下（只看 positive）。</summary>
        public bool GetButtonDown(string action, string context = InputContexts.Gameplay)
        { var b = Find(action, context); return b != null && b.positive != KeyCode.None && backend.Down(b.positive); }
        /// <summary>按键是否在本帧松开（只看 positive）。</summary>
        public bool GetButtonUp(string action, string context = InputContexts.Gameplay)
        { var b = Find(action, context); return b != null && b.positive != KeyCode.None && backend.Up(b.positive); }
        /// <summary>
        /// 压入一个输入上下文，返回出栈凭据（Dispose 即弹出）。
        /// <para>blockLower=true：从此往下屏蔽更低优先级的上下文（典型：打开 UI 后屏蔽 Gameplay）。</para>
        /// </summary>
        public IDisposable PushContext(string context, bool blockLower = true)
        {
            if (disposed) throw new ObjectDisposedException(nameof(InputService));
            if (string.IsNullOrWhiteSpace(context)) throw new ArgumentException("输入上下文不能为空。");
            var entry = new Context { Name = context, Block = blockLower }; contexts.Add(entry);
            return new DisposableAction(() => contexts.Remove(entry));
        }
        /// <summary>
        /// 释放：清空上下文与绑定。
        /// <para>Dispose 后：GetAxis/GetButton/GetButtonDown/GetButtonUp/GetBindings 静默返回
        /// 0/false/空列表；Bind/Unbind/PushContext/TryRebind/ResetToDefaults/ApplyOverrides
        /// 抛 <see cref="ObjectDisposedException"/>。</para>
        /// </summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; contexts.Clear(); bindingsByContext.Clear(); orderedBindings.Clear(); defaultBindings.Clear();
            BindingsChanged = null;
        }

        private void RaiseBindingsChanged() => BindingsChanged?.Invoke();

        private static string Key(string action, string context) => context + "/" + action;

        private static bool IsValidBinding(InputBinding binding)
        {
            if (binding == null || string.IsNullOrWhiteSpace(binding.action) || string.IsNullOrWhiteSpace(binding.context)) return false;
            bool hasAxis = !string.IsNullOrEmpty(binding.axis);
            bool hasPositive = binding.positive != KeyCode.None;
            bool hasNegative = binding.negative != KeyCode.None;
            if (hasAxis && (hasPositive || hasNegative)) return false;
            if (!hasAxis && !hasPositive && !hasNegative) return false;
            if (hasPositive && hasNegative && binding.positive == binding.negative) return false;
            return true;
        }

        private static bool BindingEquals(InputBinding left, InputBinding right)
        {
            if (left == null || right == null) return false;
            return left.action == right.action
                && left.context == right.context
                && left.axis == right.axis
                && left.positive == right.positive
                && left.negative == right.negative;
        }
    }
}
