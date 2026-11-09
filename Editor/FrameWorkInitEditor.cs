using System.IO;
using UnityEditor;
using UnityEngine;
using UPandaGF.GFEditor;

namespace UPandaGF
{
    public class FrameWorkInitEditor
    {
        private static string[] fileName = new string[]
        {
             "3rd",
             "ArtAssets",
             "AssetBundles",
             "Plugins",
             "Resources",
             "Scenes",
             "Scripts",
             "StreamingAssets"
        };


        [MenuItem("UPandaGF/Tools/创建常用目录文件夹")]
        private static void CreatForder()
        {
            foreach (string item in fileName)
            {
                CreatForderPacking(item);
            }
            AssetDatabase.Refresh();
        }
        private static void CreatForderPacking(string fileName)
        {
            // 走统一工具：逐级创建 + 生成 .meta，父目录缺失也能建出来
            string folder = "Assets/" + fileName;
            EditorAssetPathUtility.EnsureFolder(folder);

            if (AssetDatabase.IsValidFolder(folder))
            {
                Debug.Log($"{fileName}目录创建完成（{folder}）");
            }
            else
            {
                Debug.LogError($"{fileName}目录创建失败（{folder}）");
            }
        }

        [MenuItem("GameObject/UPandaGF/创建UPGameRoot")]
        [MenuItem("UPandaGF/创建UPGameRoot")]
        private static void CreatUPGameRoot()
        {
            if (GameObject.FindObjectOfType<UPGameRoot>() != null)
            {
                Debug.Log("场景中已存在带有MyComponent组件的对象，取消创建");
                return;
            }
            GameObject obj = new GameObject("UPGameRoot");
            obj.AddComponent<UPGameRoot>();

            // 启动示例（GameLaunchExample）在 Samples~/Startup 中，不再默认挂到场景；
            // 需要时从 Package Manager 导入 “Startup” 样例后自行添加。
            Debug.Log("已创建 UPGameRoot。启动示例见 Samples~/Startup（Package Manager 导入 Startup 样例）。");
        }


        [MenuItem("UPandaGF/Tools/Clean Missing Scripts in Prefab")]
        static void CleanMissingScripts()
        {
            Transform select = Selection.activeTransform;
            if(select == null)
            {
                Debug.Log("需要选中一个预制体");
                return;
            }
            if (!EditorUtility.DisplayDialog("清理 Missing Scripts",
                "将移除选中物体及其子物体上缺失脚本的组件。\n\n选中：" + select.name
                + "\n\n该操作会修改场景/预制体，建议先提交或备份。", "继续", "取消"))
                return;

            Undo.RegisterFullObjectHierarchyUndo(select.gameObject, "Clean Missing Scripts");
            try
            {
                ProcessPrefab(select);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"处理失败 错误信息: {e.Message}");
            }
            AssetDatabase.SaveAssets();
            Debug.Log("清理完成！");
        }

        private static void ProcessPrefab(Transform parent)
        {
            foreach (Transform child in parent.GetComponentsInChildren<Transform>())
            {
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(child.gameObject);
            }
        }
    }
}

