using log4net.Util;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UPandaGF;

[CustomEditor(typeof(DebugerInit))]
public class DebugerInitEditor : Editor
{
    string savePath;
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        if (GUILayout.Button("保存配置"))
        {
            string data = JsonUtility.ToJson((target as DebugerInit).logConfig, true);
            savePath = Application.streamingAssetsPath + "/Data/";
            if (!Directory.Exists(savePath))
            {
                Directory.CreateDirectory(savePath);
            }
            savePath += "LogConfig.json";
            File.WriteAllText(savePath, data);
            Debug.Log("日志配置已保存：" + savePath);
            AssetDatabase.Refresh();
        }
    }

    
}
