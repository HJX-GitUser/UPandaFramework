using UnityEngine;

namespace UPandaGF.RunTime.TaskSystem
{
    /// <summary>
    /// 示例敌人：模拟"怪物死亡时把击杀上报给任务系统"。
    /// 接入任务系统只需要 <see cref="Die"/> 里的那一行。
    /// </summary>
    [DisallowMultipleComponent]
    public class DemoEnemy : MonoBehaviour
    {
        [Tooltip("敌人 ID，要与任务目标里的 targetId 一致。")]
        public string enemyId = "wolf";
        public int maxHp = 3;

        public int CurrentHp { get; private set; }

        private void Awake()
        {
            CurrentHp = Mathf.Max(1, maxHp);
        }

        /// <summary>受击；用鼠标点击物体即可触发（需要 Collider，图元自带）。</summary>
        public void TakeDamage(int damage)
        {
            if (damage <= 0) return;
            CurrentHp -= damage;
            Debug.Log("[DemoEnemy] " + enemyId + " 剩余 HP " + CurrentHp);
            if (CurrentHp <= 0) Die();
        }

        private void Die()
        {
            // ============================================================
            // ★★★ 接入任务系统就是这么一行 ★★★
            // 敌人不需要知道任何任务的存在，任务系统自己会订阅这个事件。
            // ============================================================
            GameplayEventBus.RaiseEnemyKilled(enemyId, 1);

            Destroy(gameObject);
        }

        private void OnMouseDown()
        {
            TakeDamage(1);
        }
    }
}
