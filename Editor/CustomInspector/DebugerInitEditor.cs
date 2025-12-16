using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UPandaGF;

[CustomEditor(typeof(DebugerInit))]
public class DebugerInitEditor : Editor
{
    string savePath;
    private DebugerInit component;
    private LogConfig config;

    private void OnEnable()
    {
        EditorSceneManager.sceneSaved += OnSceneSaved;
        if (component == null)
        {
            component = target as DebugerInit;
        }
        if (config == null)
        {
#if OPEN_PLOG
            LoadConfigFromFile();
#endif
        }
    }
    private void LoadConfigFromFile()
    {
        try
        {
            string configPath = component.GetConfigDateFullPath;
            if (File.Exists(configPath))
            {
                string jsonData = File.ReadAllText(configPath);
                config = JsonUtility.FromJson<LogConfig>(jsonData);
                //Debug.Log("日志配置加载成功");
            }
            else
            {
                // 文件不存在时创建默认配置
                config = component.logConfig;
                Debug.LogWarning($"{configPath} 配置文件不存在，已创建默认配置");
                SaveConfig();
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"加载日志配置失败: {e.Message}");
        }
    }

    private void OnDisable()
    {
        EditorSceneManager.sceneSaved -= OnSceneSaved;
    }

    private void OnSceneSaved(Scene scene)
    {
#if OPEN_PLOG
        //Debug.Log($"{config.addHeadFix}:{component.logConfig.addHeadFix}");
        if (ShouldSaveConfig()) return;
        SaveConfig();
#endif
    }

    private void SaveConfig()
    {
        config = new LogConfig(component.logConfig);
        string data = JsonUtility.ToJson(config, true);
        savePath = component.GetConfigDateFilePath;
        if (!Directory.Exists(savePath))
        {
            Directory.CreateDirectory(savePath);
        }
        File.WriteAllText(component.GetConfigDateFullPath, data);
        Debug.Log("日志配置已保存：" + savePath);
        AssetDatabase.Refresh();
    }

    private bool ShouldSaveConfig()
    {
        if (component == null || component.logConfig == null || config == null) return false;
        return component.logConfig.Equals(config);
    }
}
