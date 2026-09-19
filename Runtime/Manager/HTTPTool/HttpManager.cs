using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UPandaGF;

/// <summary>
/// HTTP 管理类：对外提供 GET / POST / PUT / DELETE 便捷接口，
/// 内部持有一个 HttpClient 负责请求排队与发送。
///
/// 特性：
///   - 懒加载单例：首次访问 Instance 时创建并 DontDestroyOnLoad，跨场景常驻
///   - 全局请求头：SetGlobalHeader 设置的头部会自动合并进之后发出的每个请求
///   - 泛型反序列化：JsonUtility 自动把响应体转成 T（T 为 string 时返回原文）
///   - 失败重试：默认失败重试 3 次，重试过程回调 OnRetry，彻底失败才回调 OnFailure
///   - 可取消：返回的数据包可 Cancel()，也可 CancelAll() 一次性取消
///   - 进度回调：为数据包设置 OnProgress 即可拿到 0~1 的下载进度
///
/// 所有回调都在主线程触发，因此可以直接在其中操作 Unity 对象。
/// </summary>
public class HttpManager : LazyMonoSingletonBase<HttpManager>
{
    private HttpClient _client;
    private readonly Dictionary<string, string> _globalHeaders = new Dictionary<string, string>();

    protected override void OnAwake()
    {
        EnsureClient();
    }

    /// <summary>内部客户端：可用于设置并发上限、取消全部请求等高级操作</summary>
    public HttpClient Client
    {
        get
        {
            EnsureClient();
            return _client;
        }
    }

    /// <summary>并发上限：1 = 串行（默认，按入队顺序逐个发送）；大于 1 时同时发送多个请求</summary>
    public int MaxConcurrentRequests
    {
        get { return Client.MaxConcurrentRequests; }
        set { Client.MaxConcurrentRequests = Mathf.Max(1, value); }
    }

    // ==================== 全局请求头 ====================

    /// <summary>
    /// 设置（或覆盖）全局请求头，例如 Token。
    /// 会与每个请求自身的 Headers 合并，同名的以请求自身为准。
    /// </summary>
    public void SetGlobalHeader(string key, string value)
    {
        if (string.IsNullOrEmpty(key)) return;

        _globalHeaders[key] = value ?? string.Empty;
    }

    /// <summary>移除某个全局请求头，返回是否移除成功</summary>
    public bool RemoveGlobalHeader(string key)
    {
        return !string.IsNullOrEmpty(key) && _globalHeaders.Remove(key);
    }

    /// <summary>清空所有全局请求头</summary>
    public void ClearGlobalHeaders()
    {
        _globalHeaders.Clear();
    }

    /// <summary>全局请求头（只读，便于调试查看）</summary>
    public IReadOnlyDictionary<string, string> GlobalHeaders
    {
        get { return _globalHeaders; }
    }

    // ==================== 便捷接口 ====================

    /// <summary>
    /// GET 请求：parameters 会 URL 编码后拼到查询串（可为 null）。
    /// 返回的数据包可用于 Cancel() 或查询状态；不需要时忽略返回值即可。
    /// </summary>
    public GenericHttpPack<T> Get<T>(string url, Dictionary<string, string> parameters,
        Action<T> onSuccess, Action<string> onFailure = null) where T : class
    {
        return Get<T>(url, parameters, (data, code) => onSuccess?.Invoke(data), onFailure);
    }

    /// <summary>GET 请求（成功回调额外带 HTTP 状态码）</summary>
    public GenericHttpPack<T> Get<T>(string url, Dictionary<string, string> parameters,
        Action<T, int> onSuccess, Action<string> onFailure = null) where T : class
    {
        return SendInternal(HttpRequestType.Get, BuildUrlWithParams(url, parameters), null, null, onSuccess, onFailure);
    }

    /// <summary>
    /// POST 请求：data 为 string 时视为已写好的 JSON 原文直接提交，
    /// 为 null 表示无请求体，其它类型由 JsonUtility.ToJson 序列化。
    /// </summary>
    public GenericHttpPack<T> Post<T>(string url, object data,
        Action<T> onSuccess, Action<string> onFailure = null) where T : class
    {
        return Post<T>(url, data, (result, code) => onSuccess?.Invoke(result), onFailure);
    }

    /// <summary>POST 请求（成功回调额外带 HTTP 状态码）</summary>
    public GenericHttpPack<T> Post<T>(string url, object data,
        Action<T, int> onSuccess, Action<string> onFailure = null) where T : class
    {
        return SendInternal(HttpRequestType.Post, url, BuildBody(data), null, onSuccess, onFailure);
    }

    /// <summary>PUT 请求（请求体规则同 Post）</summary>
    public GenericHttpPack<T> Put<T>(string url, object data,
        Action<T> onSuccess, Action<string> onFailure = null) where T : class
    {
        return Put<T>(url, data, (result, code) => onSuccess?.Invoke(result), onFailure);
    }

    /// <summary>PUT 请求（成功回调额外带 HTTP 状态码）</summary>
    public GenericHttpPack<T> Put<T>(string url, object data,
        Action<T, int> onSuccess, Action<string> onFailure = null) where T : class
    {
        return SendInternal(HttpRequestType.Put, url, BuildBody(data), null, onSuccess, onFailure);
    }

    /// <summary>DELETE 请求（parameters 可拼到查询串）</summary>
    public GenericHttpPack<T> Delete<T>(string url, Dictionary<string, string> parameters = null,
        Action<T> onSuccess = null, Action<string> onFailure = null) where T : class
    {
        return Delete<T>(url, parameters, (result, code) => onSuccess?.Invoke(result), onFailure);
    }

