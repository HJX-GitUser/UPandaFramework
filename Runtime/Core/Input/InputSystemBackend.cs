using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
#endif

namespace UPandaGF
{
    /// <summary>
    /// 输入后端工厂：工程启用新 Input System 时使用 <see cref="InputSystemBackend"/>，
    /// 否则回退到旧版 Input Manager。是否启用由 Unity 的
    /// <c>ENABLE_INPUT_SYSTEM</c> 宏决定（Project Settings → Player → Active Input Handling）。
    /// <para>0.8.2 增加能力查询：配置校验与设置 UI 可提前知道“当前后端是否支持该轴名/按键”，
    /// 避免配置合法但运行期静默无效。</para>
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

        /// <summary>当前工程是否使用新输入系统后端（New 或 Both 均为 true）。</summary>
        public static bool UsesInputSystemBackend
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// 当前后端是否支持该旧版轴名。
        /// <para>旧 InputManager 后端返回 true（轴是否存在由工程 InputManager 决定，编译期无法得知）；
        /// 新后端返回其 5 个白名单轴的判断结果。</para>
        /// </summary>
        public static bool SupportsAxis(string axis)
        {
#if ENABLE_INPUT_SYSTEM
            return InputSystemBackend.SupportsAxis(axis);
#else
            return true;
#endif
        }

        /// <summary>
        /// 当前后端是否支持该 KeyCode。
        /// <para>旧 InputManager 后端返回 true（由引擎决定）；新后端按映射表判断。</para>
        /// </summary>
        public static bool SupportsKey(KeyCode key)
        {
#if ENABLE_INPUT_SYSTEM
            return InputSystemBackend.SupportsKey(key);
#else
            return true;
#endif
        }
    }

