using System;
using System.Collections.Generic;
using UnityEngine;

namespace UPandaGF
{
    public sealed partial class UPandaLogConsole
    {
        private enum ConsolePage
        {
            Logs = 0,
            Settings = 1,
            Diagnostics = 2,
        }

        private float FooterHeight { get { return 26f * panelScale; } }
        private float RowHeight { get { return 48f * panelScale; } }
        private float InfoRowHeight { get { return 32f * panelScale; } }
        private float SectionHeight { get { return 28f * panelScale; } }
        private float ControlHeight { get { return 30f * panelScale; } }
        private const string PreferencePrefix = "UPandaLogConsole.";
        private static readonly string[] PageNames = { "日志", "显示", "诊断" };

        private readonly List<LogConsoleEntry> visibleEntries = new List<LogConsoleEntry>();
        private Vector2 listScroll;
        private Vector2 detailScroll;
        private Vector2 pageScroll;
        private bool listDragging;
        private bool detailDragging;
        private bool pageDragging;
        private Vector2 listDragStartMouse;
        private Vector2 listDragStartScroll;
        private Vector2 detailDragStartMouse;
        private Vector2 detailDragStartScroll;
        private Vector2 pageDragStartMouse;
        private Vector2 pageDragStartScroll;
        private int selected = -1;
        private int cachedVersion = -1;
        private string cachedFilter;
        private bool cachedCollapse;
        private bool cachedInfo;
        private bool cachedWarning;
        private bool cachedError;

        private ConsolePage page = ConsolePage.Logs;
        private bool collapse;
        private bool showInfo = true;
        private bool showWarning = true;
        private bool showError = true;
        private string filter = string.Empty;

        private bool showTime = true;
        private bool showScene;
        private bool showMemory;
        private bool showFps;
        private bool showSearch = true;
        private bool showCopy = true;
        private bool showSave = true;
        private bool pauseCapture;
        private bool followTail = true;
        private bool clearOnSceneLoad;
        private float panelOpacity = 1f;
        private float panelScale = 1f;
        private bool focusSearch;
        private bool preferencesLoaded;

        private FrameworkDiagnosticsSnapshot diagnostics;
        private float nextDiagnosticsAt;

        private GUIStyle panelStyle;
        private GUIStyle titleStyle;
        private GUIStyle tabStyle;
        private GUIStyle buttonStyle;
        private GUIStyle activeButtonStyle;
        private GUIStyle chipStyle;
        private GUIStyle fieldStyle;
        private GUIStyle rowBoxStyle;
        private GUIStyle rowHeaderStyle;
        private GUIStyle rowHeaderSelectedStyle;
        private GUIStyle rowMessageStyle;
        private GUIStyle rowMessageSelectedStyle;
        private GUIStyle columnStyle;
        private GUIStyle columnSelectedStyle;
        private GUIStyle mutedStyle;
        private GUIStyle sectionStyle;
        private GUIStyle infoKeyStyle;
        private GUIStyle infoValueStyle;
        private GUIStyle detailStyle;
        private GUIStyle footerStyle;
        private bool stylesReady;

        public string Filter { get { return filter; } }
        public bool ShowTime { get { return showTime; } set { showTime = value; SavePreferences(); } }
        public bool ShowScene { get { return showScene; } set { showScene = value; SavePreferences(); } }
        public bool ShowMemory { get { return showMemory; } set { showMemory = value; SavePreferences(); } }
        public bool ShowFps { get { return showFps; } set { showFps = value; SavePreferences(); } }
        public bool PauseCapture { get { return pauseCapture; } set { pauseCapture = value; SavePreferences(); } }
        public bool FollowTail { get { return followTail; } set { followTail = value; SavePreferences(); } }
        public bool ClearOnSceneLoad { get { return clearOnSceneLoad; } set { clearOnSceneLoad = value; SavePreferences(); } }
        public float PanelOpacity
        {
            get { return panelOpacity; }
            set { panelOpacity = Mathf.Clamp(value, 0.35f, 1f); SavePreferences(); }
        }

        public float PanelScale
        {
            get { return panelScale; }
            set
            {
                panelScale = Mathf.Clamp(value, 0.85f, 1.8f);
                stylesReady = false;
                SavePreferences();
            }
        }

        /// <summary>打开显示设置页。</summary>
        public void OpenSettings()
        {
            Show();
            page = ConsolePage.Settings;
            pageScroll = Vector2.zero;
        }

        /// <summary>打开框架诊断页。</summary>
        public void OpenDiagnostics()
        {
            Show();
            page = ConsolePage.Diagnostics;
            pageScroll = Vector2.zero;
        }

        public void SetFilter(string value)
        {
            string next = value ?? string.Empty;
            if (next == filter) return;
            filter = next;
            InvalidateVisible();
        }

        public void SetSeverity(bool info, bool warning, bool error)
        {
            showInfo = info;
            showWarning = warning;
            showError = error;
            InvalidateVisible();
        }

        public void SetCollapse(bool value)
        {
            collapse = value;
            InvalidateVisible();
        }

        public void CopySelected()
        {
            if (selected < 0 || selected >= visibleEntries.Count) return;
            GUIUtility.systemCopyBuffer = FormatEntry(visibleEntries[selected]);
        }

        private void InvalidateVisible()
        {
            cachedVersion = -1;
        }

        partial void ResetViewAfterClear()
        {
            selected = -1;
            listScroll = Vector2.zero;
            detailScroll = Vector2.zero;
            InvalidateVisible();
        }

