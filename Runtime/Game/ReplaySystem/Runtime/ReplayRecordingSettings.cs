using System;
using UnityEngine;

namespace UPandaGF.Gameplay.Replay
{
    public enum ReplayTargetMode { Scene, Registered }
    public enum RecordingStopReason { None, User, Disabled, FrameLimit, SnapshotLimit, MemoryLimit, DurationLimit, CaptureError }
    [Serializable]
    public sealed class ReplayRecordingSettings
    {
        [Tooltip("Scene 周期发现激活物体；Registered 只录制显式注册的物体")]
        public ReplayTargetMode targets = ReplayTargetMode.Scene;
        [Range(0, 240), Tooltip("每秒采样次数；0 按每个 LateUpdate 采样")]
        public int samplesPerSecond = 30;
        [Min(0), Tooltip("目标/组件/路径缓存刷新间隔（真实秒）；0 每次采样刷新")]
        public float discoveryInterval = 1f;
        [Min(0), Tooltip("最大录制时长（游戏秒）；0 不限")]
        public float maximumDuration = 600f;
        [Min(0), Tooltip("保留帧数上限；0 不限。静止期间的末帧会合并")]
        public int maximumFrames = 18000;
        [Min(0), Tooltip("快照引用总数上限（含保持关键帧）；0 不限")]
        public int maximumSnapshots = 250000;
        [Min(0), Tooltip("录制数据估算预算 MiB；0 不限。自定义大状态请实现 IReplayMemoryEstimator")]
        public int maximumMemoryMB = 64;
    }
    public sealed class ReplayStatistics
    {
        public long samples, sceneScans, visitedTargets, createdSnapshots, estimatedDataBytes, snapshotReferences;
        public int trackedTargets, indexedSnapshots;
    }
    /// <summary>自定义状态的保留大小估算（不调用 Unity API 也可，但录制时在主线程调用）。</summary>
    public interface IReplayMemoryEstimator { long EstimateBytes(object state); }
    /// <summary>可选的无变化快速路径；返回 false 时必须保持 previous 不变。</summary>
    public interface IReplayChangeRecorder
    { bool TryCapture(GameObject go, object previous, out object value); }
}
