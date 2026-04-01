using System.IO;
using UnityEditor;
using UnityEngine;

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
            string fullPath = Application.dataPath + $"/{fileName}";
            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
                Debug.Log($"{fileName}目录创建完成");
            }
            else
            {
                Debug.Log($"{fileName}目录已存在");
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

            if (GameObject.FindObjectOfType<GameLaunchExample>() != null)
            {
                return;
            }
            GameObject obj2 = new GameObject("OnLoadCompleteExample");
            obj2.AddComponent<GameLaunchExample>();
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

