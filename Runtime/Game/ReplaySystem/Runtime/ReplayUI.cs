using UnityEngine;
using UnityEngine.UI;

namespace ReplaySystem
{
    /// <summary>
    /// UI 绑定与事件处理。挂在任意 GameObject 上，在 Inspector 中拖拽引用即可。
    /// </summary>
    public class ReplayUI : MonoBehaviour
    {
        public ReplayManager manager;

        [Header("顶部时间显示")]
        public Text timeText;

        [Header("进度条")]
        public Slider progressSlider;

        [Header("按钮")]
        public Button recordButton;       // 录制
        public Button stopRecordButton;   // 停止录制
        public Button playbackButton;     // 回放
        public Button pauseResumeButton;  // 暂停/继续
        public Button stopPlaybackButton; // 停止回放

        [Header("速度")]
        public Slider speedSlider;
        public Text speedText;

        [Header("文件")]
        public Button saveButton;         // 保存录制
        public Button loadButton;         // 加载回放

        private bool isPaused;

        void Start()
        {
            if (recordButton != null) recordButton.onClick.AddListener(OnRecord);
            if (stopRecordButton != null) stopRecordButton.onClick.AddListener(OnStopRecord);
            if (playbackButton != null) playbackButton.onClick.AddListener(OnPlayback);
            if (pauseResumeButton != null) pauseResumeButton.onClick.AddListener(OnPauseResume);
            if (stopPlaybackButton != null) stopPlaybackButton.onClick.AddListener(OnStopPlayback);
            if (saveButton != null) saveButton.onClick.AddListener(OnSave);
            if (loadButton != null) loadButton.onClick.AddListener(OnLoad);

            if (speedSlider != null)
            {
                speedSlider.minValue = 0.1f;
                speedSlider.maxValue = 5f;
                speedSlider.value = 1f;
                speedSlider.onValueChanged.AddListener(OnSpeedChanged);
            }

            if (progressSlider != null)
            {
                progressSlider.minValue = 0f;
                progressSlider.maxValue = 1f;
                progressSlider.value = 0f;
                progressSlider.onValueChanged.AddListener(OnProgressChanged);
            }
        }

        void Update()
        {
            if (manager == null) return;

            // 进度条实时反映播放进度（SetValueWithoutNotify 不会触发跳转回调，避免循环）
            if (progressSlider != null && manager.HasData)
                progressSlider.SetValueWithoutNotify(manager.Progress);

            if (timeText != null && manager.HasData)
            {
                float total = manager.TotalDuration;
                float current = total * manager.Progress;
                timeText.text = $"{FormatTime(current)} / {FormatTime(total)}";
            }

            if (speedText != null) speedText.text = $"{manager.Speed:F1}x";

            if (pauseResumeButton != null)
            {
                var label = pauseResumeButton.GetComponentInChildren<Text>();
                if (label != null) label.text = isPaused ? "继续" : "暂停";
            }
        }

        void OnRecord() => manager.StartRecording();
        void OnStopRecord() => manager.StopRecording();
        void OnPlayback() => manager.StartPlayback();
        void OnStopPlayback() => manager.StopPlayback();
        void OnSave() => manager.Save();

        void OnLoad()
        {
            // 编辑器下打开文件选择框；运行时回退到最近一次录制（可替换为 NativeFilePicker）
            string path = FileManager.OpenFileBrowser();
            if (string.IsNullOrEmpty(path)) path = FileManager.LoadLatest();
            if (!string.IsNullOrEmpty(path)) manager.Load(path);
            else Debug.LogWarning("[ReplaySystem] 未找到可加载的回放文件");
        }

        void OnPauseResume()
        {
            if (manager.CurrentState == ReplayManager.State.Playing)
            {
                manager.PausePlayback();
                isPaused = true;
            }
            else if (manager.CurrentState == ReplayManager.State.Paused)
            {
                manager.ResumePlayback();
                isPaused = false;
            }
        }

        void OnSpeedChanged(float v) => manager.SetSpeed(v);

        void OnProgressChanged(float v)
        {
            // 仅用户拖拽时触发；程序更新走 SetValueWithoutNotify，不会进入这里
            if (manager.HasData) manager.Seek(v);
        }

        static string FormatTime(float seconds)
        {
            int totalSec = Mathf.FloorToInt(seconds);
            int m = totalSec / 60;
            int s = totalSec % 60;
            return $"{m:00}:{s:00}";
        }
    }
}