        private void LoadPreferences()
        {
            if (preferencesLoaded) return;
            preferencesLoaded = true;
            collapse = PrefBool("collapse", false);
            showInfo = PrefBool("showInfo", true);
            showWarning = PrefBool("showWarning", true);
            showError = PrefBool("showError", true);
            showTime = PrefBool("showTime", true);
            showScene = PrefBool("showScene", false);
            showMemory = PrefBool("showMemory", false);
            showFps = PrefBool("showFps", false);
            showSearch = PrefBool("showSearch", true);
            showCopy = PrefBool("showCopy", true);
            showSave = PrefBool("showSave", true);
            clearOnSceneLoad = PrefBool("clearOnSceneLoad", false);
            panelOpacity = Mathf.Clamp(PlayerPrefs.GetFloat(PreferencePrefix + "opacity", 1f), 0.35f, 1f);
            float defaultScale = 1f;
            if (Application.isMobilePlatform)
            {
                defaultScale = Screen.dpi > 1f
                    ? Mathf.Clamp(Screen.dpi / 160f, 1.15f, 1.8f)
                    : Mathf.Clamp(Screen.width / 720f, 1.15f, 1.8f);
            }
            float savedScale = PlayerPrefs.HasKey(PreferencePrefix + "scale")
                ? PlayerPrefs.GetFloat(PreferencePrefix + "scale", defaultScale)
                : defaultScale;
            panelScale = Mathf.Clamp(savedScale, 0.85f, 1.8f);
        }

        private void SavePreferences()
        {
            if (!preferencesLoaded) return;
            SetPref("collapse", collapse);
            SetPref("showInfo", showInfo);
            SetPref("showWarning", showWarning);
            SetPref("showError", showError);
            SetPref("showTime", showTime);
            SetPref("showScene", showScene);
            SetPref("showMemory", showMemory);
            SetPref("showFps", showFps);
            SetPref("showSearch", showSearch);
            SetPref("showCopy", showCopy);
            SetPref("showSave", showSave);
            SetPref("clearOnSceneLoad", clearOnSceneLoad);
            PlayerPrefs.SetFloat(PreferencePrefix + "opacity", panelOpacity);
            PlayerPrefs.SetFloat(PreferencePrefix + "scale", panelScale);
            PlayerPrefs.Save();
        }

        private static bool PrefBool(string name, bool fallback)
        {
            return PlayerPrefs.GetInt(PreferencePrefix + name, fallback ? 1 : 0) != 0;
        }

        private static void SetPref(string name, bool value)
        {
            PlayerPrefs.SetInt(PreferencePrefix + name, value ? 1 : 0);
        }

        private void RefreshVisible()
        {
            if (store == null) return;
            if (cachedVersion == store.Version
                && cachedCollapse == collapse
                && cachedInfo == showInfo
                && cachedWarning == showWarning
                && cachedError == showError
                && string.Equals(cachedFilter, filter))
                return;

            store.CopyVisible(visibleEntries, collapse, showInfo, showWarning, showError, filter);
            cachedVersion = store.Version;
            cachedCollapse = collapse;
            cachedInfo = showInfo;
            cachedWarning = showWarning;
            cachedError = showError;
            cachedFilter = filter;
            if (selected >= visibleEntries.Count) selected = visibleEntries.Count - 1;
        }

        private void OnGUI()
        {
            if (!visible) return;
            LoadPreferences();
            GUISkin previousSkin = GUI.skin;
            if (icons != null && icons.scrollerSkin != null) GUI.skin = icons.scrollerSkin;
            EnsureStyles();
            RefreshVisible();

            int previousDepth = GUI.depth;
            Color previousColor = GUI.color;
            try
            {
                GUI.depth = -1000;
                GUI.color = new Color(1f, 1f, 1f, panelOpacity);
                DrawConsole();
            }
            finally
            {
                GUI.color = previousColor;
                GUI.depth = previousDepth;
                GUI.skin = previousSkin;
            }
        }

        private static readonly Color PanelColor = new Color(0.07f, 0.09f, 0.13f);

        private void DrawPanel(Rect rect)
        {
            Color previous = GUI.color;
            GUI.color = new Color(PanelColor.r, PanelColor.g, PanelColor.b, previous.a);
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill);
            GUI.color = previous;
        }
        private void DrawConsole()
        {
            Rect safe = Screen.safeArea;
            if (safe.width <= 0f || safe.height <= 0f) safe = new Rect(0f, 0f, Screen.width, Screen.height);
            float margin = !Application.isMobilePlatform && safe.width >= 900f ? 12f : 0f;
            float width = Mathf.Max(1f, safe.width - margin * 2f);
            float height = Mathf.Max(1f, safe.height - margin * 2f);
            Rect window = new Rect(safe.x + margin, safe.y + margin, width, height);

            DrawPanel(window);
            float contentTop = DrawHeader(window, window.y + 6f);
            float contentBottom = window.yMax - FooterHeight - 4f;
            Rect pageRect = new Rect(window.x + 8f, contentTop, window.width - 16f,
                Mathf.Max(1f, contentBottom - contentTop));
            if (pageRect.height > 30f)
            {
                if (page == ConsolePage.Settings) DrawSettingsPage(pageRect);
                else if (page == ConsolePage.Diagnostics) DrawDiagnosticsPage(pageRect);
                else DrawLogsPage(pageRect);
            }
            DrawFooter(new Rect(window.x, window.yMax - FooterHeight, window.width, FooterHeight));

            if (Event.current.type == EventType.KeyDown)
            {
                if (Event.current.keyCode == KeyCode.Escape)
                {
                    Hide();
                    Event.current.Use();
                }
                else if ((Event.current.control || Event.current.command) && Event.current.keyCode == KeyCode.F)
                {
                    page = ConsolePage.Logs;
                    showSearch = true;
                    focusSearch = true;
                    Event.current.Use();
                }
            }
        }

