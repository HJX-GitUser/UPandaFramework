﻿using System.Collections.Generic;
using UnityEngine;

namespace UPandaGF.RunTime.TaskSystem
{
    /// <summary>
    /// 键盘演示驱动：不用任何场景内容也能验证任务系统全流程。
    ///
    /// 键位：
    ///   1 = 击杀 1 只野狼（走事件总线）
    ///   2 = 拾取 1 个草药
    ///   3 = 到达村口
    ///   4 = 与长老对话
    ///   K = 一次性击杀 5 只野狼（快速推进"击杀 5 只野狼"）
    ///   N = 选中第一个「可提交」任务并提交（等价于点详情页的提交按钮）
    ///   J = 打开 / 关闭任务日志（由 QuestLogPanel 自己处理，这里只做提示）
    ///   S = 存档   L = 读档   R = 重置全部任务
    ///
    /// 屏幕左上角的文字用 OnGUI 直接画（仅调试用，不参与正式 UI）。
    /// </summary>
    [DisallowMultipleComponent]
    public class QuestDemoDriver : MonoBehaviour
    {
        [Header("示例 ID（要与任务资产里配的 targetId 一致）")]
        public string wolfId = "wolf";
        public string herbId = "herb";
        public string locationId = "village_gate";
        public string npcId = "elder";

        [Header("行为")]
        [Tooltip("自动接受所有“可接受”的任务。建议保持开启：目标进度只对 Active 任务累计，关掉后需要手动点面板里的「接受任务」。")]
        public bool autoAcceptAvailableQuests = true;

        [Header("显示")]
        public bool showHelp = true;
        public int fontSize = 18;

        private PlayerRewardService wallet;
        private bool acceptingQuests;

        private void Start()
        {
            QuestManager manager = QuestManager.Instance;
            if (manager == null) return;

            Debug.Log("[QuestDemo] 初始状态：" + manager.GetDebugSummary());

            // 管理器的“列表变化”事件同时被用来续接任务链：前置完成后会自动把新解锁的任务接掉
            manager.OnQuestLogChanged += HandleQuestLogChanged;
            AcceptAllAvailableQuests();
        }

        private void OnDestroy()
        {
            QuestManager manager = QuestManager.InstanceOrNull;
            if (manager != null) manager.OnQuestLogChanged -= HandleQuestLogChanged;
        }

        private void HandleQuestLogChanged()
        {
            if (autoAcceptAvailableQuests) AcceptAllAvailableQuests();
        }

        /// <summary>把所有“可接受”的任务接掉；返回本次接受的数量。</summary>
        public int AcceptAllAvailableQuests()
        {
            if (!autoAcceptAvailableQuests) return 0;

            QuestManager manager = QuestManager.InstanceOrNull;
            if (manager == null || acceptingQuests) return 0;   // 防重入：AcceptQuest 会再抛 OnQuestLogChanged

            acceptingQuests = true;
            int accepted = 0;
            try
            {
                List<QuestInstance> available = manager.GetQuestsByStatus(QuestStatus.Available);
                for (int i = 0; i < available.Count; i++)
                {
                    if (manager.AcceptQuest(available[i].QuestId)) accepted++;
                }
            }
            finally
            {
                acceptingQuests = false;
            }

            if (accepted > 0) Debug.Log("[QuestDemo] 自动接受任务 " + accepted + " 个。");
            return accepted;
        }

        /// <summary>手动接受第一个“可接受”的任务（A 键）。</summary>
        private void AcceptFirstAvailableQuest()
        {
            QuestManager manager = QuestManager.InstanceOrNull;
            if (manager == null) return;

            List<QuestInstance> available = manager.GetQuestsByStatus(QuestStatus.Available);
            if (available.Count == 0)
            {
                Debug.Log("[QuestDemo] 当前没有可接受的任务。");
                return;
            }
            manager.AcceptQuest(available[0].QuestId);
        }

        private void Update()
        {
            // ---- 四类玩法事件，全部都只有一行 ----
            if (Input.GetKeyDown(KeyCode.Alpha1)) GameplayEventBus.RaiseEnemyKilled(wolfId, 1);
            if (Input.GetKeyDown(KeyCode.Alpha2)) GameplayEventBus.RaiseItemCollected(herbId, 1);
            if (Input.GetKeyDown(KeyCode.Alpha3)) GameplayEventBus.RaiseLocationReached(locationId);
            if (Input.GetKeyDown(KeyCode.Alpha4)) GameplayEventBus.RaiseNpcTalked(npcId);

            // ---- 批量击杀，快速把"击杀 5 只野狼"推满 ----
            if (Input.GetKeyDown(KeyCode.K)) GameplayEventBus.RaiseEnemyKilled(wolfId, 5);

            // ---- 接受第一个可接受的任务 ----
            if (Input.GetKeyDown(KeyCode.A)) AcceptFirstAvailableQuest();

            // ---- 提交第一个可提交的任务 ----
            if (Input.GetKeyDown(KeyCode.N)) TurnInFirstCompletedQuest();

            QuestManager manager = QuestManager.InstanceOrNull;
            if (manager == null) return;

            // ---- 存档 / 读档 / 重置 ----
            if (Input.GetKeyDown(KeyCode.S)) manager.SaveToFile();
            if (Input.GetKeyDown(KeyCode.L)) manager.LoadFromFile();
            if (Input.GetKeyDown(KeyCode.R)) manager.ResetAllProgress();
        }

        private void TurnInFirstCompletedQuest()
        {
            QuestManager manager = QuestManager.InstanceOrNull;
            if (manager == null) return;

            var completed = manager.GetQuestsByStatus(QuestStatus.Completed);
            if (completed.Count > 0) manager.TurnInQuest(completed[0].QuestId);
        }

        private void OnGUI()
        {
            if (!showHelp) return;

            if (wallet == null) wallet = PlayerRewardService.Instance;

            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.fontSize = fontSize;
            style.richText = false;

            GUILayout.BeginArea(new Rect(12f, 12f, 560f, 320f));

            GUILayout.Label("【任务系统演示】", style);
            GUILayout.Label("1 击杀野狼 / 2 拾取草药 / 3 到达村口 / 4 与长老对话", style);
            GUILayout.Label("K 击杀 ×5   A 接受任务   N 提交任务   J 任务日志面板", style);
            GUILayout.Label("S 存档   L 读档   R 重置", style);

            QuestManager manager = QuestManager.InstanceOrNull;
            if (manager != null) GUILayout.Label(manager.GetDebugSummary(), style);
            if (wallet != null) GUILayout.Label(wallet.GetSummary(), style);

            GUILayout.EndArea();
        }
    }
}
