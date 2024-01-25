using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public class CreatDirectory
{
    private static string PATH_3rd = Application.dataPath + "/3rd";
    private static string PATH_ArtAssets = Application.dataPath + "/ArtAssets";
    private static string PATH_Resources = Application.dataPath + "/Resources";
    private static string PATH_Scenes = Application.dataPath + "/Scenes";
    private static string PATH_StreamingAssets = Application.dataPath + "/StreamingAssets";
    private static string PATH_Scripts = Application.dataPath + "/Scripts";
    private static string PATH_AssetBundle = Application.dataPath + "/AssetBundle";



    [MenuItem("Tools/创建目录文件夹")]
    private static void CreatForder()
    {
        #region 旧
        //if (!Directory.Exists(PATH_3rd))
        //{
        //    Directory.CreateDirectory(PATH_3rd);
        //    Debug.Log("第三方插件和资源目录3rd创建完成");
        //}
        //else
        //{
        //    Debug.Log("3rd文件目录已存在");
        //}
        //if (!Directory.Exists(PATH_ArtAssets))
        //{
        //    Directory.CreateDirectory(PATH_ArtAssets);
        //    Debug.Log("艺术资源目录ArtAssets创建完成");
        //}
        //else
        //{
        //    Debug.Log("ArtAssets文件目录已存在");
        //}

        //if (!Directory.Exists(PATH_Resources))
        //{
        //    Directory.CreateDirectory(PATH_Resources);
        //    Debug.Log("Resources目录创建完成");
        //}
        //else
        //{
        //    Debug.Log("Resources文件目录已存在");
        //}
        //if (!Directory.Exists(PATH_Scenes))
        //{
        //    Directory.CreateDirectory(PATH_Scenes);
        //    Debug.Log("Scene目录创建完成");
        //}
        //else
        //{
        //    Debug.Log("Scenes文件目录已存在");
        //}
        //if (!Directory.Exists(PATH_StreamingAssets))
        //{
        //    Directory.CreateDirectory(PATH_StreamingAssets);
        //    Debug.Log("StreamingAssets目录创建完成");
        //}
        //else
        //{
        //    Debug.Log("StreamingAssets文件目录已存在");
        //}
        //if (!Directory.Exists(PATH_Scripts))
        //{
        //    Directory.CreateDirectory(PATH_Scripts);
        //    Debug.Log("脚本目录创建完成");
        //}
        //else
        //{
        //    Debug.Log("Scripts文件目录已存在");
        //}
        //if (!Directory.Exists(PATH_AssetBundle))
        //{
        //    Directory.CreateDirectory(PATH_AssetBundle);
        //    Debug.Log("AssetBundle目录创建完成");
        //}
        //else
        //{
        //    Debug.Log("AssetBundle文件目录已存在");
        //}

        #endregion
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
}