        private float DrawHeader(Rect window, float y)
        {
            if (icons != null && icons.barImage != null)
                GUI.DrawTexture(new Rect(window.x, y - 2f, window.width, 3f * panelScale), icons.barImage, ScaleMode.StretchToFill);
            bool narrow = window.width < 760f;
            float titleHeight = 32f * panelScale;
            float closeSize = 28f * panelScale;
            float titleWidth = narrow ? window.width - 50f * panelScale : Mathf.Clamp(window.width * 0.25f, 180f, 280f);
            GUI.Label(new Rect(window.x + 12f, y, titleWidth, titleHeight), "UPanda Log Console", titleStyle);
            Texture2D closeIcon = icons != null ? icons.closeImage : null;
            GUIContent closeContent = closeIcon != null
                ? new GUIContent(string.Empty, closeIcon, "关闭")
                : new GUIContent("×", "关闭");
            if (GUI.Button(new Rect(window.xMax - 8f - closeSize, y + (titleHeight - closeSize) * 0.5f, closeSize, closeSize), closeContent, chipStyle))
                Hide();

            float tabHeight = 30f * panelScale;
            float tabY = narrow ? y + titleHeight + 4f : y + (titleHeight - tabHeight) * 0.5f;
            float tabX = window.x + (narrow ? 10f : titleWidth + 16f);
            float tabArea = Mathf.Max(120f, window.xMax - tabX - 12f - closeSize);
            float tabWidth = tabArea / 3f;
            Texture2D[] tabIcons =
            {
                icons != null ? icons.logImage : null,
                icons != null ? icons.softwareImage : null,
                icons != null ? icons.infoImage : null,
            };
            for (int i = 0; i < PageNames.Length; i++)
            {
                var rect = new Rect(tabX + i * tabWidth, tabY, tabWidth - 3f, tabHeight);
                if (TabButton(rect, PageNames[i], tabIcons[i], page == (ConsolePage)i))
                {
                    page = (ConsolePage)i;
                    pageScroll = Vector2.zero;
                }
            }
            return narrow ? tabY + tabHeight + 4f : y + titleHeight + 4f;
        }

        private bool TabButton(Rect rect, string label, Texture2D icon, bool active)
        {
            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = active ? new Color(0.32f, 0.5f, 0.78f) : new Color(0.24f, 0.28f, 0.34f);
            bool clicked = GUI.Button(rect, new GUIContent(label, icon), active ? activeButtonStyle : tabStyle);
            GUI.backgroundColor = previous;
            return clicked;
        }

        private void DrawLogsPage(Rect pageRect)
        {
            float y = DrawFilterBar(pageRect, pageRect.y);
            y = DrawActionBar(pageRect, y);
            float bodyHeight = Mathf.Max(40f, pageRect.yMax - y);
            bool sideBySide = pageRect.width >= 900f && Screen.width > Screen.height;
            if (sideBySide)
            {
                float listWidth = Mathf.Clamp(pageRect.width * 0.64f, 260f, pageRect.width - 220f);
                DrawLogList(new Rect(pageRect.x, y, listWidth - 4f, bodyHeight));
                DrawDetails(new Rect(pageRect.x + listWidth, y, pageRect.width - listWidth, bodyHeight));
            }
            else
            {
                float listMin = 90f * panelScale;
                float detailMin = 80f * panelScale;
                float listHeight = bodyHeight > listMin + detailMin
                    ? Mathf.Clamp(bodyHeight * 0.6f, listMin, bodyHeight - detailMin)
                    : bodyHeight * 0.6f;
                DrawLogList(new Rect(pageRect.x, y, pageRect.width, listHeight - 4f));
                DrawDetails(new Rect(pageRect.x, y + listHeight, pageRect.width, bodyHeight - listHeight));
            }
        }

        private float DrawFilterBar(Rect pageRect, float y)
        {
            float x = pageRect.x + 4f;
            float width = pageRect.width - 8f;
            if (showSearch)
            {
                float searchWidth = width >= 760f ? Mathf.Clamp(width * 0.34f, 180f, 360f) : width;
                DrawSearch(new Rect(x, y, searchWidth, ControlHeight));
                if (width >= 760f)
                {
                    DrawSeverityChips(new Rect(x + searchWidth + 8f, y,
                        Mathf.Max(120f, pageRect.xMax - x - searchWidth - 12f), ControlHeight));
                    y += ControlHeight + 6f;
                }
                else
                {
                    y += ControlHeight + 4f;
                    DrawSeverityChips(new Rect(x, y, width, ControlHeight));
                    y += ControlHeight + 6f;
                }
            }
            else
            {
                DrawSeverityChips(new Rect(x, y, width, ControlHeight));
                y += ControlHeight + 6f;
            }
            return y;
        }

        private void DrawSearch(Rect rect)
        {
            float offset = 0f;
            if (icons != null && icons.searchImage != null)
            {
                float iconSize = 18f * panelScale;
                GUI.DrawTexture(new Rect(rect.x + 4f * panelScale, rect.y + (rect.height - iconSize) * 0.5f, iconSize, iconSize),
                    icons.searchImage, ScaleMode.ScaleToFit);
                offset = iconSize + 6f * panelScale;
            }
            GUI.SetNextControlName("UPandaLogConsoleSearch");
            string value = GUI.TextField(new Rect(rect.x + offset, rect.y, rect.width - offset, rect.height),
                filter, fieldStyle);
            if (value != filter) SetFilter(value);
            if (filter.Length == 0 && GUI.GetNameOfFocusedControl() != "UPandaLogConsoleSearch")
                GUI.Label(new Rect(rect.x + offset + 6f, rect.y, rect.width - offset - 12f, rect.height),
                    "搜索日志或堆栈…", mutedStyle);
            if (focusSearch)
            {
                GUI.FocusControl("UPandaLogConsoleSearch");
                focusSearch = false;
            }
        }

        private void DrawSeverityChips(Rect rect)
        {
            float gap = 4f;
            float chipWidth = Mathf.Max(52f, (rect.width - gap * 2f) / 3f);
            if (Chip(new Rect(rect.x, rect.y, chipWidth, rect.height), "Info " + InfoCount, showInfo,
                icons != null ? icons.logImage : null))
            {
                showInfo = !showInfo;
                InvalidateVisible();
            }
            if (Chip(new Rect(rect.x + chipWidth + gap, rect.y, chipWidth, rect.height), "Warn " + WarningCount, showWarning,
                icons != null ? icons.warningImage : null))
            {
                showWarning = !showWarning;
                InvalidateVisible();
            }
            if (Chip(new Rect(rect.x + (chipWidth + gap) * 2f, rect.y, chipWidth, rect.height), "Error " + ErrorCount, showError,
                icons != null ? icons.errorImage : null))
            {
                showError = !showError;
                InvalidateVisible();
            }
        }

