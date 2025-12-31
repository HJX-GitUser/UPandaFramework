using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace UPandaGF
{
    public static class PLogger
    {
        public static LogConfig cfg;
        [Conditional("OPEN_PLOG")]
        public static void InitLog(LogConfig _cfg = null)
        {
            cfg = _cfg == null ? new LogConfig() : _cfg;
            Log("日志系统初始化...");
            if (cfg.logSave)
            {
                GameObject logObj = new GameObject("LogHelper");
                GameObject.DontDestroyOnLoad(logObj);
                PLogHelper unityLogHelper = logObj.AddComponent<PLogHelper>();
                unityLogHelper.InitLogFileModule(cfg.logFileSavePath, cfg.LogFileName);
            }
        }

        #region 普通日志
        [Conditional("OPEN_PLOG")]
        public static void Log(object obj)
        {
            if (cfg == null || !cfg.openLog) return;
            UnityEngine.Debug.Log(GenerateLog(obj.ToString()));
        }
        [Conditional("OPEN_PLOG")]
        public static void Log_red(object obj)
        {
            if (cfg == null || !cfg.openLog) return;
            UnityEngine.Debug.Log(GenerateLog($"<color=red>{obj.ToString()}</color>"));
        }
        [Conditional("OPEN_PLOG")]
        public static void Log_green(object obj)
        {
            if (cfg == null || !cfg.openLog) return;
            UnityEngine.Debug.Log(GenerateLog($"<color=green>{obj.ToString()}</color>"));
        }
        [Conditional("OPEN_PLOG")]
        public static void Log_blue(object obj)
        {
            if (cfg == null || !cfg.openLog) return;
            UnityEngine.Debug.Log(GenerateLog($"<color=blue>{obj.ToString()}</color>"));
        }
        [Conditional("OPEN_PLOG")]
        public static void Log_yellow(object obj)
        {
            if (cfg == null || !cfg.openLog) return;
            UnityEngine.Debug.Log(GenerateLog($"<color=yellow>{obj.ToString()}</color>"));
        }
        [Conditional("OPEN_PLOG")]
        public static void Log_white(object obj)
        {
            if (cfg == null || !cfg.openLog) return;
            UnityEngine.Debug.Log(GenerateLog($"<color=white>{obj.ToString()}</color>"));
        }
        [Conditional("OPEN_PLOG")]
        public static void Log_cyan(object obj)
        {
            if (cfg == null || !cfg.openLog) return;
            UnityEngine.Debug.Log(GenerateLog($"<color=cyan>{obj.ToString()}</color>"));
        }
        [Conditional("OPEN_PLOG")]
        public static void LogFormat(object obj, params object[] args)
        {
            if (cfg == null || !cfg.openLog) return;
            UnityEngine.Debug.LogFormat(GenerateLog(obj.ToString()), args);
        }
        [Conditional("OPEN_PLOG")]
        public static void LogWarning(object obj)
        {
            if (cfg == null || !cfg.openLog) return;
            UnityEngine.Debug.LogWarning(GenerateLog(obj.ToString()));
        }
        [Conditional("OPEN_PLOG")]
        public static void LogWarningFormat(object obj, params object[] args)
        {
            if (cfg == null || !cfg.openLog) return;
            UnityEngine.Debug.LogWarningFormat(GenerateLog(obj.ToString()), args);
        }
        [Conditional("OPEN_PLOG")]
        public static void LogError(object obj)
        {
            if (cfg == null || !cfg.openLog) return;
            UnityEngine.Debug.LogError(GenerateLog(obj.ToString()));
        }
        [Conditional("OPEN_PLOG")]
        public static void LogErrorFormat(object obj, params object[] args)
        {
            if (cfg == null || !cfg.openLog) return;
            UnityEngine.Debug.LogErrorFormat(GenerateLog(obj.ToString()), args);
        }
        #endregion

        private static string GenerateLog(string log)
        {
            StringBuilder stringBuilder = new StringBuilder(100);
            if (cfg.addHeadFix) stringBuilder.Append($"<{cfg.logHeadFix}>  ");
            if (cfg.openTime) stringBuilder.Append($"{DateTime.Now.ToString("hh:mm:ss-fff")}  ");
            if (cfg.showThreadID) stringBuilder.Append($"ThreadID:{Thread.CurrentThread.ManagedThreadId}\n");
            stringBuilder.Append(log);
            return stringBuilder.ToString();
        }
    }
}


