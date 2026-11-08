using UnityEngine;

namespace UPandaGF.Gameplay.Replay
{
    /// <summary>
    /// 示例扩展：录制/回放 Rigidbody 速度。
    /// 演示如何通过 IRecordableComponent 为其他组件添加支持。
    /// </summary>
    public class RigidbodyVelocityRecorder : IRecordableComponent, IReplayChangeRecorder, IReplayMemoryEstimator
    {
        public string Key => "RigidbodyVelocity";

        public bool CanHandle(GameObject go) => go.TryGetComponent<Rigidbody>(out _);

        public object Capture(GameObject go) => go.GetComponent<Rigidbody>().velocity;

        public long EstimateBytes(object state) => 32;
        public bool TryCapture(GameObject go, object previous, out object value)
        {
            var velocity = go.GetComponent<Rigidbody>().velocity;
            if (previous is Vector3 old && old == velocity) { value = previous; return false; }
            value = velocity; return true;
        }

        public void Apply(GameObject go, object state)
        {
            var rb = go.GetComponent<Rigidbody>();
            if (rb != null && state is Vector3 v) rb.velocity = v;
        }
    }
}
