using UnityEngine;
using UnityEngine.UI;

namespace UPandaGF.RunTime.TaskSystem
{
    /// <summary>HUD 追踪条中的一行（挂在自己的条目预制体上，各引用可留空）。</summary>
    public class QuestHudEntryView : MonoBehaviour
    {
        [Header("引用（可留空）")]
        public Text titleText;
        public Text progressText;
        public Slider progressBar;

        /// <summary>当前绑定的任务。</summary>
        public QuestInstance Quest { get; private set; }

        public void Bind(QuestInstance quest)
        {
            Quest = quest;
            Refresh();
        }

        public void Refresh()
        {
            if (Quest == null) return;

            if (titleText != null) titleText.text = Quest.Title;
            if (progressText != null) progressText.text = Quest.GetProgressSummary();
            if (progressBar != null) progressBar.value = Quest.Progress01;
        }
    }
}
