
using System;
using UnityEngine;

/// <summary>
/// 日志系统配置
/// </summary>
[CreateAssetMenu(fileName = "日志系统配置", menuName = "UPanda/Create DebugConfig", order = 0)]
public class LogConfig : ScriptableObject
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
    public string logFileSavePath { get { return Application.persistentDataPath + "/Output Log/"; } }
    [Header("日志文件名称")]
    public string logFileName { get { return Application.productName + " " + DateTime.Now.ToString("yyyy-MM-dd HH-mm") + ".log"; } }

}
