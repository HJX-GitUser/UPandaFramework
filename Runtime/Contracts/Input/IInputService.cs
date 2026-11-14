using System;
using System.Collections.Generic;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>
    /// 动作输入服务（契约）：按“动作名 + 上下文”查询输入，管理输入上下文栈与运行时绑定。
    /// <para>实现：Runtime/Core/Input/InputService.cs；后端选择见 InputBackendFactory；
    /// 绑定类型见 <see cref="InputBinding"/>，上下文常量见 <see cref="InputContexts"/>。</para>
    /// <para>查询方法在上下文被屏蔽/暂停（Gameplay）或绑定不存在时返回 0/false；
    /// Dispose 后查询返回 0/false、GetBindings 返回空列表，Bind/Unbind/PushContext 抛
    /// <see cref="ObjectDisposedException"/>。</para>
    /// <para>非线程安全：只在 Unity 主线程使用。</para>
    /// </summary>
    public interface IInputService
    {
        /// <summary>读动作轴值：绑定有 axis 时读轴，否则 positive/negative 相减得到 -1/0/1；无绑定/被屏蔽返回 0。</summary>
        float GetAxis(string action, string context = InputContexts.Gameplay);
        /// <summary>动作主键是否按住。</summary>
        bool GetButton(string action, string context = InputContexts.Gameplay);
        /// <summary>动作主键是否在本帧按下。</summary>
        bool GetButtonDown(string action, string context = InputContexts.Gameplay);
        /// <summary>动作主键是否在本帧松开（0.8.2 新增；用于蓄力/长按等边沿检测）。</summary>
        bool GetButtonUp(string action, string context = InputContexts.Gameplay);
        /// <summary>注册/覆盖一条绑定（拷贝语义）；非法参数抛 <see cref="ArgumentException"/>。</summary>
        void Bind(InputBinding binding);
        /// <summary>移除一条绑定；不存在返回 false。</summary>
        bool Unbind(string action, string context = InputContexts.Gameplay);
        /// <summary>返回当前绑定表的快照（拷贝，按注册顺序）；调用方修改不影响内部状态。</summary>
        IReadOnlyList<InputBinding> GetBindings();
        /// <summary>
        /// 压入一个输入上下文，返回出栈凭据（Dispose 即弹出）。
        /// <para>blockLower=true 时从此往下屏蔽更低优先级的上下文（典型：UI 屏蔽 Gameplay）。</para>
        /// </summary>
        IDisposable PushContext(string context, bool blockLower = true);

        /// <summary>绑定表发生变化（Bind/Unbind/TryRebind/ResetToDefaults/ApplyOverrides）时触发。</summary>
        event Action BindingsChanged;

        /// <summary>
        /// 设置层改键：校验冲突/保留键后应用绑定；不通过时返回原因且不修改现有绑定。
        /// <para>与现有绑定完全相同视为成功（幂等）。</para>
        /// </summary>
        InputRebindResult TryRebind(InputBinding binding, bool allowReservedKey = false);

        /// <summary>
        /// 捕获本帧按下的下一个可映射按键（Keyboard/Mouse/Gamepad → KeyCode）。
        /// <para>用于设置面板“按下新键”捕获态；无输入时返回 false。调用方应在捕获期间
        /// 压入 <see cref="InputContexts.Rebind"/> 上下文以屏蔽普通操作。</para>
        /// </summary>
        bool TryCaptureKey(out KeyCode key);

        /// <summary>恢复为构造时的默认绑定表（清空运行时覆盖）；返回是否执行了恢复。</summary>
        bool ResetToDefaults();

        /// <summary>返回构造时的默认绑定表快照（供设置 UI 展示“默认值”）。</summary>
        IReadOnlyList<InputBinding> GetDefaultBindings();

        /// <summary>导出相对默认表的覆盖项（可直接写入 <see cref="InputBindingOverlay"/>）。</summary>
        InputBinding[] ExportOverrides();

        /// <summary>应用玩家覆盖层（逐条 Bind；不校验冲突，用于启动时恢复存档设置）。</summary>
        void ApplyOverrides(IEnumerable<InputBinding> overrides);
    }
}
