using System.Collections.Generic;
using UnityEngine;

namespace ReplaySystem
{
    /// <summary>
    /// 单帧内单个物体的状态快照。
    /// </summary>
    [System.Serializable]
    public class ObjectSnapshot
    {
        public int instanceID;                 // 物体唯一标识（GameObject.GetInstanceID()）
        public Vector3 position;               // 局部坐标（localPosition）
        public Quaternion rotation;            // 局部旋转（localRotation）
        public Vector3 localScale;             // 局部缩放（localScale）

        // 可选：仅当物体有 Animator 且 float 参数发生变化时记录
        public Dictionary<string, float> animatorParams;

        // 物体名称：用于跨会话加载时按名称回退匹配（instanceID 仅在当前会话内稳定）
        public string objectName;

        // 预留：由 IRecordableComponent 扩展写入的额外组件状态（如 Rigidbody.velocity）
        public Dictionary<string, object> componentData;

        public ObjectSnapshot() { }

        public ObjectSnapshot(int instanceID, Vector3 position, Quaternion rotation, Vector3 localScale)
        {
            this.instanceID = instanceID;
            this.position = position;
            this.rotation = rotation;
            this.localScale = localScale;
        }

        public void SetComponentData(string key, object state)
        {
            if (componentData == null) componentData = new Dictionary<string, object>();
            componentData[key] = state;
        }

        public bool TryGetComponentData(string key, out object state)
        {
            state = null;
            return componentData != null && componentData.TryGetValue(key, out state);
        }
    }
}
