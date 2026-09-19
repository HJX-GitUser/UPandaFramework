﻿using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UPandaGF.RunTime.TaskSystem;

namespace UPandaGF.EditorTools
{
    /// <summary>
    /// 任务系统一键搭建工具：
    ///   1. 生成示例任务资产（ScriptableObject）到 TaskSystem/Resources/Quests；
    ///   2. 在当前场景搭出任务日志面板 + HUD 追踪条（含 EventSystem）；
    ///   3. 在当前场景搭出 3D 演示内容（地面 / 玩家 / 5 只狼 / 3 个草药 / NPC / 到达区域 / 键盘驱动）。
    ///
    /// 菜单：UPandaGF/Runtime/任务系统/…
    /// </summary>
    public static class QuestDemoInstaller
    {
        private const string ModuleFolder = "Assets/Scripts/upanda-framework/Runtime/Game/TaskSystem";
        private const string ResourcesFolder = ModuleFolder + "/Resources";
        private const string QuestFolder = ResourcesFolder + "/Quests";

        private const string KillWolvesQuestId = "Quest_KillWolves";
        private const string CollectHerbsQuestId = "Quest_CollectHerbs";
        private const string ReachGateQuestId = "Quest_ReachGate";
        private const string DailyPatrolQuestId = "Quest_DailyPatrol";

        // ================================ 菜单入口 ================================

        [MenuItem("UPandaGF/Runtime/任务系统/一键搭建（任务资产 + UI + 演示场景）", false, 0)]
        public static void BuildAll()
        {
            CreateSampleQuests();
            BuildQuestUi();
            BuildDemoScene();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[QuestDemo] 一键搭建完成。按 Play 后用 1/2/3/4 模拟玩法事件，J 开关任务日志，N 提交任务。");
        }

        [MenuItem("UPandaGF/Runtime/任务系统/1. 生成示例任务资产", false, 20)]
        public static void CreateSampleQuests()
        {
            EnsureQuestFolder();

            // ① 主线：击杀 5 只野狼（无前置，开局即可接受）
            QuestDefinition killWolves = CreateOrLoadDefinition(KillWolvesQuestId);
            killWolves.title = "野狼威胁";
            killWolves.description = "村外的野狼越来越多，去清掉 5 只吧。";
            killWolves.questType = QuestType.Main;
            killWolves.autoStart = false;
            killWolves.isRepeatable = false;
            killWolves.prerequisites = new List<string>();
            killWolves.objectives = new List<QuestObjective>
            {
                MakeObjective(ObjectiveType.Kill, "wolf", 5, "击杀 5 只野狼")
            };
            killWolves.reward = MakeReward(100, 50, new string[] { "wolf_pelt" }, new int[] { 2 });
            SaveDefinition(killWolves);

            // ② 支线：采集 + 对话（多目标 + 自动接受；前置是上面的主线）
            QuestDefinition collectHerbs = CreateOrLoadDefinition(CollectHerbsQuestId);
            collectHerbs.title = "给药师采药";
            collectHerbs.description = "采集 3 株草药，然后交给村口的长老。（本任务 autoStart，前置完成后会自动接受）";
            collectHerbs.questType = QuestType.Side;
            collectHerbs.autoStart = true;
            collectHerbs.isRepeatable = false;
            collectHerbs.prerequisites = new List<string> { KillWolvesQuestId };
            collectHerbs.objectives = new List<QuestObjective>
            {
                MakeObjective(ObjectiveType.Collect, "herb", 3, "采集 3 株草药"),
                MakeObjective(ObjectiveType.TalkToNPC, "elder", 1, "把草药交给长老")
            };
            collectHerbs.reward = MakeReward(60, 30, new string[] { "herb_potion" }, new int[] { 1 });
            SaveDefinition(collectHerbs);

            // ③ 支线：到达指定地点（单目标 + 前置）
            QuestDefinition reachGate = CreateOrLoadDefinition(ReachGateQuestId);
            reachGate.title = "探查村口";
            reachGate.description = "去村口看看情况。";
            reachGate.questType = QuestType.Side;
            reachGate.autoStart = false;
            reachGate.isRepeatable = false;
            reachGate.prerequisites = new List<string> { KillWolvesQuestId };
            reachGate.objectives = new List<QuestObjective>
            {
                MakeObjective(ObjectiveType.ReachLocation, "village_gate", 1, "到达村口")
            };
            reachGate.reward = MakeReward(20, 10, null, null);
            SaveDefinition(reachGate);

            // ④ 日常：可重复任务
            QuestDefinition dailyPatrol = CreateOrLoadDefinition(DailyPatrolQuestId);
            dailyPatrol.title = "每日巡逻";
            dailyPatrol.description = "日常任务：击杀 2 只野狼。提交后可再次接受。";
            dailyPatrol.questType = QuestType.Daily;
            dailyPatrol.autoStart = false;
            dailyPatrol.isRepeatable = true;
            dailyPatrol.prerequisites = new List<string>();
            dailyPatrol.objectives = new List<QuestObjective>
            {
                MakeObjective(ObjectiveType.Kill, "wolf", 2, "巡逻并击杀 2 只野狼")
            };
            dailyPatrol.reward = MakeReward(30, 20, null, null);
            SaveDefinition(dailyPatrol);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[QuestDemo] 已生成 4 个示例任务资产：" + QuestFolder);
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<QuestDefinition>(QuestFolder + "/" + KillWolvesQuestId + ".asset");
        }

