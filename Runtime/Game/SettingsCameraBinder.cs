using UnityEngine;

namespace UPandaGF
{
    /// <summary>
    /// 把 graphics.fieldOfView 应用到相机。挂在相机上即可，由框架注入设置服务。
    /// <para>亮度/后处理与具体渲染管线相关，未放在本组件；业务可订阅 Settings.Changed 自行消费。</para>
    /// </summary>
    [AddComponentMenu("UPandaGF/Settings/Camera Binder")]
    public sealed class SettingsCameraBinder : MonoBehaviour, IGameServicesConsumer
    {
        [SerializeField] private Camera target;
        [SerializeField] private bool applyFieldOfView = true;

        private ISettingsService settings;

        public void Configure(IGameServices services)
        {
            Bind(services != null ? services.Settings : null);
        }

        /// <summary>直接绑定设置服务（测试/自定义装配用）。</summary>
        public void Bind(ISettingsService value)
        {
            Unsubscribe();
            settings = value;
            if (isActiveAndEnabled) Subscribe();
            Apply();
        }

        private void OnEnable()
        {
            Subscribe();
            Apply();
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
            if (change.Key == "graphics.fieldOfView") Apply();
        }

        private void Apply()
        {
            if (!applyFieldOfView || settings == null || !settings.IsLoaded) return;
            if (target == null) target = GetComponent<Camera>();
            if (target == null) target = Camera.main;
            if (target == null) return;
            if (settings.TryGet("graphics.fieldOfView", out float fov))
                target.fieldOfView = Mathf.Clamp(fov, 1f, 179f);
        }
    }
}