        private bool Chip(Rect rect, string label, bool active, Texture2D icon)
        {
            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = active ? new Color(0.36f, 0.52f, 0.72f) : new Color(0.24f, 0.28f, 0.34f);
            bool clicked = GUI.Button(rect, new GUIContent(label, icon), active ? activeButtonStyle : chipStyle);
            GUI.backgroundColor = previous;
            return clicked;
        }

        private float DrawActionBar(Rect pageRect, float y)
        {
            float height = ControlHeight;
            float buttonWidth = (pageRect.width < 520f ? 68f : 86f) * Mathf.Max(1f, panelScale);
            const float gap = 4f;
            int columns = Mathf.Max(2, Mathf.FloorToInt((pageRect.width + gap) / (buttonWidth + gap)));
            int actionCount = 5 + (showCopy ? 1 : 0) + (showSave ? 1 : 0);
            int rows = Mathf.CeilToInt(actionCount / (float)columns);
            int actionIndex = 0;

            Rect NextRect()
            {
                int column = actionIndex % columns;
                int row = actionIndex / columns;
                actionIndex++;
                return new Rect(pageRect.x + column * (buttonWidth + gap), y + row * (height + gap),
                    buttonWidth, height);
            }

            if (ActionButton(NextRect(), "清空", icons != null ? icons.clearImage : null, false, true))
                Clear();
            if (ActionButton(NextRect(), "折叠", icons != null ? icons.collapseImage : null, collapse, true))
                SetCollapse(!collapse);
            if (ActionButton(NextRect(), pauseCapture ? "继续" : "暂停", null, pauseCapture, true))
                PauseCapture = !pauseCapture;
            if (ActionButton(NextRect(), followTail ? "跟随中" : "跟随", null, followTail, true))
                FollowTail = !followTail;
            if (ActionButton(NextRect(), "换场清空", icons != null ? icons.clearOnNewSceneImage : null, clearOnSceneLoad, true))
                ClearOnSceneLoad = !clearOnSceneLoad;
            if (showCopy)
            {
                bool canCopy = selected >= 0 && selected < visibleEntries.Count;
                bool previousEnabled = GUI.enabled;
                GUI.enabled = canCopy;
                if (ActionButton(NextRect(), "复制选中", icons != null ? icons.copyImage : null, false, true))
                    CopySelected();
                GUI.enabled = previousEnabled;
            }
            if (showSave && ActionButton(NextRect(), "导出", icons != null ? icons.saveLogsImage : null, false, true))
                SaveVisible();
            return y + rows * (height + gap) + 2f;
        }

        private bool ActionButton(Rect rect, string label, Texture2D icon, bool active, bool withText)
        {
            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = active ? new Color(0.36f, 0.52f, 0.72f) : Color.white;
            string contentLabel = withText || icon == null ? label : string.Empty;
            bool clicked = GUI.Button(rect, new GUIContent(contentLabel, icon), active ? activeButtonStyle : buttonStyle);
            GUI.backgroundColor = previous;
            return clicked;
        }

        private bool HandleDragScroll(Rect viewport, ref Vector2 scroll, ref bool dragging,
            ref Vector2 dragStartMouse, ref Vector2 dragStartScroll, float maxY)
        {
            Event current = Event.current;
            if (current.type == EventType.MouseDown && viewport.Contains(current.mousePosition))
            {
                dragging = true;
                dragStartMouse = current.mousePosition;
                dragStartScroll = scroll;
            }
            else if (dragging && current.type == EventType.MouseDrag)
            {
                Vector2 delta = current.mousePosition - dragStartMouse;
                if (delta.sqrMagnitude > 9f)
                {
                    scroll.y = Mathf.Clamp(dragStartScroll.y - delta.y, 0f, Mathf.Max(0f, maxY));
                    GUIUtility.hotControl = 0;
                    current.Use();
                    return true;
                }
            }
            else if (current.rawType == EventType.MouseUp)
            {
                dragging = false;
            }
            return false;
        }

        private void DrawLogList(Rect rect)
        {
            DrawPanel(rect);
            var viewport = new Rect(rect.x + 2f, rect.y + 3f, rect.width - 4f, rect.height - 6f);
            float contentWidth = Mathf.Max(1f, viewport.width - 18f);
            float contentHeight = Mathf.Max(viewport.height, visibleEntries.Count * RowHeight);
            float maxScroll = Mathf.Max(0f, contentHeight - viewport.height);

            if (Event.current.type == EventType.ScrollWheel && viewport.Contains(Event.current.mousePosition))
                followTail = false;
            if ((Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseDrag)
                && viewport.Contains(Event.current.mousePosition)
                && Event.current.mousePosition.x >= viewport.xMax - 20f)
                followTail = false;
            Rect listDragArea = viewport;
            listDragArea.width = Mathf.Max(1f, viewport.width - 20f);
            bool dragged = HandleDragScroll(listDragArea, ref listScroll, ref listDragging,
                ref listDragStartMouse, ref listDragStartScroll, maxScroll);
            if (dragged) followTail = false;
            if (followTail) listScroll.y = maxScroll;
            listScroll.y = Mathf.Clamp(listScroll.y, 0f, maxScroll);

            listScroll = GUI.BeginScrollView(viewport, listScroll,
                new Rect(0f, 0f, contentWidth, contentHeight), false, true,
                GUIStyle.none, GUI.skin.verticalScrollbar);

            int first, last;
            LogConsoleStore.GetVisibleRange(visibleEntries.Count, listScroll.y, RowHeight, viewport.height,
                out first, out last);
            for (int i = first; i < last; i++) DrawRow(i, contentWidth);
            GUI.EndScrollView();
        }