        [MenuItem("UPandaGF/Runtime/任务系统/2. 搭建任务 UI（任务日志 + HUD）", false, 21)]
        public static void BuildQuestUi()
        {
            QuestLogPanel existing = UnityEngine.Object.FindObjectOfType<QuestLogPanel>();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                Debug.LogWarning("[QuestDemo] 场景里已经有任务日志面板了，跳过创建。");
                return;
            }

            QuestUiFactory.EnsureEventSystem();
            Canvas canvas = QuestUiFactory.CreateCanvas("QuestCanvas", 100);

            Transform panel = BuildLogPanel(canvas.transform);
            BuildHud(canvas.transform);

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Selection.activeGameObject = panel.gameObject;
            Debug.Log("[QuestDemo] 任务 UI 已搭建：QuestCanvas/QuestLog（面板）+ QuestCanvas/QuestHud（追踪条）。");
        }

        [MenuItem("UPandaGF/Runtime/任务系统/3. 搭建 3D 演示内容", false, 22)]
        public static void BuildDemoScene()
        {
            if (UnityEngine.Object.FindObjectOfType<QuestDemoDriver>() != null)
            {
                Debug.LogWarning("[QuestDemo] 场景里已经有 QuestDemoDriver 了，跳过 3D 演示内容创建。");
                return;
            }

            // ---------- 地面（Plane 默认 10x10，放大 4 倍 = 40x40） ----------
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "DemoGround";
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = new Vector3(4f, 1f, 4f);
            Undo.RegisterCreatedObjectUndo(ground, "Create DemoGround");

            // ---------- 玩家（CharacterController 不需要 Rigidbody 也能触发 Trigger） ----------
            GameObject player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            player.name = "DemoPlayer";
            player.transform.position = new Vector3(0f, 1f, -8f);
            UnityEngine.Object.DestroyImmediate(player.GetComponent<CapsuleCollider>());   // 避免与 CharacterController 重复
            player.AddComponent<CharacterController>();
            player.AddComponent<DemoPlayerController>();
            Undo.RegisterCreatedObjectUndo(player, "Create DemoPlayer");

            // ---------- 5 只野狼（点击即可击杀） ----------
            for (int i = 0; i < 5; i++)
            {
                GameObject wolf = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wolf.name = "DemoWolf_" + i;
                wolf.transform.position = new Vector3(-6f + i * 3f, 0.4f, 4f + (i % 2) * 3f);
                wolf.transform.localScale = new Vector3(0.8f, 0.8f, 0.8f);

                DemoEnemy enemy = wolf.AddComponent<DemoEnemy>();
                enemy.enemyId = "wolf";
                enemy.maxHp = 1;
                Undo.RegisterCreatedObjectUndo(wolf, "Create DemoWolf");
            }

            // ---------- 3 个草药（碰到即拾取） ----------
            for (int i = 0; i < 3; i++)
            {
                GameObject herb = GameObject.CreatePrimitive(PrimitiveType.Cube);
                herb.name = "DemoHerb_" + i;
                herb.transform.position = new Vector3(-4f + i * 4f, 0.4f, 11f);
                herb.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f);

                DemoCollectible collectible = herb.AddComponent<DemoCollectible>();
                collectible.itemId = "herb";
                collectible.amount = 1;

                herb.GetComponent<Collider>().isTrigger = true;   // 碰到就拾取
                Undo.RegisterCreatedObjectUndo(herb, "Create DemoHerb");
            }

            // ---------- NPC（点击即对话） ----------
            GameObject npc = GameObject.CreatePrimitive(PrimitiveType.Cube);
            npc.name = "DemoNpc_Elder";
            npc.transform.position = new Vector3(9f, 1f, 14f);
            npc.transform.localScale = new Vector3(1.2f, 2f, 1.2f);
            DemoNpc npcComponent = npc.AddComponent<DemoNpc>();
            npcComponent.npcId = "elder";
            npcComponent.displayName = "长老";
            Undo.RegisterCreatedObjectUndo(npc, "Create DemoNpc");

