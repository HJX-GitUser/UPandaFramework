using System.Collections;
using UnityEngine;
using System;
using System.Threading.Tasks;
using System.IO;
using System.ComponentModel;

namespace UPandaGF
{
    public class DebugerInit : MonoBehaviour
    {
        private string configPath = "Data/";
        private string fileName = "LogConfig.json";
        public LogConfig logConfig;

        private void Reset()
        {
#if UNITY_EDITOR && OPEN_PLOG
            LoadConfigFromFile();
#endif
        }
        public IEnumerator Init()
        {
            yield return StartCoroutine(StreamingAssetsLoader.LoadTextFileAsync(configPath + fileName, (arg) =>
            {
                string configData = arg;
                if (configData != null)
                {
                    logConfig = JsonUtility.FromJson<LogConfig>(configData);
                    PLoger.InitLog(logConfig);
                }
            }));
        }

        public string GetConfigDateFullPath => StreamingAssetsLoader.CombinePath(configPath + fileName);
        public string GetConfigDateFilePath => StreamingAssetsLoader.CombinePath(configPath);


        private void LoadConfigFromFile()
        {
            try
            {
                string configPath = GetConfigDateFullPath;
                if (File.Exists(configPath))
                {
                    string jsonData = File.ReadAllText(configPath);
                    logConfig = JsonUtility.FromJson<LogConfig>(jsonData);
                }
                else
                {
                    if (!Directory.Exists(GetConfigDateFilePath))
                    {
                        Directory.CreateDirectory(GetConfigDateFilePath);
                    }
                    logConfig = new LogConfig();
                    string json = JsonUtility.ToJson(logConfig);
                    File.WriteAllText(GetConfigDateFullPath, json);
                    Debug.Log("日志配置已保存：" + GetConfigDateFullPath);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"加载日志配置失败: {e.Message}");
            }
        }
    }
}

