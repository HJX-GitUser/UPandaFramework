using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UPandaGF.GFEditor
{
    /// <summary>
    /// 0.6.0 迁移辅助：删除场景中旧日志面板残留对象，并为开启调试模式的根节点创建新控制台。
    /// <para>只处理名为 <c>Reporter</c> 且存在 Missing Script 的对象，执行前有确认与 Undo。</para>
    /// </summary>
    public static class LogConsoleMigration
    {
        [MenuItem("UPandaGF/Tools/迁移日志面板（清理旧对象）")]
        public static void MigrateOpenScenes()
        {
            var roots = new List<GameObject>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded) roots.AddRange(scene.GetRootGameObjects());
            }

            var oldObjects = new List<GameObject>();
            foreach (GameObject root in roots)
            {
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                {
                    if (child.name != "Reporter") continue;
                    bool missingScript = false;
                    foreach (Component component in child.GetComponents<Component>())
                    {
                        if (component == null) { missingScript = true; break; }
                    }
                    if (missingScript) oldObjects.Add(child.gameObject);
                }
            }

            if (oldObjects.Count == 0)
            {
                Debug.Log("[LogConsoleMigration] 未找到需要清理的旧日志面板对象。");
                return;
            }
            if (!EditorUtility.DisplayDialog(
                "迁移日志面板",
                "找到 " + oldObjects.Count + " 个旧日志面板对象（含 Missing Script）。\n" +
                "将删除这些对象，并在开启调试模式的 UPGameRoot 下创建 UPandaLogConsole。继续？",
                "迁移", "取消"))
                return;

            foreach (GameObject item in oldObjects) Undo.DestroyObjectImmediate(item);

            int created = 0;
            foreach (GameObject root in roots)
            {
                UPGameRoot gameRoot = root.GetComponent<UPGameRoot>();
                if (gameRoot == null || gameRoot.logConsole != null || !gameRoot.Config.EnableDebugModel) continue;
                DebugerInit debugerInit = gameRoot.GetComponentInChildren<DebugerInit>(true);
                if (debugerInit == null) continue;

                var consoleObject = new GameObject("LogConsole");
                consoleObject.transform.SetParent(debugerInit.transform, false);
                var console = consoleObject.AddComponent<UPandaLogConsole>();
                LogConsoleIconLoader.Assign(console);
                Undo.RegisterCreatedObjectUndo(consoleObject, "Create Log Console");
                gameRoot.logConsole = console;
                EditorUtility.SetDirty(gameRoot);
                created++;
            }

            Debug.Log("[LogConsoleMigration] 已删除 " + oldObjects.Count + " 个旧对象，创建 " + created + " 个日志控制台。");
        }
    }
}
