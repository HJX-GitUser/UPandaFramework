﻿using UnityEngine;
using UnityEngine.UI;

namespace UPandaGF.RunTime.TaskSystem
{
    /// <summary>
    /// UGUI 构建工具：让任务 UI 既能用编辑器搭好（手动/一键生成），
    /// 也能在完全没有预制体时于运行时自动生成，方便"接上就能跑"。
    ///
    /// 字体统一由 UPandaGF.UnityBuiltinFont 提供：
    ///   · 编辑器里创建 Text 时用内置字体资产（可序列化，安全）；
    ///   · 运行时优先用系统里的中文字体（动态字体不参与序列化），<see cref="EnsureReadableFonts"/> 负责批量替换；
    ///   · 这样即使内置字体没有中文字形，游戏里也能正常显示中文。
    /// </summary>
    public static class QuestUiFactory
    {
        // ================================ 字体 ================================

        /// <summary>
        /// 内置字体资产（可安全序列化进场景），主要用于编辑器搭 UI。
        /// 具体规则（按 Unity 版本选名、只警告一次）见 <see cref="UPandaGF.UnityBuiltinFont.Get"/>。
        /// </summary>
        public static Font GetBuiltinFont()
        {
            return UnityBuiltinFont.Get();
        }

        /// <summary>
        /// 默认字体：运行期优先用系统动态字体（有中文字形，且不受内置字体改名影响），
        /// 编辑器（未运行）时退回内置字体资产（可序列化）。
        /// </summary>
        public static Font GetDefaultFont()
        {
            return UnityBuiltinFont.GetDefault();
        }

        /// <summary>运行时动态创建的系统字体（优先中文），仅在 Play 模式下可用。</summary>
        public static Font GetRuntimeCjkFont()
        {
            return UnityBuiltinFont.GetCjkRuntimeFont();
        }

        /// <summary>
        /// 把整棵 UI 里"还在用内置字体（不含中文字形）"的 Text 换成运行时中文字体。
        /// 只在运行时执行 —— 动态字体不是资产，写进场景会在下次加载时变成 None。
        /// </summary>
        public static void EnsureReadableFonts(GameObject root)
        {
            UnityBuiltinFont.ReplaceBuiltinFonts(root);
        }

        // ============================== 基础构件 ==============================

        /// <summary>创建一个带 RectTransform 的 UI 空物体。</summary>
        public static RectTransform CreateUiObject(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            if (parent != null) rect.SetParent(parent, false);
            return rect;
        }

