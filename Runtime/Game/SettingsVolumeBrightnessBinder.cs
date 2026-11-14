using System;
using System.Reflection;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>
    /// 把 graphics.brightness 写到 URP/HDRP Volume 的 ColorAdjustments.postExposure。
    /// 使用反射，不直接引用 URP/HDRP 程序集；未安装 Volume 框架时组件不生效。
    /// <para>亮度 1.0 对应 0 EV，0.5 约 -1 EV，1.5 约 +0.58 EV。</para>
    /// </summary>
    [AddComponentMenu("UPandaGF/Settings/Volume Brightness Binder")]
    public sealed class SettingsVolumeBrightnessBinder : MonoBehaviour, IGameServicesConsumer
    {
        private static readonly string[] ColorAdjustmentTypes =
        {
            "UnityEngine.Rendering.Universal.ColorAdjustments, Unity.RenderPipelines.Universal.Runtime",
            "UnityEngine.Rendering.HighDefinition.ColorAdjustments, Unity.RenderPipelines.HighDefinition.Runtime"
        };

        [SerializeField] private Component volume;
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
            if (change.Key == "graphics.brightness") Apply();
        }

        private void Apply()
        {
            if (settings == null || !settings.IsLoaded) return;
            if (volume == null) volume = FindVolume();
            if (volume == null) return;

            object profile = GetProperty(volume, "profile");
            if (profile == null) return;
            Type adjustmentType = FindAdjustmentType();
            if (adjustmentType == null) return;

            object adjustments = TryGetComponent(profile, adjustmentType);
            if (adjustments == null) return;
            object parameter = GetProperty(adjustments, "postExposure");
            if (parameter == null) return;

            settings.TryGet("graphics.brightness", out float brightness);
            float exposure = Mathf.Log(Mathf.Max(brightness, 0.0001f), 2f);
            SetProperty(parameter, "value", exposure);
        }

        private Component FindVolume()
        {
            MonoBehaviour[] behaviours = GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] != null && behaviours[i].GetType().Name == "Volume") return behaviours[i];
            }
            return null;
        }

        private static Type FindAdjustmentType()
        {
            for (int i = 0; i < ColorAdjustmentTypes.Length; i++)
            {
                Type type = Type.GetType(ColorAdjustmentTypes[i], false);
                if (type != null) return type;
            }
            return null;
        }

        private static object TryGetComponent(object profile, Type componentType)
        {
            MethodInfo method = null;
            MethodInfo[] methods = profile.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < methods.Length; i++)
            {
                ParameterInfo[] parameters = methods[i].GetParameters();
                if (methods[i].Name == "TryGet" && parameters.Length == 2
                    && parameters[0].ParameterType == typeof(Type))
                {
                    method = methods[i];
                    break;
                }
            }
            if (method == null) return null;
            object[] arguments = { componentType, null };
            try
            {
                object result = method.Invoke(profile, arguments);
                if (result is bool found && found) return arguments[1];
                return null;
            }
            catch (Exception) { return null; }
        }

        private static object GetProperty(object target, string name)
        {
            PropertyInfo property = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            return property != null ? property.GetValue(target) : null;
        }

        private static void SetProperty(object target, string name, object value)
        {
            PropertyInfo property = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property != null && property.CanWrite) property.SetValue(target, value);
        }
    }
}
