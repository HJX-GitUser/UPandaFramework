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
        [Header("显示FPS")]
        [Tooltip("仅编辑器模式下可以显示")]
        public bool showFPS = false;

        public LogConfig logConfig;

        float deltaTime = 0.0f;
        GUIStyle mStyle;
        Rect rect = new Rect(0, 0, 500, 300);
        Rect buttonRect = new Rect(0, 0, 200, 50);
      

        private void Reset()
        {
#if UNITY_EDITOR && OPEN_PLOG
            LoadConfigFromFile();
#endif
        }
        void Awake()
        {
            mStyle = new GUIStyle();
            mStyle.alignment = TextAnchor.UpperLeft;
            mStyle.normal.background = null;
            mStyle.fontSize = 35;
            mStyle.normal.textColor = Color.white;
#if !UNITY_EDITOR
            showFPS = false;
#endif
        }

        void Update()
        {
            if (showFPS || UPGameRoot.Instance.EnableDebugModel) deltaTime += (Time.deltaTime - deltaTime) * 0.1f;
        }

        public IEnumerator Init()
        {
            yield return StartCoroutine(StreamingAssetsLoader.LoadTextFileAsync(configPath + fileName, (arg) =>
            {
                string configData = arg;
                if (configData != null)
                {
                    logConfig = JsonUtility.FromJson<LogConfig>(configData);
                    PLogger.InitLog(logConfig);
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

        void OnGUI()
        {
            if (showFPS || UPGameRoot.Instance.EnableDebugModel)
            {
                float fps = 1.0f / deltaTime;
                string text = string.Format(" FPS:{0:N0} ", fps);
                if (UPGameRoot.Instance.EnableDebugModel)
                {
                    if (UPGameRoot.Instance.reporter != null && !UPGameRoot.Instance.reporter.show)
                    {
                        if (GUI.Button(buttonRect, text))
                        {
                            UPGameRoot.Instance.reporter.ShowLogWindows();
                        }
                    }
                }
                else
                {
                    GUI.Label(rect, text, mStyle);
                }
                Rect appInfoRect = new Rect(Screen.width - 400, Screen.height - 30, 500, 300);
            }

        }
    }
}