            // ---------- 到达区域（隐藏的 Trigger 立方体） ----------
            GameObject gate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gate.name = "DemoGateZone";
            gate.transform.position = new Vector3(0f, 1.5f, 20f);
            gate.transform.localScale = new Vector3(14f, 3f, 4f);
            gate.GetComponent<Collider>().isTrigger = true;
            UnityEngine.Object.DestroyImmediate(gate.GetComponent<MeshRenderer>());   // 只保留触发体，不显示方块
            DemoLocationTrigger trigger = gate.AddComponent<DemoLocationTrigger>();
            trigger.locationId = "village_gate";
            trigger.oneShot = true;
            Undo.RegisterCreatedObjectUndo(gate, "Create DemoGateZone");

            // ---------- 键盘驱动 ----------
            GameObject driver = new GameObject("QuestDemoDriver");
            driver.AddComponent<QuestDemoDriver>();
            Undo.RegisterCreatedObjectUndo(driver, "Create QuestDemoDriver");

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[QuestDemo] 3D 演示内容已创建：WASD 移动玩家，点击方块杀狼 / 与 NPC 对话，走进村口区域触发到达目标。");
        }

        // ============================== UI 具体搭建 ==============================

        private static Transform BuildLogPanel(Transform canvas)
        {
            // 外层常驻（脚本挂这里，保证隐藏面板时 Update 仍在跑，快捷键才能重新打开）
            RectTransform logRoot = QuestUiFactory.CreateUiObject("QuestLog", canvas);
            QuestUiFactory.ApplyAnchors(logRoot, new Vector2(0f, 0f), new Vector2(0.42f, 1f), 16f, 8f, 16f, 16f);

            // 真正显示/隐藏的面板本体
            RectTransform body = QuestUiFactory.CreateUiObject("Body", logRoot);
            QuestUiFactory.Stretch(body);
            Image background = body.gameObject.AddComponent<Image>();
            background.color = new Color(0.07f, 0.09f, 0.13f, 0.92f);

            Text title = QuestUiFactory.CreateText("Title", body, "任务日志", 26, TextAnchor.MiddleLeft, Color.white);
            QuestUiFactory.ApplyAnchors(title.rectTransform, new Vector2(0f, 0.93f), new Vector2(1f, 1f), 16f, 16f, 6f, 0f);

            RectTransform content;
            ScrollRect scroll = QuestUiFactory.CreateScrollView("QuestScroll", body, out content);
            QuestUiFactory.ApplyAnchors((RectTransform)scroll.transform, new Vector2(0f, 0.42f), new Vector2(1f, 0.92f), 16f, 16f, 0f, 0f);

            Text detailTitle = QuestUiFactory.CreateText("DetailTitle", body, "未选择任务", 22, TextAnchor.MiddleLeft, new Color(1f, 0.92f, 0.7f));
            QuestUiFactory.ApplyAnchors(detailTitle.rectTransform, new Vector2(0f, 0.36f), new Vector2(1f, 0.41f), 16f, 16f, 0f, 0f);

            Text detailBody = QuestUiFactory.CreateText("DetailBody", body, "点击左侧列表查看详情。", 18, TextAnchor.UpperLeft, new Color(0.9f, 0.9f, 0.9f));
            QuestUiFactory.ApplyAnchors(detailBody.rectTransform, new Vector2(0f, 0.16f), new Vector2(1f, 0.36f), 16f, 16f, 0f, 0f);

            Text rewardText = QuestUiFactory.CreateText("RewardText", body, "奖励：-", 18, TextAnchor.MiddleLeft, new Color(0.7f, 1f, 0.8f));
            QuestUiFactory.ApplyAnchors(rewardText.rectTransform, new Vector2(0f, 0.10f), new Vector2(1f, 0.16f), 16f, 16f, 0f, 0f);

            RectTransform buttonBar = QuestUiFactory.CreateUiObject("Buttons", body);
            QuestUiFactory.ApplyAnchors(buttonBar, new Vector2(0f, 0.02f), new Vector2(1f, 0.09f), 16f, 16f, 0f, 0f);

            Text acceptLabel;
            Button acceptButton = QuestUiFactory.CreateButton("AcceptButton", buttonBar, "接受任务",
                new Color(0.55f, 0.45f, 0.15f, 1f), out acceptLabel);
            QuestUiFactory.ApplyAnchors((RectTransform)acceptButton.transform, new Vector2(0f, 0f), new Vector2(0.32f, 1f));

            Text trackLabel;
            Button trackButton = QuestUiFactory.CreateButton("TrackButton", buttonBar, "追踪",
                new Color(0.2f, 0.45f, 0.7f, 1f), out trackLabel);
            QuestUiFactory.ApplyAnchors((RectTransform)trackButton.transform, new Vector2(0.34f, 0f), new Vector2(0.66f, 1f));

            Text turnInLabel;
            Button turnInButton = QuestUiFactory.CreateButton("TurnInButton", buttonBar, "提交任务",
                new Color(0.25f, 0.6f, 0.35f, 1f), out turnInLabel);
            QuestUiFactory.ApplyAnchors((RectTransform)turnInButton.transform, new Vector2(0.68f, 0f), new Vector2(1f, 1f));

            QuestLogPanel panel = logRoot.gameObject.AddComponent<QuestLogPanel>();
            panel.panelRoot = body.gameObject;
            panel.titleText = title;
            panel.listContent = content;
            panel.detailTitleText = detailTitle;
            panel.detailBodyText = detailBody;
            panel.rewardText = rewardText;
            panel.acceptButton = acceptButton;
            panel.acceptButtonLabel = acceptLabel;
            panel.trackButton = trackButton;
            panel.trackButtonLabel = trackLabel;
            panel.turnInButton = turnInButton;
            panel.visibleOnStart = false;
            panel.toggleKey = KeyCode.J;

            return logRoot;
        }

        private static void BuildHud(Transform canvas)
        {
            RectTransform hud = QuestUiFactory.CreateUiObject("QuestHud", canvas);
            QuestUiFactory.ApplyAnchors(hud, new Vector2(0.6f, 0.72f), new Vector2(1f, 1f), 8f, 16f, 16f, 8f);

            Image background = hud.gameObject.AddComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.35f);

            Text header = QuestUiFactory.CreateText("Header", hud, "任务追踪", 20, TextAnchor.MiddleLeft, new Color(1f, 0.92f, 0.7f));
            QuestUiFactory.ApplyAnchors(header.rectTransform, new Vector2(0f, 0.82f), new Vector2(1f, 1f), 12f, 12f, 4f, 0f);

            // 条目容器：纵向布局、行高由 LayoutElement 决定
            RectTransform entries = QuestUiFactory.CreateUiObject("Entries", hud);
            QuestUiFactory.ApplyAnchors(entries, new Vector2(0f, 0f), new Vector2(1f, 0.82f), 10f, 10f, 0f, 6f);

            VerticalLayoutGroup layout = entries.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 2f;

            QuestHudTracker tracker = hud.gameObject.AddComponent<QuestHudTracker>();
            tracker.container = entries;
            tracker.maxEntries = 3;

            Text emptyHint = QuestUiFactory.CreateText("EmptyHint", hud, "（暂无进行中的任务）", 16, TextAnchor.UpperLeft, new Color(0.8f, 0.8f, 0.8f));
            QuestUiFactory.ApplyAnchors(emptyHint.rectTransform, new Vector2(0f, 0.55f), new Vector2(1f, 0.82f), 14f, 14f, 0f, 0f);
            tracker.emptyHintText = emptyHint;
        }

        // ============================== 任务资产辅助 ==============================

        private static void EnsureQuestFolder()
        {
            if (!AssetDatabase.IsValidFolder(ModuleFolder))
            {
                AssetDatabase.CreateFolder("Assets/Scripts/upanda-framework/Runtime/Game", "TaskSystem");
            }
            if (!AssetDatabase.IsValidFolder(ResourcesFolder))
            {
                AssetDatabase.CreateFolder(ModuleFolder, "Resources");
            }
            if (!AssetDatabase.IsValidFolder(QuestFolder))
            {
                AssetDatabase.CreateFolder(ResourcesFolder, "Quests");
            }
        }

        private static QuestDefinition CreateOrLoadDefinition(string assetName)
        {
            string path = QuestFolder + "/" + assetName + ".asset";
            QuestDefinition definition = AssetDatabase.LoadAssetAtPath<QuestDefinition>(path);

            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<QuestDefinition>();
                AssetDatabase.CreateAsset(definition, path);
            }

            definition.questId = assetName;      // ID 固定成资产名，避免改名后存档对不上
            return definition;
        }

        private static void SaveDefinition(QuestDefinition definition)
        {
            EditorUtility.SetDirty(definition);
        }

        private static QuestObjective MakeObjective(ObjectiveType type, string targetId, int requiredAmount, string description)
        {
            QuestObjective objective = new QuestObjective();
            objective.objectiveId = type + "_" + targetId;   // 稳定 ID：存档按它对齐进度
            objective.type = type;
            objective.targetId = targetId;
            objective.requiredAmount = requiredAmount;
            objective.description = description;
            return objective;
        }

        private static QuestReward MakeReward(int experience, int gold, string[] itemIds, int[] amounts)
        {
            QuestReward reward = new QuestReward();
            reward.experience = experience;
            reward.gold = gold;
            reward.items = new List<ItemStack>();

            if (itemIds != null && amounts != null)
            {
                int count = Mathf.Min(itemIds.Length, amounts.Length);
                for (int i = 0; i < count; i++) reward.AddItem(itemIds[i], amounts[i]);
            }
            return reward;
        }
    }
}
