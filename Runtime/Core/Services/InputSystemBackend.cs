using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace UPandaGF
{
    /// <summary>
    /// 输入后端工厂：工程启用新 Input System 时使用 <see cref="InputSystemBackend"/>，
    /// 否则回退到旧版 Input Manager。是否启用由 Unity 的
    /// <c>ENABLE_INPUT_SYSTEM</c> 宏决定（Project Settings → Player → Active Input Handling）。
    /// </summary>
    public static class InputBackendFactory
    {
        public static IInputBackend Create()
        {
#if ENABLE_INPUT_SYSTEM
            return new InputSystemBackend();
#else
            return new UnityLegacyInputBackend();
#endif
        }
    }

#if ENABLE_INPUT_SYSTEM
    /// <summary>
    /// 新 Input System 后端：把 `IInputBackend` 的“轴名 / KeyCode”语义映射到
    /// Keyboard、Mouse、Gamepad 设备上，使现有 InputService 与 InputBinding 配置无需改写。
    /// <para>轴名支持旧版常用集合：Vertical / Horizontal / Mouse X / Mouse Y / Mouse ScrollWheel；
    /// 其它轴名返回 0 并只提示一次，接入自定义轴时建议直接在业务层用 InputAction。</para>
    /// </summary>
    public sealed class InputSystemBackend : IInputBackend
    {
        private static readonly HashSet<string> warnedAxes = new HashSet<string>();

        public float Axis(string name)
        {
            switch (name)
            {
                case "Vertical":
                    return Mathf.Clamp(ReadKeyboardAxis(Key.S, Key.W) + ReadGamepadAxis(g => g.leftStick.y.ReadValue()), -1f, 1f);
                case "Horizontal":
                    return Mathf.Clamp(ReadKeyboardAxis(Key.A, Key.D) + ReadGamepadAxis(g => g.leftStick.x.ReadValue()), -1f, 1f);
                case "Mouse X":
                    return Mouse.current != null ? Mouse.current.delta.ReadValue().x * 0.1f : 0f;
                case "Mouse Y":
                    return Mouse.current != null ? Mouse.current.delta.ReadValue().y * 0.1f : 0f;
                case "Mouse ScrollWheel":
                    // 旧版默认 sensitivity 0.1、一格滚动约 120，这里保持同样的观感。
                    return Mouse.current != null ? Mouse.current.scroll.ReadValue().y * (0.1f / 120f) : 0f;
                default:
                    if (warnedAxes.Add(name))
                    {
                        Debug.LogWarning("[InputSystemBackend] 未映射的轴名：" + name + "，返回 0。自定义轴请用 InputAction。");
                    }
                    return 0f;
            }
        }

        public bool Held(KeyCode key)
        {
            var mouse = Mouse.current;
            switch (key)
            {
                case KeyCode.Mouse0: return mouse != null && mouse.leftButton.isPressed;
                case KeyCode.Mouse1: return mouse != null && mouse.rightButton.isPressed;
                case KeyCode.Mouse2: return mouse != null && mouse.middleButton.isPressed;
            }

            var keyboard = Keyboard.current;
            Key mapped = ToKey(key);
            return keyboard != null && mapped != Key.None && keyboard[mapped].isPressed;
        }

        public bool Down(KeyCode key)
        {
            var mouse = Mouse.current;
            switch (key)
            {
                case KeyCode.Mouse0: return mouse != null && mouse.leftButton.wasPressedThisFrame;
                case KeyCode.Mouse1: return mouse != null && mouse.rightButton.wasPressedThisFrame;
                case KeyCode.Mouse2: return mouse != null && mouse.middleButton.wasPressedThisFrame;
            }

            var keyboard = Keyboard.current;
            Key mapped = ToKey(key);
            return keyboard != null && mapped != Key.None && keyboard[mapped].wasPressedThisFrame;
        }

        private static float ReadKeyboardAxis(Key negative, Key positive)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return 0f;
            }
            float value = 0f;
            if (keyboard[negative].isPressed) value -= 1f;
            if (keyboard[positive].isPressed) value += 1f;
            return value;
        }

        private static float ReadGamepadAxis(System.Func<Gamepad, float> read)
        {
            var gamepad = Gamepad.current;
            return gamepad != null ? read(gamepad) : 0f;
        }

        /// <summary>把旧版 KeyCode 映射到 Input System 的 Key；不支持的键返回 Key.None。</summary>
        private static Key ToKey(KeyCode key)
        {
            if (key >= KeyCode.A && key <= KeyCode.Z)
            {
                return (Key)((int)Key.A + (int)(key - KeyCode.A));
            }
            if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9)
            {
                return (Key)((int)Key.Digit0 + (int)(key - KeyCode.Alpha0));
            }
            if (key >= KeyCode.F1 && key <= KeyCode.F12)
            {
                return (Key)((int)Key.F1 + (int)(key - KeyCode.F1));
            }

            switch (key)
            {
                case KeyCode.Space: return Key.Space;
                case KeyCode.Return: return Key.Enter;
                case KeyCode.KeypadEnter: return Key.NumpadEnter;
                case KeyCode.Escape: return Key.Escape;
                case KeyCode.Tab: return Key.Tab;
                case KeyCode.Backspace: return Key.Backspace;
                case KeyCode.Delete: return Key.Delete;
                case KeyCode.LeftShift: return Key.LeftShift;
                case KeyCode.RightShift: return Key.RightShift;
                case KeyCode.LeftControl: return Key.LeftCtrl;
                case KeyCode.RightControl: return Key.RightCtrl;
                case KeyCode.LeftAlt: return Key.LeftAlt;
                case KeyCode.RightAlt: return Key.RightAlt;
                case KeyCode.UpArrow: return Key.UpArrow;
                case KeyCode.DownArrow: return Key.DownArrow;
                case KeyCode.LeftArrow: return Key.LeftArrow;
                case KeyCode.RightArrow: return Key.RightArrow;
                case KeyCode.Home: return Key.Home;
                case KeyCode.End: return Key.End;
                case KeyCode.PageUp: return Key.PageUp;
                case KeyCode.PageDown: return Key.PageDown;
                default: return Key.None;
            }
        }
    }
#endif
}
