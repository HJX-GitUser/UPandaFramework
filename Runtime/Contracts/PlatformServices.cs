using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

namespace UPandaGF
{
    // ==========================================================================================
    // 平台能力契约：日志 / HTTP / 对象池
    //
    // 为什么单独一个文件：这三类能力此前完全在服务容器之外（PLogger 静态门面、
    // HttpManager 单例、GameObjectPoolMgr 单例），业务同时存在"服务接口"和"单例"两条取用路径。
    // 补齐契约后，业务只依赖 UPandaGF.Contracts；具体实现由 GameServices 在启动时装配。
    //
    // 程序集约束：本程序集不能引用 UPandaGF.Runtime。因此这里的接口不能出现
    // HttpManager / GenericHttpPack<T> / GameObjectPoolMgr 等运行时类型，只能用 Task +
    // 本文件定义的抽象表达。
    // ==========================================================================================

    #region 日志

    /// <summary>日志级别。与 <see cref="ILogService"/> 配合，便于实现按级别过滤与测试替身。</summary>
    public enum LogLevel
    {
        Info,
        Warning,
        Error,
        Exception
    }

    /// <summary>
    /// 可注入的日志服务。
    /// <para>与静态 <c>PLogger</c> 的分工：PLogger 的 Log/LogWarning 带 <c>[Conditional("OPEN_PLOG")]</c>，
    /// 未定义宏时调用点会被编译期整体剔除，适合热路径；ILogService 不参与编译期剔除，
    /// 适合需要"始终可观测、可替换、可在测试中断言"的模块。</para>
    /// <para>实现必须保证：任何级别失败都不能向调用方抛异常（日志系统不能打断业务）。</para>
    /// </summary>
    public interface ILogService
    {
        bool IsEnabled(LogLevel level);

        /// <summary>按级别输出。message 允许为 null（实现自行转成 "null"）。</summary>
        void Log(LogLevel level, object message);

        /// <summary>输出异常（实现应尽量保留堆栈）。</summary>
        void LogException(Exception exception);
    }

    /// <summary>ILogService 的便捷扩展：把常用级别收敛成 Info/Warning/Error。</summary>
    public static class LogServiceExtensions
    {
        public static void Info(this ILogService log, object message) { if (log != null) log.Log(LogLevel.Info, message); }
        public static void Warning(this ILogService log, object message) { if (log != null) log.Log(LogLevel.Warning, message); }
        public static void Error(this ILogService log, object message) { if (log != null) log.Log(LogLevel.Error, message); }
    }

    #endregion

    #region HTTP

    /// <summary>单次 HTTP 请求的可选参数。为 null 时使用实现默认值。</summary>
    public sealed class HttpRequestOptions
    {
        /// <summary>失败后的重试次数（不含首次），默认 3。4xx/非幂等方法的自动重试规则由实现决定。</summary>
        public int RetryCount = 3;

        /// <summary>是否允许对 POST/PUT 这类非幂等请求自动重试（默认 false）。</summary>
        public bool RetryNonIdempotent;

        /// <summary>超时秒数，默认 15；小于等于 0 表示使用实现默认值。</summary>
        public int TimeoutSeconds = 15;

        /// <summary>重试前等待秒数，默认 0.5。</summary>
        public float RetryDelay = 0.5f;

        /// <summary>本次请求附加的请求头；与全局头同名的以本次为准。</summary>
        public IReadOnlyDictionary<string, string> Headers;
    }

    /// <summary>
    /// HTTP 调用结果。失败不抛异常（取消除外，取消以 TaskCanceled 表达），
    /// 调用方通过 <see cref="Success"/> 与 <see cref="Error"/> 区分。
    /// </summary>
    public readonly struct HttpResult<T> where T : class
    {
        public bool Success { get; }
        public T Data { get; }
        public int StatusCode { get; }
        public string Error { get; }

        private HttpResult(bool success, T data, int statusCode, string error)
        {
            Success = success;
            Data = data;
            StatusCode = statusCode;
            Error = error;
        }

        public static HttpResult<T> Ok(T data, int statusCode) { return new HttpResult<T>(true, data, statusCode, null); }
        public static HttpResult<T> Fail(string error, int statusCode = 0) { return new HttpResult<T>(false, null, statusCode, error); }

        public override string ToString()
        {
            return Success ? $"HTTP {StatusCode} OK" : $"HTTP {StatusCode} 失败：{Error}";
        }
    }

