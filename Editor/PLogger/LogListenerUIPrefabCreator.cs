using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using System.IO;

public class LogListenerUIPrefabCreator : EditorWindow
{
    private string prefabName = "LogListenerPanel";
    private string savePath = "Assets/";
    private Font logFont;
    private int fontSize = 14;
    private Color panelColor = new Color(0.1f, 0.1f, 0.1f, 0.95f);
    private Color headerColor = new Color(0.2f, 0.2f, 0.2f, 1f);
    private Color toolbarColor = new Color(0.15f, 0.15f, 0.15f, 1f);
    private Color footerColor = new Color(0.2f, 0.2f, 0.2f, 1f);
    private Color buttonColor = new Color(0.3f, 0.3f, 0.3f, 1f);

    [MenuItem("UPandaGF/日志系统/UGUI日志面板创建器")]
    static void Init()
    {
        LogListenerUIPrefabCreator window = GetWindow<LogListenerUIPrefabCreator>("日志监听面板创建器");
        window.minSize = new Vector2(350, 400);
        window.Show();
    }

    private void OnGUI()
    {
        GUILayout.Label("日志监听面板创建器", EditorStyles.boldLabel);
        GUILayout.Space(10);

        EditorGUILayout.LabelField("基本设置", EditorStyles.boldLabel);
        prefabName = EditorGUILayout.TextField("预制体名称", prefabName);
        savePath = EditorGUILayout.TextField("保存路径", savePath);

        GUILayout.Space(10);
        EditorGUILayout.LabelField("字体设置", EditorStyles.boldLabel);
        logFont = (Font)EditorGUILayout.ObjectField("日志字体", logFont, typeof(Font), false);
        fontSize = EditorGUILayout.IntSlider("字体大小", fontSize, 10, 20);

        GUILayout.Space(10);
        EditorGUILayout.LabelField("颜色设置", EditorStyles.boldLabel);
        panelColor = EditorGUILayout.ColorField("面板颜色", panelColor);
        headerColor = EditorGUILayout.ColorField("标题栏颜色", headerColor);
        toolbarColor = EditorGUILayout.ColorField("工具栏颜色", toolbarColor);
        footerColor = EditorGUILayout.ColorField("底部栏颜色", footerColor);
        buttonColor = EditorGUILayout.ColorField("按钮颜色", buttonColor);

        GUILayout.Space(20);

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("创建面板", GUILayout.Height(30)))
        {
            CreatePanelWithSettings();
        }

        if (GUILayout.Button("默认设置创建", GUILayout.Height(30)))
        {
            ResetToDefaults();
            CreatePanelWithSettings();
        }

        EditorGUILayout.EndHorizontal();

        GUILayout.Space(10);

        if (GUILayout.Button("一键创建场景预制体", GUILayout.Height(40)))
        {
            CreateScenePrefab();
        }

