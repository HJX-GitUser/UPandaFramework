using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UPandaGF;

/// <summary>
/// HTTP 请求方法
/// </summary>
public enum HttpRequestType
{
    Get,
    Post,
    Put,
    Delete
}

/// <summary>
/// 单个 HTTP 请求的数据包（抽象基类）。
/// 职责：保存请求参数、创建/销毁底层 UnityWebRequest、对外暴露各类回调。
/// 具体响应类型的解析由子类实现（见 GenericHttpPack&lt;T&gt;）。
/// </summary>
public abstract class HttpPack
{
    /// <summary>请求地址</summary>
    public string Url { get; set; }

    /// <summary>请求方法</summary>
    public HttpRequestType Type { get; set; }

    /// <summary>请求体（JSON 字符串；GET 会忽略，POST / PUT / DELETE 有效）</summary>
    public string Parameters { get; set; }

    /// <summary>请求体原始字节；设置后优先于 Parameters（用于上传二进制数据）</summary>
    public byte[] BodyRaw { get; set; }

    /// <summary>失败后的重试次数（不含首次请求），默认 3，即最多请求 1+3 次</summary>
    public int RetryCount { get; set; } = 3;

    /// <summary>超时时间（秒），默认 15</summary>
    public int Timeout { get; set; } = 15;

    /// <summary>重试前的等待时间（秒），默认 0.5；设为 0 表示立即重试</summary>
    public float RetryDelay { get; set; } = 0.5f;

    /// <summary>已发起的尝试次数（0 = 尚未发送；由 HttpClient 维护，可在 OnRetry 中读取）</summary>
    public int AttemptCount { get; set; }

    /// <summary>本次请求的请求头（发送时会与全局请求头合并，同名单次优先）</summary>
    public Dictionary<string, string> Headers { get; } = new Dictionary<string, string>();

    /// <summary>底层请求对象：由 CreateWebRequest 创建，重试时会重建，请求结束后置空</summary>
    public UnityWebRequest WebRequest { get; set; }

    /// <summary>是否已被取消；取消后不再回调、不再重试</summary>
    public bool IsCanceled { get; private set; }

    /// <summary>失败回调（仅在重试次数耗尽后触发一次，参数为错误信息）</summary>
    public Action<string> OnFailure { get; set; }

    /// <summary>下载进度回调（0~1）；仅当设置了该回调时才会逐帧轮询，不影响无回调时的性能</summary>
    public Action<float> OnProgress { get; set; }

    /// <summary>重试回调（剩余重试次数, 上一次的错误信息）</summary>
    public Action<int, string> OnRetry { get; set; }

    /// <summary>
    /// 创建（或重建）底层 UnityWebRequest。
    /// 首次发送与每次重试都会调用，因此这里必须可重复执行（内部会先释放旧请求）。
    /// </summary>
    public virtual void CreateWebRequest()
    {
        DisposeWebRequest();

        WebRequest = new UnityWebRequest(Url, GetHttpVerb(Type));
        WebRequest.timeout = Timeout;

        byte[] body = GetBodyBytes();
        if (body != null && body.Length > 0)
        {
            WebRequest.uploadHandler = new UploadHandlerRaw(body);
        }
        WebRequest.downloadHandler = new DownloadHandlerBuffer();

        // 写入请求头（全局头已由 HttpManager 合并进 Headers）
        foreach (var header in Headers)
        {
            if (string.IsNullOrEmpty(header.Key)) continue;

            WebRequest.SetRequestHeader(header.Key, header.Value ?? string.Empty);
        }

        // 有请求体且未显式指定 Content-Type 时，给一个 JSON 默认值
        if (body != null && body.Length > 0 && !Headers.ContainsKey("Content-Type"))
        {
            WebRequest.SetRequestHeader("Content-Type", "application/json;charset=utf-8");
        }
    }

