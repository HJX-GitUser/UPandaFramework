﻿using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace UPandaGF.RunTime.TaskSystem
{
    /// <summary>
    /// 任务日志面板（UGUI）：左侧任务列表 + 右侧详情 / 奖励 + 追踪与提交按钮。
    ///
    /// 设计要点：
    ///   · 面板不轮询任务状态 —— 订阅 QuestManager 的事件，有变化才刷新；
    ///   · Update 里只做"快捷键开关面板"，不查任何任务数据；
    ///   · listContent / 各行都可以留空，留空时自动生成，方便没有预制体时直接跑。
    ///
    /// ⚠️ panelRoot 建议指向**子物体**（真正显示的面板本体），不要指向本脚本所在物体：
    ///    如果把自己隐藏了，Update 不再执行，快捷键就打不开面板了。
    ///    留空时会退化为用 CanvasGroup 控制可见性，同样安全。
    /// </summary>
    [DisallowMultipleComponent]
    public class QuestLogPanel : MonoBehaviour
    {
        [Header("引用（可留空）")]
        [Tooltip("被显示/隐藏的面板本体；建议指向子物体。留空 = 用 CanvasGroup 控制可见性。")]
        public GameObject panelRoot;
        public Text titleText;
        [Tooltip("列表项父节点；留空则自动生成一个纵向列表。")]
        public Transform listContent;
        [Tooltip("列表项预制体；留空则用代码生成的行。")]
        public QuestLogEntryView entryPrefab;
        public Text detailTitleText;
        public Text detailBodyText;
        public Text rewardText;
        [Tooltip("接受任务按钮；留空时运行时会自动补一个（复制提交按钮的结构）。")]
        public Button acceptButton;
        public Text acceptButtonLabel;
        public Button trackButton;
        public Text trackButtonLabel;
        public Button turnInButton;

        [Header("行为")]
        public bool visibleOnStart = false;
        public KeyCode toggleKey = KeyCode.J;
        [Tooltip("额外的「提交选中任务」快捷键，None 表示不启用。")]
        public KeyCode turnInKey = KeyCode.None;
        [Tooltip("是否在列表里显示锁定（前置未满足）的任务。")]
        public bool showLocked = false;
        [Tooltip("是否在列表里显示已提交完成的任务。")]
        public bool showTurnedIn = false;

        private readonly Dictionary<string, QuestLogEntryView> rows = new Dictionary<string, QuestLogEntryView>();
        private readonly StringBuilder detailBuilder = new StringBuilder();

        private QuestManager manager;
        private QuestInstance selected;
        private CanvasGroup canvasGroup;
        private bool isVisible;

        public bool IsVisible { get { return isVisible; } }

        // ================================ 生命周期 ================================

        private void Awake()
        {
            ResolveReferences();
            EnsureButtons();
            QuestUiFactory.EnsureReadableFonts(gameObject);
            SetVisible(visibleOnStart);
        }

        private void OnEnable()
        {
            if (!Application.isPlaying) return;

            manager = QuestManager.Instance;      // 没有就自动创建
            if (manager == null) return;

            // 只订阅事件，不做轮询
            manager.OnQuestLogChanged += RebuildList;
            manager.OnObjectiveProgressChanged += HandleObjectiveProgressChanged;
            manager.OnQuestStatusChanged += HandleQuestStatusChanged;

            RebuildList();
        }

        private void OnDisable()
        {
            if (!Application.isPlaying || manager == null) return;

            manager.OnQuestLogChanged -= RebuildList;
            manager.OnObjectiveProgressChanged -= HandleObjectiveProgressChanged;
            manager.OnQuestStatusChanged -= HandleQuestStatusChanged;
        }

        private void Update()
        {
            // 仅快捷键检测；任务状态刷新全部走事件
            if (toggleKey != KeyCode.None && Input.GetKeyDown(toggleKey)) Toggle();
            if (turnInKey != KeyCode.None && Input.GetKeyDown(turnInKey)) HandleTurnInClicked();
        }

        private void ResolveReferences()
        {
            if (listContent == null)
            {
                // 没有任何 UI 配置时，给一个"能看"的默认列表
                RectTransform created = QuestUiFactory.CreateVerticalListRoot("QuestList", transform);
                ApplyAnchorsToDefaultList(created);
                listContent = created;
            }
            if (panelRoot == null) canvasGroup = GetComponent<CanvasGroup>();
        }

        private static void ApplyAnchorsToDefaultList(RectTransform rect)
        {
            QuestUiFactory.ApplyAnchors(rect, new Vector2(0f, 0f), new Vector2(1f, 1f), 12f, 12f, 60f, 12f);
        }

        private void EnsureButtons()
        {
            EnsureAcceptButton();       // 没接线时在运行时兜底补一个"接受任务"按钮

            if (acceptButton != null) acceptButton.onClick.AddListener(HandleAcceptClicked);
            if (turnInButton != null) turnInButton.onClick.AddListener(HandleTurnInClicked);
            if (trackButton != null) trackButton.onClick.AddListener(HandleTrackClicked);
        }

        /// <summary>
        /// 兜底：场景里没有手动挂"接受任务"按钮时，复制「提交任务」按钮的结构在运行时补一个，
        /// 并把三个按钮重排成三等份 —— 这样已经搭好的旧场景不改也能接受任务。
        /// </summary>
        private void EnsureAcceptButton()
        {
            if (acceptButton != null || turnInButton == null) return;

            GameObject clone = Instantiate(turnInButton.gameObject, turnInButton.transform.parent);
            clone.name = "AcceptButton";

            acceptButton = clone.GetComponent<Button>();
            if (acceptButton == null)
            {
                Destroy(clone);
                return;
            }
            acceptButton.onClick.RemoveAllListeners();      // 克隆体不带运行时监听，这里再保险一次

            acceptButtonLabel = clone.GetComponentInChildren<Text>(true);
            if (acceptButtonLabel != null) acceptButtonLabel.text = "接受任务";

            // 三等份：接受 0~0.32、追踪 0.34~0.66、提交 0.68~1
            SetButtonAnchors(acceptButton, 0f, 0.32f);
            SetButtonAnchors(trackButton, 0.34f, 0.66f);
            SetButtonAnchors(turnInButton, 0.68f, 1f);
        }

        private static void SetButtonAnchors(Button button, float min, float max)
        {
            if (button == null) return;
            QuestUiFactory.ApplyAnchors((RectTransform)button.transform, new Vector2(min, 0f), new Vector2(max, 1f));
        }

        // ============================== 显示 / 隐藏 ==============================

        public void Toggle() { SetVisible(!isVisible); }

        public void Show() { SetVisible(true); }

        public void Hide() { SetVisible(false); }

        public void SetVisible(bool visible)
        {
            isVisible = visible;

            if (panelRoot != null && panelRoot != gameObject)
            {
                panelRoot.SetActive(visible);
                return;
            }

            // 退化方案：用 CanvasGroup 控制，本物体保持激活（否则 Update 不再运行）
            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();
            canvasGroup.alpha = visible ? 1f : 0f;
            canvasGroup.interactable = visible;
            canvasGroup.blocksRaycasts = visible;
        }

        // ================================ 列表刷新 ================================

        /// <summary>整表重建（任务解锁 / 接受 / 提交 / 追踪变化时调用）。</summary>
        public void RebuildList()
        {
            if (listContent == null) return;

            for (int i = 0; i < listContent.childCount; i++)
            {
                listContent.GetChild(i).gameObject.SetActive(false);      // 先停用，避免重建期间被布局计算
            }
            for (int i = listContent.childCount - 1; i >= 0; i--)
            {
                Destroy(listContent.GetChild(i).gameObject);
            }
            rows.Clear();

            if (titleText != null)
            {
                int activeCount = 0;
                List<QuestInstance> all = manager != null ? manager.GetAllQuests() : new List<QuestInstance>();
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i].status == QuestStatus.Active) activeCount++;
                }
                titleText.text = "任务日志（进行中 " + activeCount + "）";
            }

            if (manager == null) return;

            List<QuestInstance> quests = manager.GetAllQuests();
            for (int i = 0; i < quests.Count; i++)
            {
                QuestInstance quest = quests[i];
                if (!ShouldShow(quest)) continue;

                QuestLogEntryView row = CreateRow();
                if (row == null) continue;

                row.Bind(quest, HandleRowClicked);
                rows[quest.QuestId] = row;
            }

            // 选中的任务被过滤掉了就清空详情
            if (selected != null && !rows.ContainsKey(selected.QuestId)) selected = null;
            RefreshDetail();
        }

        private bool ShouldShow(QuestInstance quest)
        {
            if (quest == null) return false;
            if (quest.status == QuestStatus.Locked && !showLocked) return false;
            if (quest.status == QuestStatus.TurnedIn && !showTurnedIn) return false;
            return true;
        }

        private QuestLogEntryView CreateRow()
        {
            QuestLogEntryView row;
            if (entryPrefab != null)
            {
                row = Instantiate(entryPrefab, listContent);
                row.gameObject.SetActive(true);
            }
            else
            {
                row = QuestUiFactory.CreateLogEntryRow(listContent);
            }
            QuestUiFactory.EnsureReadableFonts(row.gameObject);
            return row;
        }

        /// <summary>只刷新某一行的进度（进度上报走这里，避免整表重建）。</summary>
        private void RefreshRow(QuestInstance quest)
        {
            if (quest == null) return;
            QuestLogEntryView row;
            if (rows.TryGetValue(quest.QuestId, out row) && row != null) row.Refresh();
        }

        // ================================ 事件回调 ================================

        private void HandleObjectiveProgressChanged(QuestInstance quest, QuestObjectiveProgress objective)
        {
            RefreshRow(quest);
            if (selected != null && selected.QuestId == quest.QuestId) RefreshDetail();
        }

        private void HandleQuestStatusChanged(QuestInstance quest)
        {
            RefreshRow(quest);
            if (selected != null && selected.QuestId == quest.QuestId) RefreshDetail();
        }

        private void HandleRowClicked(QuestInstance quest)
        {
            selected = quest;
            RefreshDetail();
        }

        /// <summary>接受选中的任务：Available → Active，之后进度才会开始累计。</summary>
        private void HandleAcceptClicked()
        {
            if (manager == null || selected == null) return;
            if (!manager.AcceptQuest(selected.QuestId))
            {
                Debug.LogWarning("[QuestLogPanel] 接受失败：" + selected.Title + "（当前状态 " + QuestText.GetStatusName(selected.status) + "）");
            }
            RefreshDetail();
        }

        private void HandleTrackClicked()
        {
            if (manager == null || selected == null) return;
            manager.TrackQuest(selected.QuestId, !selected.isTracked);
            RefreshDetail();
        }

        private void HandleTurnInClicked()
        {
            if (manager == null || selected == null) return;
            manager.TurnInQuest(selected.QuestId);
            RefreshDetail();
        }

        // ================================ 详情面板 ================================

        private void RefreshDetail()
        {
            if (selected != null && manager != null)
            {
                // 实例可能因重新初始化被替换过，按 ID 重新取一次，避免显示过期对象
                QuestInstance fresh = manager.GetQuest(selected.QuestId);
                if (fresh != null) selected = fresh;
            }

            if (selected == null)
            {
                if (detailTitleText != null) detailTitleText.text = "未选择任务";
                if (detailBodyText != null) detailBodyText.text = "点击左侧列表查看详情。";
                if (rewardText != null) rewardText.text = string.Empty;
                if (acceptButton != null) acceptButton.interactable = false;
                if (turnInButton != null) turnInButton.interactable = false;
                if (trackButton != null) trackButton.interactable = false;
                return;
            }

            if (detailTitleText != null)
            {
                detailTitleText.text = selected.Title + "  （" + QuestText.GetQuestTypeName(selected.Type) + " · "
                                       + QuestText.GetStatusName(selected.status) + "）";
            }

            if (detailBodyText != null)
            {
                detailBuilder.Length = 0;
                if (!string.IsNullOrEmpty(selected.Description)) detailBuilder.AppendLine(selected.Description).AppendLine();

                for (int i = 0; i < selected.Objectives.Count; i++)
                {
                    QuestObjectiveProgress objective = selected.Objectives[i];
                    detailBuilder.Append(objective.IsCompleted ? "[√] " : "[  ] ")
                                 .Append(objective.GetDisplayText())
                                 .Append("   ").Append(objective.GetProgressText())
                                 .AppendLine();
                }
                detailBodyText.text = detailBuilder.ToString();
            }

            if (rewardText != null)
            {
                string reward = selected.Reward == null ? QuestText.NoReward : selected.Reward.GetSummary();
                rewardText.text = "奖励：" + reward;
            }

            if (acceptButton != null) acceptButton.interactable = selected.status == QuestStatus.Available;
            if (turnInButton != null) turnInButton.interactable = selected.status == QuestStatus.Completed;
            if (trackButton != null)
            {
                trackButton.interactable = selected.status == QuestStatus.Active;
                if (trackButtonLabel != null)
                {
                    trackButtonLabel.text = selected.isTracked ? "取消追踪" : "追踪";
                }
            }
        }
    }
}
