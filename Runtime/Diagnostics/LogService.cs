using System;

namespace UPandaGF
{
    /// <summary>
    /// <see cref="ILogService"/> 的默认实现：把调用转给框架日志门面 <see cref="PLogger"/>。
    ///
    /// <para>与 PLogger 的关系：PLogger 的 Log/LogWarning 带 [Conditional("OPEN_PLOG")]，
    /// 未定义宏时调用点会被编译期剔除；本类走 PLogger.Write（非 Conditional），
    /// 因此"注入后的日志一定可观测"，代价是失去编译期零成本。高频路径请继续直接用 PLogger。</para>
    ///
    /// <para>线程：与 Unity Debug 一致，任意线程可调用；格式化用 PLogger 的线程内缓存。</para>
    /// <para>异常约定：任何日志失败都吞掉，绝不向业务抛异常。</para>
    /// </summary>
    public sealed class LogService : ILogService
    {
        /// <summary>无状态、可安全共享的默认实例；业务不需要自己 new。</summary>
        public static readonly LogService Default = new LogService();

        public bool IsEnabled(LogLevel level)
        {
            return PLogger.IsEnabled(level);
        }

        public void Log(LogLevel level, object message)
        {
            try { PLogger.Write(level, message); }
            catch (Exception) { /* 日志系统不能打断业务 */ }
        }

        public void LogException(Exception exception)
        {
            try { PLogger.WriteException(exception); }
            catch (Exception) { /* 日志系统不能打断业务 */ }
        }
    }
}