        private void DrawRow(int index, float width)
        {
            LogConsoleEntry entry = visibleEntries[index];
            Rect row = new Rect(0f, index * RowHeight, width, RowHeight - 2f);
            bool isSelected = selected == index;
            // 旧图标里的 even/odd 行底是白底，和白字冲突；这里只保留选中行底色。
            if (isSelected)
            {
                if (icons != null && icons.selectedImage != null)
                {
                    GUI.DrawTexture(row, icons.selectedImage, ScaleMode.StretchToFill);
                }
                else
                {
                    Color previousBackground = GUI.backgroundColor;
                    GUI.backgroundColor = new Color(0.2f, 0.35f, 0.6f);
                    GUI.Box(row, GUIContent.none, rowBoxStyle);
                    GUI.backgroundColor = previousBackground;
                }
            }

            Texture2D levelIcon = icons == null ? null
                : entry.Level == LogConsoleLevel.Warning ? icons.warningImage
                : entry.Level == LogConsoleLevel.Error ? icons.errorImage
                : icons.logImage;
            float textX = row.x + 8f;
            if (levelIcon != null)
            {
                float iconSize = 18f * panelScale;
                GUI.DrawTexture(new Rect(row.x + 5f, row.y + (RowHeight - 2f - iconSize) * 0.5f, iconSize, iconSize),
                    levelIcon, ScaleMode.ScaleToFit);
                textX = row.x + 5f + iconSize + 5f;
            }

            string header = LevelTag(entry.Level);
            bool drawTime = showTime && width > 600f * panelScale;
            bool drawScene = showScene && width > 700f * panelScale;
            if (!drawTime) header = entry.TimestampUtc.ToLocalTime().ToString("HH:mm:ss") + "  " + header;
            if (!drawScene && !string.IsNullOrEmpty(entry.SceneName)) header += "  · " + entry.SceneName;
            if (entry.RepeatCount > 1) header += "  ×" + entry.RepeatCount;

            float right = row.xMax - 6f;
            float textRight = right;
            if (showFps && width > 900f * panelScale)
                DrawColumn(ref textRight, row, entry.Fps.ToString("0") + " fps", 62f * panelScale, isSelected);
            if (showMemory && width > 800f * panelScale)
                DrawColumn(ref textRight, row, entry.MemoryMb.ToString("0.0") + " MB", 72f * panelScale, isSelected);
            if (drawScene)
                DrawColumn(ref textRight, row, entry.SceneName, 100f * panelScale, isSelected);
            if (drawTime)
                DrawColumn(ref textRight, row, entry.TimestampUtc.ToLocalTime().ToString("HH:mm:ss"), 62f * panelScale, isSelected);

            float textWidth = Mathf.Max(60f, textRight - textX - 8f);
            float lineHeight = 16f * panelScale;
            GUI.Label(new Rect(textX, row.y + 4f * panelScale, textWidth, lineHeight), header,
                isSelected ? rowHeaderSelectedStyle : rowHeaderStyle);
            string message = entry.Message.Replace('\n', ' ');
            if (message.Length > 220) message = message.Substring(0, 220) + "…";
            GUI.Label(new Rect(textX, row.y + 4f * panelScale + lineHeight + 2f * panelScale,
                textWidth, 20f * panelScale), message,
                isSelected ? rowMessageSelectedStyle : rowMessageStyle);

            if (GUI.Button(row, GUIContent.none, GUIStyle.none)) selected = index;
        }

        private void DrawColumn(ref float right, Rect row, string value, float width, bool selected)
        {
            if (string.IsNullOrEmpty(value)) return;
            right -= width;
            GUI.Label(new Rect(right, row.y + 4f * panelScale, width - 4f, 16f * panelScale), value,
                selected ? columnSelectedStyle : columnStyle);
        }

        private void DrawDetails(Rect rect)
        {
            DrawPanel(rect);
            GUI.Label(new Rect(rect.x + 8f, rect.y + 4f, rect.width - 16f, 20f), "详情", sectionStyle);
            if (selected < 0 || selected >= visibleEntries.Count)
            {
                GUI.Label(new Rect(rect.x + 10f, rect.y + 30f, rect.width - 20f, 20f), "选择一条日志查看堆栈。", mutedStyle);
                return;
            }

            LogConsoleEntry entry = visibleEntries[selected];
            string text = entry.Message;
            if (!string.IsNullOrEmpty(entry.StackTrace)) text += "\n\n" + entry.StackTrace;
            var viewport = new Rect(rect.x + 4f, rect.y + 28f, rect.width - 8f, Mathf.Max(1f, rect.height - 34f));
            Rect detailDragArea = viewport;
            detailDragArea.width = Mathf.Max(1f, viewport.width - 20f);
            HandleDragScroll(detailDragArea, ref detailScroll, ref detailDragging,
                ref detailDragStartMouse, ref detailDragStartScroll, 4000f - viewport.height);
            detailScroll = GUI.BeginScrollView(viewport, detailScroll,
                new Rect(0f, 0f, Mathf.Max(1f, viewport.width - 18f), 4000f), false, true,
                GUIStyle.none, GUI.skin.verticalScrollbar);
            GUI.Label(new Rect(0f, 0f, viewport.width - 22f, 3990f), text, detailStyle);
            GUI.EndScrollView();
        }

