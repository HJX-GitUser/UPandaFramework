using System.Collections;
using UnityEngine;
using System;
using System.Threading.Tasks;

namespace UPandaGF
{
    public class DebugerInit : MonoBehaviour
    {
        private string configPath = "Data/";
        private string fileName = "LogConfig.json";
        public LogConfig logConfig;

        public IEnumerator Init()
        {
            yield return StartCoroutine(StreamingAssetsLoader.LoadTextFile(configPath + fileName, (arg) =>
            {
                string configData = arg;
                if (configData != null)
                {
                    logConfig = JsonUtility.FromJson<LogConfig>(configData);
                    PLoger.InitLog(logConfig);
                }
            }));
        }

       
    }
}

