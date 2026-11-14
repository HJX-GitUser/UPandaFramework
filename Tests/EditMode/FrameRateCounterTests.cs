using NUnit.Framework;
using UPandaGF;

namespace UPandaGF.Tests
{
    /// <summary>帧率统计不依赖 Time.timeScale；暂停时仍按真实帧间隔计算。</summary>
    public class FrameRateCounterTests
    {
        [Test]
        public void Tick_UsesUnscaledDeltaTime()
        {
            var counter = new FrameRateCounter(0.5f);
            Assert.AreEqual(0f, counter.Fps, 0.0001f);
            for (int i = 0; i < 30; i++) counter.Tick(1f / 60f);
            Assert.AreEqual(60f, counter.Fps, 0.1f);
        }

        [Test]
        public void Tick_ZeroDeltaTimeDoesNotChangeResult()
        {
            var counter = new FrameRateCounter(0.1f);
            counter.Tick(0.1f);
            float first = counter.Fps;
            counter.Tick(0f);
            Assert.AreEqual(first, counter.Fps, 0.0001f);
            counter.Reset();
            Assert.AreEqual(0f, counter.Fps, 0.0001f);
        }
    }
}