    /// <summary>DELETE 请求（成功回调额外带 HTTP 状态码）</summary>
    public GenericHttpPack<T> Delete<T>(string url, Dictionary<string, string> parameters,
        Action<T, int> onSuccess, Action<string> onFailure = null) where T : class
    {
        return SendInternal(HttpRequestType.Delete, BuildUrlWithParams(url, parameters), null, null, onSuccess, onFailure);
    }

    // ==================== 高级接口 ====================

    /// <summary>
    /// 创建但**不发送**的数据包：适合需要自定义请求头 / 超时 / 重试次数 /
    /// 进度回调 / 重试回调的场景，配置完成后调用 Send(pack) 入队。
    ///
    /// 示例：
    ///   var pack = HttpManager.Instance.CreateRequest&lt;MyData&gt;(HttpRequestType.Post, url, json);
    ///   pack.Headers["Authorization"] = "Bearer xxx";
    ///   pack.Timeout = 30;
    ///   pack.OnProgress = p => Debug.Log(p);
    ///   HttpManager.Instance.Send(pack);
    /// </summary>
    public GenericHttpPack<T> CreateRequest<T>(HttpRequestType type, string url, string body = null) where T : class
    {
        return new GenericHttpPack<T>
        {
            Type = type,
            Url = url,
            Parameters = body
        };
    }

    /// <summary>发送一个已配置好的数据包（会自动合并全局请求头）；返回是否成功入队</summary>
    public bool Send<T>(GenericHttpPack<T> pack) where T : class
    {
        return SendInternal(pack, null);
    }

    /// <summary>取消全部请求：清空等待队列，并中断正在发送的请求（不会触发任何回调）</summary>
    public void CancelAll()
    {
        if (_client != null) _client.CancelAll();
    }

    // ==================== 内部实现 ====================

    /// <summary>确保 HttpClient 已挂载在单例物体上</summary>
    private void EnsureClient()
    {
        if (_client != null) return;

        _client = GetComponent<HttpClient>();
        if (_client == null)
        {
            _client = gameObject.AddComponent<HttpClient>();
        }
    }

    /// <summary>便捷接口的统一出口：组装数据包、合并请求头并入队</summary>
    private GenericHttpPack<T> SendInternal<T>(HttpRequestType type, string url, string body,
        Dictionary<string, string> headers, Action<T, int> onSuccess, Action<string> onFailure) where T : class
    {
        var pack = new GenericHttpPack<T>
        {
            Type = type,
            Url = url,
            Parameters = body,
            OnSuccess = onSuccess,
            OnFailure = onFailure
        };

        return SendInternal(pack, headers) ? pack : null;
    }

    /// <summary>
    /// 校验 + 合并请求头 + 入队。
    /// 请求头优先级：全局 &lt; 参数传入 &lt; 数据包自身已设置的（后写覆盖先写）。
    /// </summary>
    private bool SendInternal<T>(GenericHttpPack<T> pack, Dictionary<string, string> headers) where T : class
    {
        if (pack == null) return false;

        if (string.IsNullOrEmpty(pack.Url))
        {
            const string message = "HTTP 请求未发送：URL 为空";
            PLogger.LogError($"[HttpManager] {message}");
            pack.OnFailure?.Invoke(message);
            return false;
        }

        foreach (var header in _globalHeaders)
        {
            pack.Headers[header.Key] = header.Value;
        }
        if (headers != null)
        {
            foreach (var header in headers)
            {
                if (string.IsNullOrEmpty(header.Key)) continue;

                pack.Headers[header.Key] = header.Value ?? string.Empty;
            }
        }

        Client.SendRequest(pack);
        return true;
    }

    /// <summary>
    /// 构造请求体：null 表示没有请求体；string 视为已写好的 JSON 原文；其它类型走 JsonUtility。
    /// </summary>
    private static string BuildBody(object data)
    {
        if (data == null) return null;

        string raw = data as string;
        if (raw != null) return raw;

        return JsonUtility.ToJson(data);
    }

    /// <summary>
    /// 把参数字典拼到 URL：键值都做 URL 编码；
    /// 已带查询串时用 &amp; 追加（不会再拼一个 ?），# 片段会被保留到最末尾。
    /// </summary>
    private static string BuildUrlWithParams(string url, Dictionary<string, string> parameters)
    {
        if (string.IsNullOrEmpty(url) || parameters == null || parameters.Count == 0) return url;

        // 摘掉 # 片段：参数要插在它前面
        string fragment = string.Empty;
        int hashIndex = url.IndexOf('#');
        if (hashIndex >= 0)
        {
            fragment = url.Substring(hashIndex);
            url = url.Substring(0, hashIndex);
        }

        var query = new StringBuilder();
        foreach (var parameter in parameters)
        {
            if (string.IsNullOrEmpty(parameter.Key)) continue;

            if (query.Length > 0) query.Append('&');
            query.Append(UnityWebRequest.EscapeURL(parameter.Key));
            query.Append('=');
            query.Append(UnityWebRequest.EscapeURL(parameter.Value ?? string.Empty));
        }

        // 所有参数的键都为空时不做任何拼接
        if (query.Length == 0) return url + fragment;

        var sb = new StringBuilder(url);
        if (url.IndexOf('?') < 0)
        {
            sb.Append('?');
        }
        else if (!url.EndsWith("?") && !url.EndsWith("&"))
        {
            sb.Append('&');
        }

        sb.Append(query);
        sb.Append(fragment);
        return sb.ToString();
    }
}
