/* ==========================================================================================
 * InputAndTime —— 暂停/时钟 + 输入后端抽象 + 动作映射
 *
 * 【三块内容】
 *   GameTimeService：实现 IPauseService + IGameClock。嵌套暂停只有第一个凭据生效、
 *                    最后一个凭据释放时才恢复 timeScale/音频；暂停期间 DeltaTime 返回 0。
 *   IInputBackend / UnityLegacyInputBackend：把 Input.GetAxisRaw/GetKey 抽成接口，
 *                    便于替换成新 Input System 或做纯逻辑单测（不需要真按键）。
 *   InputService：动作名 → 按键/轴 的映射表 + 上下文栈。
 *
 * 【上下文栈规则】
 *   PushContext("UI") 会把 UI 压栈；从栈顶往下找：遇到同名上下文→允许；
 *   遇到 Block=true 的条目→拒绝（阻止继续往下找）。
 *   所以“打开菜单后 Gameplay 失效、UI 仍然响应”是天然行为。
 *
 * 【暂停与输入】Gameplay 上下文在暂停时直接失效；UI 上下文不受影响。
 * ========================================================================================== */

using System;
using System.Collections.Generic;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>
    /// 嵌套暂停凭据 + 游戏时钟。
    /// <para>为什么用“凭据集合”而不是 bool：多个系统可能同时要求暂停（菜单、后台、加载中），
    /// 用计数/集合才不会出现“其中一个先恢复就把全员解了”。</para>
    /// </summary>
    public sealed class GameTimeService : IPauseService, IGameClock, IDisposable
    {
        /// <summary>活着的暂停凭据集合。Count == 0 表示未暂停。</summary>
        private readonly HashSet<object> pauses = new HashSet<object>();
        /// <summary>暂停前的 timeScale，恢复时原样写回。</summary>
        private float resumeScale;
        /// <summary>暂停前的音频暂停状态，恢复时原样写回。</summary>
        private bool resumeAudio, disposed;
        public bool IsPaused => pauses.Count > 0;
        /// <summary>游戏内时间步长：暂停时为 0（业务用这个就不会在暂停时继续跑）。</summary>
        public float DeltaTime => IsPaused ? 0 : Time.deltaTime;
        /// <summary>真实帧时间，不受暂停/timeScale 影响（UI 动画、计时器 unscaled 模式用）。</summary>
        public float UnscaledDeltaTime => Time.unscaledDeltaTime;
        /// <summary>
        /// 申请一份暂停凭据。
        /// <para>只有从“未暂停”变成“暂停”时才会真的改 Time.timeScale/AudioListener.pause；
        /// 返回的 IDisposable 必须 Dispose（重复 Dispose 安全）。</para>
        /// </summary>
        public IDisposable Pause(string reason = "Gameplay")
        {
            if (disposed) throw new ObjectDisposedException(nameof(GameTimeService));
            // 首个凭据：记录原值后暂停
            if (pauses.Count == 0)
            {
                resumeScale = Time.timeScale; resumeAudio = AudioListener.pause;
                Time.timeScale = 0; AudioListener.pause = true;
            }
            object key = new object(); pauses.Add(key);
            return new DisposableAction(() => {
                // 最后一个凭据释放时才恢复，避免多重暂停互相覆盖
                if (pauses.Remove(key) && pauses.Count == 0 && !disposed) Restore();
            });
        }
        /// <summary>
        /// 设置游戏时间缩放。
        /// <para>暂停期间不直接改 Time.timeScale，而是“记账”到 resumeScale，等恢复时生效 ——
        /// 否则暂停期间改的时间缩放会被 Restore 覆盖掉。</para>
        /// </summary>
        public void SetTimeScale(float scale)
        {
            if (disposed) throw new ObjectDisposedException(nameof(GameTimeService));
            if (float.IsNaN(scale) || float.IsInfinity(scale) || scale < 0) throw new ArgumentOutOfRangeException(nameof(scale));
            if (IsPaused) resumeScale = scale; else Time.timeScale = scale;
        }
        /// <summary>恢复暂停前的 timeScale 与音频状态。</summary>
        private void Restore() { Time.timeScale = resumeScale; AudioListener.pause = resumeAudio; }
        /// <summary>释放：如果还处于暂停，先恢复全局时钟，避免退出后 timeScale 停在 0。</summary>
        public void Dispose()
        {
            if (disposed) return;
            if (IsPaused) Restore();
            disposed = true; pauses.Clear();
        }
    }
    /// <summary>输入后端抽象：把“怎么读键”与“怎么映射动作”分开，方便替换/单测。</summary>
    public interface IInputBackend
    {
        /// <summary>读一条轴的原始值（不平滑，对应 Input.GetAxisRaw）。</summary>
        float Axis(string name);
        /// <summary>按键是否按住。</summary>
        bool Held(KeyCode key);
        /// <summary>按键是否在本帧按下。</summary>
        bool Down(KeyCode key);
    }
    /// <summary>基于旧版 Input Manager（Input.GetAxisRaw/GetKey）的实现。</summary>
    public sealed class UnityLegacyInputBackend : IInputBackend
    {
        public float Axis(string name) => UnityEngine.Input.GetAxisRaw(name);
        public bool Held(KeyCode key) => UnityEngine.Input.GetKey(key);
        public bool Down(KeyCode key) => UnityEngine.Input.GetKeyDown(key);
    }
    /// <summary>
    /// 一条动作绑定。
    /// <para>axis 与 positive/negative 二选一：填了 axis 就用轴（支持手柄/平滑），否则用两个按键相减得到 -1/0/1。</para>
    /// <para>context 决定它属于哪个输入上下文（Gameplay / UI ...），默认 Gameplay。</para>
    /// </summary>
    [Serializable]
    public sealed class InputBinding
    {
        public string action;
        public string context = "Gameplay";
        public string axis;
        public KeyCode positive, negative;
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
            new InputBinding { action = "Confirm", context = "UI", positive = KeyCode.Return },
            new InputBinding { action = "Cancel", context = "UI", positive = KeyCode.Escape }
        };
    }
    /// <summary>动作映射与上下文栈。暂停时屏蔽 Gameplay，UI 仍可响应。</summary>
    public sealed class InputService : IInputService, IDisposable
    {
        /// <summary>上下文栈里的一项：Name 是上下文名，Block 表示“到此为止，不继续往下找”。</summary>
        private sealed class Context { public string Name; public bool Block; }
        /// <summary>绑定表，key 为 "context/action"（见 Key）。</summary>
        private readonly Dictionary<string, InputBinding> bindings = new Dictionary<string, InputBinding>();
        /// <summary>上下文栈（栈顶 = 最后 Push 的）。</summary>
        private readonly List<Context> contexts = new List<Context>();
        private readonly IInputBackend backend;
        private readonly IPauseService pause;
        private bool disposed;
        /// <param name="bindings">为 null 时用 <see cref="InputBinding.Defaults"/></param>
        public InputService(IInputBackend backend, IPauseService pause, IEnumerable<InputBinding> bindings = null)
        {
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend)); this.pause = pause;
            foreach (var binding in bindings ?? InputBinding.Defaults()) Bind(binding);
        }
        /// <summary>绑定表的 key：同一动作可以在不同上下文下有不同按键（如 Esc 在 Gameplay 是暂停、在 UI 是取消）。</summary>
        private static string Key(string action, string context) => context + "/" + action;
        /// <summary>
        /// 注册/覆盖一条绑定。
        /// <para>注意这里是“拷贝”一份而非保存外部引用：调用方后续改对象不会影响已注册的绑定。</para>
        /// </summary>
        public void Bind(InputBinding binding)
        {
            if (disposed) throw new ObjectDisposedException(nameof(InputService));
            if (binding == null || string.IsNullOrWhiteSpace(binding.action) || string.IsNullOrWhiteSpace(binding.context)) throw new ArgumentException("输入动作和上下文不能为空。");
            this.bindings[Key(binding.action, binding.context)] = new InputBinding { action = binding.action, context = binding.context,
                axis = binding.axis, positive = binding.positive, negative = binding.negative };
        }
        /// <summary>
        /// 当前是否允许某上下文：
        /// <para>1) 已释放 → 否；Gameplay 且处于暂停 → 否（但 UI 不受影响）。</para>
        /// <para>2) 从栈顶往下扫：遇到同名上下文→允许；遇到 Block→拒绝；扫到底→默认允许。</para>
        /// </summary>
        private bool Allowed(string context)
        {
            if (disposed || (context == "Gameplay" && pause != null && pause.IsPaused)) return false;
            for (int i = contexts.Count - 1; i >= 0; i--)
            {
                if (contexts[i].Name == context) return true;
                if (contexts[i].Block) return false;
            }
            return true;
        }
        /// <summary>找到可用绑定；上下文被屏蔽或未注册时返回 null（调用方都当 0/false 处理）。</summary>
        private InputBinding Find(string action, string context) => Allowed(context) && bindings.TryGetValue(Key(action, context), out var binding) ? binding : null;
        /// <summary>读轴值：有 axis 用轴；否则 positive/negative 相减得到 -1/0/1。</summary>
        public float GetAxis(string action, string context = "Gameplay")
        {
            var binding = Find(action, context); if (binding == null) return 0;
            if (!string.IsNullOrEmpty(binding.axis)) return backend.Axis(binding.axis);
            return (binding.positive != KeyCode.None && backend.Held(binding.positive) ? 1 : 0) - (binding.negative != KeyCode.None && backend.Held(binding.negative) ? 1 : 0);
        }
        /// <summary>按键是否按住（只看 positive）。</summary>
        public bool GetButton(string action, string context = "Gameplay")
        { var b = Find(action, context); return b != null && b.positive != KeyCode.None && backend.Held(b.positive); }
        /// <summary>按键是否在本帧按下（只看 positive）。</summary>
        public bool GetButtonDown(string action, string context = "Gameplay")
        { var b = Find(action, context); return b != null && b.positive != KeyCode.None && backend.Down(b.positive); }
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
        /// <summary>释放：清空上下文与绑定；之后的 Get/Push 抛 ObjectDisposedException。</summary>
        public void Dispose() { disposed = true; contexts.Clear(); bindings.Clear(); }
    }
}
