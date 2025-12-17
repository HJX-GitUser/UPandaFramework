using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UPandaGF
{
    public class FrameWorkInitEditor
    {
        private static string PATH_3rd = Application.dataPath + "/3rd";
        private static string PATH_ArtAssets = Application.dataPath + "/ArtAssets";
        private static string PATH_Resources = Application.dataPath + "/Resources";
        private static string PATH_Scenes = Application.dataPath + "/Scenes";
        private static string PATH_StreamingAssets = Application.dataPath + "/StreamingAssets";
        private static string PATH_Scripts = Application.dataPath + "/Scripts";
        private static string PATH_AssetBundle = Application.dataPath + "/AssetBundles";



        [MenuItem("UPandaGF/Tools/创建目录文件夹")]
        private static void CreatForder()
        {
            CreatForderPacking(PATH_3rd);
            CreatForderPacking(PATH_ArtAssets);
            CreatForderPacking(PATH_Resources);
            CreatForderPacking(PATH_Scenes);
            CreatForderPacking(PATH_StreamingAssets);
            CreatForderPacking(PATH_Scripts);
            CreatForderPacking(PATH_AssetBundle);
            AssetDatabase.Refresh();
        }
        private static void CreatForderPacking(string path)
        {
            string name = path.Substring(path.LastIndexOf('/') + 1);
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
                Debug.Log($"{name}目录创建完成");
            }
            else
            {
                Debug.Log($"{name}目录已存在");
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
    }
}

