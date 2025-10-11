using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;

namespace UPandaGF
{
    public class DebugerInit : MonoBehaviour
    {
        private string configPath = "/Data/";
        private string fileNmae = "Ploger.json";
        public LogConfig logConfig;
        public void Init(UnityAction callback)
        {
            string p = Path.Combine(configPath, fileNmae);
            LoadJson(p, callback);
        }


        private void LoadJson(string filePath, UnityAction callback)
        {
            StartCoroutine(LoadJsonDataAsync(filePath, callback));
        }

        IEnumerator LoadJsonDataAsync(string filePath, UnityAction callback)
        {
            string url = "file://" + Application.streamingAssetsPath + filePath;
            Debug.Log(url);

            UnityWebRequest request = UnityWebRequest.Get(url);
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                // 反序列化 JSON 数据
                Debug.Log("反序列化 JSON 数据");
                logConfig = JsonUtility.FromJson<LogConfig>(request.downloadHandler.text);
                Debug.Log(logConfig.logHeadFix);
            }
            else
            {
                Debug.LogError("Failed to load debugConfig!  " + request.error);
            }
            PLoger.InitLog(logConfig);
            callback?.Invoke();
        }
    }
}

