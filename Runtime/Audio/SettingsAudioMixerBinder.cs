using UnityEngine;
using UnityEngine.Audio;

namespace UPandaGF
{
    /// <summary>
    /// 可选的 AudioMixer 消费器：把 audio.master/music/sfx/ui/voice、muteAll 和
    /// muteWhenUnfocused 写入暴露参数；同时覆盖“框架默认音量只到 AudioListener”的缺口。
    /// <para>不直接由 GameServices 创建，项目在场景里挂到常驻对象并指定 AudioMixer 即可。</para>
    /// </summary>
    [AddComponentMenu("UPandaGF/Settings/Audio Mixer Binder")]
    public sealed class SettingsAudioMixerBinder : MonoBehaviour, IGameServicesConsumer
    {
        [SerializeField] private AudioMixer mixer;
        [SerializeField] private string masterParameter = "MasterVolume";
        [SerializeField] private string musicParameter = "MusicVolume";
        [SerializeField] private string sfxParameter = "SFXVolume";
        [SerializeField] private string uiParameter = "UIVolume";
        [SerializeField] private string voiceParameter = "VoiceVolume";

        private ISettingsService settings;
        private bool focused = true;

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

        private void OnApplicationFocus(bool hasFocus)
        {
            focused = hasFocus;
            Apply();
        }

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
            if (change.Key != null && change.Key.StartsWith("audio.", System.StringComparison.Ordinal)) Apply();
        }

        private void Apply()
        {
            if (mixer == null || settings == null || !settings.IsLoaded) return;
            bool mute = settings.Get<bool>("audio.muteAll")
                        || (settings.Get<bool>("audio.muteWhenUnfocused") && !focused);
            SetVolume(masterParameter, mute ? -80f : ToDb(settings.Get<float>("audio.master")));
            SetVolume(musicParameter, ToDb(settings.Get<float>("audio.music")));
            SetVolume(sfxParameter, ToDb(settings.Get<float>("audio.sfx")));
            SetVolume(uiParameter, ToDb(settings.Get<float>("audio.ui")));
            SetVolume(voiceParameter, ToDb(settings.Get<float>("audio.voice")));
        }

        private void SetVolume(string parameter, float decibels)
        {
            if (!string.IsNullOrEmpty(parameter)) mixer.SetFloat(parameter, decibels);
        }

        private static float ToDb(float linear)
        {
            if (linear <= 0.0001f) return -80f;
            return Mathf.Log10(Mathf.Clamp01(linear)) * 20f;
        }
    }
}