    /// <summary>
    /// HTTP 服务。所有回调与结果都在 Unity 主线程完成；返回的 Task 支持取消。
    /// <para>T 为 string 时返回响应原文；其它类型由实现做 JSON 反序列化。</para>
    /// <para>高阶能力（逐帧进度、OnRetry、自定义数据包）仍然只在具体 HttpManager 上提供，
    /// 契约层只覆盖业务最常用的请求面。</para>
    /// </summary>
    public interface IHttpService
    {
        IReadOnlyDictionary<string, string> GlobalHeaders { get; }

        void SetGlobalHeader(string key, string value);
        bool RemoveGlobalHeader(string key);
        void ClearGlobalHeaders();

        /// <summary>并发上限：1 = 串行（默认）；大于 1 时同时发送多个请求。</summary>
        int MaxConcurrentRequests { get; set; }

        /// <summary>取消全部请求：清空等待队列并中断进行中的请求（取消不触发失败回调）。</summary>
        void CancelAll();

        Task<HttpResult<T>> GetAsync<T>(string url, IReadOnlyDictionary<string, string> query = null,
            HttpRequestOptions options = null, CancellationToken token = default) where T : class;

        Task<HttpResult<T>> PostAsync<T>(string url, object body = null,
            HttpRequestOptions options = null, CancellationToken token = default) where T : class;

        Task<HttpResult<T>> PutAsync<T>(string url, object body = null,
            HttpRequestOptions options = null, CancellationToken token = default) where T : class;

        Task<HttpResult<T>> DeleteAsync<T>(string url, IReadOnlyDictionary<string, string> query = null,
            HttpRequestOptions options = null, CancellationToken token = default) where T : class;
    }

    #endregion

    #region 对象池

    /// <summary>
    /// 单个对象池的公开能力。由具体管理器实现；生命周期由创建者（根作用域或场景作用域）拥有。
    /// <para>所有方法必须在 Unity 主线程调用；取/还是异步加载与实例化的唯一入口。</para>
    /// </summary>
    public interface IPool
    {
        /// <summary>取一个实例：优先复用空闲实例，没有则加载预制体并创建。失败/超时返回 null。</summary>
        Task<GameObject> GetObjAsync(string path, AssetLoadMethod method = AssetLoadMethod.Resources);

        /// <summary>回调形式（与 Task 形式等价），加载失败以 null 回调。</summary>
        void GetObjAsync(string path, UnityAction<GameObject> callback, AssetLoadMethod method = AssetLoadMethod.Resources);

        /// <summary>以原路径归还实例。重复归还、跨池归还、代次不一致会被拒绝并记录。</summary>
        void PushObj(string path, GameObject instance);

        /// <summary>预热：在容量允许时提前创建并放回空闲池。</summary>
        Task Prewarm(string path, int count, AssetLoadMethod method = AssetLoadMethod.Resources);

        /// <summary>每个路径的空闲实例上限；0 表示不限。</summary>
        int MaxCountPerKey { get; set; }

        /// <summary>当前空闲实例总数。</summary>
        int TotalPooledCount { get; }

        /// <summary>清空空闲实例并推进代次；之后仍可继续使用该池。</summary>
        void Clear();
    }

    /// <summary>
    /// 对象池服务：负责创建"根生命周期池"和"作用域池"。
    /// <para>为什么是工厂而不是一个全局池：池的价值在于生命周期与场景/流程对齐，
    /// 全局池会让跨场景实例泄漏；需要跨场景复用的场景可以用 <see cref="Shared"/>。</para>
    /// </summary>
    public interface IPoolService
    {
        /// <summary>与整个应用（服务容器）同生命周期的共享池。</summary>
        IPool Shared { get; }

        /// <summary>创建一个由作用域拥有的池：作用域释放时销毁空闲实例和已借出实例。</summary>
        IPool CreatePool(ILifetimeScope scope);
    }

    #endregion
}
