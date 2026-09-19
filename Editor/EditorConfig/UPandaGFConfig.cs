using System.Text;
using UnityEditor;
using UnityEngine;
using System.IO;

namespace UPandaGF.GFEditor
{
    public static class UPandaGFConfig
    {
        public static string configPath = "Assets/Editor/EditorConfig/";


        public static void SaveJsonConfig(object config, string fileName)
        {
            try
            {
                string json = JsonUtility.ToJson(config, true);
                if (!Directory.Exists(configPath))
                {
                    Directory.CreateDirectory(configPath);
                    AssetDatabase.Refresh();  // 重要：刷新 AssetDatabase
                }
                string fullPath = Path.Combine(configPath, fileName + ".json");
                File.WriteAllText(fullPath, json, Encoding.UTF8);
                AssetDatabase.Refresh();
                //Debug.Log($"配置已保存: {fullPath}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"保存配置失败: {e.Message}");
            }
        }

        public static T LoadJsonConfig<T>(string fileName) where T : class, new()
        {
            T config = new T();
            string fullPath = Path.Combine(configPath, fileName + ".json");
            if (File.Exists(fullPath))
            {
                try
                {
                    string json = File.ReadAllText(fullPath, Encoding.UTF8);
                    config = JsonUtility.FromJson<T>(json);
                    //Debug.Log($"配置已加载: {fullPath}");
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"加载配置失败: {e.Message}");
                }
            }
            return config;
        }
    }

}
