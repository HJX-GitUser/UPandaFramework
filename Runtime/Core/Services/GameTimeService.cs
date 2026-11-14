/* ==========================================================================================
 * GameTimeService —— 可嵌套暂停凭据 + 游戏时钟
 *
 * 【为什么用“凭据集合”而不是 bool】
 *   多个系统可能同时要求暂停（菜单、后台、加载中），用计数/集合才不会出现
 *   “其中一个先恢复就把全员解了”。
 *
 * 【暂停与输入】
 *   暂停只改变 Time.timeScale 与 AudioListener.pause；输入屏蔽由 InputService 读取
 *   IPauseService.IsPaused 完成（Gameplay 上下文失效，UI 不受影响）。
 *   输入系统说明见 Runtime/Core/Input/README.md。
 *
 * 【历史】
 *   0.8.2 之前本类与输入实现同处 InputAndTime.cs；0.8.2 代码整理后拆到本文件，
 *   输入实现迁至 Runtime/Core/Input/。
 * ========================================================================================== */

using System;
using System.Collections.Generic;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>
    /// 嵌套暂停凭据 + 游戏时钟。
    /// <para>为什么用“凭据集合”而不是 bool：多个系统可能同时要求暂停（菜单、后台、加载中），
    /// 用计数/集合才不会出现“其中一个先恢复就把全员解了”。</para>
    /// </summary>
    public sealed class GameTimeService : IPauseService, IGameClock, IDisposable
    {
        /// <summary>活着的暂停凭据集合。Count == 0 表示未暂停。</summary>
        private readonly HashSet<object> pauses = new HashSet<object>();
        /// <summary>暂停前的 timeScale，恢复时原样写回。</summary>
        private float resumeScale;
        /// <summary>暂停前的音频暂停状态，恢复时原样写回。</summary>
        private bool resumeAudio, disposed;
        public bool IsPaused => pauses.Count > 0;
        /// <summary>游戏内时间步长：暂停时为 0（业务用这个就不会在暂停时继续跑）。</summary>
        public float DeltaTime => IsPaused ? 0 : Time.deltaTime;
        /// <summary>真实帧时间，不受暂停/timeScale 影响（UI 动画、计时器 unscaled 模式用）。</summary>
        public float UnscaledDeltaTime => Time.unscaledDeltaTime;
        /// <summary>
        /// 申请一份暂停凭据。
        /// <para>只有从“未暂停”变成“暂停”时才会真的改 Time.timeScale/AudioListener.pause；
        /// 返回的 IDisposable 必须 Dispose（重复 Dispose 安全）。</para>
        /// </summary>
        public IDisposable Pause(string reason = "Gameplay")
        {
            if (disposed) throw new ObjectDisposedException(nameof(GameTimeService));
            // 首个凭据：记录原值后暂停
            if (pauses.Count == 0)
            {
                resumeScale = Time.timeScale; resumeAudio = AudioListener.pause;
                Time.timeScale = 0; AudioListener.pause = true;
            }
            object key = new object(); pauses.Add(key);
            return new DisposableAction(() => {
                // 最后一个凭据释放时才恢复，避免多重暂停互相覆盖
                if (pauses.Remove(key) && pauses.Count == 0 && !disposed) Restore();
            });
        }
        /// <summary>
        /// 设置游戏时间缩放。
        /// <para>暂停期间不直接改 Time.timeScale，而是“记账”到 resumeScale，等恢复时生效 ——
        /// 否则暂停期间改的时间缩放会被 Restore 覆盖掉。</para>
        /// </summary>
        public void SetTimeScale(float scale)
        {
            if (disposed) throw new ObjectDisposedException(nameof(GameTimeService));
            if (float.IsNaN(scale) || float.IsInfinity(scale) || scale < 0) throw new ArgumentOutOfRangeException(nameof(scale));
            if (IsPaused) resumeScale = scale; else Time.timeScale = scale;
        }
        /// <summary>恢复暂停前的 timeScale 与音频状态。</summary>
        private void Restore() { Time.timeScale = resumeScale; AudioListener.pause = resumeAudio; }
        /// <summary>释放：如果还处于暂停，先恢复全局时钟，避免退出后 timeScale 停在 0。</summary>
        public void Dispose()
        {
            if (disposed) return;
            if (IsPaused) Restore();
            disposed = true; pauses.Clear();
        }
    }
}
