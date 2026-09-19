using UnityEngine;

namespace UPandaGF.RunTime.TaskSystem
{
    /// <summary>示例到达区域：玩家进入一次即上报"到达地点"。</summary>
    [DisallowMultipleComponent]
    public class DemoLocationTrigger : MonoBehaviour
    {
        [Tooltip("地点 ID，要与任务目标里的 targetId 一致。")]
        public string locationId = "village_gate";
        [Tooltip("只触发一次（到达类目标通常只需一次）。")]
        public bool oneShot = true;
        [Tooltip("需要的标签；留空表示任何物体进入都算。")]
        public string requiredTag = "";

        private bool triggered;

        private void OnTriggerEnter(Collider other)
        {
            if (oneShot && triggered) return;
            if (!IsValidVisitor(other)) return;

            triggered = true;
            GameplayEventBus.RaiseLocationReached(locationId);
        }

        private bool IsValidVisitor(Collider other)
        {
            if (other == null) return false;
            if (string.IsNullOrEmpty(requiredTag)) return true;
            return other.gameObject.tag == requiredTag;
        }
    }
}
