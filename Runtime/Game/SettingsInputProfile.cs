using System;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>
    /// 输入偏好的只读消费入口：控制器/相机代码直接读这些属性，不必各自拼 key。
    /// <para>挂在任意常驻对象上；语言切换等无关设置不会触发 <see cref="Changed"/>。</para>
    /// </summary>
    [AddComponentMenu("UPandaGF/Settings/Input Profile")]
    public sealed class SettingsInputProfile : MonoBehaviour, IGameServicesConsumer
    {
        private ISettingsService settings;

        public float MouseSensitivity { get; private set; } = 1f;
        public float GamepadSensitivity { get; private set; } = 1f;
        public float GamepadDeadzone { get; private set; } = 0.2f;
        public bool InvertY { get; private set; }
        public bool Vibration { get; private set; } = true;
        public AimMode AimMode { get; private set; } = AimMode.Hold;

        public event Action Changed;

        public void Configure(IGameServices services)
        {
            Unsubscribe();
            settings = services != null ? services.Settings : null;
            if (isActiveAndEnabled) Subscribe();
            Refresh();
        }

        private void OnEnable()
        {
            Subscribe();
            Refresh();
        }

        private void OnDisable() { Unsubscribe(); }
        private void OnDestroy() { Unsubscribe(); }

        private void Subscribe()
        {
            if (settings == null) return;
            settings.Changed -= OnSettingsChanged;
            settings.Changed += OnSettingsChanged;
        }

        private void Unsubscribe()
        {
            if (settings == null) return;
            settings.Changed -= OnSettingsChanged;
        }

        private void OnSettingsChanged(SettingsChange change)
        {
            if (change.Key == null || !change.Key.StartsWith("input.", StringComparison.Ordinal)) return;
            Refresh();
            Changed?.Invoke();
        }

        private void Refresh()
        {
            if (settings == null || !settings.IsLoaded) return;
            settings.TryGet("input.mouseSensitivity", out float mouse);
            settings.TryGet("input.gamepadSensitivity", out float gamepad);
            settings.TryGet("input.gamepadDeadzone", out float deadzone);
            settings.TryGet("input.invertY", out bool invert);
            settings.TryGet("input.vibration", out bool vibration);
            settings.TryGet("input.aimMode", out AimMode aim);
            MouseSensitivity = mouse;
            GamepadSensitivity = gamepad;
            GamepadDeadzone = deadzone;
            InvertY = invert;
            Vibration = vibration;
            AimMode = aim;
        }
    }
}
