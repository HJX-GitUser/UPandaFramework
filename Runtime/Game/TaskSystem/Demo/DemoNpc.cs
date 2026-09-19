using UnityEngine;

namespace UPandaGF.RunTime.TaskSystem
{
    /// <summary>示例 NPC：点击（或用外部代码调用 Interact）即上报"完成一次对话"。</summary>
    [DisallowMultipleComponent]
    public class DemoNpc : MonoBehaviour
    {
        [Tooltip("NPC ID，要与任务目标里的 targetId 一致。")]
        public string npcId = "elder";
        public string displayName = "长老";

        /// <summary>完成一次对话。</summary>
        public void Interact()
        {
            GameplayEventBus.RaiseNpcTalked(npcId);
        }

        private void OnMouseDown()
        {
            Interact();
        }
    }
}
