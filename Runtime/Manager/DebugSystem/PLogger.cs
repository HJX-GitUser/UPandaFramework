using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>
    /// 框架日志门面。
    ///
    /// 编译期剔除：普通日志 / Warning 带 [Conditional("OPEN_PLOG")]，未定义该宏时调用点会被编译器整体移除
    /// （连实参表达式都不求值），因此关闭日志是零运行时开销的。
    /// 错误日志例外：LogError / LogErrorFormat / LogException 不参与编译期剔除，
    /// 避免"监听者异常被隔离、下载失败被降级，但线上没有任何错误日志"。
    /// 宏开关：菜单 UPandaGF -> 日志系统 -> 启动日志 / 剔除日志（对 Standalone/iOS/Android/WebGL 四个平台组生效）。
    ///
    /// 运行时开关：<see cref="cfg"/> 里的 openLog / openWarning / openError / maxLogLength 等字段，
    /// 配置来自 StreamingAssets/Data/LogConfig.json（由 DebugerInit 读取后调用 InitLog 覆盖）。
    ///
    /// 注意：事件参数是 object，值类型会装箱；高频路径请先判断再拼字符串，或改用 Reporter 面板观察。
    /// </summary>
    public static class PLogger
    {
        /// <summary>
        /// 运行配置。**默认给一个实例**，保证"配置没加载 / 加载失败"时不会让整个日志系统静默失效
        /// （原实现 cfg 为 null 时所有 API 直接 return 且没有任何提示 —— 出包后"一条日志都没有"多半源于此）。
        /// 由 DebugerInit 成功读到 LogConfig.json 后通过 <see cref="InitLog"/> 覆盖。
        /// </summary>
        public static LogConfig cfg = new LogConfig();

        /// <summary>是否已由外部（DebugerInit）成功加载过配置。未定义 OPEN_PLOG 时恒为 false（此时日志调用本身已被剔除）</summary>
        public static bool IsInitialized { get; private set; }

        private const string NullText = "null";

        /// <summary>[ThreadStatic] 复用 StringBuilder：日志可能来自任意线程，避免每条日志都分配一个 StringBuilder</summary>
        [ThreadStatic]
        private static StringBuilder s_builder;

        #region 初始化

        [Conditional("OPEN_PLOG")]
        public static void InitLog(LogConfig _cfg = null)
        {
            cfg = _cfg == null ? new LogConfig() : _cfg;
            IsInitialized = true;

            Log("日志系统初始化...");
            if (!cfg.logSave) return;

            if (cfg.saveOverwrite) Log($"<color=yellow>日志覆盖:</color>{cfg.logFileSavePath}{cfg.LogFileName}");
            else Log($"<color=yellow>日志输出路径:</color>{cfg.logFileSavePath}{cfg.LogFileName}");

            // 修复：原实现这里没有任何异常保护，目录不可写/文件被占用会让 InitLog 直接中断
            try
            {
                GameObject logObj = new GameObject("LogHelper");
                GameObject.DontDestroyOnLoad(logObj);
                PLogHelper unityLogHelper = logObj.AddComponent<PLogHelper>();
                unityLogHelper.InitLogFileModule(cfg.logFileSavePath, cfg.LogFileName);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"[PLogger] 日志文件模块初始化失败，已跳过文件落盘：{e}");
            }
        }

        #endregion

        #region 级别开关

        private static bool CanLog { get { return cfg != null && cfg.openLog; } }
        private static bool CanWarning { get { return CanLog && cfg.openWarning; } }
        // Error 不随 openLog 总开关静默：只受 openError 控制（默认 true）。
        private static bool CanError { get { return cfg == null || cfg.openError; } }

        #endregion

        #region 普通日志

        [Conditional("OPEN_PLOG")]
        public static void Log(object obj)
        {
            if (!CanLog) return;
            UnityEngine.Debug.Log(GenerateLog(Safe(obj)));
        }
        [Conditional("OPEN_PLOG")]
        public static void Log_red(object obj)
        {
            if (!CanLog) return;
            UnityEngine.Debug.Log(GenerateLog($"<color=red>{Safe(obj)}</color>"));
        }
        [Conditional("OPEN_PLOG")]
        public static void Log_green(object obj)
        {
            if (!CanLog) return;
            UnityEngine.Debug.Log(GenerateLog($"<color=green>{Safe(obj)}</color>"));
        }
        [Conditional("OPEN_PLOG")]
        public static void Log_blue(object obj)
        {
            if (!CanLog) return;
            UnityEngine.Debug.Log(GenerateLog($"<color=blue>{Safe(obj)}</color>"));
        }
        [Conditional("OPEN_PLOG")]
        public static void Log_yellow(object obj)
        {
            if (!CanLog) return;
            UnityEngine.Debug.Log(GenerateLog($"<color=yellow>{Safe(obj)}</color>"));
        }
        [Conditional("OPEN_PLOG")]
        public static void Log_white(object obj)
        {
            if (!CanLog) return;
            UnityEngine.Debug.Log(GenerateLog($"<color=white>{Safe(obj)}</color>"));
        }
        [Conditional("OPEN_PLOG")]
        public static void Log_cyan(object obj)
        {
            if (!CanLog) return;
            UnityEngine.Debug.Log(GenerateLog($"<color=cyan>{Safe(obj)}</color>"));
        }

        /// <summary>带格式化的日志。注意：先格式化再拼前缀（原实现把前缀拼进格式串再交给 Debug.LogFormat，
        /// 前缀里若含 { } 或 {0} 会抛 FormatException / 被参数替换）。</summary>
        [Conditional("OPEN_PLOG")]
        public static void LogFormat(object obj, params object[] args)
        {
            if (!CanLog) return;
            UnityEngine.Debug.Log(GenerateLog(Format(Safe(obj), args)));
        }

        [Conditional("OPEN_PLOG")]
        public static void LogWarning(object obj)
        {
            if (!CanWarning) return;
            UnityEngine.Debug.LogWarning(GenerateLog(Safe(obj)));
        }

        [Conditional("OPEN_PLOG")]
        public static void LogWarningFormat(object obj, params object[] args)
        {
            if (!CanWarning) return;
            UnityEngine.Debug.LogWarning(GenerateLog(Format(Safe(obj), args)));
        }

        public static void LogError(object obj)
        {
            if (!CanError) return;
            UnityEngine.Debug.LogError(GenerateLog(Safe(obj)));
        }

        public static void LogErrorFormat(object obj, params object[] args)
        {
            if (!CanError) return;
            UnityEngine.Debug.LogError(GenerateLog(Format(Safe(obj), args)));
        }

        /// <summary>异常日志（字符串形式），与 Reporter 的 LogType.Exception 对应</summary>
        public static void LogException(object obj)
        {
            if (!CanError) return;
            UnityEngine.Debug.LogError(GenerateLog($"<color=orange>[Exception]</color> {Safe(obj)}"));
        }

        /// <summary>异常日志（Exception 对象，Unity 会附带完整堆栈）</summary>
        public static void LogException(Exception ex)
        {
            if (!CanError) return;
            if (ex == null)
            {
                LogException((object)null);
                return;
            }
            UnityEngine.Debug.LogException(ex);
        }

        #endregion

        #region 可注入日志的运行时入口（不做编译期剔除）

        /// <summary>
        /// 按级别输出，<b>不带</b> <c>[Conditional]</c>：即使未定义 OPEN_PLOG 也会保留调用。
        /// <para>供 <see cref="ILogService"/> 适配器（LogService）使用，保证注入日志"调了就有输出"。</para>
        /// <para>代价是调用点实参总会求值，因此每帧/高频路径仍应使用 <see cref="Log"/>/<see cref="LogWarning"/>。</para>
        /// </summary>
        public static void Write(LogLevel level, object obj)
        {
            switch (level)
            {
                case LogLevel.Info:
                    if (!CanLog) return;
                    UnityEngine.Debug.Log(GenerateLog(Safe(obj)));
                    break;
                case LogLevel.Warning:
                    if (!CanWarning) return;
                    UnityEngine.Debug.LogWarning(GenerateLog(Safe(obj)));
                    break;
                case LogLevel.Error:
                    if (!CanError) return;
                    UnityEngine.Debug.LogError(GenerateLog(Safe(obj)));
                    break;
                default:
                    if (!CanError) return;
                    UnityEngine.Debug.LogError(GenerateLog($"<color=orange>[Exception]</color> {Safe(obj)}"));
                    break;
            }
        }

        /// <summary>异常输出（保留原堆栈），运行时开关同 Error。</summary>
        public static void WriteException(Exception exception)
        {
            if (!CanError) return;
            if (exception == null) { Write(LogLevel.Exception, null); return; }
            UnityEngine.Debug.LogException(exception);
        }

        /// <summary>查询某级别当前是否会输出（运行时开关；不代表编译期宏状态）。</summary>
        public static bool IsEnabled(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Info: return CanLog;
                case LogLevel.Warning: return CanWarning;
                default: return CanError;
            }
        }

        #endregion

        #region 内部

        private static string Safe(object obj)
        {
            if (obj == null) return NullText;
            string text = obj.ToString();
            return text ?? NullText;
        }

        /// <summary>格式化：失败不抛异常（日志系统不应该因为一条写错的格式串把业务打断）</summary>
        private static string Format(string format, object[] args)
        {
            if (args == null || args.Length == 0) return format;
            try
            {
                return string.Format(format, args);
            }
            catch (FormatException)
            {
                return format + "  [PLogger] 格式化失败：格式串与参数不匹配";
            }
        }

        /// <summary>按 cfg.maxLogLength 截断超长日志（0 = 不限）</summary>
        private static string Clip(string log)
        {
            if (log == null) return NullText;
            int max = cfg != null ? cfg.maxLogLength : 0;
            if (max <= 0 || log.Length <= max) return log;
            return log.Substring(0, max) + $"...[已截断，原长 {log.Length}]";
        }

        private static string GenerateLog(string log)
        {
            StringBuilder sb = s_builder;
            if (sb == null)
            {
                sb = new StringBuilder(160);
                s_builder = sb;
            }
            else
            {
                sb.Length = 0;
            }

            if (cfg != null)
            {
                if (cfg.addHeadFix) sb.Append('<').Append(cfg.logHeadFix).Append(">  ");
                // 修复：原实现用 hh（12 小时制，且没有 AM/PM）—— 13:20 会显示成 01:20，排查时间线必然踩坑
                if (cfg.openTime) sb.Append(DateTime.Now.ToString("HH:mm:ss.fff")).Append("  ");
                if (cfg.showThreadID) sb.Append("ThreadID:").Append(Thread.CurrentThread.ManagedThreadId).Append('\n');
            }

            sb.Append(Clip(log));
            return sb.ToString();
        }

        #endregion
    }
}
