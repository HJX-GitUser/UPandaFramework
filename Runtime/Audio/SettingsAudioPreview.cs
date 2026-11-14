using UnityEngine;

namespace UPandaGF
{
    /// <summary>设置面板试听：生成一个短促的正弦音，不依赖任何资源；播完自动销毁。</summary>
    public static class SettingsAudioPreview
    {
        private const int SampleRate = 44100;
        private const float Duration = 0.18f;

        public static void Play(float sfxVolume, float masterVolume, bool muteAll)
        {
            if (muteAll) return;
            float volume = Mathf.Clamp01(sfxVolume) * Mathf.Clamp01(masterVolume);
            if (volume <= 0.0001f) return;

            int samples = Mathf.Max(1, Mathf.RoundToInt(SampleRate * Duration));
            var data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)SampleRate;
                float envelope = 1f - (i / (float)samples);
                data[i] = Mathf.Sin(2f * Mathf.PI * 880f * t) * 0.25f * envelope;
            }

            AudioClip clip = AudioClip.Create("SettingsPreview", samples, 1, SampleRate, false);
            clip.SetData(data, 0);
            var host = new GameObject("SettingsAudioPreview");
            var source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = volume;
            source.clip = clip;
            source.Play();
            Object.Destroy(host, Duration + 0.1f);
            Object.Destroy(clip, Duration + 0.2f);
        }
    }
}
