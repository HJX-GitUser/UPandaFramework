using UnityEngine;

namespace UPandaGF.RunTime.TaskSystem
{
    /// <summary>示例可拾取物：进入触发器即上报"收集到物品"。</summary>
    [DisallowMultipleComponent]
    public class DemoCollectible : MonoBehaviour
    {
        [Tooltip("物品 ID，要与任务目标里的 targetId 一致。")]
        public string itemId = "herb";
        public int amount = 1;
        [Tooltip("需要的标签；留空表示任何物体进入都算（用字符串比较，未定义的 Tag 也不会报错）。")]
        public string requiredTag = "";

        private void OnTriggerEnter(Collider other)
        {
            if (!IsValidPicker(other)) return;

            GameplayEventBus.RaiseItemCollected(itemId, Mathf.Max(1, amount));
            Destroy(gameObject);
        }

        private bool IsValidPicker(Collider other)
        {
            if (other == null) return false;
            if (string.IsNullOrEmpty(requiredTag)) return true;
            // 注意：不要用 CompareTag，Tag 未定义时会抛异常；直接比较字符串最安全
            return other.gameObject.tag == requiredTag;
        }
    }
}
