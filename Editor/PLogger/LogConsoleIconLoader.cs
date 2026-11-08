using System.IO;
using UnityEditor;
using UnityEngine;

namespace UPandaGF.GFEditor
{
    /// <summary>把 LogConsole 模块自带图标/皮肤绑定到组件上（只在编辑器创建时调用）。</summary>
    public static class LogConsoleIconLoader
    {
        public static void Assign(UPandaLogConsole console)
        {
            if (console == null) return;
            MonoScript script = MonoScript.FromMonoBehaviour(console);
            string scriptPath = AssetDatabase.GetAssetPath(script);
            if (string.IsNullOrEmpty(scriptPath)) return;
            string directory = Path.Combine(Path.GetDirectoryName(scriptPath) ?? string.Empty, "Icons").Replace('\\', '/');

            var icons = new LogConsoleIcons
            {
                clearImage = Load<Texture2D>(directory, "clear.png"),
                collapseImage = Load<Texture2D>(directory, "collapse.png"),
                clearOnNewSceneImage = Load<Texture2D>(directory, "clearOnSceneLoaded.png"),
                showTimeImage = Load<Texture2D>(directory, "timer_1.png"),
                showSceneImage = Load<Texture2D>(directory, "UnityIcon.png"),
                userImage = Load<Texture2D>(directory, "user.png"),
                showMemoryImage = Load<Texture2D>(directory, "memory.png"),
                softwareImage = Load<Texture2D>(directory, "software.png"),
                dateImage = Load<Texture2D>(directory, "date.png"),
                showFpsImage = Load<Texture2D>(directory, "fps.png"),
                infoImage = Load<Texture2D>(directory, "info.png"),
                saveLogsImage = Load<Texture2D>(directory, "Save.png"),
                searchImage = Load<Texture2D>(directory, "search.png"),
                copyImage = Load<Texture2D>(directory, "copy.png"),
                closeImage = Load<Texture2D>(directory, "close.png"),
                buildFromImage = Load<Texture2D>(directory, "buildFrom.png"),
                systemInfoImage = Load<Texture2D>(directory, "ComputerIcon.png"),
                graphicsInfoImage = Load<Texture2D>(directory, "graphicCard.png"),
                backImage = Load<Texture2D>(directory, "back.png"),
                logImage = Load<Texture2D>(directory, "log_icon.png"),
                warningImage = Load<Texture2D>(directory, "warning_icon.png"),
                errorImage = Load<Texture2D>(directory, "error_icon.png"),
                barImage = Load<Texture2D>(directory, "bar.png"),
                buttonActiveImage = Load<Texture2D>(directory, "button_active.png"),
                evenLogImage = Load<Texture2D>(directory, "even_log.png"),
                oddLogImage = Load<Texture2D>(directory, "odd_log.png"),
                selectedImage = Load<Texture2D>(directory, "selected.png"),
                scrollerSkin = Load<GUISkin>(directory, "logConsoleScrollerSkin.guiskin"),
            };
            console.Icons = icons;
            EditorUtility.SetDirty(console);
        }

        private static T Load<T>(string directory, string fileName) where T : Object
        {
            return AssetDatabase.LoadAssetAtPath<T>(Path.Combine(directory, fileName).Replace('\\', '/'));
        }
    }
}