        private void DrawSettingsPage(Rect rect)
        {
            float width = rect.width - 18f;
            Rect pageDragArea = rect;
            pageDragArea.width = Mathf.Max(1f, rect.width - 20f);
            HandleDragScroll(pageDragArea, ref pageScroll, ref pageDragging,
                ref pageDragStartMouse, ref pageDragStartScroll, 1800f - rect.height);
            pageScroll = GUI.BeginScrollView(rect, pageScroll,
                new Rect(0f, 0f, width, 1800f), false, true, GUIStyle.none, GUI.skin.verticalScrollbar);
            float y = 4f;
            DrawSection(ref y, width, "应用 / 设备");
            DrawInfo(ref y, width, "应用", Application.productName + "  " + Application.version, icons != null ? icons.softwareImage : null);
            DrawInfo(ref y, width, "包名", Application.identifier, icons != null ? icons.infoImage : null);
            DrawInfo(ref y, width, "设备", SystemInfo.deviceModel + " / " + SystemInfo.deviceName, icons != null ? icons.systemInfoImage : null);
            DrawInfo(ref y, width, "系统", SystemInfo.operatingSystem, icons != null ? icons.softwareImage : null);
            DrawInfo(ref y, width, "处理器", SystemInfo.processorType + " × " + SystemInfo.processorCount, icons != null ? icons.systemInfoImage : null);
            DrawInfo(ref y, width, "图形", SystemInfo.graphicsDeviceName, icons != null ? icons.graphicsInfoImage : null);
            DrawInfo(ref y, width, "内存", "系统 " + SystemInfo.systemMemorySize + " MB / 显存 " + SystemInfo.graphicsMemorySize + " MB",
                icons != null ? icons.showMemoryImage : null);
            DrawInfo(ref y, width, "运行", "Unity " + Application.unityVersion + " / " + Screen.width + " × " + Screen.height,
                icons != null ? icons.infoImage : null);
            DrawInfo(ref y, width, "构建", string.IsNullOrEmpty(Application.buildGUID) ? "编辑器 / 未提供" : Application.buildGUID,
                icons != null ? icons.buildFromImage : null);
            DrawInfo(ref y, width, "会话", "启动 " + startedAt.ToString("HH:mm:ss") + " / 已运行 " +
                Time.realtimeSinceStartup.ToString("0") + " s", icons != null ? icons.dateImage : null);
            DrawInfo(ref y, width, "厂商", string.IsNullOrEmpty(Application.companyName) ? "未设置" : Application.companyName,
                icons != null ? icons.userImage : null);
            DrawInfo(ref y, width, "场景", string.IsNullOrEmpty(sceneName) ? "-" : sceneName, icons != null ? icons.showSceneImage : null);
            DrawInfo(ref y, width, "缓冲", RetainedCount + " 条 / 估算 " + store.EstimatedBytes / (1024f * 1024f) + " MB",
                icons != null ? icons.showMemoryImage : null);

            DrawSection(ref y, width, "日志列");
            bool changed = false;
            var columnLabels = new[] { "时间", "场景", "内存", "帧率" };
            var columnIcons = new[]
            {
                icons != null ? icons.showTimeImage : null,
                icons != null ? icons.showSceneImage : null,
                icons != null ? icons.showMemoryImage : null,
                icons != null ? icons.showFpsImage : null,
            };
            var columnValues = new[] { showTime, showScene, showMemory, showFps };
            if (DrawToggleGroup(ref y, width, columnLabels, columnIcons, columnValues))
            {
                showTime = columnValues[0];
                showScene = columnValues[1];
                showMemory = columnValues[2];
                showFps = columnValues[3];
                changed = true;
            }

            DrawSection(ref y, width, "工具栏");
            var toolbarLabels = new[] { "搜索", "复制", "导出", "换场清空" };
            var toolbarIcons = new[]
            {
                icons != null ? icons.searchImage : null,
                icons != null ? icons.copyImage : null,
                icons != null ? icons.saveLogsImage : null,
                icons != null ? icons.clearOnNewSceneImage : null,
            };
            var toolbarValues = new[] { showSearch, showCopy, showSave, clearOnSceneLoad };
            if (DrawToggleGroup(ref y, width, toolbarLabels, toolbarIcons, toolbarValues))
            {
                showSearch = toolbarValues[0];
                showCopy = toolbarValues[1];
                showSave = toolbarValues[2];
                clearOnSceneLoad = toolbarValues[3];
                changed = true;
            }

            DrawSection(ref y, width, "行为");
            var behaviorLabels = new[] { "暂停捕获", "自动跟随" };
            var behaviorValues = new[] { pauseCapture, followTail };
            if (DrawToggleGroup(ref y, width, behaviorLabels, new Texture2D[2], behaviorValues))
            {
                pauseCapture = behaviorValues[0];
                followTail = behaviorValues[1];
                changed = true;
            }

            DrawSection(ref y, width, "外观");
            GUI.Label(new Rect(10f, y, 130f, InfoRowHeight), "界面缩放 " + Mathf.RoundToInt(panelScale * 100f) + "%", mutedStyle);
            float scale = GUI.HorizontalSlider(new Rect(145f, y + 8f, Mathf.Max(80f, width - 170f), 24f), panelScale, 0.85f, 1.8f);
            if (!Mathf.Approximately(scale, panelScale))
            {
                panelScale = Mathf.Clamp(scale, 0.85f, 1.8f);
                stylesReady = false;
                changed = true;
            }
            y += InfoRowHeight;
            GUI.Label(new Rect(10f, y, 130f, InfoRowHeight), "不透明度 " + Mathf.RoundToInt(panelOpacity * 100f) + "%", mutedStyle);
            float opacity = GUI.HorizontalSlider(new Rect(145f, y + 8f, Mathf.Max(80f, width - 170f), 24f), panelOpacity, 0.35f, 1f);
            if (!Mathf.Approximately(opacity, panelOpacity))
            {
                panelOpacity = Mathf.Clamp(opacity, 0.35f, 1f);
                changed = true;
            }
            y += InfoRowHeight;

            DrawSection(ref y, width, "导出");
            float pathLabelWidth = 96f * panelScale;
            float pathFieldX = 10f + pathLabelWidth + 8f;
            float pathFieldWidth = Mathf.Max(80f, width - pathFieldX - 12f);
            GUI.Label(new Rect(10f, y, pathLabelWidth, InfoRowHeight), "输出目录", mutedStyle);
            GUI.TextField(new Rect(pathFieldX, y, pathFieldWidth, InfoRowHeight), ExportDirectory, fieldStyle);
            y += InfoRowHeight;
            if (!string.IsNullOrEmpty(LastExportPath))
            {
                GUI.Label(new Rect(10f, y, pathLabelWidth, InfoRowHeight), "最近导出", mutedStyle);
                GUI.TextField(new Rect(pathFieldX, y, pathFieldWidth, InfoRowHeight), LastExportPath, fieldStyle);
                y += InfoRowHeight;
            }
            float exportButtonWidth = Mathf.Min(110f * panelScale, (width - 24f) * 0.5f);
            if (GUI.Button(new Rect(10f, y, exportButtonWidth, ControlHeight), "复制路径", buttonStyle))
                GUIUtility.systemCopyBuffer = ExportDirectory;
            if (GUI.Button(new Rect(10f + exportButtonWidth + 6f, y, exportButtonWidth, ControlHeight), "导出当前日志", buttonStyle))
                SaveVisible();
            y += ControlHeight + 4f;

            if (changed) SavePreferences();
            GUI.EndScrollView();
        }

