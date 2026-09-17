using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace UPandaGF.RunTime.TaskSystem
{
    /// <summary>
    /// HUD 追踪条：屏幕上常驻显示最近接受的若干个活跃任务进度（默认最多 3 个）。
    ///
    /// 同样只在 QuestManager 事件到达时刷新，没有 Update 轮询。
    /// container 留空时会在本物体下自动建一个纵向列表；条目预制体留空时用代码生成。
    /// </summary>
    [DisallowMultipleComponent]
    public class QuestHudTracker : MonoBehaviour
    {
        [Header("引用（可留空）")]
        [Tooltip("追踪条目的父节点；留空则自动生成。")]
        public Transform container;
        [Tooltip("条目预制体；留空则用代码生成。")]
        public QuestHudEntryView entryPrefab;
        [Tooltip("没有活跃任务时显示的提示文本（可留空）。")]
        public Text emptyHintText;

        [Header("行为")]
        [Tooltip("最多显示几条。")]
        public int maxEntries = 3;
        [Tooltip("只显示被追踪的任务；关闭则显示所有活跃任务。")]
        public bool showOnlyTracked = true;

        private QuestManager manager;
        private readonly Dictionary<string, QuestHudEntryView> rows = new Dictionary<string, QuestHudEntryView>();

        private void Awake()
        {
            if (container == null) container = transform;
            EnsureVerticalLayout(container);
            QuestUiFactory.EnsureReadableFonts(gameObject);
        }

        /// <summary>容器没有纵向布局时补一个，否则自动生成的多行会叠在同一个位置。</summary>
        private static void EnsureVerticalLayout(Transform parent)
        {
            if (parent == null) return;
            if (parent.GetComponent<VerticalLayoutGroup>() != null) return;

            VerticalLayoutGroup layout = parent.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 2f;
        }

        private void OnEnable()
        {
            if (!Application.isPlaying) return;

            manager = QuestManager.Instance;
            if (manager == null) return;

            manager.OnQuestLogChanged += Rebuild;
            manager.OnObjectiveProgressChanged += HandleObjectiveProgressChanged;
            manager.OnQuestStatusChanged += HandleQuestStatusChanged;

            Rebuild();
        }

        private void OnDisable()
        {
            if (!Application.isPlaying || manager == null) return;

            manager.OnQuestLogChanged -= Rebuild;
            manager.OnObjectiveProgressChanged -= HandleObjectiveProgressChanged;
            manager.OnQuestStatusChanged -= HandleQuestStatusChanged;
        }

        /// <summary>整表重建（任务接受 / 提交 / 追踪变化时）。</summary>
        public void Rebuild()
        {
            if (container == null) return;

            for (int i = container.childCount - 1; i >= 0; i--)
            {
                Destroy(container.GetChild(i).gameObject);
            }
            rows.Clear();

            if (manager == null) return;

            List<QuestInstance> quests = showOnlyTracked ? manager.GetTrackedQuests() : manager.GetActiveQuests();
            int limit = Mathf.Min(maxEntries, quests.Count);

            for (int i = 0; i < limit; i++)
            {
                QuestInstance quest = quests[i];

                QuestHudEntryView row;
                if (entryPrefab != null)
                {
                    row = Instantiate(entryPrefab, container);
                    row.gameObject.SetActive(true);
                }
                else
                {
                    row = QuestUiFactory.CreateHudEntryRow(container);
                }
                QuestUiFactory.EnsureReadableFonts(row.gameObject);

                row.Bind(quest);
                rows[quest.QuestId] = row;
            }

            if (emptyHintText != null) emptyHintText.gameObject.SetActive(limit == 0);
        }

        private void HandleObjectiveProgressChanged(QuestInstance quest, QuestObjectiveProgress objective)
        {
            RefreshRow(quest);
        }

        private void HandleQuestStatusChanged(QuestInstance quest)
        {
            RefreshRow(quest);
        }

        private void RefreshRow(QuestInstance quest)
        {
            if (quest == null) return;
            QuestHudEntryView row;
            if (rows.TryGetValue(quest.QuestId, out row) && row != null) row.Refresh();
        }
    }
}
