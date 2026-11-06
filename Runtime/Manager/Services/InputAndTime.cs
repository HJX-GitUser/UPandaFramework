using System;
using System.Collections.Generic;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>嵌套暂停凭据；只在首个暂停和最后一个恢复时改变 Unity 全局时钟。</summary>
    public sealed class GameTimeService : IPauseService, IGameClock, IDisposable
    {
        private readonly HashSet<object> pauses = new HashSet<object>();
        private float resumeScale;
        private bool resumeAudio, disposed;
        public bool IsPaused => pauses.Count > 0;
        public float DeltaTime => IsPaused ? 0 : Time.deltaTime;
        public float UnscaledDeltaTime => Time.unscaledDeltaTime;
        public IDisposable Pause(string reason = "Gameplay")
        {
            if (disposed) throw new ObjectDisposedException(nameof(GameTimeService));
            if (pauses.Count == 0)
            {
                resumeScale = Time.timeScale; resumeAudio = AudioListener.pause;
                Time.timeScale = 0; AudioListener.pause = true;
            }
            object key = new object(); pauses.Add(key);
            return new DisposableAction(() => {
                if (pauses.Remove(key) && pauses.Count == 0 && !disposed) Restore();
            });
        }
        public void SetTimeScale(float scale)
        {
            if (disposed) throw new ObjectDisposedException(nameof(GameTimeService));
            if (float.IsNaN(scale) || float.IsInfinity(scale) || scale < 0) throw new ArgumentOutOfRangeException(nameof(scale));
            if (IsPaused) resumeScale = scale; else Time.timeScale = scale;
        }
        private void Restore() { Time.timeScale = resumeScale; AudioListener.pause = resumeAudio; }
        public void Dispose()
        {
            if (disposed) return;
            if (IsPaused) Restore();
            disposed = true; pauses.Clear();
        }
    }
    public interface IInputBackend
    {
        float Axis(string name);
        bool Held(KeyCode key);
        bool Down(KeyCode key);
    }
    public sealed class UnityLegacyInputBackend : IInputBackend
    {
        public float Axis(string name) => UnityEngine.Input.GetAxisRaw(name);
        public bool Held(KeyCode key) => UnityEngine.Input.GetKey(key);
        public bool Down(KeyCode key) => UnityEngine.Input.GetKeyDown(key);
    }
    [Serializable]
    public sealed class InputBinding
    {
        public string action;
        public string context = "Gameplay";
        public string axis;
        public KeyCode positive, negative;
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
        private sealed class Context { public string Name; public bool Block; }
        private readonly Dictionary<string, InputBinding> bindings = new Dictionary<string, InputBinding>();
        private readonly List<Context> contexts = new List<Context>();
        private readonly IInputBackend backend;
        private readonly IPauseService pause;
        private bool disposed;
        public InputService(IInputBackend backend, IPauseService pause, IEnumerable<InputBinding> bindings = null)
        {
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend)); this.pause = pause;
            foreach (var binding in bindings ?? InputBinding.Defaults()) Bind(binding);
        }
        private static string Key(string action, string context) => context + "/" + action;
        public void Bind(InputBinding binding)
        {
            if (disposed) throw new ObjectDisposedException(nameof(InputService));
            if (binding == null || string.IsNullOrWhiteSpace(binding.action) || string.IsNullOrWhiteSpace(binding.context)) throw new ArgumentException("输入动作和上下文不能为空。");
            this.bindings[Key(binding.action, binding.context)] = new InputBinding { action = binding.action, context = binding.context,
                axis = binding.axis, positive = binding.positive, negative = binding.negative };
        }
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
        private InputBinding Find(string action, string context) => Allowed(context) && bindings.TryGetValue(Key(action, context), out var binding) ? binding : null;
        public float GetAxis(string action, string context = "Gameplay")
        {
            var binding = Find(action, context); if (binding == null) return 0;
            if (!string.IsNullOrEmpty(binding.axis)) return backend.Axis(binding.axis);
            return (binding.positive != KeyCode.None && backend.Held(binding.positive) ? 1 : 0) - (binding.negative != KeyCode.None && backend.Held(binding.negative) ? 1 : 0);
        }
        public bool GetButton(string action, string context = "Gameplay")
        { var b = Find(action, context); return b != null && b.positive != KeyCode.None && backend.Held(b.positive); }
        public bool GetButtonDown(string action, string context = "Gameplay")
        { var b = Find(action, context); return b != null && b.positive != KeyCode.None && backend.Down(b.positive); }
        public IDisposable PushContext(string context, bool blockLower = true)
        {
            if (disposed) throw new ObjectDisposedException(nameof(InputService));
            if (string.IsNullOrWhiteSpace(context)) throw new ArgumentException("输入上下文不能为空。");
            var entry = new Context { Name = context, Block = blockLower }; contexts.Add(entry);
            return new DisposableAction(() => contexts.Remove(entry));
        }
        public void Dispose() { disposed = true; contexts.Clear(); bindings.Clear(); }
    }
}
