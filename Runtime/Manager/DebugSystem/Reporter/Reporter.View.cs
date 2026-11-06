using System;
using System.Collections.Generic;
using UnityEngine;
using UPandaGF;

public partial class Reporter
{
    private GUISkin viewSkin;
    private GUIStyle textStyle, mutedStyle, titleStyle, buttonStyle, activeStyle, rowStyle, selectedRowStyle, detailStyle, fieldStyle;
    private readonly List<Texture2D> ownedTextures = new List<Texture2D>();
    private Texture2D surface, toolbarSurface, alternateSurface, selectedSurface, divider;
    private Vector2 scrollPosition, detailScroll, actionScroll, infoScroll;
    private float styleScale = -1, detailFraction = .36f;
    private float panelOpacity = 1f;
    private Rect listViewport, detailViewport;
    private string detailText = "", cachedDetailText = "";
    private int detailSample = -1;
    private Log detailLog;
    private float detailTextWidth, detailTextHeight;
    private int draggingArea;
    private Vector2 pressPosition, pressScroll;
    private bool dragMoved, draggingSplitter, focusSearch;
    private FrameworkDiagnosticsSnapshot frameworkDiagnostics;
    private float nextDiagnosticsAt;

    public void OnGUIDraw()
    {
        if (!show || !Initialized || !ownsInstance || !isActiveAndEnabled) return;
        GUISkin previousSkin = GUI.skin;
        Color previousColor = GUI.color, previousBackground = GUI.backgroundColor, previousContent = GUI.contentColor;
        Matrix4x4 previousMatrix = GUI.matrix;
        bool previousEnabled = GUI.enabled;
        int previousDepth = GUI.depth;
        try
        {
            GUI.matrix = Matrix4x4.identity;
            GUI.depth = -10000;
            GUI.color = GUI.backgroundColor = GUI.contentColor = Color.white;
            GUI.enabled = true;
            EnsureView();
            GUI.skin = viewSkin;
            RefreshVisible();
            HandleKeyboard();
            Rect safe = Screen.safeArea;
            if (safe.width <= 0 || safe.height <= 0) safe = new Rect(0, 0, Screen.width, Screen.height);
            float margin = safe.width >= 600 ? 12 : 0;
            Rect window = new Rect(safe.x + margin, Screen.height - safe.yMax + margin,
                Mathf.Max(1, safe.width - margin * 2), Mathf.Max(1, safe.height - margin * 2));
            GUI.color = new Color(1f, 1f, 1f, panelOpacity);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), DimTexture);
            GUI.BeginGroup(window);
            try
            {
                GUI.DrawTexture(new Rect(0, 0, window.width, window.height), surface);
                float y = DrawHeader(window.width);
                if (currentView == ReportView.Info) DrawInformation(new Rect(0, y, window.width, Mathf.Max(1, window.height - y)));
                else
                {
                    y = DrawFilters(window.width, y);
                    y = DrawActions(window.width, y);
                    Rect body = new Rect(0, y, window.width, Mathf.Max(1, window.height - y - 27));
                    LayoutPanes(body);
                    DrawLogList();
                    DrawDetails();
                    DrawStatus(new Rect(0, window.height - 27, window.width, 27));
                }
            }
            finally { GUI.EndGroup(); }
        }
        finally
        {
            GUI.skin = previousSkin;
            GUI.color = previousColor; GUI.backgroundColor = previousBackground; GUI.contentColor = previousContent;
            GUI.matrix = previousMatrix; GUI.enabled = previousEnabled; GUI.depth = previousDepth;
        }
    }

    private Texture2D dimTexture;
    private Texture2D DimTexture => dimTexture != null ? dimTexture : (dimTexture = Solid(new Color(0.04f, .07f, .12f, .72f)));
    private Texture2D Solid(Color color)
    {
        var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
        texture.SetPixel(0, 0, color); texture.Apply(false, true); ownedTextures.Add(texture); return texture;
    }
    private static Color Rgb(string hex) { Color value; ColorUtility.TryParseHtmlString(hex, out value); return value; }
    private void EnsureView()
    {
        float scale = Mathf.Clamp(size.y, 16, 64);
        if (viewSkin != null && Mathf.Approximately(scale, styleScale)) return;
        DisposeView();
        styleScale = scale;
        viewSkin = Instantiate(images.reporterScrollerSkin != null ? images.reporterScrollerSkin : GUI.skin);
        viewSkin.hideFlags = HideFlags.HideAndDontSave;
        var font = UPandaGF.UnityBuiltinFont.GetDefault();
        if (font != null) viewSkin.font = font;
        int fontSize = Mathf.RoundToInt(Mathf.Clamp(scale * .44f, 12, 22));
        surface = Solid(Rgb("#F4F7FB")); toolbarSurface = Solid(Rgb("#E8EDF4"));
        alternateSurface = Solid(Rgb("#EAF0F7")); selectedSurface = Solid(Rgb("#356BC4")); divider = Solid(Rgb("#CFD9E7"));
        textStyle = new GUIStyle(viewSkin.label) { fontSize = fontSize, alignment = TextAnchor.MiddleLeft, richText = false, clipping = TextClipping.Clip, wordWrap = false };
        textStyle.normal.textColor = Rgb("#354B66");
        mutedStyle = new GUIStyle(textStyle); mutedStyle.normal.textColor = Rgb("#6A7F99");
        titleStyle = new GUIStyle(textStyle) { fontSize = fontSize + 3, fontStyle = FontStyle.Bold };
        buttonStyle = new GUIStyle(viewSkin.button) { fontSize = fontSize, alignment = TextAnchor.MiddleCenter, richText = false, padding = new RectOffset(4, 4, 2, 2), margin = new RectOffset() };
        buttonStyle.normal.background = toolbarSurface; buttonStyle.normal.textColor = Rgb("#425570");
        buttonStyle.hover.background = alternateSurface; buttonStyle.hover.textColor = Rgb("#23496F");
        buttonStyle.active.background = selectedSurface; buttonStyle.active.textColor = Color.white;
        activeStyle = new GUIStyle(buttonStyle); activeStyle.normal.background = Solid(Rgb("#CEDFF5"));
        rowStyle = new GUIStyle(textStyle) { padding = new RectOffset(0, 4, 0, 0) };
        selectedRowStyle = new GUIStyle(rowStyle); selectedRowStyle.normal.textColor = Color.white;
        detailStyle = new GUIStyle(textStyle) { wordWrap = true, alignment = TextAnchor.UpperLeft, padding = new RectOffset(6, 6, 6, 6) };
        fieldStyle = new GUIStyle(viewSkin.textField) { fontSize = fontSize, richText = false, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(8, 8, 2, 2) };
        fieldStyle.normal.textColor = Rgb("#354B66"); fieldStyle.normal.background = Solid(Color.white);
        fieldStyle.focused.textColor = Rgb("#354B66"); fieldStyle.focused.background = fieldStyle.normal.background;
        viewSkin.verticalScrollbar.fixedWidth = 24;
        viewSkin.verticalScrollbar.normal.background = toolbarSurface;
        viewSkin.verticalScrollbarThumb.normal.background = Solid(Rgb("#A9BCD5"));
        viewSkin.verticalScrollbarThumb.hover.background = viewSkin.verticalScrollbarThumb.normal.background;
        viewSkin.verticalScrollbarThumb.active.background = viewSkin.verticalScrollbarThumb.normal.background;
        viewSkin.verticalScrollbarThumb.fixedWidth = 24;
        viewSkin.verticalScrollbarUpButton.fixedHeight = viewSkin.verticalScrollbarDownButton.fixedHeight = 0;
        viewSkin.horizontalScrollbar.fixedHeight = 20;
        viewSkin.horizontalSlider.fixedHeight = 32;
        viewSkin.horizontalSliderThumb.fixedWidth = 32;
        viewSkin.horizontalSliderThumb.fixedHeight = 32;
    }
    private void DisposeView()
    {
        if (viewSkin != null) Destroy(viewSkin);
        viewSkin = null;
        foreach (var texture in ownedTextures) if (texture != null) Destroy(texture);
        ownedTextures.Clear(); dimTexture = null;
    }
    private float RowHeight => Mathf.Max(48, Mathf.Clamp(size.y, 26, 64) + 8);
    private float ControlHeight => Mathf.Max(48, Mathf.Clamp(size.y, 28, 48) + 4);
    private void HandleKeyboard()
    {
        var e = Event.current;
        if (e.type != EventType.KeyDown) return;
        if (e.keyCode == KeyCode.Escape) { HideLogWindows(); e.Use(); }
        else if ((e.control || e.command) && e.keyCode == KeyCode.F)
        {
            showSearchText = true; currentView = ReportView.Logs; focusSearch = true; e.Use();
        }
    }
    private float DrawHeader(float width)
    {
        bool compact = width < 600;
        float h = ControlHeight + 16;
        float closeWidth = compact ? 72 : 104;
        float switchWidth = compact ? 112 : 176;
        float actionsWidth = closeWidth + switchWidth + 28;
        bool stacked = width - actionsWidth < 180;
        float buttonY = stacked ? h : 8;
        float totalHeight = stacked ? h * 2 : h;
        GUI.DrawTexture(new Rect(0, 0, width, totalHeight), toolbarSurface);
        GUI.Label(new Rect(14, 0, Mathf.Max(1, stacked ? width - 28 : width - actionsWidth - 14), h),
            currentView == ReportView.Info ? "设备 / 框架诊断" : "运行日志", titleStyle);
        if (stacked)
        {
            closeWidth = Mathf.Min(closeWidth, (width - 36) * .4f);
            switchWidth = Mathf.Max(1, width - closeWidth - 36);
        }
        if (IconButton(new Rect(width - closeWidth - switchWidth - 20, buttonY, switchWidth, h - 16),
            currentView == ReportView.Info ? images.backImage : images.infoImage,
            currentView == ReportView.Info ? "返回日志" : "显示设置", currentView == ReportView.Info, true))
            currentView = currentView == ReportView.Info ? ReportView.Logs : ReportView.Info;
        if (IconButton(new Rect(width - closeWidth - 12, buttonY, closeWidth, h - 16), images.closeImage, "关闭", false, true)) HideLogWindows();
        h = totalHeight;
        return h;
    }
    private bool IconButton(Rect rect, Texture2D image, string label, bool active, bool withText = false)
    {
        bool clicked = GUI.Button(rect, new GUIContent(image == null ? label : "", label), active ? activeStyle : buttonStyle);
        if (image != null)
        {
            float icon = Mathf.Min(24, rect.height - 10);
            GUI.DrawTexture(new Rect(withText ? rect.x + 7 : rect.x + (rect.width - icon) / 2, rect.y + (rect.height - icon) / 2, icon, icon), image, ScaleMode.ScaleToFit);
            if (withText) GUI.Label(new Rect(rect.x + icon + 12, rect.y, Mathf.Max(0, rect.width - icon - 14), rect.height), label, textStyle);
        }
        return clicked;
    }
    private float DrawFilters(float width, float y)
    {
        float h = ControlHeight + 8;
        bool wide = width >= 760;
        if (showSearchText)
        {
            float searchWidth = wide ? width - 310 : width - 24;
            if (images.searchImage != null) GUI.DrawTexture(new Rect(16, y + (h - 20) / 2, 20, 20), images.searchImage, ScaleMode.ScaleToFit);
            GUI.SetNextControlName("ReporterSearch");
            var rect = new Rect(44, y + 5, Mathf.Max(1, searchWidth - 48), h - 10);
            string value = GUI.TextField(rect, filterText, fieldStyle);
            if (focusSearch) { GUI.FocusControl("ReporterSearch"); focusSearch = false; }
            if (value != filterText) SetFilter(value);
            if (filterText.Length == 0 && GUI.GetNameOfFocusedControl() != "ReporterSearch") GUI.Label(new Rect(rect.x + 6, rect.y, rect.width - 12, rect.height), "搜索日志或堆栈…", mutedStyle);
            if (!wide) y += h;
        }
        float start = wide && showSearchText ? width - 296 : 12;
        float chip = Mathf.Min(94, (width - start - 20) / 3);
        if (SeverityChip(new Rect(start, y + 5, chip, h - 10), images.logImage, store.LogCount, showLog, "普通日志")) { showLog = !showLog; visibleDirty = true; }
        if (SeverityChip(new Rect(start + chip + 4, y + 5, chip, h - 10), images.warningImage, store.WarningCount, showWarning, "警告")) { showWarning = !showWarning; visibleDirty = true; }
        if (SeverityChip(new Rect(start + (chip + 4) * 2, y + 5, chip, h - 10), images.errorImage, store.ErrorCount, showError, "错误 / 异常 / 断言")) { showError = !showError; visibleDirty = true; }
        return y + h;
    }
    private bool SeverityChip(Rect rect, Texture2D icon, int count, bool enabled, string label)
    {
        bool clicked = GUI.Button(rect, new GUIContent("", label), enabled ? activeStyle : buttonStyle);
        Color previous = GUI.color;
        if (!enabled) GUI.color = new Color(1, 1, 1, .4f);
        if (icon != null) GUI.DrawTexture(new Rect(rect.x + 8, rect.y + (rect.height - 20) / 2, 20, 20), icon, ScaleMode.ScaleToFit);
        GUI.Label(new Rect(rect.x + 34, rect.y, rect.width - 36, rect.height), count.ToString(), textStyle);
        GUI.color = previous;
        return clicked;
    }
    private float DrawActions(float width, float y)
    {
        float h = ControlHeight + 8;
        int buttons = 4 + (showClearOnNewSceneLoadedButton ? 1 : 0) + (showTimeButton ? 1 : 0) + (showSceneButton ? 1 : 0)
            + (showMemButton ? 1 : 0) + (showFpsButton ? 1 : 0) + (showCopyButton ? 1 : 0) + (showSaveButton ? 1 : 0);
        bool text = width >= 1000;
        float buttonWidth = text ? Mathf.Max(100, textStyle.fontSize * 4 + 38) : 56;
        Rect viewport = new Rect(8, y, width - 16, h);
        float contentWidth = Mathf.Max(viewport.width, buttons * (buttonWidth + 4));
        HandleDrag(viewport, 2, ref actionScroll, true);
        actionScroll = GUI.BeginScrollView(viewport, actionScroll, new Rect(0, 0, contentWidth, h - 1), false, false, GUIStyle.none, GUIStyle.none);
        float x = 0;
        if (Action(ref x, buttonWidth, h, images.clearImage, "清空", false, text)) ClearLogs();
        if (Action(ref x, buttonWidth, h, images.collapseImage, "折叠", collapse, text)) SetCollapse(!collapse);
        if (Action(ref x, buttonWidth, h, null, paused ? "继续" : "暂停", paused, false)) paused = !paused;
        if (Action(ref x, buttonWidth, h, null, "跟随", followTail, false)) followTail = !followTail;
        if (showClearOnNewSceneLoadedButton && Action(ref x, buttonWidth, h, images.clearOnNewSceneImage, "换场清空", clearOnNewSceneLoaded, text)) clearOnNewSceneLoaded = !clearOnNewSceneLoaded;
        if (showTimeButton && Action(ref x, buttonWidth, h, images.showTimeImage, "时间", showTime, text)) showTime = !showTime;
        if (showSceneButton && Action(ref x, buttonWidth, h, images.showSceneImage, "场景", showScene, text)) showScene = !showScene;
        if (showMemButton && Action(ref x, buttonWidth, h, images.showMemoryImage, "内存", showMemory, text)) showMemory = !showMemory;
        if (showFpsButton && Action(ref x, buttonWidth, h, images.showFpsImage, "帧率", showFps, text)) showFps = !showFps;
        if (showCopyButton)
        {
            bool enabled = GUI.enabled; GUI.enabled = selectedLog != null;
            if (Action(ref x, buttonWidth, h, images.copyImage, "复制", false, text)) CopySelection();
            GUI.enabled = enabled;
        }
        if (showSaveButton && Action(ref x, buttonWidth, h, images.saveLogsImage, "导出", false, text)) SaveLogsToDevice();
        GUI.EndScrollView();
        if (actionScroll.x + viewport.width < contentWidth - 1)
            GUI.Label(new Rect(width - 20, y + 4, 14, h - 8), "›", titleStyle);
        return y + h;
    }
    private bool Action(ref float x, float width, float h, Texture2D icon, string label, bool enabled, bool text)
    {
        bool result = IconButton(new Rect(x, 4, width, h - 8), icon, label, enabled, text);
        x += width + 4; return result;
    }
    private void LayoutPanes(Rect body)
    {
        bool sideBySide = body.width >= 1000;
        float part = sideBySide ? body.width * detailFraction : body.height * detailFraction;
        Rect splitter;
        if (sideBySide)
        {
            listViewport = new Rect(body.x, body.y, body.width - part - 16, body.height);
            splitter = new Rect(listViewport.xMax, body.y, 16, body.height);
            detailViewport = new Rect(splitter.xMax, body.y, part, body.height);
        }
        else
        {
            listViewport = new Rect(body.x, body.y, body.width, Mathf.Max(1, body.height - part - 16));
            splitter = new Rect(body.x, listViewport.yMax, body.width, 16);
            detailViewport = new Rect(body.x, splitter.yMax, body.width, Mathf.Max(1, part));
        }
        GUI.DrawTexture(splitter, divider);
        var e = Event.current;
        if (e.type == EventType.MouseDown && splitter.Contains(e.mousePosition)) { draggingSplitter = true; e.Use(); }
        if (draggingSplitter && e.type == EventType.MouseDrag)
        {
            detailFraction = Mathf.Clamp(sideBySide ? (body.xMax - e.mousePosition.x) / body.width : (body.yMax - e.mousePosition.y) / body.height, .22f, .65f);
            e.Use();
        }
        if (e.rawType == EventType.MouseUp) draggingSplitter = false;
    }
    private void DrawLogList()
    {
        RefreshVisible();
        float height = RowHeight;
        float maxScroll = Mathf.Max(0, currentLog.Count * height - listViewport.height);
        bool wheel = Event.current.type == EventType.ScrollWheel && listViewport.Contains(Event.current.mousePosition);
        if (Event.current.type == EventType.MouseDown && listViewport.Contains(Event.current.mousePosition)
            && Event.current.mousePosition.x >= listViewport.xMax - 16) followTail = false;
        Rect draggable = listViewport; draggable.width = Mathf.Max(1, draggable.width - 16);
        HandleDrag(draggable, 1, ref scrollPosition, false);
        if (followTail && !wheel && draggingArea != 1) scrollPosition.y = maxScroll;
        scrollPosition.y = Mathf.Clamp(scrollPosition.y, 0, maxScroll);
        scrollPosition = GUI.BeginScrollView(listViewport, scrollPosition,
            new Rect(0, 0, Mathf.Max(1, listViewport.width - 16), Mathf.Max(1, currentLog.Count * height)), false, true, GUIStyle.none, viewSkin.verticalScrollbar);
        if (wheel) followTail = scrollPosition.y >= maxScroll - 1;
        int first, end;
        VisibleRange(currentLog.Count, scrollPosition.y, listViewport.height, height, out first, out end);
        float width = Mathf.Max(1, listViewport.width - 16);
        for (int i = first; i < end; i++)
        {
            var log = currentLog[i]; bool selected = log == selectedLog;
            var row = new Rect(0, i * height, width, height);
            GUI.DrawTexture(row, selected ? selectedSurface : (i % 2 == 0 ? surface : alternateSurface));
            if (GUI.Button(row, GUIContent.none, GUIStyle.none)) SelectLog(log);
            Texture2D icon = log.logType == _LogType.Log ? images.logImage : log.logType == _LogType.Warning ? images.warningImage : images.errorImage;
            if (icon != null) GUI.DrawTexture(new Rect(12, row.y + (height - 20) / 2, 20, 20), icon, ScaleMode.ScaleToFit);
            GUIStyle style = selected ? selectedRowStyle : rowStyle;
            float right = width - 8;
            if (collapse) DrawColumn(ref right, row.y, height, 44, log.count.ToString(), style);
            if (width > 760 && showFps) DrawColumn(ref right, row.y, height, 65, log.sample.fpsText + " fps", style);
            if (width > 760 && showMemory) DrawColumn(ref right, row.y, height, 82, log.sample.memory.ToString("0.0") + " MB", style);
            if (width > 600 && showScene) DrawColumn(ref right, row.y, height, 108, log.sample.GetSceneName(), style);
            if (width > 450 && showTime) DrawColumn(ref right, row.y, height, 76, log.sample.time.ToString("0.000"), style);
            GUI.Label(new Rect(44, row.y, Mathf.Max(1, right - 48), height), log.preview ?? log.condition, style);
        }
        if (currentLog.Count == 0) GUI.Label(new Rect(18, 18, Mathf.Max(1, width - 36), 50), store.Count == 0 ? "暂无日志，等待应用输出。" : "没有匹配的日志，试试调整搜索或级别筛选。", detailStyle);
        GUI.EndScrollView();
    }
    internal static void VisibleRange(int count, float scroll, float viewport, float rowHeight, out int first, out int end)
    {
        rowHeight = Mathf.Max(1, rowHeight);
        first = Mathf.Clamp(Mathf.FloorToInt(Mathf.Max(0, scroll) / rowHeight), 0, count);
        end = Mathf.Min(count, first + Mathf.CeilToInt(Mathf.Max(0, viewport) / rowHeight) + 2);
    }
    private void DrawColumn(ref float right, float y, float h, float width, string value, GUIStyle style)
    {
        right -= width; GUI.Label(new Rect(right, y, width - 6, h), value, style);
    }
    private void SelectLog(Log log) { selectedLog = log; detailScroll = Vector2.zero; followTail = false; }
    private void CopySelection()
    {
        if (selectedLog == null) return;
        GUIUtility.systemCopyBuffer = selectedLog.condition + Environment.NewLine + Environment.NewLine + selectedLog.stacktrace;
        SetStatus("已复制日志与堆栈");
    }
    private void DrawDetails()
    {
        GUI.BeginGroup(detailViewport);
        try
        {
            GUI.DrawTexture(new Rect(0, 0, detailViewport.width, detailViewport.height), surface);
            GUI.Label(new Rect(12, 0, detailViewport.width - 24, 32), "日志详情", titleStyle);
            if (selectedLog == null)
            {
                GUI.Label(new Rect(12, 40, detailViewport.width - 24, Mathf.Max(1, detailViewport.height - 48)), "选择一条日志，查看完整消息和调用堆栈。", detailStyle); return;
            }
            var sample = selectedLog.sample;
            if (detailLog != selectedLog || detailSample != selectedLog.sampleId)
            {
                detailLog = selectedLog; detailSample = selectedLog.sampleId;
                detailText = selectedLog.condition + "\n\n" + (string.IsNullOrEmpty(selectedLog.stacktrace) ? "（无调用堆栈）" : selectedLog.stacktrace);
            }
            GUI.Label(new Rect(12, 33, detailViewport.width - 24, 28), selectedLog.logType + "  ·  " + sample.time.ToString("0.000") + "s  ·  " + sample.GetSceneName(), mutedStyle);
            Rect viewport = new Rect(8, 64, Mathf.Max(1, detailViewport.width - 16), Mathf.Max(1, detailViewport.height - 68));
            float width = Mathf.Max(1, viewport.width - 16);
            if (detailTextWidth != width || cachedDetailText != detailText)
            {
                detailTextWidth = width; cachedDetailText = detailText;
                detailTextHeight = detailStyle.CalcHeight(new GUIContent(detailText), width);
            }
            detailScroll = GUI.BeginScrollView(viewport, detailScroll, new Rect(0, 0, width, Mathf.Max(viewport.height, detailTextHeight)), false, true, GUIStyle.none, viewSkin.verticalScrollbar);
            // 忽略修改后的返回值，保留文本选择和 Ctrl/Cmd+C，不修改原始日志。
            GUI.TextArea(new Rect(0, 0, width, Mathf.Max(viewport.height, detailTextHeight)), detailText, detailStyle);
            GUI.EndScrollView();
        }
        finally { GUI.EndGroup(); }
    }
    private void HandleDrag(Rect rect, int area, ref Vector2 scroll, bool horizontal)
    {
        var e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
        {
            draggingArea = area; pressPosition = e.mousePosition; pressScroll = scroll; dragMoved = false;
        }
        if (draggingArea == area && e.type == EventType.MouseDrag)
        {
            var delta = e.mousePosition - pressPosition;
            if (delta.sqrMagnitude > 25) dragMoved = true;
            if (dragMoved)
            {
                if (horizontal) scroll.x = Mathf.Max(0, pressScroll.x - delta.x);
                else { scroll.y = Mathf.Max(0, pressScroll.y - delta.y); followTail = false; }
                GUIUtility.hotControl = 0; e.Use();
            }
        }
        if (draggingArea == area && e.rawType == EventType.MouseUp)
        {
            if (dragMoved) e.Use(); draggingArea = 0;
        }
    }
    private void DrawStatus(Rect rect)
    {
        GUI.DrawTexture(rect, toolbarSurface);
        string text = Time.realtimeSinceStartup < statusUntil ? statusMessage
            : string.Format("{0} / {1} 条  ·  {2} FPS  ·  GC {3:0.0} MB{4}{5}", currentLog.Count, store.Count, fpsText, cachedMemory,
                paused ? "  ·  已暂停 · 待处理 " + PendingLogCount + " 条" : "", DroppedLogCount > 0 ? "  ·  已丢弃 " + DroppedLogCount : "");
        GUI.Label(new Rect(rect.x + 12, rect.y, rect.width - 24, rect.height), text, mutedStyle);
    }
    private void DrawInformation(Rect rect)
    {
        if (frameworkDiagnostics == null || Time.realtimeSinceStartup >= nextDiagnosticsAt)
        {
            frameworkDiagnostics = FrameworkDiagnostics.Capture(UPGameRoot.Instance);
            nextDiagnosticsAt = Time.realtimeSinceStartup + .5f;
        }
        float line = ControlHeight + 10;
        int diagnosticRows = 13 + Math.Max(1, frameworkDiagnostics.Modules.Count) + Math.Max(1, frameworkDiagnostics.StartupSteps.Count)
            + (string.IsNullOrEmpty(frameworkDiagnostics.InitializationError) ? 0 : 1);
        float contentHeight = line * (19 + diagnosticRows) + 120;
        infoScroll = GUI.BeginScrollView(rect, infoScroll, new Rect(0, 0, Mathf.Max(1, rect.width - 16), contentHeight), false, true, GUIStyle.none, viewSkin.verticalScrollbar);
        float y = 8;
        InfoLine(ref y, rect.width, "应用", Application.productName + "  " + Application.version, images.softwareImage);
        InfoLine(ref y, rect.width, "设备", SystemInfo.deviceModel + " / " + SystemInfo.deviceName, images.systemInfoImage);
        InfoLine(ref y, rect.width, "系统", SystemInfo.operatingSystem, images.softwareImage);
        InfoLine(ref y, rect.width, "图形", SystemInfo.graphicsDeviceName, images.graphicsInfoImage);
        InfoLine(ref y, rect.width, "内存", "系统 " + SystemInfo.systemMemorySize + " MB / 显存 " + SystemInfo.graphicsMemorySize + " MB", images.showMemoryImage);
        InfoLine(ref y, rect.width, "运行", "Unity " + Application.unityVersion + " / " + Screen.width + " × " + Screen.height, images.infoImage);
        InfoLine(ref y, rect.width, "构建", buildDate, images.buildFromImage);
        InfoLine(ref y, rect.width, "启动", logDate, images.dateImage);
        InfoLine(ref y, rect.width, "用户", string.IsNullOrEmpty(UserData) ? "未设置" : UserData, images.userImage);
        InfoLine(ref y, rect.width, "缓冲", store.Count + " 条 / 估算 " + TotalMemUsage.ToString("0.00") + " MB", images.showMemoryImage);
        GUI.Label(new Rect(16, y, 100, line), "界面尺寸", textStyle);
        float scale = GUI.HorizontalSlider(new Rect(125, y + line / 2 - 16, Mathf.Max(1, rect.width - 165), 32), size.y, 16, 64);
        if (!Mathf.Approximately(scale, size.y)) size = new Vector2(scale, scale);
        y += line;
        GUI.Label(new Rect(16, y, 105, line), "不透明度 " + Mathf.RoundToInt(panelOpacity * 100) + "%", textStyle);
        panelOpacity = GUI.HorizontalSlider(new Rect(125, y + line / 2 - 16, Mathf.Max(1, rect.width - 165), 32), panelOpacity, .2f, 1f);
        y += line;
        GUI.Label(new Rect(16, y, rect.width - 32, line), "工具栏显示", titleStyle); y += line;
        var settings = new[] { "换场清空", "时间", "场景", "内存", "帧率", "搜索", "复制", "导出" };
        bool[] values = { showClearOnNewSceneLoadedButton, showTimeButton, showSceneButton, showMemButton, showFpsButton, showSearchText, showCopyButton, showSaveButton };
        int columns = rect.width >= 650 ? 4 : 2;
        float cell = (rect.width - 40) / columns;
        for (int i = 0; i < settings.Length; i++)
        {
            var box = new Rect(16 + i % columns * cell, y + i / columns * line, cell - 6, line - 8);
            if (GUI.Button(box, (values[i] ? "✓ " : "") + settings[i], values[i] ? activeStyle : buttonStyle)) values[i] = !values[i];
        }
        showClearOnNewSceneLoadedButton = values[0]; showTimeButton = values[1]; showSceneButton = values[2]; showMemButton = values[3]; showFpsButton = values[4]; showSearchText = values[5]; showCopyButton = values[6]; showSaveButton = values[7];
        DrawFrameworkDiagnostics(ref y, rect.width, line, frameworkDiagnostics);
        GUI.EndScrollView();
    }
    private void DrawFrameworkDiagnostics(ref float y, float width, float line, FrameworkDiagnosticsSnapshot snapshot)
    {
        float keyWidth = width >= 600 ? 180 : 120;
        DiagnosticSection(ref y, width, line, "框架状态");
        InfoLine(ref y, width, "生命周期", snapshot.FrameworkState + (string.IsNullOrEmpty(snapshot.CurrentStep) ? "" : " / " + snapshot.CurrentStep), images.infoImage, keyWidth);
        InfoLine(ref y, width, "启动总耗时", snapshot.TotalStartupMilliseconds + " ms", images.showTimeImage, keyWidth);
        if (!string.IsNullOrEmpty(snapshot.InitializationError)) InfoLine(ref y, width, "初始化错误", snapshot.InitializationError, images.errorImage, keyWidth);

        DiagnosticSection(ref y, width, line, "模块状态");
        if (snapshot.Modules.Count == 0) InfoLine(ref y, width, "模块", "尚未注册", images.infoImage, keyWidth);
        foreach (var module in snapshot.Modules)
        {
            string value = ModuleStateText(module.State);
            if (module.Milliseconds >= 0) value += " · " + module.Milliseconds + " ms";
            if (!string.IsNullOrEmpty(module.Detail)) value += " · " + module.Detail;
            InfoLine(ref y, width, module.Name, value, images.infoImage, keyWidth);
        }

        DiagnosticSection(ref y, width, line, "启动步骤耗时");
        if (snapshot.StartupSteps.Count == 0) InfoLine(ref y, width, "启动", "尚未执行", images.showTimeImage, keyWidth);
        foreach (var step in snapshot.StartupSteps)
            InfoLine(ref y, width, step.Name, step.Milliseconds + " ms · " + (step.Completed ? "完成" : "失败 / 取消"), images.showTimeImage, keyWidth);

        DiagnosticSection(ref y, width, line, "资源统计");
        var resources = snapshot.Resources;
        InfoLine(ref y, width, "Resources", $"缓存 {resources.CachedEntries} / 加载中 {resources.LoadingEntries} / 引用 {resources.TotalReferences} / 零引用 {resources.ZeroReferenceEntries}", images.showMemoryImage, keyWidth);
        var bundles = snapshot.AssetBundles;
        InfoLine(ref y, width, "AssetBundle", $"已加载 {bundles.LoadedBundles} / 包请求 {bundles.PendingBundles} / 资源请求 {bundles.PendingAssets} / 退役 {bundles.RetiringBundles} / 引用 {bundles.LeasePins}", images.showMemoryImage, keyWidth);
        InfoLine(ref y, width, "场景", $"作用域 {snapshot.SceneScopes} / 待处理请求 {snapshot.PendingSceneRequests}", images.showSceneImage, keyWidth);
        InfoLine(ref y, width, "UI", $"已打开 {snapshot.OpenPanels} / 加载中 {snapshot.PendingPanels}", images.infoImage, keyWidth);
    }
    private void DiagnosticSection(ref float y, float width, float line, string title)
    {
        y += 8;
        GUI.Label(new Rect(16, y, width - 32, line), title, titleStyle);
        y += line;
    }
    private static string ModuleStateText(FrameworkModuleState state)
    {
        switch (state)
        {
            case FrameworkModuleState.Pending: return "等待";
            case FrameworkModuleState.Running: return "运行中";
            case FrameworkModuleState.Ready: return "就绪";
            case FrameworkModuleState.Failed: return "失败";
            case FrameworkModuleState.RolledBack: return "已回滚";
            default: return state.ToString();
        }
    }
    private void InfoLine(ref float y, float width, string key, string value, Texture2D icon, float keyWidth = 65)
    {
        float h = ControlHeight + 10;
        if (icon != null) GUI.DrawTexture(new Rect(16, y + (h - 22) / 2, 22, 22), icon, ScaleMode.ScaleToFit);
        GUI.Label(new Rect(48, y, keyWidth, h), key, mutedStyle);
        GUI.Label(new Rect(53 + keyWidth, y, Mathf.Max(1, width - keyWidth - 85), h), value ?? "", textStyle);
        y += h;
    }
}