        private void DrawDiagnosticsPage(Rect rect)
        {
            FrameworkDiagnosticsSnapshot snapshot = GetDiagnostics();
            float width = rect.width - 18f;
            Rect pageDragArea = rect;
            pageDragArea.width = Mathf.Max(1f, rect.width - 20f);
            HandleDragScroll(pageDragArea, ref pageScroll, ref pageDragging,
                ref pageDragStartMouse, ref pageDragStartScroll, 3000f - rect.height);
            pageScroll = GUI.BeginScrollView(rect, pageScroll,
                new Rect(0f, 0f, width, 3000f), false, true, GUIStyle.none, GUI.skin.verticalScrollbar);
            float y = 4f;
            DrawSection(ref y, width, "框架状态");
            DrawInfo(ref y, width, "生命周期", snapshot.FrameworkState +
                (string.IsNullOrEmpty(snapshot.CurrentStep) ? string.Empty : " / " + snapshot.CurrentStep),
                icons != null ? icons.infoImage : null);
            DrawInfo(ref y, width, "启动总耗时", snapshot.TotalStartupMilliseconds + " ms", icons != null ? icons.showTimeImage : null);
            if (!string.IsNullOrEmpty(snapshot.InitializationError))
                DrawInfo(ref y, width, "初始化错误", snapshot.InitializationError, icons != null ? icons.errorImage : null);

            DrawSection(ref y, width, "模块状态");
            if (snapshot.Modules.Count == 0) DrawInfo(ref y, width, "模块", "尚未注册", icons != null ? icons.infoImage : null);
            foreach (FrameworkModuleDiagnostic module in snapshot.Modules)
            {
                string value = ModuleStateText(module.State);
                if (module.Milliseconds >= 0) value += " · " + module.Milliseconds + " ms";
                if (!string.IsNullOrEmpty(module.Detail)) value += " · " + module.Detail;
                DrawInfo(ref y, width, module.Name, value, icons != null ? icons.infoImage : null);
            }

            DrawSection(ref y, width, "启动步骤耗时");
            if (snapshot.StartupSteps.Count == 0) DrawInfo(ref y, width, "启动", "尚未执行", icons != null ? icons.showTimeImage : null);
            foreach (StartupStepMetric step in snapshot.StartupSteps)
                DrawInfo(ref y, width, step.Name, step.Milliseconds + " ms · " + (step.Completed ? "完成" : "失败 / 取消"),
                    icons != null ? icons.showTimeImage : null);

            DrawSection(ref y, width, "资源统计");
            ResourceLoaderDiagnostics resources = snapshot.Resources;
            DrawInfo(ref y, width, "Resources",
                "缓存 " + resources.CachedEntries + " / 加载中 " + resources.LoadingEntries
                + " / 引用 " + resources.TotalReferences + " / 零引用 " + resources.ZeroReferenceEntries,
                icons != null ? icons.showMemoryImage : null);
            AssetBundleDiagnostics bundles = snapshot.AssetBundles;
            DrawInfo(ref y, width, "AssetBundle",
                "已加载 " + bundles.LoadedBundles + " / 包请求 " + bundles.PendingBundles
                + " / 资源请求 " + bundles.PendingAssets + " / 退役 " + bundles.RetiringBundles
                + " / 引用 " + bundles.LeasePins,
                icons != null ? icons.showMemoryImage : null);
            DrawInfo(ref y, width, "场景", "作用域 " + snapshot.SceneScopes + " / 待处理请求 " + snapshot.PendingSceneRequests,
                icons != null ? icons.showSceneImage : null);
            DrawInfo(ref y, width, "UI", "已打开 " + snapshot.OpenPanels + " / 加载中 " + snapshot.PendingPanels,
                icons != null ? icons.infoImage : null);

            DrawSection(ref y, width, "应用 / 运行时");
            DrawInfo(ref y, width, "平台", Application.platform + " / " + SystemInfo.deviceType, icons != null ? icons.softwareImage : null);
            DrawInfo(ref y, width, "Unity", Application.unityVersion, icons != null ? icons.infoImage : null);
            DrawInfo(ref y, width, "场景", string.IsNullOrEmpty(sceneName) ? "-" : sceneName, icons != null ? icons.showSceneImage : null);
            DrawInfo(ref y, width, "FPS / GC", Fps.ToString("0") + " / " + MemoryMb.ToString("0.0") + " MB",
                icons != null ? icons.showFpsImage : null);
            DrawInfo(ref y, width, "日志", RetainedCount + " 条 / 待处理 " + PendingCount + " / 丢弃 " + DroppedCount,
                icons != null ? icons.logImage : null);
            GUI.EndScrollView();
        }

