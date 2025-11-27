using System;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;

namespace UPandaGF
{
    public class DebugerInit : MonoBehaviour
    {
        private string configPath = "Data/";
        private string fileName = "LogConfig.json";
        public LogConfig logConfig;

        public IEnumerator Init()
        {
            string filePath = Path.Combine(configPath, fileName);
            string url = UPGameRoot.GetStreamingAssetsPath() + "/" + filePath;
            UnityWebRequest request = UnityWebRequest.Get(url);
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                logConfig = JsonUtility.FromJson<LogConfig>(request.downloadHandler.text);
            }
            else
            {
                Debug.LogError("Failed to load debugConfig!  " + request.error);
            }
            PLoger.InitLog(logConfig);
        }
    }
}

