using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
namespace UPandaGF
{
    /// <summary>WebGL 使用 Unity 帧循环等待真实时间，不依赖托管线程或 TimerQueue。</summary>
    public static class PlatformTasks
    {
        public static Task Delay(TimeSpan interval, CancellationToken token = default)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return WaitFrames(interval, token);
#else
            return Task.Delay(interval, token);
#endif
        }
#if UNITY_WEBGL && !UNITY_EDITOR
        private static async Task WaitFrames(TimeSpan interval, CancellationToken token)
        {
            if (interval < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
            double deadline = Time.realtimeSinceStartupAsDouble + interval.TotalSeconds;
            while (Time.realtimeSinceStartupAsDouble < deadline) { token.ThrowIfCancellationRequested(); await Task.Yield(); }
            token.ThrowIfCancellationRequested();
        }
#endif
    }
}
