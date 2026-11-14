using System.Collections.Generic;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>
    /// 输入后端抽象：把“怎么读键”与“怎么映射动作”分开，方便替换成新 Input System
    /// 或做纯逻辑单测（不需要真按键）。业务一般不直接使用本接口。
    /// <para>0.8.2 增加 <see cref="Up"/> 与 <see cref="TryCaptureKey"/>：前者用于 GetButtonUp，
    /// 后者用于设置面板“按下新键”捕获。</para>
    /// <para>新后端实现见 <see cref="InputSystemBackend"/>，工厂见 <see cref="InputBackendFactory"/>。</para>
    /// </summary>
    public interface IInputBackend
    {
        /// <summary>读一条轴的原始值（不平滑，对应 Input.GetAxisRaw）。</summary>
        float Axis(string name);
        /// <summary>按键是否按住。</summary>
        bool Held(KeyCode key);
        /// <summary>按键是否在本帧按下。</summary>
        bool Down(KeyCode key);
        /// <summary>按键是否在本帧松开。</summary>
        bool Up(KeyCode key);
        /// <summary>捕获本帧按下的下一个可映射键；没有则返回 false。</summary>
        bool TryCaptureKey(out KeyCode key);
    }
    /// <summary>基于旧版 Input Manager（Input.GetAxisRaw/GetKey/GetKeyUp）的实现。</summary>
    public sealed class UnityLegacyInputBackend : IInputBackend
    {
        public float Axis(string name) => UnityEngine.Input.GetAxisRaw(name);
        public bool Held(KeyCode key) => UnityEngine.Input.GetKey(key);
        public bool Down(KeyCode key) => UnityEngine.Input.GetKeyDown(key);
        public bool Up(KeyCode key) => UnityEngine.Input.GetKeyUp(key);

        /// <summary>逐键查询 GetKeyDown；先用 anyKeyDown 过滤，避免每帧遍历整个候选表。</summary>
        public bool TryCaptureKey(out KeyCode key)
        {
            key = KeyCode.None;
            if (!UnityEngine.Input.anyKeyDown) return false;
            var candidates = InputCaptureKeys.All;
            for (int i = 0; i < candidates.Length; i++)
            {
                if (!UnityEngine.Input.GetKeyDown(candidates[i])) continue;
                key = candidates[i];
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// 可捕获的 KeyCode 候选表（供旧/新后端共用）。
    /// <para>只包含两套后端都能表达为 KeyCode 的键：字母、数字、F1–F12、小键盘、标点、
    /// 方向键、修饰键、鼠标 0–6、手柄按钮 0–19。</para>
    /// </summary>
    public static class InputCaptureKeys
    {
        public static readonly KeyCode[] All = Build();

        private static KeyCode[] Build()
        {
            var list = new List<KeyCode>(160);
            for (int i = 0; i < 26; i++) list.Add((KeyCode)((int)KeyCode.A + i));
            for (int i = 0; i < 10; i++) list.Add((KeyCode)((int)KeyCode.Alpha0 + i));
            for (int i = 0; i < 12; i++) list.Add((KeyCode)((int)KeyCode.F1 + i));
            for (int i = 0; i < 10; i++) list.Add((KeyCode)((int)KeyCode.Keypad0 + i));
            list.AddRange(new[]
            {
                KeyCode.Space, KeyCode.Return, KeyCode.KeypadEnter, KeyCode.Escape, KeyCode.Tab,
                KeyCode.Backspace, KeyCode.Delete, KeyCode.Insert, KeyCode.Home, KeyCode.End,
                KeyCode.PageUp, KeyCode.PageDown,
                KeyCode.LeftShift, KeyCode.RightShift, KeyCode.LeftControl, KeyCode.RightControl,
                KeyCode.LeftAlt, KeyCode.RightAlt, KeyCode.AltGr,
                KeyCode.LeftArrow, KeyCode.RightArrow, KeyCode.UpArrow, KeyCode.DownArrow,
                KeyCode.BackQuote, KeyCode.Quote, KeyCode.Semicolon, KeyCode.Comma, KeyCode.Period,
                KeyCode.Slash, KeyCode.Backslash, KeyCode.LeftBracket, KeyCode.RightBracket,
                KeyCode.Minus, KeyCode.Equals,
                KeyCode.KeypadDivide, KeyCode.KeypadMultiply, KeyCode.KeypadPlus,
                KeyCode.KeypadMinus, KeyCode.KeypadPeriod, KeyCode.KeypadEquals,
                KeyCode.CapsLock, KeyCode.Numlock, KeyCode.ScrollLock, KeyCode.Pause, KeyCode.Menu,
                KeyCode.Mouse0, KeyCode.Mouse1, KeyCode.Mouse2, KeyCode.Mouse3, KeyCode.Mouse4,
                KeyCode.Mouse5, KeyCode.Mouse6
            });
            for (int i = 0; i < 20; i++) list.Add((KeyCode)((int)KeyCode.JoystickButton0 + i));
            return list.ToArray();
        }
    }
}