        /// <summary>铺满父物体（可指定四边内缩）。</summary>
        public static void Stretch(RectTransform rect, float left = 0f, float right = 0f, float top = 0f, float bottom = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        /// <summary>同时设置锚点与四边偏移（left/right/top/bottom 都从锚点区域向内计算）。</summary>
        public static void ApplyAnchors(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax,
                                        float left = 0f, float right = 0f, float top = 0f, float bottom = 0f)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        /// <summary>创建纯色底板。</summary>
        public static Image CreatePanel(string name, Transform parent, Color color)
        {
            RectTransform rect = CreateUiObject(name, parent);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        /// <summary>创建文本。</summary>
        public static Text CreateText(string name, Transform parent, string content, int fontSize, TextAnchor anchor, Color color)
        {
            RectTransform rect = CreateUiObject(name, parent);
            Text text = rect.gameObject.AddComponent<Text>();
            text.font = GetDefaultFont();
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;      // 文本不吃点击，避免挡住按钮
            return text;
        }

        /// <summary>创建按钮，labelText 输出按钮文字。</summary>
        public static Button CreateButton(string name, Transform parent, string label, Color background, out Text labelText)
        {
            RectTransform rect = CreateUiObject(name, parent);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = background;

            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            labelText = CreateText("Label", rect, label, 18, TextAnchor.MiddleCenter, Color.white);
            Stretch((RectTransform)labelText.transform, 6f, 6f, 2f, 2f);
            return button;
        }

        /// <summary>创建只读进度条（无把手）。</summary>
        public static Slider CreateProgressBar(string name, Transform parent, Color backgroundColor, Color fillColor)
        {
            RectTransform rect = CreateUiObject(name, parent);
            Image background = rect.gameObject.AddComponent<Image>();
            background.color = backgroundColor;

            RectTransform fillArea = CreateUiObject("Fill Area", rect);
            Stretch(fillArea, 0f, 0f, 0f, 0f);

            RectTransform fill = CreateUiObject("Fill", fillArea);
            Stretch(fill, 0f, 0f, 0f, 0f);
            Image fillImage = fill.gameObject.AddComponent<Image>();
            fillImage.color = fillColor;

            Slider slider = rect.gameObject.AddComponent<Slider>();
            slider.fillRect = fill;
            slider.direction = Slider.Direction.LeftToRight;
            slider.targetGraphic = background;
            slider.interactable = false;
            slider.transition = Selectable.Transition.None;
            slider.value = 0f;
            return slider;
        }

        /// <summary>创建 ScrollView，content 输出滚动内容节点（已挂 VerticalLayoutGroup）。</summary>
        public static ScrollRect CreateScrollView(string name, Transform parent, out RectTransform content)
        {
            RectTransform rect = CreateUiObject(name, parent);
            Image background = rect.gameObject.AddComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.25f);

            ScrollRect scroll = rect.gameObject.AddComponent<ScrollRect>();

            RectTransform viewport = CreateUiObject("Viewport", rect);
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();      // 用 RectMask2D 裁剪，不需要额外的 Mask 贴图

            content = CreateUiObject("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;

            VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = false;      // 行高由 LayoutElement.preferredHeight 决定
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 4f;
            layout.padding = new RectOffset(6, 6, 6, 6);

            ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 25f;
            return scroll;
        }

        /// <summary>创建一个"运行时用"的纵向列表根（不需要 ScrollView 时的极简版）。</summary>
        public static RectTransform CreateVerticalListRoot(string name, Transform parent)
        {
            RectTransform rect = CreateUiObject(name, parent);
            Stretch(rect);

            VerticalLayoutGroup layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 4f;

            ContentSizeFitter fitter = rect.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rect;
        }

        /// <summary>创建 Canvas（Overlay + 1920x1080 缩放）。</summary>
        public static Canvas CreateCanvas(string name, int sortOrder = 100)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            Canvas canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortOrder;

            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        /// <summary>
        /// 保证场景里有 EventSystem（否则按钮点不动）。
        /// 注意：若工程启用了新版 Input System，请把 StandaloneInputModule 换成 InputSystemUIInputModule。
        /// </summary>
        public static void EnsureEventSystem()
        {
            if (UnityEngine.EventSystems.EventSystem.current != null) return;
            if (UnityEngine.Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() != null) return;

            new GameObject("EventSystem",
                typeof(UnityEngine.EventSystems.EventSystem),
                typeof(UnityEngine.EventSystems.StandaloneInputModule));
        }

        // ========================= 运行时自动生成的列表行 =========================

        /// <summary>生成"任务日志"列表项（无预制体时使用）。</summary>
        public static QuestLogEntryView CreateLogEntryRow(Transform parent)
        {
            RectTransform row = CreateUiObject("QuestEntry", parent);

            Image background = row.gameObject.AddComponent<Image>();
            background.color = new Color(1f, 1f, 1f, 0.12f);

            LayoutElement layout = row.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = 62f;
            layout.minHeight = 62f;

            Button button = row.gameObject.AddComponent<Button>();
            button.targetGraphic = background;

            Text title = CreateText("Title", row, "任务标题", 20, TextAnchor.MiddleLeft, Color.white);
            ApplyAnchors(title.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.62f, 1f), 10f, 0f, 4f, 0f);

            Text status = CreateText("Status", row, "支线 · 进行中", 15, TextAnchor.MiddleRight, new Color(0.75f, 0.9f, 1f));
            ApplyAnchors(status.rectTransform, new Vector2(0.62f, 0.5f), new Vector2(1f, 1f), 0f, 10f, 4f, 0f);

            Text progress = CreateText("Progress", row, "0/1", 15, TextAnchor.MiddleLeft, new Color(0.8f, 0.8f, 0.8f));
            ApplyAnchors(progress.rectTransform, new Vector2(0f, 0f), new Vector2(0.62f, 0.5f), 10f, 0f, 0f, 4f);

            Slider bar = CreateProgressBar("ProgressBar", row, new Color(1f, 1f, 1f, 0.15f), new Color(0.35f, 0.75f, 0.95f));
            ApplyAnchors((RectTransform)bar.transform, new Vector2(0.62f, 0.15f), new Vector2(1f, 0.42f), 0f, 10f, 0f, 0f);

            QuestLogEntryView view = row.gameObject.AddComponent<QuestLogEntryView>();
            view.button = button;
            view.background = background;
            view.titleText = title;
            view.statusText = status;
            view.progressText = progress;
            view.progressBar = bar;
            return view;
        }

        /// <summary>生成 HUD 追踪条目（无预制体时使用）。</summary>
        public static QuestHudEntryView CreateHudEntryRow(Transform parent)
        {
            RectTransform row = CreateUiObject("QuestHudEntry", parent);

            LayoutElement layout = row.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = 46f;
            layout.minHeight = 46f;

            Text title = CreateText("Title", row, "任务标题", 18, TextAnchor.MiddleLeft, Color.white);
            ApplyAnchors(title.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.7f, 1f));

            Text progress = CreateText("Progress", row, "0/1", 15, TextAnchor.MiddleRight, new Color(1f, 0.9f, 0.6f));
            ApplyAnchors(progress.rectTransform, new Vector2(0.7f, 0.5f), new Vector2(1f, 1f));

            Slider bar = CreateProgressBar("ProgressBar", row, new Color(0f, 0f, 0f, 0.35f), new Color(1f, 0.8f, 0.3f));
            ApplyAnchors((RectTransform)bar.transform, new Vector2(0f, 0.1f), new Vector2(1f, 0.32f));

            QuestHudEntryView view = row.gameObject.AddComponent<QuestHudEntryView>();
            view.titleText = title;
            view.progressText = progress;
            view.progressBar = bar;
            return view;
        }
    }
}
