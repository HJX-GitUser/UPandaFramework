using System;
using UnityEngine;
using UnityEngine.UI;

namespace UPandaGF.RunTime.TaskSystem
{
    /// <summary>任务日志列表中的一行（挂在自己的列表项预制体上，各引用可留空）。</summary>
    public class QuestLogEntryView : MonoBehaviour
    {
        [Header("引用（可留空，留空则不显示该元素）")]
        public Button button;
        public Image background;
        public Text titleText;
        public Text statusText;
        public Text progressText;
        public Slider progressBar;

        /// <summary>当前绑定的任务。</summary>
        public QuestInstance Quest { get; private set; }

        private Action<QuestInstance> clickAction;

        /// <summary>绑定任务数据与点击回调（可重复调用，不会重复注册事件）。</summary>
        public void Bind(QuestInstance quest, Action<QuestInstance> onClick)
        {
            Quest = quest;
            clickAction = onClick;

            if (button != null)
            {
                button.onClick.RemoveListener(HandleClick);
                button.onClick.AddListener(HandleClick);
            }
            Refresh();
        }

        /// <summary>按任务当前状态刷新文本与进度条。</summary>
        public void Refresh()
        {
            if (Quest == null) return;

            if (titleText != null) titleText.text = Quest.Title;
            if (statusText != null)
            {
                statusText.text = QuestText.GetQuestTypeName(Quest.Type) + " · " + QuestText.GetStatusName(Quest.status);
            }
            if (progressText != null)
            {
                progressText.text = Quest.status == QuestStatus.Completed
                    ? "已完成，可提交"
                    : Quest.GetProgressSummary();
            }
            if (progressBar != null) progressBar.value = Quest.Progress01;
            if (background != null)
            {
                // 用底色区分状态，方便肉眼确认状态机是否正确
                if (Quest.status == QuestStatus.Completed) background.color = new Color(0.25f, 0.45f, 0.25f, 0.6f);
                else if (Quest.status == QuestStatus.Active) background.color = new Color(1f, 1f, 1f, 0.12f);
                else if (Quest.status == QuestStatus.Available) background.color = new Color(0.2f, 0.35f, 0.5f, 0.5f);
                else background.color = new Color(0.15f, 0.15f, 0.15f, 0.4f);
            }
        }

        private void HandleClick()
        {
            if (clickAction != null) clickAction(Quest);
        }
    }
}
