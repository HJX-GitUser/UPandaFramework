using UnityEngine;

namespace ReplaySystem
{
    /// <summary>
    /// 示例扩展：录制/回放 Rigidbody 速度。
    /// 演示如何通过 IRecordableComponent 为其他组件添加支持。
    /// </summary>
    public class RigidbodyVelocityRecorder : IRecordableComponent
    {
        public string Key => "RigidbodyVelocity";

        public bool CanHandle(GameObject go) => go.GetComponent<Rigidbody>() != null;

        public object Capture(GameObject go) => go.GetComponent<Rigidbody>().velocity;

        public void Apply(GameObject go, object state)
        {
            var rb = go.GetComponent<Rigidbody>();
            if (rb != null && state is Vector3 v) rb.velocity = v;
        }
    }
}
