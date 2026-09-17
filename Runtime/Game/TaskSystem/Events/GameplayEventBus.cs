using System;
using UnityEngine;

namespace UPandaGF.RunTime.TaskSystem
{
    /// <summary>
    /// 玩法 → 任务 的「事件总线」。
    /// 游戏侧只需要在事件发生的那一刻调一行：
    /// <code>
    /// GameplayEventBus.RaiseEnemyKilled("wolf", 1);   // 狼死了一只
    /// </code>
    /// QuestManager 会自动订阅这些事件并推进对应任务进度，不需要任何 Update 轮询。
    ///
    /// 说明：这里用静态事件是为了让"敌人死亡"这类随处可见的代码不用持有任何引用；
    /// 订阅方（QuestManager）必须在 OnEnable/OnDisable 成对地订阅/退订，避免重复累加。
    /// </summary>
    public static class GameplayEventBus
    {
        /// <summary>敌人被击杀：(敌人 ID, 数量)。</summary>
        public static event Action<string, int> EnemyKilled;

        /// <summary>拾取到物品：(物品 ID, 数量)。</summary>
        public static event Action<string, int> ItemCollected;

        /// <summary>玩家到达某地点：(地点 ID)。</summary>
        public static event Action<string> LocationReached;

        /// <summary>玩家与 NPC 完成一次对话：(NPC ID)。</summary>
        public static event Action<string> NpcTalked;

        // ------------------------------- 派发入口 -------------------------------

        public static void RaiseEnemyKilled(string enemyId, int amount = 1)
        {
            if (amount <= 0) return;
            Action<string, int> handler = EnemyKilled;
            if (handler != null) handler(enemyId, amount);
        }

        public static void RaiseItemCollected(string itemId, int amount = 1)
        {
            if (amount <= 0) return;
            Action<string, int> handler = ItemCollected;
            if (handler != null) handler(itemId, amount);
        }

        public static void RaiseLocationReached(string locationId)
        {
            Action<string> handler = LocationReached;
            if (handler != null) handler(locationId);
        }

        public static void RaiseNpcTalked(string npcId)
        {
            Action<string> handler = NpcTalked;
            if (handler != null) handler(npcId);
        }

        /// <summary>清空所有订阅者（测试 / 关服 / 域重载兜底用）。</summary>
        public static void ClearSubscribers()
        {
            EnemyKilled = null;
            ItemCollected = null;
            LocationReached = null;
            NpcTalked = null;
        }

        /// <summary>
        /// 关闭「Enter Play Mode Options（不重载域）」时，静态事件会残留上一次运行的订阅者
        /// → 每次进入播放模式前强制清空一次，避免任务进度被重复累加。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticStateOnPlayModeEnter()
        {
            ClearSubscribers();
        }
    }
}
