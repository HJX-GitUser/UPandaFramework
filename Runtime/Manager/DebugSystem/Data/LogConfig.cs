
using System;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>
    /// 日志系统配置
    /// </summary>
    [Serializable]
    public class LogConfig
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
        public string LogFileSavePath { get { return Application.persistentDataPath + "/" + logFileSavePath; } }
        //[Header("日志文件名称")]
        public string LogFileName { get { return Application.productName + " " + DateTime.Now.ToString("yyyy-MM-dd HH-mm") + ".log"; } }

    }
}

