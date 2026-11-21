using Cinemachine;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>
    /// 把 graphics.fieldOfView 写入 Cinemachine 虚拟相机镜头。
    /// <para>框架包已声明 <c>com.unity.cinemachine@2.10.3</c>；源码拷贝需手动安装同版本。</para>
    /// </summary>
    [AddComponentMenu("UPandaGF/Settings/Cinemachine Binder")]
    public sealed class SettingsCinemachineBinder : MonoBehaviour, IGameServicesConsumer
    {
        [SerializeField] private CinemachineVirtualCamera target;

        private ISettingsService settings;

        public void Configure(IGameServices services)
        {
            Unsubscribe();
            settings = services != null ? services.Settings : null;
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
            if (target == null)
            {
                target = GetComponent<CinemachineVirtualCamera>();
                if (target == null) target = FindObjectOfType<CinemachineVirtualCamera>();
            }
            if (target == null || settings == null || !settings.IsLoaded) return;
            if (settings.TryGet("graphics.fieldOfView", out float fov))
            {
                target.m_Lens.FieldOfView = Mathf.Clamp(fov, 1f, 179f);
            }
        }
    }
}
