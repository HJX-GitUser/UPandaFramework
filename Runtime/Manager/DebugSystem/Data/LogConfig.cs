
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
        [Header("是否添加日志前缀")]
        public bool addHeadFix = true;
        [Header("日志前缀")]
        public string logHeadFix = "###";
        [Header("是否显示时间")]
        public bool openTime = true;
        [Header("显示线程id")]
        public bool showThreadID = true;
        [Header("日志文件储存开关")]
        public bool logSave = false;
        [Header("文件储存路径")]
        public string logFileSavePath = "Output Log/";
       
        public string LogFileSavePath { get { return Application.persistentDataPath + "/" + logFileSavePath; } }
        public string LogFileName { get { return Application.productName + " " + DateTime.Now.ToString("yyyy-MM-dd HH-mm") + ".log"; } }

        public LogConfig() { }
        public LogConfig(LogConfig arg)
        {
            openLog = arg.openLog;
            addHeadFix = arg.addHeadFix;
            logHeadFix = arg.logHeadFix;
            openTime = arg.openTime;
            showThreadID = arg.showThreadID;
            logSave = arg.logSave;
            logFileSavePath = arg.logFileSavePath;
        }

        public bool Equals(LogConfig other)
        {
            if (!(other is LogConfig config)) return false;

            return (openLog, addHeadFix, logHeadFix, openTime, showThreadID, logSave, logFileSavePath)
                .Equals((other.openLog, other.addHeadFix, other.logHeadFix, other.openTime,
                        other.showThreadID, other.logSave, other.logFileSavePath));
        }


        public override bool Equals(object obj)
        {
            if (ReferenceEquals(this, obj)) return true;//引用相等性检查
            if (!(obj is LogConfig config)) return false;
            return Equals(config);
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

        public static bool operator ==(LogConfig left, LogConfig right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null) return false;
            return left.Equals(right);
        }

        public static bool operator !=(LogConfig left, LogConfig right) => !(left == right);
    }

}

