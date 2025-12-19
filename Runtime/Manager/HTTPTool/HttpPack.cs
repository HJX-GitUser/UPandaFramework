using System;
using UnityEngine.Networking;

// HttpRequestType.cs
public enum HttpRequestType
{
    Get,
    Post,
    Put,
    Delete
}

/// <summary>
/// 封装单个HTTP请求的数据包
/// </summary>
public abstract class HttpPack
{
    public string Url { get; set; }
    public HttpRequestType Type { get; set; }
    public string Parameters { get; set; }
    public int RetryCount { get; set; } = 3; // 默认重试次数
    public int Timeout { get; set; } = 15; // 默认超时时间（秒）
    public UnityWebRequest WebRequest { get; set; }
    public Action<string, int> OnSuccess { get; set; } // 成功回调 (返回数据, HTTP状态码)
    public Action<string> OnFailure { get; set; } // 失败回调 (错误信息)
    public Action<float> OnProgress { get; set; } // 进度回调 (0.0 ~ 1.0)

    public abstract void HandleResponse(); // 处理服务器响应
}