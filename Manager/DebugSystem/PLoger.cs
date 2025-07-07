using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using UnityEngine;

public class PLoger
{
    public static LogConfig cfg;
    [Conditional("OPEN_PLOG")]
    public static void InitLog(LogConfig _cfg = null)
    {
        cfg = _cfg == null ? new LogConfig() : _cfg;
        if (cfg.logSave)
        {
            GameObject logObj = new GameObject("LogHelper");
            GameObject.DontDestroyOnLoad(logObj);
            PLogHelper unityLogHelper = logObj.AddComponent<PLogHelper>();
            unityLogHelper.InitLogFileModule(cfg.logFileSavePath, cfg.logFileName);
        }
        if (cfg.showFPS)
        {
            GameObject fpsObj = new GameObject("FPS");
            GameObject.DontDestroyOnLoad(fpsObj);
            fpsObj.AddComponent<ShowFPS>();
        }
    }

    #region ∆’Õ®»’÷æ
    [Conditional("OPEN_PLOG")]
    public static void Log(object obj)
    {
        if (!cfg.openLog) return;
        UnityEngine.Debug.Log(GenerateLog(obj.ToString()));
    }
    [Conditional("OPEN_PLOG")]
    public static void LogFormat(object obj, params object[] args)
    {
        if (!cfg.openLog) return;
        UnityEngine.Debug.LogFormat(GenerateLog(obj.ToString()), args);
    }
    [Conditional("OPEN_PLOG")]
    public static void LogWarning(object obj)
    {
        if (!cfg.openLog) return;
        UnityEngine.Debug.LogWarning(GenerateLog(obj.ToString()));
    }
    [Conditional("OPEN_PLOG")]
    public static void LogWarningFormat(object obj, params object[] args)
    {
        if (!cfg.openLog) return;
        UnityEngine.Debug.LogWarningFormat(GenerateLog(obj.ToString()), args);
    }
    [Conditional("OPEN_PLOG")]
    public static void LogError(object obj)
    {
        if (!cfg.openLog) return;
        UnityEngine.Debug.LogError(GenerateLog(obj.ToString()));
    }
    [Conditional("OPEN_PLOG")]
    public static void LogErrorFormat(object obj, params object[] args)
    {
        if (!cfg.openLog) return;
        UnityEngine.Debug.LogErrorFormat(GenerateLog(obj.ToString()), args);
    }
    #endregion


    private static string GenerateLog(string log)
    {
        StringBuilder stringBuilder = new StringBuilder($"<{cfg.logHeadFix}>", 100);
        if (cfg.openTime) stringBuilder.Append($" {DateTime.Now.ToString("hh:mm:ss-fff")}");
        if (cfg.showThreadID) stringBuilder.Append($" ThreadID:{Thread.CurrentThread.ManagedThreadId}\n");
        stringBuilder.Append(log);
        return stringBuilder.ToString();
    }
}
