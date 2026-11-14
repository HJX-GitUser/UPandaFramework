using System;
using UnityEngine;
using UPandaGF;

namespace UPandaGF.Samples
{
    /// <summary>
    /// 系统设置使用场景示例：
    /// 1) 从场景打开框架内置的 <see cref="SettingsPanel"/>；
    /// 2) 演示业务如何读取/监听设置并真正消费（UI 缩放、灯光亮度、旋转速度）；
    /// 3) 演示推荐配置、提交预览、恢复默认与打印当前设置。
    /// <para>由 UPGameRoot 在场景就绪后注入 <see cref="IGameServices"/> 与 <see cref="IUIService"/>。
    /// 场景可用编辑器菜单 UPandaGF/示例/创建设置系统示例场景 一键生成。</para>
    /// </summary>
    [AddComponentMenu("UPandaGF/Samples/Settings Demo")]
    public sealed class SettingsDemoController : MonoBehaviour, IGameServicesConsumer, IUIServiceConsumer
    {
        [Tooltip("演示用主相机；亮度/视野等示例效果会作用到它。")]
        public Camera demoCamera;
        [Tooltip("演示用平行光；设置里的亮度会映射到它的 intensity。")]
        public Light demoLight;
        [Tooltip("按鼠标灵敏度旋转的物体。")]
        public Transform spinTarget;

        private IUIService ui;
        private ISettingsService settings;
        private bool ready;
        private float spinSpeed = 30f;
        private string lastEvent = "等待框架就绪…";

        public void Configure(IGameServices value)
        {
            settings = value != null ? value.Settings : null;
            if (settings == null)
            {
                lastEvent = "IGameServices.Settings 为空";
                Debug.LogError("[SettingsDemo] 未取得 ISettingsService，请确认 UPGameRoot 已完成启动。");
                return;
            }

            settings.Changed += OnSettingsChanged;
            settings.PreviewStateChanged += OnPreviewStateChanged;
            ready = true;
            lastEvent = "框架已就绪，可以打开设置";
            ApplyConsumption();
        }

        public void ConfigureUi(IUIService value)
        {
            ui = value;
            ApplyUiScale();
        }

        private void OnDestroy()
        {
            if (settings == null) return;
            settings.Changed -= OnSettingsChanged;
            settings.PreviewStateChanged -= OnPreviewStateChanged;
        }

        private void Update()
        {
            if (spinTarget != null)
                spinTarget.Rotate(0f, spinSpeed * Time.unscaledDeltaTime, 0f);
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(20f, 20f, 560f, 300f), GUI.skin.box);
            GUILayout.Label("UPandaFramework 系统设置示例");
            GUILayout.Space(4f);

            if (!ready || settings == null)
            {
                GUILayout.Label(lastEvent);
                GUILayout.EndArea();
                return;
            }

            GUILayout.Label("状态：" + lastEvent);
            GUILayout.Label(BuildSummary());
            if (settings.IsPreviewing) GUILayout.Label("当前有预览中的修改：可在面板底部确认/还原，或点下面“提交预览”。");
            GUILayout.Space(6f);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("打开设置", GUILayout.Height(32f))) OpenSettings();
            if (GUILayout.Button("推荐设置", GUILayout.Height(32f))) ApplyRecommended();
            if (GUILayout.Button("提交预览", GUILayout.Height(32f))) CommitPreview();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("恢复默认", GUILayout.Height(28f))) ResetAll();
            if (GUILayout.Button("打印设置", GUILayout.Height(28f))) PrintSettings();
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);
            GUILayout.Label("设置面板由描述表生成；新增设置项无需修改本示例。\n" +
                            "示例消费：UI 缩放、灯光亮度、鼠标灵敏度→旋转速度。");
            GUILayout.EndArea();
        }

        private void OpenSettings()
        {
            if (ui == null)
            {
                lastEvent = "IUIService 尚未注入";
                return;
            }

            ui.ShowPanel<SettingsPanel>(null, panel =>
            {
                if (panel == null) lastEvent = "设置面板打开失败（检查 UI/Canvas 是否就绪）";
                else lastEvent = "设置面板已打开";
            });
        }

        private void ApplyRecommended()
        {
            if (settings == null) return;
            // 画质预设是 PreviewConfirm 项：先进入预览，随后统一提交。
            settings.Set("graphics.preset", QualityPreset.Medium);
            settings.Set("audio.master", 0.7f);
            settings.Set("audio.music", 0.6f);
            settings.Set("input.mouseSensitivity", 1.2f);
            settings.Set("accessibility.uiScale", 1.05f);
            _ = settings.CommitAsync();
            lastEvent = "已应用推荐设置";
            ApplyConsumption();
        }

        private void CommitPreview()
        {
            if (settings == null || !settings.IsPreviewing) return;
            _ = settings.CommitAsync();
            lastEvent = "预览修改已提交";
        }

        private void ResetAll()
        {
            if (settings == null) return;
            settings.Reset(SettingsScope.All);
            lastEvent = "已恢复全部默认设置";
            ApplyConsumption();
        }

        private void PrintSettings()
        {
            if (settings == null) return;
            Debug.Log("[SettingsDemo] 当前设置：\n" + BuildSummary() +
                      "\n完整 JSON：\n" + JsonUtility.ToJson(settings.Current, true));
        }

        private void OnSettingsChanged(SettingsChange change)
        {
            lastEvent = "设置变更：" + change.Key;
            if (change.Key == "accessibility.uiScale") ApplyUiScale();
            if (change.Key == "graphics.brightness") ApplyLight();
            if (change.Key == "input.mouseSensitivity") ApplySpinSpeed();
        }

        private void OnPreviewStateChanged(bool previewing)
        {
            lastEvent = previewing ? "进入预览：请确认或还原" : "预览结束";
        }

        /// <summary>把设置值消费到实际对象上；真实项目在这里驱动相机/UI/音频/特效。</summary>
        private void ApplyConsumption()
        {
            ApplyUiScale();
            ApplyLight();
            ApplySpinSpeed();
        }

        private void ApplyUiScale()
        {
            if (ui == null || settings == null) return;
            ui.SetUiScale(settings.Get<float>("accessibility.uiScale"));
        }

        private void ApplyLight()
        {
            if (demoLight == null || settings == null) return;
            float brightness = settings.Get<float>("graphics.brightness"); // 0.5–1.5
            demoLight.intensity = Mathf.Clamp(0.4f + brightness * 0.8f, 0.4f, 2f);
        }

        private void ApplySpinSpeed()
        {
            if (settings == null) return;
            spinSpeed = 30f * settings.Get<float>("input.mouseSensitivity");
        }

        private string BuildSummary()
        {
            if (settings == null) return "未取得设置服务";
            SettingsData data = settings.Current;
            return string.Format(
                "主音量 {0:P0} | 音乐 {1:P0} | 音效 {2:P0}\n" +
                "画质 {3} | 抗锯齿 {4} | 渲染缩放 {5:0.##}\n" +
                "鼠标灵敏度 {6:0.##} | 反转 Y {7} | UI 缩放 {8:0.##}\n" +
                "语言 {9}",
                data.audio.master, data.audio.music, data.audio.sfx,
                data.graphics.preset, data.graphics.antiAliasing, data.graphics.renderScale,
                data.input.mouseSensitivity, data.input.invertY, data.accessibility.uiScale,
                string.IsNullOrEmpty(data.language.ui) ? "跟随系统" : data.language.ui);
        }
    }
}
