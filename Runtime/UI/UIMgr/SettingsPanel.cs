using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace UPandaGF
{
    /// <summary>
    /// 系统设置面板（P0）：分区按钮 + 描述表驱动的滚动列表 + 预览确认条 + 恢复默认 + 键位捕获。
    /// <para>布局在运行时按代码搭建，预制体只需要根节点（RectTransform + CanvasGroup + 本组件），
    /// 避免把几十个设置项固化进预制体；新增设置项由 <see cref="SettingsPresenter"/> 自动生成行。</para>
    /// <para>打开方式：<c>services.UI.ShowPanelAsync&lt;SettingsPanel&gt;()</c>（或传入起始分区）。</para>
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    [UILoadInfo(E_UI_Layer.Popup, AssetLoadMethod.Resources, "UI/SettingsPanel",
        placement = UIPanelPlacement.Window, modal = true, pauseGame = true, pool = false)]
    public sealed class SettingsPanel : BasePanel
    {
        private const float PanelWidth = 960f;
        private const float PanelHeight = 640f;

        private static readonly Color PanelColor = new Color(0.07f, 0.08f, 0.10f, 0.97f);
        private static readonly Color RowColor = new Color(0.15f, 0.16f, 0.19f, 0.95f);
        private static readonly Color DisabledRowColor = new Color(0.12f, 0.12f, 0.14f, 0.70f);
        private static readonly Color AccentColor = new Color(0.25f, 0.62f, 0.95f, 1f);
        private static readonly Color TextColor = new Color(0.92f, 0.93f, 0.95f, 1f);
        private static readonly Color MutedColor = new Color(0.64f, 0.67f, 0.72f, 1f);
        private static readonly Color ButtonColor = new Color(0.22f, 0.24f, 0.28f, 1f);

        private SettingsPresenter presenter;
        private ISettingsService settings;
        private IInputService input;
        private IDisposable uiContext;

        private RectTransform content;
        private RectTransform categoryBar;
        private RectTransform dropdownOverlay;
        private GameObject previewBar;
        private Text titleText;
        private Text closeLabel;
        private Text resetLabel;
        private Text previewText;
        private Text keepLabel;
        private Text revertLabel;
        private Text resetCategoryLabel;
        private Text statusText;
        private UIFocusNavigator focusNavigator;
        private UIFocusInputBinder focusInputBinder;
        private float previewStartTime;
        private ILocalizationService localization;
        private SettingsCategory currentCategory;
        private bool built;
        private int resetConfirmStage;
        private float resetConfirmDeadline;
        private float rebindDeadline;
        private readonly List<Button> categoryButtons = new List<Button>();

        public override void OnOpen(object panelArg = null)
        {
            if (Services == null || Services.Settings == null)
            {
                Debug.LogError("[SettingsPanel] 缺少 IGameServices.Settings；请通过 UPGameRoot 注入后再打开。");
                return;
            }

            settings = Services.Settings;
            input = Services.Input;
            localization = Services.Localization;

            if (!built)
            {
                BuildLayout();
                built = true;
            }

            presenter = new SettingsPresenter(settings, input, localization);
            presenter.BindingsChanged += RebuildCurrentCategory;
            presenter.RebindMessage += OnRebindMessage;
            settings.PreviewStateChanged += OnPreviewStateChanged;
            if (localization != null) localization.LocaleChanged += OnLocaleChanged;
            if (input != null) uiContext = input.PushContext(InputContexts.UI);
            if (focusInputBinder != null) focusInputBinder.Configure(Services);
            if (focusInputBinder != null) focusInputBinder.enabled = true;

            BuildCategoryButtons();
            if (focusNavigator != null && categoryButtons.Count > 0)
                focusNavigator.SetDefault(categoryButtons[0]);
            SettingsCategory start = presenter.Categories.Count > 0 ? presenter.Categories[0] : SettingsCategory.Audio;
            if (panelArg is SettingsCategory requested && ContainsCategory(requested)) start = requested;
            SelectCategory(start);
            previewStartTime = Time.unscaledTime;
            UpdatePreviewBar(settings.IsPreviewing);
            if (statusText != null)
            {
                statusText.text = settings is SettingsService concrete && concrete.SafeModeActive
                    ? L("settings.panel.safeMode", "已启用安全模式：窗口化 1280×720、低画质")
                    : string.Empty;
            }
        }

        public override void OnRefresh(object panelArg = null)
        {
            if (presenter == null) return;
            if (panelArg is SettingsCategory requested && ContainsCategory(requested))
                SelectCategory(requested);
            else
                RebuildCurrentCategory();
        }

        public override void OnClose()
        {
            if (presenter != null)
            {
                presenter.BindingsChanged -= RebuildCurrentCategory;
                presenter.RebindMessage -= OnRebindMessage;
                presenter.Dispose();
                presenter = null;
            }
            if (settings != null) settings.PreviewStateChanged -= OnPreviewStateChanged;
            if (localization != null) localization.LocaleChanged -= OnLocaleChanged;
            if (focusNavigator != null) focusNavigator.BackRequested -= ClosePanel;
            uiContext?.Dispose();
            uiContext = null;
            ClearRows();
        }

        private void Update()
        {
            if (presenter == null) return;
            presenter.PollRebind();
            if (focusInputBinder != null) focusInputBinder.enabled = !presenter.IsCapturing;
            if (presenter.IsCapturing && rebindDeadline > 0f && Time.unscaledTime > rebindDeadline)
                presenter.CancelRebind(L("settings.rebind.timeout", "改键超时，已取消"));
            if (resetConfirmStage == 1 && Time.unscaledTime > resetConfirmDeadline) RestoreResetButton();
            if (settings != null && settings.IsPreviewing) RefreshPreviewTip();
        }

        #region 布局

        private void BuildLayout()
        {
            RectTransform root = (RectTransform)transform;
            if (root.sizeDelta.x < 1f || root.sizeDelta.y < 1f) root.sizeDelta = new Vector2(PanelWidth, PanelHeight);

            Image background = GetComponent<Image>();
            if (background == null) background = gameObject.AddComponent<Image>();
            background.color = PanelColor;
            background.raycastTarget = true;

            CanvasGroup group = GetComponent<CanvasGroup>();
            group.interactable = true;
            group.blocksRaycasts = true;

            // 移动端才把窗口收进安全区；PC 的 Window 面板保持固定尺寸，避免变成全屏。
            if (Application.isMobilePlatform && GetComponent<UISafeArea>() == null)
                gameObject.AddComponent<UISafeArea>();
            focusNavigator = GetComponent<UIFocusNavigator>();
            if (focusNavigator == null) focusNavigator = gameObject.AddComponent<UIFocusNavigator>();
            focusNavigator.BackRequested -= ClosePanel;
            focusNavigator.BackRequested += ClosePanel;
            focusInputBinder = GetComponent<UIFocusInputBinder>();
            if (focusInputBinder == null) focusInputBinder = gameObject.AddComponent<UIFocusInputBinder>();

            titleText = CreateText("Title", transform, L("settings.panel.title", "系统设置"), 26, TextColor, TextAnchor.MiddleLeft,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -64f), new Vector2(444f, -24f), true);

            Button close = CreateButton("CloseButton", transform, L("settings.panel.close", "关闭"), ClosePanel);
            closeLabel = close.GetComponentInChildren<Text>();
            SetRect((RectTransform)close.transform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-118f, -54f), new Vector2(-28f, -20f), new Vector2(1f, 1f));

            GameObject bar = CreateRect("CategoryBar", transform);
            categoryBar = (RectTransform)bar.transform;
            SetRect(categoryBar, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(24f, -142f), new Vector2(-24f, -100f), new Vector2(0.5f, 1f));
            HorizontalLayoutGroup barLayout = bar.AddComponent<HorizontalLayoutGroup>();
            barLayout.childAlignment = TextAnchor.MiddleLeft;
            barLayout.spacing = 6f;
            barLayout.childControlWidth = true;
            barLayout.childControlHeight = true;
            barLayout.childForceExpandWidth = false;
            barLayout.childForceExpandHeight = true;

            BuildScroll();

            // Dropdown 列表必须在 ScrollRect 遮罩之外生成，否则会被 RectMask2D 裁剪。
            GameObject overlayObject = CreateRect("DropdownOverlay", transform);
            dropdownOverlay = (RectTransform)overlayObject.transform;
            SetRect(dropdownOverlay, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            overlayObject.AddComponent<SettingsDropdownCanvasFixer>();
            overlayObject.transform.SetAsLastSibling();

            Button reset = CreateButton("ResetButton", transform, L("settings.panel.reset", "恢复默认"), OnResetClicked);
            SetRect((RectTransform)reset.transform, new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(24f, 22f), new Vector2(184f, 58f), new Vector2(0f, 0f));
            resetLabel = reset.GetComponentInChildren<Text>();

            Button resetCategory = CreateButton("ResetCategoryButton", transform,
                L("settings.panel.resetCategory", "恢复本页"), OnResetCategoryClicked);
            SetRect((RectTransform)resetCategory.transform, new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(196f, 22f), new Vector2(340f, 58f), new Vector2(0f, 0f));
            resetCategoryLabel = resetCategory.GetComponentInChildren<Text>();

            statusText = CreateText("Status", transform, string.Empty, 13, MutedColor, TextAnchor.MiddleLeft,
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(352f, 18f), new Vector2(-120f, 44f), false);

            BuildPreviewBar();
        }

        private void BuildScroll()
        {
            GameObject scroll = CreateRect("Scroll", transform);
            RectTransform scrollRect = (RectTransform)scroll.transform;
            SetRect(scrollRect, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(24f, 126f), new Vector2(-24f, -156f), new Vector2(0.5f, 0.5f));

            ScrollRect view = scroll.AddComponent<ScrollRect>();
            view.horizontal = false;
            view.vertical = true;
            view.movementType = ScrollRect.MovementType.Clamped;
            view.scrollSensitivity = 30f;

            GameObject viewport = CreateRect("Viewport", scroll.transform);
            RectTransform viewportRect = (RectTransform)viewport.transform;
            SetRect(viewportRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            Image viewportImage = viewport.AddComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0.25f);
            viewport.AddComponent<RectMask2D>();

            GameObject contentObject = CreateRect("Content", viewport.transform);
            content = (RectTransform)contentObject.transform;
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, 0f);

            VerticalLayoutGroup layout = contentObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            ContentSizeFitter fitter = contentObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            view.viewport = viewportRect;
            view.content = content;
        }

        private void BuildPreviewBar()
        {
            previewBar = CreateRect("PreviewBar", transform);
            RectTransform rect = (RectTransform)previewBar.transform;
            SetRect(rect, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(24f, 70f), new Vector2(-24f, 118f), new Vector2(0.5f, 0f));
            Image image = previewBar.AddComponent<Image>();
            image.color = new Color(0.16f, 0.22f, 0.30f, 0.98f);

            previewText = CreateText("PreviewText", previewBar.transform,
                L("settings.panel.previewTip", "设置已临时应用，请确认保留或还原（超时自动还原）"),
                14, TextColor, TextAnchor.MiddleLeft,
                new Vector2(0f, 0f), new Vector2(0.7f, 1f), new Vector2(16f, 0f), new Vector2(0f, 0f), false);

            Button keep = CreateButton("PreviewKeep", previewBar.transform, L("settings.panel.previewKeep", "保留"), OnPreviewKeep);
            keepLabel = keep.GetComponentInChildren<Text>();
            SetRect((RectTransform)keep.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-232f, -16f), new Vector2(-132f, 16f), new Vector2(1f, 0.5f));
            Button revert = CreateButton("PreviewRevert", previewBar.transform, L("settings.panel.previewRevert", "还原"), OnPreviewRevert);
            revertLabel = revert.GetComponentInChildren<Text>();
            SetRect((RectTransform)revert.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-122f, -16f), new Vector2(-22f, 16f), new Vector2(1f, 0.5f));

            previewBar.SetActive(false);
        }

        #endregion

        #region 分区与行

        private void BuildCategoryButtons()
        {
            for (int i = categoryButtons.Count - 1; i >= 0; i--)
            {
                if (categoryButtons[i] != null) Destroy(categoryButtons[i].gameObject);
            }
            categoryButtons.Clear();

            IReadOnlyList<SettingsCategory> categories = presenter.Categories;
            for (int i = 0; i < categories.Count; i++)
            {
                SettingsCategory captured = categories[i];
                Button button = CreateButton("Category_" + captured, categoryBar,
                    presenter != null ? presenter.CategoryLabel(captured) : SettingsText.Category(captured),
                    () => SelectCategory(captured));
                LayoutElement layout = button.gameObject.AddComponent<LayoutElement>();
                layout.preferredWidth = 92f;
                layout.minWidth = 70f;
                layout.preferredHeight = 34f;
                categoryButtons.Add(button);
            }
        }

        private void SelectCategory(SettingsCategory category)
        {
            currentCategory = category;
            for (int i = 0; i < categoryButtons.Count; i++)
            {
                if (categoryButtons[i] == null) continue;
                string expected = "Category_" + category;
                Image image = categoryButtons[i].targetGraphic as Image;
                if (image != null)
                    image.color = categoryButtons[i].gameObject.name == expected ? AccentColor : ButtonColor;
            }
            RebuildCurrentCategory();
        }

        private void RebuildCurrentCategory()
        {
            if (presenter == null || content == null) return;
            ClearRows();
            IReadOnlyList<UISettingItem> items = presenter.BuildItems(currentCategory);
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] != null) CreateRow(items[i]);
            }
        }

        private void ClearRows()
        {
            if (content == null) return;
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                Transform child = content.GetChild(i);
                if (child != null) Destroy(child.gameObject);
            }
        }

        private void CreateRow(UISettingItem item)
        {
            GameObject rowObject = CreateRect("Row_" + item.Key, content);
            Image rowImage = rowObject.AddComponent<Image>();
            bool available = item.CanInteract == null || item.CanInteract();
            rowImage.color = available ? RowColor : DisabledRowColor;

            LayoutElement layout = rowObject.AddComponent<LayoutElement>();
            layout.minHeight = 64f;
            layout.preferredHeight = 64f;

            CreateText("Label", rowObject.transform, item.Label, 16, TextColor, TextAnchor.MiddleLeft,
                new Vector2(0f, 0.5f), new Vector2(0.62f, 1f), new Vector2(16f, 0f), new Vector2(-8f, -6f), false);
            CreateText("Description", rowObject.transform, item.Description, 12, MutedColor, TextAnchor.MiddleLeft,
                new Vector2(0f, 0f), new Vector2(0.62f, 0.5f), new Vector2(16f, 6f), new Vector2(-8f, 0f), false);

            UISettingRow row;
            switch (item.Control)
            {
                case UISettingControl.Toggle:
                    BuildToggleControl(rowObject.transform);
                    row = rowObject.AddComponent<UISettingToggleRow>();
                    break;
                case UISettingControl.Slider:
                    BuildSliderControl(rowObject.transform);
                    row = rowObject.AddComponent<UISettingSliderRow>();
                    break;
                case UISettingControl.Dropdown:
                    BuildDropdownControl(rowObject.transform);
                    row = rowObject.AddComponent<UISettingDropdownRow>();
                    break;
                case UISettingControl.ReadOnly:
                    BuildValueLabel(rowObject.transform);
                    row = rowObject.AddComponent<UISettingReadOnlyRow>();
                    break;
                default:
                    BuildButtonControl(rowObject.transform);
                    row = rowObject.AddComponent<UISettingButtonRow>();
                    break;
            }

            row.Bind(item);
            if (item.Control == UISettingControl.Key && !available)
            {
                // 轴绑定显示为只读：这里不额外处理，按钮行仍会显示当前键位。
            }
        }

        #endregion

        #region 控件构建

        private void BuildToggleControl(Transform parent)
        {
            GameObject toggleObject = CreateRect("Toggle", parent);
            RectTransform rect = (RectTransform)toggleObject.transform;
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(-24f, 0f);
            rect.sizeDelta = new Vector2(30f, 30f);

            Image background = toggleObject.AddComponent<Image>();
            background.color = new Color(0.32f, 0.34f, 0.38f, 1f);
            Toggle toggle = toggleObject.AddComponent<Toggle>();
            toggle.targetGraphic = background;

            GameObject checkObject = CreateRect("Checkmark", toggleObject.transform);
            RectTransform checkRect = (RectTransform)checkObject.transform;
            SetRect(checkRect, Vector2.zero, Vector2.one, new Vector2(5f, 5f), new Vector2(-5f, -5f), new Vector2(0.5f, 0.5f));
            Image check = checkObject.AddComponent<Image>();
            check.color = AccentColor;
            toggle.graphic = check;
        }

        private void BuildDropdownControl(Transform parent)
        {
            GameObject dropdownObject = CreateRect("Dropdown", parent);
            RectTransform rect = (RectTransform)dropdownObject.transform;
            SetRect(rect, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-24f, -17f), new Vector2(-24f, 17f), new Vector2(1f, 0.5f));
            rect.sizeDelta = new Vector2(260f, 34f);

            Image background = dropdownObject.AddComponent<Image>();
            background.color = new Color(0.28f, 0.30f, 0.35f, 1f);
            Dropdown dropdown = dropdownObject.AddComponent<Dropdown>();
            dropdown.targetGraphic = background;
            dropdown.captionText = CreateText("Caption", dropdownObject.transform, string.Empty, 14,
                TextColor, TextAnchor.MiddleLeft, Vector2.zero, Vector2.one,
                new Vector2(12f, 0f), new Vector2(-28f, 0f), false);
            CreateText("Arrow", dropdownObject.transform, "▼", 11, MutedColor, TextAnchor.MiddleCenter,
                new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-24f, 0f), new Vector2(-6f, 0f), false);

            GameObject template = CreateRect("Template",
                dropdownOverlay != null ? (Transform)dropdownOverlay : dropdownObject.transform);
            RectTransform templateRect = (RectTransform)template.transform;
            templateRect.anchorMin = new Vector2(0.5f, 0.5f);
            templateRect.anchorMax = new Vector2(0.5f, 0.5f);
            templateRect.pivot = new Vector2(0f, 1f);
            templateRect.anchoredPosition = Vector2.zero;
            templateRect.sizeDelta = new Vector2(260f, 160f);
            Image templateBackground = template.AddComponent<Image>();
            templateBackground.color = new Color(0.16f, 0.18f, 0.22f, 1f);

            ScrollRect scroll = template.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            GameObject viewport = CreateRect("Viewport", template.transform);
            RectTransform viewportRect = (RectTransform)viewport.transform;
            SetRect(viewportRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            viewport.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.01f);
            viewport.AddComponent<RectMask2D>();

            GameObject content = CreateRect("Content", viewport.transform);
            RectTransform contentRect = (RectTransform)content.transform;
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(0f, 28f);

            GameObject item = CreateRect("Item", content.transform);
            RectTransform itemRect = (RectTransform)item.transform;
            itemRect.anchorMin = new Vector2(0f, 1f);
            itemRect.anchorMax = new Vector2(1f, 1f);
            itemRect.pivot = new Vector2(0.5f, 1f);
            itemRect.anchoredPosition = Vector2.zero;
            itemRect.sizeDelta = new Vector2(0f, 28f);
            Image itemBackground = item.AddComponent<Image>();
            itemBackground.color = new Color(0.22f, 0.25f, 0.30f, 1f);
            Toggle toggle = item.AddComponent<Toggle>();
            toggle.targetGraphic = itemBackground;
            Text itemLabel = CreateText("Item Label", item.transform, string.Empty, 13, TextColor,
                TextAnchor.MiddleLeft, Vector2.zero, Vector2.one,
                new Vector2(12f, 0f), new Vector2(-8f, 0f), false);

            scroll.viewport = viewportRect;
            scroll.content = contentRect;
            dropdown.template = templateRect;
            dropdown.itemText = itemLabel;
            SettingsDropdownAnchor anchor = dropdownObject.AddComponent<SettingsDropdownAnchor>();
            anchor.Bind(dropdown, templateRect, dropdownOverlay);
            template.SetActive(false);
        }

        private void BuildSliderControl(Transform parent)
        {            GameObject sliderObject = CreateRect("Slider", parent);
            RectTransform sliderRect = (RectTransform)sliderObject.transform;
            sliderRect.anchorMin = new Vector2(1f, 0.5f);
            sliderRect.anchorMax = new Vector2(1f, 0.5f);
            sliderRect.pivot = new Vector2(1f, 0.5f);
            sliderRect.anchoredPosition = new Vector2(-24f, -6f);
            sliderRect.sizeDelta = new Vector2(230f, 22f);

            Slider slider = sliderObject.AddComponent<Slider>();
            slider.direction = Slider.Direction.LeftToRight;

            GameObject bgObject = CreateRect("Background", sliderObject.transform);
            SetRect((RectTransform)bgObject.transform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(0f, -4f), new Vector2(0f, 4f), new Vector2(0.5f, 0.5f));
            Image bg = bgObject.AddComponent<Image>();
            bg.color = new Color(0.30f, 0.32f, 0.36f, 1f);

            GameObject fillArea = CreateRect("Fill Area", sliderObject.transform);
            SetRect((RectTransform)fillArea.transform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(0f, -3f), new Vector2(0f, 3f), new Vector2(0.5f, 0.5f));
            GameObject fill = CreateRect("Fill", fillArea.transform);
            SetRect((RectTransform)fill.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            Image fillImage = fill.AddComponent<Image>();
            fillImage.color = AccentColor;
            slider.fillRect = (RectTransform)fill.transform;

            GameObject handleArea = CreateRect("Handle Slide Area", sliderObject.transform);
            SetRect((RectTransform)handleArea.transform, Vector2.zero, Vector2.one, new Vector2(10f, 0f), new Vector2(-10f, 0f), new Vector2(0.5f, 0.5f));
            GameObject handle = CreateRect("Handle", handleArea.transform);
            RectTransform handleRect = (RectTransform)handle.transform;
            handleRect.sizeDelta = new Vector2(18f, 18f);
            Image handleImage = handle.AddComponent<Image>();
            handleImage.color = new Color(0.95f, 0.95f, 0.97f, 1f);
            slider.handleRect = handleRect;
            slider.targetGraphic = handleImage;

            CreateText("Value", parent, "0", 13, TextColor, TextAnchor.MiddleRight,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-244f, -22f), new Vector2(-164f, -4f), false);
        }

        private void BuildButtonControl(Transform parent)
        {
            GameObject buttonObject = CreateRect("Button", parent);
            RectTransform rect = (RectTransform)buttonObject.transform;
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(-24f, 0f);
            rect.sizeDelta = new Vector2(210f, 34f);

            Image image = buttonObject.AddComponent<Image>();
            image.color = new Color(0.26f, 0.28f, 0.33f, 1f);
            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;

            Text value = CreateText("Value", buttonObject.transform, string.Empty, 13, TextColor, TextAnchor.MiddleCenter,
                Vector2.zero, Vector2.one, new Vector2(10f, 0f), new Vector2(-10f, 0f), false);
            value.raycastTarget = false;
        }

        private void BuildValueLabel(Transform parent)
        {
            CreateText("Value", parent, string.Empty, 13, MutedColor, TextAnchor.MiddleRight,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-230f, -13f), new Vector2(-24f, 13f), false);
        }

        #endregion

        #region 操作

        private void OnPreviewKeep()
        {
            if (settings == null) return;
            _ = settings.CommitAsync();
        }

        private void OnPreviewRevert()
        {
            settings?.Rollback();
        }

        private void OnPreviewStateChanged(bool previewing)
        {
            previewStartTime = Time.unscaledTime;
            UpdatePreviewBar(previewing);
        }

        private void UpdatePreviewBar(bool previewing)
        {
            if (previewBar != null) previewBar.SetActive(previewing);
            if (previewing) RefreshPreviewTip();
        }

        private void RefreshPreviewTip()
        {
            if (previewText == null || settings == null) return;
            string tip = L("settings.panel.previewTip", "设置已临时应用，请确认保留或还原（超时自动还原）");
            if (settings.PreviewSeconds <= 0f) { previewText.text = tip; return; }
            float remaining = Mathf.Max(0f, settings.PreviewSeconds - (Time.unscaledTime - previewStartTime));
            int seconds = Mathf.CeilToInt(remaining);
            string countdown = localization != null
                ? localization.Format("settings.panel.previewCountdown", seconds)
                : "剩余 " + seconds + " 秒";
            previewText.text = tip + " " + countdown;
        }

        private void OnResetCategoryClicked()
        {
            if (settings == null) return;
            settings.Reset(SettingsScope.Category, currentCategory.ToString());
        }

        private void OnResetClicked()
        {
            if (settings == null) return;
            if (resetConfirmStage == 0)
            {
                resetConfirmStage = 1;
                resetConfirmDeadline = Time.unscaledTime + 2f;
                if (resetLabel != null) resetLabel.text = L("settings.panel.resetConfirm", "再次点击确认");
                return;
            }
            RestoreResetButton();
            settings.Reset(SettingsScope.All);
        }

        private void RestoreResetButton()
        {
            resetConfirmStage = 0;
            if (resetLabel != null) resetLabel.text = L("settings.panel.reset", "恢复默认");
        }

        private void OnRebindMessage(string message)
        {
            if (statusText != null) statusText.text = message;
            if (presenter != null && !presenter.IsCapturing) rebindDeadline = 0f;
            else rebindDeadline = Time.unscaledTime + 5f;
        }

        private void ClosePanel()
        {
            if (UI != null) UI.ClosePanel<SettingsPanel>();
        }

        private bool ContainsCategory(SettingsCategory category)
        {
            if (presenter == null) return false;
            for (int i = 0; i < presenter.Categories.Count; i++)
            {
                if (presenter.Categories[i] == category) return true;
            }
            return false;
        }

        private void OnLocaleChanged(LocalizationChange change)
        {
            if (presenter == null) return;
            if (presenter.Categories.Count == 0) return;
            if (presenter.IsCapturing) presenter.CancelRebind(L("settings.rebind.canceled", "已取消改键"));
            RefreshStaticTexts();
            BuildCategoryButtons();
            if (focusNavigator != null && categoryButtons.Count > 0)
                focusNavigator.SetDefault(categoryButtons[0]);
            SettingsCategory target = ContainsCategory(currentCategory) ? currentCategory : presenter.Categories[0];
            SelectCategory(target);
            UpdatePreviewBar(settings != null && settings.IsPreviewing);
        }

        private void RefreshStaticTexts()
        {
            if (titleText != null) titleText.text = L("settings.panel.title", "系统设置");
            if (closeLabel != null) closeLabel.text = L("settings.panel.close", "关闭");
            if (resetLabel != null && resetConfirmStage == 0)
                resetLabel.text = L("settings.panel.reset", "恢复默认");
            else if (resetLabel != null)
                resetLabel.text = L("settings.panel.resetConfirm", "再次点击确认");
            if (previewText != null)
                previewText.text = L("settings.panel.previewTip", "设置已临时应用，请确认保留或还原（超时自动还原）");
            if (resetCategoryLabel != null)
                resetCategoryLabel.text = L("settings.panel.resetCategory", "恢复本页");
            if (keepLabel != null) keepLabel.text = L("settings.panel.previewKeep", "保留");
            if (revertLabel != null) revertLabel.text = L("settings.panel.previewRevert", "还原");
            if (settings != null && settings.IsPreviewing) RefreshPreviewTip();
        }

        /// <summary>面板固定文案的本地化；本地化服务不可用时用代码兜底。</summary>
        private string L(string key, string fallback)
        {
            if (localization != null)
            {
                string localized = localization.Get(key);
                if (!string.IsNullOrEmpty(localized) && localized != key) return localized;
            }
            string text = SettingsText.Get(key);
            return text == key ? fallback : text;
        }

        #endregion

        #region 基础 UI 工具

        private static GameObject CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin, Vector2 offsetMax, Vector2 pivot)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            rect.pivot = pivot;
        }

        private static Text CreateText(string name, Transform parent, string value, int size, Color color,
            TextAnchor alignment, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, bool bold)
        {
            GameObject go = CreateRect(name, parent);
            RectTransform rect = (RectTransform)go.transform;
            SetRect(rect, anchorMin, anchorMax, offsetMin, offsetMax, new Vector2(0.5f, 0.5f));

            Text text = go.AddComponent<Text>();
            text.font = UnityBuiltinFont.GetDefault();
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            text.text = value ?? string.Empty;
            return text;
        }

        private static Button CreateButton(string name, Transform parent, string label, Action onClick)
        {
            GameObject go = CreateRect(name, parent);
            Image image = go.AddComponent<Image>();
            image.color = ButtonColor;
            Button button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => onClick?.Invoke());

            Text text = CreateText("Label", go.transform, label, 14, TextColor, TextAnchor.MiddleCenter,
                Vector2.zero, Vector2.one, new Vector2(6f, 0f), new Vector2(-6f, 0f), false);
            text.raycastTarget = false;
            return button;
        }

        #endregion
    }
}
