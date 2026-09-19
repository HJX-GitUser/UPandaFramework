using System;
using System.Collections.Generic;
using UnityEngine;

namespace UPandaGF.RunTime.TaskSystem
{
    /// <summary>
    /// 演示用的「玩家奖励钱包」：把经验 / 金币 / 物品存在内存里并派发变化事件。
    /// 正式项目可换成自己的背包与属性系统（实现 IRewardReceiver 即可）。
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerRewardService : MonoBehaviour, IRewardReceiver
    {
        private static PlayerRewardService instance;

        /// <summary>全局实例；场景里没有时会自动创建一个（DontDestroyOnLoad）。</summary>
        public static PlayerRewardService Instance
        {
            get
            {
                if (instance != null) return instance;
                instance = FindObjectOfType<PlayerRewardService>();
                if (instance == null)
                {
                    GameObject go = new GameObject("PlayerRewardService");
                    instance = go.AddComponent<PlayerRewardService>();
                }
                return instance;
            }
        }

        [Header("当前持有（运行时数值，可在 Inspector 观察）")]
        public int gold;
        public int experience;
        public List<ItemStack> items = new List<ItemStack>();

        // 变化事件：UI 可以直接订阅，无需轮询
        public event Action<int> OnGoldChanged;          // 参数 = 最新金币
        public event Action<int> OnExperienceChanged;    // 参数 = 最新经验
        public event Action<string, int> OnItemChanged;  // 参数 = (物品 ID, 最新数量)

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Debug.LogWarning("[PlayerRewardService] 已存在实例，销毁重复的：" + name);
                Destroy(gameObject);
                return;
            }
            instance = this;
            if (transform.parent == null) DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        // ---------------------------- IRewardReceiver ----------------------------

        public void GrantExperience(int amount)
        {
            if (amount <= 0) return;
            experience += amount;
            if (OnExperienceChanged != null) OnExperienceChanged(experience);
            Debug.Log("[奖励] 获得经验 " + amount + "，当前 " + experience);
        }

        public void GrantGold(int amount)
        {
            if (amount <= 0) return;
            gold += amount;
            if (OnGoldChanged != null) OnGoldChanged(gold);
            Debug.Log("[奖励] 获得金币 " + amount + "，当前 " + gold);
        }

        public void GrantItem(string itemId, int amount)
        {
            if (string.IsNullOrEmpty(itemId) || amount <= 0) return;
            int index = FindItemIndex(itemId);
            if (index >= 0)
            {
                ItemStack stack = items[index];
                stack.amount += amount;
                items[index] = stack;               // 结构体：改完要写回
            }
            else
            {
                items.Add(new ItemStack(itemId, amount));
            }
            if (OnItemChanged != null) OnItemChanged(itemId, GetItemAmount(itemId));
            Debug.Log("[奖励] 获得物品 " + itemId + " x" + amount + "，当前 " + GetItemAmount(itemId));
        }

        // ------------------------------- 查询接口 -------------------------------

        public int GetItemAmount(string itemId)
        {
            int index = FindItemIndex(itemId);
            return index < 0 ? 0 : items[index].amount;
        }

        /// <summary>把当前持有拼成一行，供调试面板显示。</summary>
        public string GetSummary()
        {
            string itemSummary = "无";
            if (items.Count > 0)
            {
                List<string> parts = new List<string>();
                for (int i = 0; i < items.Count; i++) parts.Add(items[i].ToString());
                itemSummary = string.Join(QuestText.ListSeparator, parts);
            }
            return "金币 " + gold + " / 经验 " + experience + " / 物品 " + itemSummary;
        }

        private int FindItemIndex(string itemId)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (string.Equals(items[i].itemId, itemId, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }
    }
}