#if ENABLE_INPUT_SYSTEM
    /// <summary>
    /// 新 Input System 后端：把 `IInputBackend` 的“轴名 / KeyCode”语义映射到
    /// Keyboard、Mouse、Gamepad 设备上，使现有 InputService 与 InputBinding 配置无需改写。
    /// <para>0.8.2 修复并补齐：
    /// 数字键 Alpha0–9 显式映射到 Digit0/Digit1…Digit9；
    /// Vertical/Horizontal 支持方向键；
    /// 小键盘、标点、Insert/CapsLock/NumLock/Print/Pause/Menu、鼠标侧键、
    /// Gamepad 按钮（KeyCode.JoystickButton0–15）均可读取；
    /// 未映射的按键只警告一次，不再静默。</para>
    /// <para>轴名支持旧版常用集合：Vertical / Horizontal / Mouse X / Mouse Y / Mouse ScrollWheel；
    /// 其它轴名返回 0 并只提示一次。自定义轴/组合键建议后续迁移到 InputAction 后端。</para>
    /// </summary>
    public sealed class InputSystemBackend : IInputBackend
    {
        private static readonly HashSet<string> warnedAxes = new HashSet<string>();
        private static readonly HashSet<KeyCode> warnedKeys = new HashSet<KeyCode>();
        /// <summary>Key → KeyCode 反向映射（用于捕获“按下的键”）；惰性构建。</summary>
        private static Dictionary<Key, KeyCode> reverseKeyMap;

        /// <summary>新后端支持的旧版轴名（与 <see cref="Axis"/> 的白名单一致）。</summary>
        private static readonly HashSet<string> supportedAxes = new HashSet<string>
        {
            "Vertical", "Horizontal", "Mouse X", "Mouse Y", "Mouse ScrollWheel"
        };

        /// <summary>
        /// KeyCode.JoystickButton0–15 对应的 Gamepad 按钮顺序（框架约定，0.8.2 起生效）。
        /// <para>参考 Unity 旧 InputManager 默认映射：button0=Fire1(A/Cross)、1=Fire2(B/Circle)、
        /// 2=Fire3(X/Square)、3=Jump(Y/Triangle)；其余为框架补充顺序。</para>
        /// </summary>
        private static readonly GamepadButton[] joystickButtonMap =
        {
            GamepadButton.South,         // 0：A / Cross
            GamepadButton.East,          // 1：B / Circle
            GamepadButton.West,          // 2：X / Square
            GamepadButton.North,         // 3：Y / Triangle
            GamepadButton.LeftShoulder,  // 4：LB / L1
            GamepadButton.RightShoulder, // 5：RB / R1
            GamepadButton.Select,        // 6：Back / Share
            GamepadButton.Start,         // 7：Start / Options
            GamepadButton.LeftStick,     // 8：L3
            GamepadButton.RightStick,    // 9：R3
            GamepadButton.DpadUp,        // 10
            GamepadButton.DpadDown,      // 11
            GamepadButton.DpadLeft,      // 12
            GamepadButton.DpadRight,     // 13
            GamepadButton.LeftTrigger,   // 14：LT / L2
            GamepadButton.RightTrigger   // 15：RT / R2
        };

        public static bool SupportsAxis(string axis) => !string.IsNullOrEmpty(axis) && supportedAxes.Contains(axis);

        public static bool SupportsKey(KeyCode key)
        {
            if (key == KeyCode.None) return true;
            if (key >= KeyCode.Mouse0 && key <= KeyCode.Mouse6)
            {
                // Mouse0/1/2 + Mouse3(back)/Mouse4(forward) 已支持；Mouse5/6 暂不支持。
                return key <= KeyCode.Mouse4;
            }
            if (key >= KeyCode.JoystickButton0 && key <= KeyCode.JoystickButton19)
                return key <= KeyCode.JoystickButton15;
            if (key >= KeyCode.Joystick1Button0 && key <= KeyCode.Joystick8Button19) return false;
            return ToKey(key) != Key.None;
        }

        public float Axis(string name)
        {
            switch (name)
            {
                case "Vertical":
                    return Mathf.Clamp(
                        ReadKeyboardAxis(Key.S, Key.W, Key.DownArrow, Key.UpArrow) + ReadGamepadY(), -1f, 1f);
                case "Horizontal":
                    return Mathf.Clamp(
                        ReadKeyboardAxis(Key.A, Key.D, Key.LeftArrow, Key.RightArrow) + ReadGamepadX(), -1f, 1f);
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
                        Debug.LogWarning("[InputSystemBackend] 未映射的轴名：" + name
                            + "，返回 0。新后端仅支持 Vertical/Horizontal/Mouse X/Mouse Y/Mouse ScrollWheel；"
                            + "自定义轴建议后续迁移 InputAction。");
                    }
                    return 0f;
            }
        }

        public bool Held(KeyCode key)
        {
            ButtonControl button = ResolveButton(key, out bool supported);
            return supported && button != null && button.isPressed;
        }

        public bool Down(KeyCode key)
        {
            ButtonControl button = ResolveButton(key, out bool supported);
            return supported && button != null && button.wasPressedThisFrame;
        }

        public bool Up(KeyCode key)
        {
            ButtonControl button = ResolveButton(key, out bool supported);
            return supported && button != null && button.wasReleasedThisFrame;
        }

        /// <summary>
        /// 捕获本帧按下的下一个键：鼠标侧键 → 手柄按钮 → 键盘。
        /// <para>只返回两套后端都能表达的 KeyCode；未映射键直接跳过。</para>
        /// </summary>
        public bool TryCaptureKey(out KeyCode key)
        {
            key = KeyCode.None;

            var mouse = Mouse.current;
            if (mouse != null)
            {
                if (mouse.leftButton.wasPressedThisFrame) { key = KeyCode.Mouse0; return true; }
                if (mouse.rightButton.wasPressedThisFrame) { key = KeyCode.Mouse1; return true; }
                if (mouse.middleButton.wasPressedThisFrame) { key = KeyCode.Mouse2; return true; }
                if (mouse.backButton.wasPressedThisFrame) { key = KeyCode.Mouse3; return true; }
                if (mouse.forwardButton.wasPressedThisFrame) { key = KeyCode.Mouse4; return true; }
            }

            var gamepad = Gamepad.current;
            if (gamepad != null)
            {
                for (int i = 0; i < joystickButtonMap.Length; i++)
                {
                    if (!gamepad[joystickButtonMap[i]].wasPressedThisFrame) continue;
                    key = (KeyCode)((int)KeyCode.JoystickButton0 + i);
                    return true;
                }
            }

            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                var map = ReverseKeyMap;
                var keys = keyboard.allKeys;
                for (int i = 0; i < keys.Count; i++)
                {
                    var control = keys[i];
                    if (control == null || !control.wasPressedThisFrame) continue;
                    if (map.TryGetValue(control.keyCode, out key)) return true;
                }
            }
            return false;
        }

        private static Dictionary<Key, KeyCode> ReverseKeyMap
        {
            get
            {
                if (reverseKeyMap != null) return reverseKeyMap;
                var map = new Dictionary<Key, KeyCode>();
                var candidates = InputCaptureKeys.All;
                for (int i = 0; i < candidates.Length; i++)
                {
                    var keyCode = candidates[i];
                    if (keyCode >= KeyCode.Mouse0) continue; // 鼠标/手柄按键由 TryCaptureKey 单独处理。
                    var mapped = ToKey(keyCode);
                    if (mapped == Key.None || map.ContainsKey(mapped)) continue;
                    map[mapped] = keyCode;
                }
                reverseKeyMap = map;
                return reverseKeyMap;
            }
        }

        /// <summary>
        /// 把 KeyCode 解析成按钮控件；解析不到时按“设备不可用/不支持”返回 false。
        /// <para>统一入口让 Held/Down/Up 使用同一套映射与告警去重逻辑。</para>
        /// </summary>
        private static ButtonControl ResolveButton(KeyCode key, out bool supported)
        {
            supported = false;
            if (key == KeyCode.None) return null;

            // 鼠标：Mouse0/1/2 + Mouse3(back)/Mouse4(forward)。Mouse5/6 暂不支持。
            if (key >= KeyCode.Mouse0 && key <= KeyCode.Mouse6)
            {
                var mouse = Mouse.current;
                if (mouse == null) return null;
                switch (key)
                {
                    case KeyCode.Mouse0: supported = true; return mouse.leftButton;
                    case KeyCode.Mouse1: supported = true; return mouse.rightButton;
                    case KeyCode.Mouse2: supported = true; return mouse.middleButton;
                    case KeyCode.Mouse3: supported = true; return mouse.backButton;
                    case KeyCode.Mouse4: supported = true; return mouse.forwardButton;
                    default: return UnsupportedKey(key);
                }
            }

            // 手柄按钮：JoystickButton0–15 映射到标准 Gamepad 按钮。
            if (key >= KeyCode.JoystickButton0 && key <= KeyCode.JoystickButton19)
            {
                int index = (int)key - (int)KeyCode.JoystickButton0;
                if (index >= joystickButtonMap.Length) return UnsupportedKey(key);
                var gamepad = Gamepad.current;
                if (gamepad == null) return null; // 设备未连接：静默返回 false，由调用方按无输入处理。
                supported = true;
                return gamepad[joystickButtonMap[index]];
            }

            // 其他玩家槽位的手柄按键（Joystick1Button0…）当前不支持；不要再落到键盘映射里。
            if (key >= KeyCode.Joystick1Button0 && key <= KeyCode.Joystick8Button19) return UnsupportedKey(key);

            var keyboard = Keyboard.current;
            Key mapped = ToKey(key);
            if (keyboard == null || mapped == Key.None) return UnsupportedKey(key);
            supported = true;
            return keyboard[mapped];
        }

        private static ButtonControl UnsupportedKey(KeyCode key)
        {
            if (!warnedKeys.Add(key))
                return null;
            Debug.LogWarning("[InputSystemBackend] 未映射的按键：" + key
                + "，返回 false。请改用受支持的键位，或在后续版本使用 InputAction 后端。");
            return null;
        }

        private static float ReadKeyboardAxis(Key negative, Key positive, Key altNegative, Key altPositive)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return 0f;
            float value = 0f;
            if (keyboard[negative].isPressed || keyboard[altNegative].isPressed) value -= 1f;
            if (keyboard[positive].isPressed || keyboard[altPositive].isPressed) value += 1f;
            return value;
        }

        private static float ReadGamepadX()
        {
            var gamepad = Gamepad.current;
            return gamepad != null ? gamepad.leftStick.x.ReadValue() : 0f;
        }

        private static float ReadGamepadY()
        {
            var gamepad = Gamepad.current;
            return gamepad != null ? gamepad.leftStick.y.ReadValue() : 0f;
        }

        /// <summary>把旧版 KeyCode 映射到 Input System 的 Key；不支持的键返回 Key.None。</summary>
        internal static Key ToKey(KeyCode key)
        {
            if (key >= KeyCode.A && key <= KeyCode.Z)
                return (Key)((int)Key.A + (int)(key - KeyCode.A));
            if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9)
            {
                // 注意：Input System 的 Key 数字块顺序是 Digit1..Digit9, Digit0（0 在最后），
                // 不能用 Digit0 + 偏移；这里逐键显式映射（0.8.2 修复）。
                switch (key)
                {
                    case KeyCode.Alpha0: return Key.Digit0;
                    case KeyCode.Alpha1: return Key.Digit1;
                    case KeyCode.Alpha2: return Key.Digit2;
                    case KeyCode.Alpha3: return Key.Digit3;
                    case KeyCode.Alpha4: return Key.Digit4;
                    case KeyCode.Alpha5: return Key.Digit5;
                    case KeyCode.Alpha6: return Key.Digit6;
                    case KeyCode.Alpha7: return Key.Digit7;
                    case KeyCode.Alpha8: return Key.Digit8;
                    default: return Key.Digit9;
                }
            }
            if (key >= KeyCode.F1 && key <= KeyCode.F12)
                return (Key)((int)Key.F1 + (int)(key - KeyCode.F1));
            if (key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9)
                return (Key)((int)Key.Numpad0 + (int)(key - KeyCode.Keypad0));

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
                case KeyCode.AltGr: return Key.AltGr;
                case KeyCode.LeftMeta: return Key.LeftMeta;
                case KeyCode.RightMeta: return Key.RightMeta;
                case KeyCode.LeftWindows: return Key.LeftWindows;
                case KeyCode.RightWindows: return Key.RightWindows;
                case KeyCode.UpArrow: return Key.UpArrow;
                case KeyCode.DownArrow: return Key.DownArrow;
                case KeyCode.LeftArrow: return Key.LeftArrow;
                case KeyCode.RightArrow: return Key.RightArrow;
                case KeyCode.Home: return Key.Home;
                case KeyCode.End: return Key.End;
                case KeyCode.PageUp: return Key.PageUp;
                case KeyCode.PageDown: return Key.PageDown;
                case KeyCode.Insert: return Key.Insert;
                case KeyCode.Print: return Key.PrintScreen;
                case KeyCode.CapsLock: return Key.CapsLock;
                case KeyCode.Numlock: return Key.NumLock;
                case KeyCode.ScrollLock: return Key.ScrollLock;
                case KeyCode.Pause: return Key.Pause;
                case KeyCode.Menu: return Key.ContextMenu;
                case KeyCode.KeypadDivide: return Key.NumpadDivide;
                case KeyCode.KeypadMultiply: return Key.NumpadMultiply;
                case KeyCode.KeypadPlus: return Key.NumpadPlus;
                case KeyCode.KeypadMinus: return Key.NumpadMinus;
                case KeyCode.KeypadPeriod: return Key.NumpadPeriod;
                case KeyCode.KeypadEquals: return Key.NumpadEquals;
                case KeyCode.BackQuote: return Key.Backquote;
                case KeyCode.Quote: return Key.Quote;
                case KeyCode.Semicolon: return Key.Semicolon;
                case KeyCode.Comma: return Key.Comma;
                case KeyCode.Period: return Key.Period;
                case KeyCode.Slash: return Key.Slash;
                case KeyCode.Backslash: return Key.Backslash;
                case KeyCode.LeftBracket: return Key.LeftBracket;
                case KeyCode.RightBracket: return Key.RightBracket;
                case KeyCode.Minus: return Key.Minus;
                case KeyCode.Equals: return Key.Equals;
                default: return Key.None;
            }
        }
    }

    /// <summary>
    /// 键盘 KeyCode → Input System Key 的映射查询（测试与配置校验用）。
    /// <para><see cref="MapToKeyValue"/> 返回 <c>(int)Key</c>，0 表示不支持（Key.None）。
    /// 不直接暴露 <c>Key</c> 类型，避免测试/工具程序集必须引用 Unity.InputSystem。</para>
    /// </summary>
    public static class InputKeyMapping
    {
        public static bool IsSupported(KeyCode key) => InputSystemBackend.SupportsKey(key);

        public static int MapToKeyValue(KeyCode key)
        {
            if (key == KeyCode.None) return 0;
            return (int)InputSystemBackend.ToKey(key);
        }
    }
#endif
}
