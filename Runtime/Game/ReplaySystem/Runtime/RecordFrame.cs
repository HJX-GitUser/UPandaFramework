using System.Collections.Generic;
using UnityEngine;

namespace ReplaySystem
{
    /// <summary>
    /// 一帧的录制数据：录制时的游戏时间 + 该帧所有发生变化的物体快照。
    /// </summary>
    [System.Serializable]
    public class RecordFrame
    {
        public float time;                     // 录制时的游戏时间（Time.time）
        public List<ObjectSnapshot> snapshots; // 该帧所有发生变化的物体快照列表

        public RecordFrame() { }

        public RecordFrame(float time)
        {
            this.time = time;
            this.snapshots = new List<ObjectSnapshot>();
        }
    }
}
