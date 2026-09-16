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
            UPGameRoot root = UPGameRoot.Instance;
            if (root == null) return;   // 修复：该组件被放到没有 UPGameRoot 的场景里时空引用
            if (showFPS || root.Config.EnableDebugModel) deltaTime += (Time.deltaTime - deltaTime) * 0.1f;
        }

        public IEnumerator Init()
        {
            yield return StartCoroutine(StreamingAssetsLoader.LoadTextFileAsync(configPath + fileName, (arg) =>
            {
                // 修复：配置读不到 / 解析失败时不再让整个日志系统静默失效（PLogger.cfg 已有默认实例，这里再补一条明确告警）
                if (string.IsNullOrEmpty(arg))
                {
                    UnityEngine.Debug.LogWarning($"[PLogger] 未读到日志配置（{configPath + fileName}），使用默认配置。需要文件落盘请在 StreamingAssets/Data/LogConfig.json 中打开 logSave。");
                    PLogger.InitLog(logConfig);
                    return;
                }

                LogConfig parsed = JsonUtility.FromJson<LogConfig>(arg);
                if (parsed == null)
                {
                    UnityEngine.Debug.LogWarning($"[PLogger] 日志配置解析失败（{configPath + fileName}），使用默认配置。");
                    PLogger.InitLog(logConfig);
                    return;
                }

                logConfig = parsed;
                PLogger.InitLog(logConfig);
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
            UPGameRoot root = UPGameRoot.Instance;
            if (root == null) return;   // 修复：空引用保护
            if (!showFPS && !root.Config.EnableDebugModel) return;

            float fps = deltaTime > 0f ? 1.0f / deltaTime : 0f;   // 修复：deltaTime 为 0 时不再除零
            string text = string.Format(" FPS:{0:N0} ", fps);
            if (root.Config.EnableDebugModel)
            {
                if (root.reporter != null && !root.reporter.show)
                {
                    if (GUI.Button(buttonRect, text))
                    {
                        root.reporter.ShowLogWindows();
                    }
                }
            }
            else
            {
                GUI.Label(rect, text, mStyle);
            }
        }
    }
}