        private FrameworkDiagnosticsSnapshot GetDiagnostics()
        {
            if (diagnostics == null || Time.realtimeSinceStartup >= nextDiagnosticsAt)
            {
                diagnostics = FrameworkDiagnostics.Capture(UPGameRoot.Instance);
                nextDiagnosticsAt = Time.realtimeSinceStartup + 0.5f;
            }
            return diagnostics;
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

        private void DrawSection(ref float y, float width, string title)
        {
            y += 4f;
            GUI.Label(new Rect(10f, y, Mathf.Max(1f, width - 20f), SectionHeight - 4f), title, sectionStyle);
            y += SectionHeight;
        }

        private void DrawInfo(ref float y, float width, string key, string value, Texture2D icon)
        {
            if (icon != null)
            {
                float iconSize = 20f * panelScale;
                GUI.DrawTexture(new Rect(10f, y + (InfoRowHeight - iconSize) * 0.5f, iconSize, iconSize),
                    icon, ScaleMode.ScaleToFit);
            }
            GUI.Label(new Rect(38f, y, 96f, InfoRowHeight), key, infoKeyStyle);
            GUI.Label(new Rect(136f, y, Mathf.Max(1f, width - 150f), InfoRowHeight), value ?? string.Empty, infoValueStyle);
            y += InfoRowHeight;
        }

        private bool DrawToggleGroup(ref float y, float width, string[] labels, Texture2D[] icons, bool[] values)
        {
            int columns = width >= 700f ? 4 : 2;
            int rows = (labels.Length + columns - 1) / columns;
            const float gap = 6f;
            float cellWidth = (width - 20f - gap * (columns - 1)) / columns;
            bool changed = false;
            for (int i = 0; i < labels.Length; i++)
            {
                int row = i / columns;
                int column = i % columns;
                var rect = new Rect(10f + column * (cellWidth + gap), y + row * (ControlHeight + 4f),
                    cellWidth, ControlHeight);
                Color previous = GUI.backgroundColor;
                GUI.backgroundColor = values[i] ? new Color(0.36f, 0.52f, 0.72f) : Color.white;
                if (GUI.Button(rect, new GUIContent((values[i] ? "✓ " : string.Empty) + labels[i],
                    icons != null && i < icons.Length ? icons[i] : null),
                    values[i] ? activeButtonStyle : buttonStyle))
                {
                    values[i] = !values[i];
                    changed = true;
                }
                GUI.backgroundColor = previous;
            }
            y += rows * (ControlHeight + 4f);
            return changed;
        }

        private void DrawFooter(Rect rect)
        {
            DrawPanel(rect);
            float textWidth = rect.width - 20f;
            if (page != ConsolePage.Logs)
            {
                float backWidth = 84f * panelScale;
                var backRect = new Rect(rect.xMax - 10f - backWidth, rect.y + 3f, backWidth, rect.height - 6f);
                if (GUI.Button(backRect, new GUIContent("返回日志", icons != null ? icons.backImage : null), buttonStyle))
                    page = ConsolePage.Logs;
                textWidth -= backWidth + 12f;
            }
            string state = pauseCapture ? "已暂停 / 待处理 " + PendingCount : "运行中";
            string text = string.Format("{0}  ·  可见 {1} / 保留 {2}  ·  丢弃 {3}  ·  {4} FPS  ·  GC {5:0.0} MB  ·  {6}  ·  {7}",
                PageNames[(int)page], visibleEntries.Count, RetainedCount, DroppedCount, Fps, MemoryMb,
                string.IsNullOrEmpty(sceneName) ? "-" : sceneName, state);
            GUI.Label(new Rect(rect.x + 10f, rect.y + 2f, Mathf.Max(1f, textWidth), rect.height - 4f), text, footerStyle);
        }

        private int ScaledFont(int size)
        {
            return Mathf.Max(9, Mathf.RoundToInt(size * panelScale));
        }

        private void EnsureStyles()
        {
            if (stylesReady) return;
            stylesReady = true;

            if (GUI.skin.scrollView != null)
            {
                GUI.skin.scrollView.normal.background = null;
                GUI.skin.scrollView.hover.background = null;
                GUI.skin.scrollView.active.background = null;
            }
            panelStyle = new GUIStyle(GUI.skin.box);
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = ScaledFont(16),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.84f, 0.87f, 0.90f) },
            };
            tabStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = ScaledFont(12),
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(4, 4, 2, 2),
            };
            tabStyle.normal.textColor = Color.white;
            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = ScaledFont(12),
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(4, 4, 2, 2),
                clipping = TextClipping.Clip,
            };
            buttonStyle.normal.textColor = new Color(0.84f, 0.87f, 0.90f);
            buttonStyle.hover.textColor = Color.white;
            activeButtonStyle = new GUIStyle(buttonStyle);
            activeButtonStyle.normal.textColor = Color.white;
            if (icons != null && icons.buttonActiveImage != null)
            {
                activeButtonStyle.normal.background = icons.buttonActiveImage;
                activeButtonStyle.hover.background = icons.buttonActiveImage;
                activeButtonStyle.active.background = icons.buttonActiveImage;
            }
            chipStyle = new GUIStyle(buttonStyle);
            chipStyle.normal.textColor = Color.white;
            fieldStyle = new GUIStyle(GUI.skin.textField)
            {
                fontSize = ScaledFont(12),
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(6, 6, 2, 2),
            };
            fieldStyle.normal.textColor = new Color(0.84f, 0.87f, 0.90f);
            fieldStyle.focused.textColor = fieldStyle.normal.textColor;
            rowBoxStyle = new GUIStyle(GUI.skin.box);
            rowHeaderStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = ScaledFont(11),
                normal = { textColor = new Color(0.72f, 0.78f, 0.85f) },
            };
            rowHeaderSelectedStyle = new GUIStyle(rowHeaderStyle);
            rowHeaderSelectedStyle.normal.textColor = Color.white;
            rowMessageStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = ScaledFont(12),
                wordWrap = false,
                clipping = TextClipping.Clip,
                normal = { textColor = new Color(0.84f, 0.87f, 0.90f) },
            };
            rowMessageSelectedStyle = new GUIStyle(rowMessageStyle);
            rowMessageSelectedStyle.normal.textColor = Color.white;
            columnStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = ScaledFont(10),
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = new Color(0.62f, 0.68f, 0.75f) },
            };
            columnSelectedStyle = new GUIStyle(columnStyle);
            columnSelectedStyle.normal.textColor = new Color(0.9f, 0.94f, 1f);
            mutedStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = ScaledFont(11),
                normal = { textColor = new Color(0.62f, 0.68f, 0.75f) },
                clipping = TextClipping.Clip,
            };
            sectionStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = ScaledFont(13),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.84f, 0.87f, 0.90f) },
            };
            infoKeyStyle = new GUIStyle(mutedStyle)
            {
                alignment = TextAnchor.MiddleLeft,
            };
            infoValueStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = ScaledFont(12),
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                normal = { textColor = new Color(0.84f, 0.87f, 0.90f) },
            };
            detailStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = ScaledFont(12),
                wordWrap = true,
                normal = { textColor = new Color(0.84f, 0.87f, 0.90f) },
            };
            footerStyle = new GUIStyle(mutedStyle);
        }

        private static string LevelTag(LogConsoleLevel level)
        {
            if (level == LogConsoleLevel.Warning) return "WARN";
            if (level == LogConsoleLevel.Error) return "ERROR";
            return "INFO";
        }
    }
}