        GUILayout.Space(10);
        EditorGUILayout.HelpBox("创建的预制体会包含完整的日志监听UI界面，可直接拖入场景使用。", MessageType.Info);
    }

    private void ResetToDefaults()
    {
        prefabName = "LogListenerPanel";
        savePath = "Assets/";
        fontSize = 14;
        panelColor = new Color(0.1f, 0.1f, 0.1f, 0.95f);
        headerColor = new Color(0.2f, 0.2f, 0.2f, 1f);
        toolbarColor = new Color(0.15f, 0.15f, 0.15f, 1f);
        footerColor = new Color(0.2f, 0.2f, 0.2f, 1f);
        buttonColor = new Color(0.3f, 0.3f, 0.3f, 1f);
    }

    private void CreatePanelWithSettings()
    {
        GameObject canvasObj = CreateCanvas();
        GameObject panel = CreatePanel(canvasObj.transform);

        LogListenerManager manager = SetupManagerComponent(panel);

        GameObject header = CreateHeader(panel.transform);
        GameObject toolbar = CreateToolbar(panel.transform);
        GameObject content = CreateContent(panel.transform);
        GameObject footer = CreateFooter(panel.transform);

        SetupReferences(manager, header, toolbar, content, footer);

        SavePrefab(panel);

        DestroyImmediate(canvasObj);

        Debug.Log($"✅ 日志监听面板创建成功: {savePath}/{prefabName}.prefab");

        AssetDatabase.Refresh();

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{savePath}/{prefabName}.prefab");
        Selection.activeObject = prefab;

        EditorGUIUtility.PingObject(prefab);
    }

    private GameObject CreateCanvas()
    {
        GameObject canvasObj = new GameObject("LogListenerCanvas_Temp");

        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        canvasObj.AddComponent<GraphicRaycaster>();

        return canvasObj;
    }

    private GameObject CreatePanel(Transform parent)
    {
        GameObject panel = CreateUIObject(prefabName, parent);

        RectTransform rt = panel.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.1f, 0.1f);
        rt.anchorMax = new Vector2(0.9f, 0.9f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        Image img = panel.AddComponent<Image>();
        img.color = panelColor;

        VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 2;
        layout.padding = new RectOffset(2, 2, 2, 2);

        ContentSizeFitter fitter = panel.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

        return panel;
    }

    private LogListenerManager SetupManagerComponent(GameObject panel)
    {
        LogListenerManager manager = panel.AddComponent<LogListenerManager>();

        SerializedObject serializedManager = new SerializedObject(manager);

        serializedManager.FindProperty("showOnError").boolValue = true;
        serializedManager.FindProperty("autoStart").boolValue = true;
        serializedManager.FindProperty("toggleKey").enumValueIndex = (int)KeyCode.F12;
        serializedManager.FindProperty("ctrlRequired").boolValue = true;

        serializedManager.FindProperty("infoColor").colorValue = Color.white;
        serializedManager.FindProperty("warningColor").colorValue = Color.yellow;
        serializedManager.FindProperty("errorColor").colorValue = Color.red;
        serializedManager.FindProperty("exceptionColor").colorValue = new Color(1f, 0.5f, 0f);

        serializedManager.FindProperty("timestampColor").colorValue = new Color(0.6f, 0.8f, 1f, 0.8f);

        serializedManager.FindProperty("fontSize").intValue = fontSize;

        serializedManager.ApplyModifiedProperties();

        return manager;
    }

    private GameObject CreateHeader(Transform parent)
    {
        GameObject header = CreateUIObject("Header", parent);

        LayoutElement layoutElem = header.AddComponent<LayoutElement>();
        layoutElem.preferredHeight = 40;

        RectTransform rt = header.GetComponent<RectTransform>();

        HorizontalLayoutGroup layout = header.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.spacing = 10;

        header.AddComponent<Image>().color = headerColor;

        GameObject titleObj = CreateUIObject("Title", header.transform);
        LayoutElement titleLayout = titleObj.AddComponent<LayoutElement>();
        titleLayout.preferredWidth = 150;

        RectTransform titleRt = titleObj.GetComponent<RectTransform>();

        Text titleText = titleObj.AddComponent<Text>();
        titleText.text = "日志监听器";
        titleText.fontSize = 20;
        titleText.fontStyle = FontStyle.Bold;
        titleText.color = Color.white;
        titleText.alignment = TextAnchor.MiddleLeft;

        GameObject statusObj = CreateUIObject("StatusText", header.transform);
        LayoutElement statusLayout = statusObj.AddComponent<LayoutElement>();
        statusLayout.flexibleWidth = 1;

        Text statusText = statusObj.AddComponent<Text>();
        statusText.text = "状态: 就绪";
        statusText.fontSize = 14;
        statusText.color = new Color(0.8f, 0.8f, 0.8f);
        statusText.alignment = TextAnchor.MiddleCenter;

        GameObject closeBtn = CreateButton("CloseButton", header.transform, "×", 20);
        LayoutElement btnLayout = closeBtn.AddComponent<LayoutElement>();
        btnLayout.preferredWidth = 30;
        btnLayout.preferredHeight = 30;

        closeBtn.GetComponent<Image>().color = new Color(1f, 0.3f, 0.3f, 1f);

        return header;
    }

    private GameObject CreateToolbar(Transform parent)
    {
        GameObject toolbar = CreateUIObject("Toolbar", parent);

        LayoutElement layoutElem = toolbar.AddComponent<LayoutElement>();
        layoutElem.preferredHeight = 40;

        HorizontalLayoutGroup layout = toolbar.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.spacing = 8;
        layout.padding = new RectOffset(8, 8, 4, 4);

        toolbar.AddComponent<Image>().color = toolbarColor;

        CreateButton("ClearButton", toolbar.transform, "清除", 12);
        CreateButton("SaveButton", toolbar.transform, "保存", 12);
        CreateButton("CopyButton", toolbar.transform, "复制", 12);
        CreateButton("ExportButton", toolbar.transform, "导出", 12);

        GameObject spacer1 = CreateUIObject("Spacer1", toolbar.transform);
        spacer1.AddComponent<LayoutElement>().flexibleWidth = 0.1f;

        GameObject filterLabel = CreateUIObject("FilterLabel", toolbar.transform);
        LayoutElement labelLayout = filterLabel.AddComponent<LayoutElement>();
        labelLayout.preferredWidth = 40;

        Text labelText = filterLabel.AddComponent<Text>();
        labelText.text = "过滤:";
        labelText.fontSize = 12;
        labelText.color = Color.white;
        labelText.alignment = TextAnchor.MiddleLeft;

        GameObject filterInput = CreateInputField("FilterInput", toolbar.transform, "输入关键词...");
        LayoutElement inputLayout = filterInput.AddComponent<LayoutElement>();
        inputLayout.preferredWidth = 150;
        inputLayout.preferredHeight = 25;

        GameObject spacer2 = CreateUIObject("Spacer2", toolbar.transform);
        spacer2.AddComponent<LayoutElement>().flexibleWidth = 0.1f;

        GameObject autoScrollObj = CreateToggle("AutoScrollToggle", toolbar.transform, "自动滚动");
        LayoutElement toggleLayout = autoScrollObj.AddComponent<LayoutElement>();
        toggleLayout.preferredWidth = 80;

        GameObject spacer3 = CreateUIObject("Spacer3", toolbar.transform);
        spacer3.AddComponent<LayoutElement>().flexibleWidth = 0.1f;

        GameObject toggleGroup = CreateUIObject("ToggleGroup", toolbar.transform);
        HorizontalLayoutGroup toggleGroupLayout = toggleGroup.AddComponent<HorizontalLayoutGroup>();
        toggleGroupLayout.spacing = 5;
        toggleGroupLayout.childForceExpandWidth = false;

        CreateToggle("InfoToggle", toggleGroup.transform, "Info").GetComponent<Toggle>().isOn = true;
        CreateToggle("WarningToggle", toggleGroup.transform, "Warn").GetComponent<Toggle>().isOn = true;
        CreateToggle("ErrorToggle", toggleGroup.transform, "Error").GetComponent<Toggle>().isOn = true;
        CreateToggle("ExceptionToggle", toggleGroup.transform, "Excep").GetComponent<Toggle>().isOn = true;

        return toolbar;
    }

    private GameObject CreateContent(Transform parent)
    {
        GameObject content = CreateUIObject("Content", parent);
        LayoutElement layoutElem = content.AddComponent<LayoutElement>();
        //layoutElem.flexibleWidth = 1;
        layoutElem.flexibleHeight = 1;

        RectTransform rt = content.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(1534, 500);

        GameObject scrollView = CreateUIObject("ScrollView", content.transform);
        RectTransform scrollRt = scrollView.GetComponent<RectTransform>();
        scrollRt.anchorMin = Vector2.zero;
        scrollRt.anchorMax = Vector2.one;
        scrollRt.offsetMin = Vector2.zero;
        scrollRt.offsetMax = Vector2.zero;

        ScrollRect scrollRect = scrollView.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.decelerationRate = 0.135f;
        scrollRect.scrollSensitivity = 20;

        Image scrollBg = scrollView.AddComponent<Image>();
        scrollBg.color = new Color(0.05f, 0.05f, 0.05f, 1f);

        GameObject viewport = CreateUIObject("Viewport", scrollView.transform);
        RectTransform viewportRt = viewport.GetComponent<RectTransform>();
        viewportRt.anchorMin = Vector2.zero;
        viewportRt.anchorMax = Vector2.one;
        viewportRt.offsetMin = new Vector2(2, 2);
        viewportRt.offsetMax = new Vector2(-2, -2);

        Image viewportImg = viewport.AddComponent<Image>();
        viewportImg.color = new Color(0, 0, 0, 1);

        Mask mask = viewport.AddComponent<Mask>();
        mask.showMaskGraphic = false;

        GameObject logText = CreateUIObject("LogText", viewport.transform);
        RectTransform logTextRt = logText.GetComponent<RectTransform>();
        logTextRt.anchorMin = Vector2.zero;
        logTextRt.anchorMax = new Vector2(1, 1);
        logTextRt.offsetMin = new Vector2(5, 5);
        logTextRt.offsetMax = new Vector2(-5, -5);

        Text text = logText.AddComponent<Text>();
        text.color = Color.white;
        text.fontSize = fontSize;
        text.alignment = TextAnchor.UpperLeft;
        text.supportRichText = true;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;

        if (logFont != null)
        {
            text.font = logFont;
        }
        else
        {
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        ContentSizeFitter sizeFitter = logText.AddComponent<ContentSizeFitter>();
        sizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        sizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        GameObject scrollbar = CreateScrollbar("Scrollbar Vertical", scrollView.transform);
        RectTransform scrollbarRt = scrollbar.GetComponent<RectTransform>();
        scrollbarRt.anchorMin = new Vector2(1, 0);
        scrollbarRt.anchorMax = new Vector2(1, 1);
        scrollbarRt.offsetMin = new Vector2(-20, 0);
        scrollbarRt.offsetMax = new Vector2(0, 0);
        scrollbarRt.pivot = new Vector2(1, 1);

        scrollRect.viewport = viewportRt;
        scrollRect.content = logTextRt;
        scrollRect.verticalScrollbar = scrollbar.GetComponent<Scrollbar>();
        scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;

        return content;
    }

    private GameObject CreateFooter(Transform parent)
    {
        GameObject footer = CreateUIObject("Footer", parent);

        LayoutElement layoutElem = footer.AddComponent<LayoutElement>();
        layoutElem.preferredHeight = 30;

        HorizontalLayoutGroup layout = footer.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childForceExpandWidth = true;
        layout.spacing = 20;
        layout.padding = new RectOffset(10, 10, 0, 0);

        footer.AddComponent<Image>().color = footerColor;

        GameObject lineCount = CreateUIObject("LineCount", footer.transform);
        Text lineText = lineCount.AddComponent<Text>();
        lineText.text = "行数: 0";
        lineText.fontSize = 12;
        lineText.color = new Color(0.8f, 0.8f, 0.8f);
        lineText.alignment = TextAnchor.MiddleLeft;

        GameObject lastUpdate = CreateUIObject("LastUpdate", footer.transform);
        Text updateText = lastUpdate.AddComponent<Text>();
        updateText.text = "最后更新: --:--:--";
        updateText.fontSize = 12;
        updateText.color = new Color(0.8f, 0.8f, 0.8f);
        updateText.alignment = TextAnchor.MiddleLeft;

        return footer;
    }

    private void SetupReferences(LogListenerManager manager, GameObject header,
                                GameObject toolbar, GameObject content, GameObject footer)
    {
        manager.logPanel = manager.gameObject;
        manager.logText = content.transform.Find("ScrollView/Viewport/LogText").GetComponent<Text>();
        manager.logScrollRect = content.transform.Find("ScrollView").GetComponent<ScrollRect>();

        manager.closeButton = header.transform.Find("CloseButton").GetComponent<Button>();
        manager.statusText = header.transform.Find("StatusText").GetComponent<Text>();

        Transform toolbarTransform = toolbar.transform;
        manager.clearButton = toolbarTransform.Find("ClearButton").GetComponent<Button>();
        manager.saveButton = toolbarTransform.Find("SaveButton").GetComponent<Button>();
        manager.copyButton = toolbarTransform.Find("CopyButton").GetComponent<Button>();
        manager.exportButton = toolbarTransform.Find("ExportButton").GetComponent<Button>();
        manager.filterInput = toolbarTransform.Find("FilterInput").GetComponent<InputField>();
        manager.autoScrollToggle = toolbarTransform.Find("AutoScrollToggle").GetComponent<Toggle>();

        Transform toggleGroup = toolbarTransform.Find("ToggleGroup");
        manager.infoToggle = toggleGroup.Find("InfoToggle").GetComponent<Toggle>();
        manager.warningToggle = toggleGroup.Find("WarningToggle").GetComponent<Toggle>();
        manager.errorToggle = toggleGroup.Find("ErrorToggle").GetComponent<Toggle>();
        manager.exceptionToggle = toggleGroup.Find("ExceptionToggle").GetComponent<Toggle>();
    }

    private void SavePrefab(GameObject panel)
    {
        if (!savePath.EndsWith("/"))
        {
            savePath += "/";
        }

        if (!Directory.Exists(savePath))
        {
            Directory.CreateDirectory(savePath);
        }

        string fullPath = $"{savePath}{prefabName}.prefab";
        fullPath = AssetDatabase.GenerateUniqueAssetPath(fullPath);

        PrefabUtility.SaveAsPrefabAsset(panel, fullPath);
    }

    private void CreateScenePrefab()
    {
        savePath = "Assets/";
        CreatePanelWithSettings();

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{savePath}{prefabName}.prefab");
        if (prefab != null)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.name = "LogListenerSystem";

            Undo.RegisterCreatedObjectUndo(instance, "创建日志监听系统");
            Selection.activeGameObject = instance;

            Debug.Log("✅ 日志监听系统已创建到当前场景！");
        }
    }

    // 辅助函数
    private static GameObject CreateUIObject(string name, Transform parent)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        obj.AddComponent<RectTransform>();
        return obj;
    }

    private static GameObject CreateButton(string name, Transform parent, string text, int fontSize = 14)
    {
        GameObject btn = CreateUIObject(name, parent);

        Image image = btn.AddComponent<Image>();
        image.color = new Color(0.3f, 0.3f, 0.3f, 1f);

        Button button = btn.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = new Color(0.3f, 0.3f, 0.3f, 1f);
        colors.highlightedColor = new Color(0.4f, 0.4f, 0.4f, 1f);
        colors.pressedColor = new Color(0.2f, 0.2f, 0.2f, 1f);
        colors.disabledColor = new Color(0.2f, 0.2f, 0.2f, 0.5f);
        button.colors = colors;

        GameObject textObj = CreateUIObject("Text", btn.transform);
        RectTransform textRT = textObj.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;

        Text textComp = textObj.AddComponent<Text>();
        textComp.text = text;
        textComp.fontSize = fontSize;
        textComp.color = Color.white;
        textComp.alignment = TextAnchor.MiddleCenter;
        textComp.font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        return btn;
    }

    private static GameObject CreateInputField(string name, Transform parent, string placeholder)
    {
        GameObject input = CreateUIObject(name, parent);

        Image image = input.AddComponent<Image>();
        image.color = new Color(0.1f, 0.1f, 0.1f, 1f);
        image.type = Image.Type.Sliced;

        InputField inputField = input.AddComponent<InputField>();

        GameObject textArea = CreateUIObject("Text Area", input.transform);
        textArea.AddComponent<RectTransform>();
        textArea.AddComponent<RectMask2D>();
        RectTransform textAreaRT = textArea.GetComponent<RectTransform>();
        textAreaRT.anchorMin = Vector2.zero;
        textAreaRT.anchorMax = Vector2.one;
        textAreaRT.offsetMin = new Vector2(5, 0);
        textAreaRT.offsetMax = new Vector2(-5, 0);

        GameObject textObj = CreateUIObject("Text", textArea.transform);
        Text text = textObj.AddComponent<Text>();
        text.color = Color.white;
        text.fontSize = 12;
        text.alignment = TextAnchor.MiddleLeft;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        RectTransform textRT = textObj.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;

        GameObject placeholderObj = CreateUIObject("Placeholder", textArea.transform);
        Text placeholderText = placeholderObj.AddComponent<Text>();
        placeholderText.text = placeholder;
        placeholderText.fontSize = 12;
        placeholderText.color = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        placeholderText.alignment = TextAnchor.MiddleLeft;
        placeholderText.horizontalOverflow = HorizontalWrapMode.Overflow;
        placeholderText.verticalOverflow = VerticalWrapMode.Truncate;
        placeholderText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        placeholderText.fontStyle = FontStyle.Italic;

        RectTransform placeholderRT = placeholderObj.GetComponent<RectTransform>();
        placeholderRT.anchorMin = Vector2.zero;
        placeholderRT.anchorMax = Vector2.one;
        placeholderRT.offsetMin = Vector2.zero;
        placeholderRT.offsetMax = Vector2.zero;

        inputField.textComponent = text;
        inputField.placeholder = placeholderText;
        inputField.text = "";

        return input;
    }

    private static GameObject CreateToggle(string name, Transform parent, string label)
    {
        GameObject toggleObj = CreateUIObject(name, parent);
        LayoutElement layout = toggleObj.AddComponent<LayoutElement>();
        layout.preferredWidth = 50;
        layout.minHeight = 20;

        Toggle toggle = toggleObj.AddComponent<Toggle>();
        toggle.isOn = true;

        ColorBlock colors = toggle.colors;
        colors.normalColor = new Color(0.2f, 0.2f, 0.2f, 1f);
        colors.highlightedColor = new Color(0.3f, 0.3f, 0.3f, 1f);
        colors.pressedColor = new Color(0.1f, 0.1f, 0.1f, 1f);
        colors.disabledColor = new Color(0.1f, 0.1f, 0.1f, 0.5f);
        colors.colorMultiplier = 1f;
        toggle.colors = colors;

        GameObject background = CreateUIObject("Background", toggleObj.transform);
        Image bgImage = background.AddComponent<Image>();
        bgImage.color = new Color(0.2f, 0.2f, 0.2f, 1f);

        RectTransform bgRT = background.GetComponent<RectTransform>();
        bgRT.anchorMin = new Vector2(0, 0.5f);
        bgRT.anchorMax = new Vector2(0, 0.5f);
        bgRT.offsetMin = new Vector2(0, -8);
        bgRT.offsetMax = new Vector2(16, 8);
        bgRT.pivot = new Vector2(0, 0.5f);

        GameObject checkmark = CreateUIObject("Checkmark", background.transform);
        Image checkImage = checkmark.AddComponent<Image>();
        checkImage.color = new Color(0.1f, 0.5f, 1f, 1f);

        RectTransform checkRT = checkmark.GetComponent<RectTransform>();
        checkRT.anchorMin = new Vector2(0, 0);
        checkRT.anchorMax = new Vector2(1, 1);
        checkRT.offsetMin = new Vector2(2, 2);
        checkRT.offsetMax = new Vector2(-2, -2);

        GameObject labelObj = CreateUIObject("Label", toggleObj.transform);
        Text labelText = labelObj.AddComponent<Text>();
        labelText.text = label;
        labelText.fontSize = 10;
        labelText.color = new Color(0.8f, 0.8f, 0.8f);
        labelText.alignment = TextAnchor.MiddleLeft;
        labelText.horizontalOverflow = HorizontalWrapMode.Overflow;
        labelText.verticalOverflow = VerticalWrapMode.Truncate;
        labelText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        RectTransform labelRT = labelObj.GetComponent<RectTransform>();
        labelRT.anchorMin = new Vector2(0, 0.5f);
        labelRT.anchorMax = new Vector2(1, 0.5f);
        labelRT.offsetMin = new Vector2(20, -7);
        labelRT.offsetMax = new Vector2(0, 7);
        labelRT.pivot = new Vector2(0, 0.5f);

        toggle.graphic = checkImage;
        toggle.targetGraphic = bgImage;

        return toggleObj;
    }

    private static GameObject CreateScrollbar(string name, Transform parent)
    {
        GameObject scrollbar = CreateUIObject(name, parent);

        Scrollbar sb = scrollbar.AddComponent<Scrollbar>();
        sb.direction = Scrollbar.Direction.BottomToTop;

        Image bg = scrollbar.AddComponent<Image>();
        bg.color = new Color(0.1f, 0.1f, 0.1f, 1f);

        GameObject slidingArea = CreateUIObject("Sliding Area", scrollbar.transform);
        RectTransform slidingRT = slidingArea.GetComponent<RectTransform>();
        slidingRT.anchorMin = Vector2.zero;
        slidingRT.anchorMax = Vector2.one;
        slidingRT.offsetMin = Vector2.zero;
        slidingRT.offsetMax = Vector2.zero;

        GameObject handle = CreateUIObject("Handle", slidingArea.transform);
        Image handleImg = handle.AddComponent<Image>();
        handleImg.color = new Color(0.3f, 0.3f, 0.3f, 1f);

        RectTransform handleRT = handle.GetComponent<RectTransform>();
        handleRT.anchorMin = new Vector2(0, 0);
        handleRT.anchorMax = new Vector2(1, 1);
        handleRT.offsetMin = new Vector2(2, 2);
        handleRT.offsetMax = new Vector2(-2, -2);

        sb.handleRect = handleRT;
        sb.targetGraphic = handleImg;

        return scrollbar;
    }
}
