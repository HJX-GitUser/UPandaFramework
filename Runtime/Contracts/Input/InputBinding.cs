using System;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>
    /// 一条动作绑定。
    /// <para>axis 与 positive/negative 二选一：填了 axis 就用轴（支持手柄/平滑），
    /// 否则用两个按键相减得到 -1/0/1。</para>
    /// <para>context 决定它属于哪个输入上下文（Gameplay / UI ...），默认 Gameplay。</para>
    /// <para>0.8.2 起该类型从 Core 下沉到 Contracts，使 <see cref="IInputService"/> 可以在
    /// 契约层暴露绑定查询/重绑，而业务程序集无需引用 Core。</para>
    /// </summary>
    [Serializable]
    public sealed class InputBinding
    {
        public string action;
        public string context = InputContexts.Gameplay;
        [Tooltip("旧版输入轴名（如 Vertical / Horizontal / Mouse X）；与 positive/negative 二选一。")]
        public string axis;
        public KeyCode positive, negative;

        /// <summary>返回一份字段级拷贝：注册/查询都使用拷贝，避免调用方后续修改影响内部状态。</summary>
        public InputBinding Clone()
        {
            return new InputBinding
            {
                action = action,
                context = context,
                axis = axis,
                positive = positive,
                negative = negative
            };
        }

        /// <summary>工程默认绑定表（可被 UPGameRootConfig.inputBindings 覆盖）。</summary>
        public static InputBinding[] Defaults() => new[] {
            new InputBinding { action = "MoveVertical", axis = "Vertical" },
            new InputBinding { action = "MoveHorizontal", axis = "Horizontal" },
            new InputBinding { action = "Handbrake", positive = KeyCode.Space },
            new InputBinding { action = "CameraCycle", positive = KeyCode.C },
            new InputBinding { action = "Camera1", positive = KeyCode.Alpha1 },
            new InputBinding { action = "Camera2", positive = KeyCode.Alpha2 },
            new InputBinding { action = "Camera3", positive = KeyCode.Alpha3 },
            new InputBinding { action = "Pause", positive = KeyCode.Escape },
            new InputBinding { action = "Interact", positive = KeyCode.E },
            new InputBinding { action = "Jump", positive = KeyCode.Space },
            new InputBinding { action = "Confirm", context = InputContexts.UI, positive = KeyCode.Return },
            new InputBinding { action = "Cancel", context = InputContexts.UI, positive = KeyCode.Escape },
            new InputBinding { action = "NavigateVertical", context = InputContexts.UI, axis = "Vertical" },
            new InputBinding { action = "NavigateHorizontal", context = InputContexts.UI, axis = "Horizontal" }
        };
    }
}
