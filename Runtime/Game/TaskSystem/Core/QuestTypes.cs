using System;
using System.Collections.Generic;
using UnityEngine;

namespace UPandaGF.RunTime.TaskSystem
{
    /// <summary>任务类型：主线 / 支线 / 日常。</summary>
    public enum QuestType
    {
        Main = 0,
        Side = 1,
        Daily = 2
    }

    /// <summary>
    /// 任务状态机：Locked → Available → Active → Completed → TurnedIn。
    /// 可重复任务（isRepeatable）提交后会回到 Available，可再次接受。
    /// </summary>
    public enum QuestStatus
    {
        /// <summary>锁定：前置任务未满足，玩家看不到。</summary>
        Locked = 0,
        /// <summary>可用：前置已满足，等待玩家接受（autoStart 任务会自动接受）。</summary>
        Available = 1,
        /// <summary>进行中：已接受，正在累计目标进度。</summary>
        Active = 2,
        /// <summary>已完成：所有目标都已达成，等待提交（领奖）。</summary>
        Completed = 3,
        /// <summary>已提交：奖励已发放，任务结束。</summary>
        TurnedIn = 4
    }

    /// <summary>任务目标类型。targetId 的含义随类型变化。</summary>
    public enum ObjectiveType
    {
        /// <summary>击杀怪物：targetId = 敌人 ID（如 "wolf"）。</summary>
        Kill = 0,
        /// <summary>收集物品：targetId = 物品 ID（如 "herb"）。</summary>
        Collect = 1,
        /// <summary>到达地点：targetId = 地点 ID（如 "village_gate"）。</summary>
        ReachLocation = 2,
        /// <summary>与 NPC 对话：targetId = NPC ID（如 "elder"）。</summary>
        TalkToNPC = 3
    }

    /// <summary>物品 + 数量。JSON 存档友好（只用公共字段）。</summary>
    [Serializable]
    public struct ItemStack
    {
        public string itemId;
        public int amount;

        public ItemStack(string itemId, int amount)
        {
            this.itemId = itemId;
            this.amount = amount;
        }

        public bool IsValid { get { return !string.IsNullOrEmpty(itemId) && amount > 0; } }

        public override string ToString()
        {
            return itemId + " x" + amount;
        }
    }

    /// <summary>任务奖励：经验 + 金币 + 若干物品。全部为 0 表示无奖励。</summary>
    [Serializable]
    public class QuestReward
    {
        public int experience;
        public int gold;
        public List<ItemStack> items = new List<ItemStack>();

        public bool IsEmpty
        {
            get { return experience <= 0 && gold <= 0 && (items == null || items.Count == 0); }
        }

        public void AddItem(string itemId, int amount)
        {
            if (items == null) items = new List<ItemStack>();
            items.Add(new ItemStack(itemId, amount));
        }

        /// <summary>给 UI 用的一行摘要，例如 "100 经验，50 金币，herb x2"。</summary>
        public string GetSummary()
        {
            List<string> parts = new List<string>();
            if (experience > 0) parts.Add(experience + " " + QuestText.Experience);
            if (gold > 0) parts.Add(gold + " " + QuestText.Gold);
            if (items != null)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i].IsValid) parts.Add(items[i].ToString());
                }
            }
            return parts.Count == 0 ? QuestText.NoReward : string.Join(QuestText.ListSeparator, parts);
        }
    }

    /// <summary>
    /// 集中管理界面文案，方便后续做多语言。
    /// </summary>
    public static class QuestText
    {
        public const string ListSeparator = "，";
        public const string NoReward = "无";

        public const string Experience = "经验";
        public const string Gold = "金币";

        public const string StatusLocked = "未解锁";
        public const string StatusAvailable = "可接受";
        public const string StatusActive = "进行中";
        public const string StatusCompleted = "可提交";
        public const string StatusTurnedIn = "已完成";

        public const string TypeMain = "主线";
        public const string TypeSide = "支线";
        public const string TypeDaily = "日常";

        public const string ObjectiveKill = "击杀";
        public const string ObjectiveCollect = "收集";
        public const string ObjectiveReachLocation = "到达";
        public const string ObjectiveTalkToNPC = "对话";

        public static string GetStatusName(QuestStatus status)
        {
            switch (status)
            {
                case QuestStatus.Locked: return StatusLocked;
                case QuestStatus.Available: return StatusAvailable;
                case QuestStatus.Active: return StatusActive;
                case QuestStatus.Completed: return StatusCompleted;
                case QuestStatus.TurnedIn: return StatusTurnedIn;
                default: return status.ToString();
            }
        }

        public static string GetQuestTypeName(QuestType type)
        {
            switch (type)
            {
                case QuestType.Main: return TypeMain;
                case QuestType.Side: return TypeSide;
                case QuestType.Daily: return TypeDaily;
                default: return type.ToString();
            }
        }

        public static string GetObjectiveTypeName(ObjectiveType type)
        {
            switch (type)
            {
                case ObjectiveType.Kill: return ObjectiveKill;
                case ObjectiveType.Collect: return ObjectiveCollect;
                case ObjectiveType.ReachLocation: return ObjectiveReachLocation;
                case ObjectiveType.TalkToNPC: return ObjectiveTalkToNPC;
                default: return type.ToString();
            }
        }
    }
}
