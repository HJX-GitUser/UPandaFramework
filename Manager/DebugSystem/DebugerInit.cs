using System;
using UnityEngine;
namespace UPandaGF
{
    public class DebugerInit : MonoBehaviour
    {
        [Header("是否打开日志系统")]
        public bool openLog = true;
        [Header("日志前缀")]
        public string logHeadFix = "###";
        [Header("是否显示时间")]
        public bool openTime = true;
        [Header("显示线程id")]
        public bool showThreadID = true;
        [Header("日志文件储存开关")]
        public bool logSave = false;
        [Header("是否显示FPS")]
        public bool showFPS = true;
        [Header("文件储存路径")]
        public string logFileSavePath = "Output Log/";
        public LogConfig logConfig;
        public void Init()
        {
            if(logConfig == null)
            {
                logConfig = new LogConfig();
            }
            logConfig.openLog = openLog;
            logConfig.logHeadFix = logHeadFix;
            logConfig.openTime = openTime;
            logConfig.showThreadID = showThreadID;
            logConfig.logSave = logSave;
            logConfig.showFPS = showFPS;
            logConfig.logFileSavePath = logFileSavePath;

            PLoger.InitLog(logConfig);
        }
    }
}

