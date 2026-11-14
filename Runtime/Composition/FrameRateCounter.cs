using UnityEngine;

namespace UPandaGF
{
    /// <summary>
    /// 基于真实时间的帧率统计（不受 Time.timeScale 影响）。
    /// <para>每 <see cref="WindowSeconds"/> 秒统计一次，避免 EMA 在暂停/切后台时缓慢漂移。</para>
    /// </summary>
    public sealed class FrameRateCounter
    {
        private readonly float windowSeconds;
        private int frames;
        private float elapsed;

        public FrameRateCounter(float windowSeconds = 0.5f)
        {
            this.windowSeconds = Mathf.Max(0.05f, windowSeconds);
        }

        /// <summary>最近一个统计窗口的帧率；窗口未结束时为上一次结果（初始 0）。</summary>
        public float Fps { get; private set; }

        public void Tick(float unscaledDeltaTime)
        {
            if (unscaledDeltaTime <= 0f) return;
            frames++;
            elapsed += unscaledDeltaTime;
            if (elapsed < windowSeconds) return;
            Fps = frames / elapsed;
            frames = 0;
            elapsed = 0f;
        }

        public void Reset()
        {
            frames = 0;
            elapsed = 0f;
            Fps = 0f;
        }
    }
}