    /// <summary>
    /// 释放底层请求（连同上传/下载句柄）。
    /// 重试前、请求结束、取消、异常路径都会走到这里，因此内部做空值保护。
    /// </summary>
    public virtual void DisposeWebRequest()
    {
        if (WebRequest == null) return;

        try
        {
            WebRequest.Dispose();
        }
        catch (Exception e)
        {
            PLogger.LogWarning($"[HttpPack] 释放请求失败：{e.Message}");
        }
        WebRequest = null;
    }

    /// <summary>
    /// 取消请求：进行中的请求会被 Abort，仍在队列里的会被跳过。
    /// 取消后不会触发 OnSuccess / OnFailure / OnRetry。
    /// </summary>
    public void Cancel()
    {
        if (IsCanceled) return;

        IsCanceled = true;

        if (WebRequest == null) return;

        try
        {
            WebRequest.Abort();
        }
        catch (Exception e)
        {
            PLogger.LogWarning($"[HttpPack] 取消请求失败：{e.Message}");
        }
    }

    /// <summary>上报当前下载进度（0~1）；回调内抛异常不会影响请求流程</summary>
    public void ReportProgress()
    {
        if (OnProgress == null || WebRequest == null) return;

        try
        {
            OnProgress(WebRequest.downloadProgress);
        }
        catch (Exception e)
        {
            PLogger.LogError($"[HttpPack] 进度回调异常：{e.Message}");
        }
    }

    /// <summary>处理服务器响应（子类实现：解析响应体并回调）</summary>
    public abstract void HandleResponse();

    /// <summary>取请求体字节：BodyRaw 优先，其次 UTF-8 编码的 Parameters；GET 无请求体</summary>
    private byte[] GetBodyBytes()
    {
        if (BodyRaw != null && BodyRaw.Length > 0) return BodyRaw;
        if (Type == HttpRequestType.Get) return null;
        if (string.IsNullOrEmpty(Parameters)) return null;

        return Encoding.UTF8.GetBytes(Parameters);
    }

    /// <summary>枚举转 UnityWebRequest 的 HTTP 方法常量（避免 ToString().ToUpper() 的字符串分配）</summary>
    private static string GetHttpVerb(HttpRequestType type)
    {
        switch (type)
        {
            case HttpRequestType.Post: return UnityWebRequest.kHttpVerbPOST;
            case HttpRequestType.Put: return UnityWebRequest.kHttpVerbPUT;
            case HttpRequestType.Delete: return UnityWebRequest.kHttpVerbDELETE;
            default: return UnityWebRequest.kHttpVerbGET;
        }
    }
}

/// <summary>
/// 泛型响应数据包：把响应文本反序列化为 T 后回调。
/// T 为 string 时直接返回响应原文（不做 JSON 解析）。
/// </summary>
public class GenericHttpPack<T> : HttpPack where T : class
{
    /// <summary>成功回调（反序列化结果, HTTP 状态码）</summary>
    public Action<T, int> OnSuccess { get; set; }

    /// <summary>
    /// 解析响应并回调：
    /// 1) T 为 string -> 直接返回响应原文；
    /// 2) 响应体为空 -> 走失败回调并给出明确原因；
    /// 3) 否则 JsonUtility.FromJson&lt;T&gt;，解析异常同样走失败回调（不会抛给调用方）。
    /// </summary>
    public override void HandleResponse()
    {
        int code = (int)WebRequest.responseCode;
        string text = WebRequest.downloadHandler != null ? WebRequest.downloadHandler.text : null;

        if (typeof(T) == typeof(string))
        {
            OnSuccess?.Invoke(text as T, code);
            return;
        }

        if (string.IsNullOrEmpty(text))
        {
            OnFailure?.Invoke($"HTTP Response Empty. URL: {Url}, Code: {code}, 无法反序列化为 {typeof(T).Name}");
            return;
        }

        try
        {
            T data = JsonUtility.FromJson<T>(text);
            OnSuccess?.Invoke(data, code);
        }
        catch (Exception e)
        {
            OnFailure?.Invoke($"JSON Parse Error. URL: {Url}, Code: {code}, Message: {e.Message}");
        }
    }
}
