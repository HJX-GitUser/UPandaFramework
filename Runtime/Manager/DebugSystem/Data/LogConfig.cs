using System;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>
    /// 日志系统配置（对应 StreamingAssets/Data/LogConfig.json，由 DebugerInit 读取后交给 PLogger.InitLog）。
    ///
    /// 2026-09 补充：openWarning / openError 分级开关、maxLogLength 单条截断；
    /// 并把 GetHashCode 与 Equals 对齐（原实现 Equals 比字段、GetHashCode 用引用哈希，
    /// 违反"相等对象哈希相同"的契约，放进 Dictionary/HashSet 会失效）。
    /// </summary>
    [Serializable]
    public class LogConfig
    {
        [Header("日志总开关（false 时下面各级也一起静默）")]
        public bool openLog = true;
        [Header("是否输出 Warning")]
        public bool openWarning = true;
        [Header("是否输出 Error / Exception")]
        public bool openError = true;
        [Header("是否添加日志前缀")]
        public bool addHeadFix = true;
        [Header("日志前缀")]
        public string logHeadFix = "###";
        [Header("是否显示时间")]
        public bool openTime = true;
        [Header("显示线程id")]
        public bool showThreadID = true;
        [Header("单条日志最大长度（0 = 不限，超出部分截断）")]
        public int maxLogLength = 0;
        [Header("日志储存到本地")]
        public bool logSave = false;
        [Header("日志储存覆盖")]
        public bool saveOverwrite = false;
        [Header("文件储存路径")]
        public string logFileSavePath = "Output Log/";

        /// <summary>文件落盘目录（完整路径，位于 persistentDataPath 下）</summary>
        public string LogFileSavePath { get { return Application.persistentDataPath + "/" + logFileSavePath; } }

        /// <summary>日志文件名（saveOverwrite 为 false 时带时间戳）</summary>
        public string LogFileName { get { return saveOverwrite ? Application.productName + ".log" : Application.productName + " " + DateTime.Now.ToString("yyyy-MM-dd HH-mm") + ".log"; } }

        public LogConfig() { }

        public LogConfig(LogConfig arg)
        {
            if (arg == null) return;
            openLog = arg.openLog;
            openWarning = arg.openWarning;
            openError = arg.openError;
            addHeadFix = arg.addHeadFix;
            logHeadFix = arg.logHeadFix;
            openTime = arg.openTime;
            showThreadID = arg.showThreadID;
            maxLogLength = arg.maxLogLength;
            logSave = arg.logSave;
            saveOverwrite = arg.saveOverwrite;
            logFileSavePath = arg.logFileSavePath;
        }

        public bool Equals(LogConfig other)
        {
            if (!(other is LogConfig config)) return false;

            return (openLog, openWarning, openError, addHeadFix, logHeadFix, openTime, showThreadID, maxLogLength, logSave, saveOverwrite, logFileSavePath)
                .Equals((config.openLog, config.openWarning, config.openError, config.addHeadFix, config.logHeadFix, config.openTime,
                        config.showThreadID, config.maxLogLength, config.logSave, config.saveOverwrite, config.logFileSavePath));
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(this, obj)) return true;//引用相等性检查
            if (!(obj is LogConfig config)) return false;
            return Equals(config);
        }

        /// <summary>与 Equals 使用同一组字段（原实现返回 base.GetHashCode()，与 Equals 不一致）</summary>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + openLog.GetHashCode();
                hash = hash * 31 + openWarning.GetHashCode();
                hash = hash * 31 + openError.GetHashCode();
                hash = hash * 31 + addHeadFix.GetHashCode();
                hash = hash * 31 + (logHeadFix != null ? logHeadFix.GetHashCode() : 0);
                hash = hash * 31 + openTime.GetHashCode();
                hash = hash * 31 + showThreadID.GetHashCode();
                hash = hash * 31 + maxLogLength.GetHashCode();
                hash = hash * 31 + logSave.GetHashCode();
                hash = hash * 31 + saveOverwrite.GetHashCode();
                hash = hash * 31 + (logFileSavePath != null ? logFileSavePath.GetHashCode() : 0);
                return hash;
            }
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
