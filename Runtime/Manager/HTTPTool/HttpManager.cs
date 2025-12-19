using UnityEngine;
using System.Collections.Generic;
using System.Text;
using System;
using UnityEngine.Networking;

/// <summary>
/// HTTP管理类，提供对外的API接口
/// </summary>
public class HttpManager : LazyMonoSingletonBase<HttpManager>
{
    private HttpClient _client;
    private Dictionary<string, string> _globalHeaders = new Dictionary<string, string>();

    protected override void Awake()
    {
        base.Awake();
        _client = gameObject.AddComponent<HttpClient>();
    }

    /// <summary>
    /// 设置全局请求头（如Token）
    /// </summary>
    public void SetGlobalHeader(string key, string value)
    {
        _globalHeaders[key] = value;
    }

    public void Get<T>(string url, Dictionary<string, string> parameters, Action<T> onSuccess, Action<string> onFailure = null) where T : class
    {
        var pack = new GenericHttpPack<T>
        {
            Url = BuildUrlWithParams(url, parameters),
            Type = HttpRequestType.Get,
            OnSuccess = (data, code) => onSuccess?.Invoke(data),
            OnFailure = onFailure
        };
        pack.CreateWebRequest();
        _client.SendRequest(pack);
    }

    public void Post<T>(string url, object data, Action<T> onSuccess, Action<string> onFailure = null) where T : class
    {
        string jsonData = JsonUtility.ToJson(data);
        var pack = new GenericHttpPack<T>
        {
            Url = url,
            Type = HttpRequestType.Post,
            Parameters = jsonData,
            OnSuccess = (data, code) => onSuccess?.Invoke(data),
            OnFailure = onFailure
        };
        pack.CreateWebRequest();
        _client.SendRequest(pack);
    }

    private string BuildUrlWithParams(string url, Dictionary<string, string> parameters)
    {
        if (parameters == null || parameters.Count == 0) return url;

        var sb = new StringBuilder();
        sb.Append(url);
        sb.Append("?");
        bool isFirst = true;
        foreach (var param in parameters)
        {
            if (!isFirst) sb.Append("&");
            sb.Append($"{UnityWebRequest.EscapeURL(param.Key)}={UnityWebRequest.EscapeURL(param.Value)}");
            isFirst = false;
        }
        return sb.ToString();
    }
}

/// <summary>
/// 泛型HTTP数据包，用于处理具体类型的响应
/// </summary>
public class GenericHttpPack<T> : HttpPack where T : class
{
    public new Action<T, int> OnSuccess { get; set; }

    public override void HandleResponse()
    {
        string text = WebRequest.downloadHandler.text;
        T data = null;

        if (typeof(T) == typeof(string))
        {
            data = text as T;
        }
        else
        {
            try
            {
                data = JsonUtility.FromJson<T>(text);
            }
            catch (System.Exception ex)
            {
                OnFailure?.Invoke($"JSON Parse Error: {ex.Message}");
                return;
            }
        }
        OnSuccess?.Invoke(data, (int)WebRequest.responseCode);
    }

    public void CreateWebRequest()
    {
        string finalUrl = (Type == HttpRequestType.Get) ? Url : Url;
        WebRequest = new UnityWebRequest(finalUrl, Type.ToString().ToUpper());

        if (Type == HttpRequestType.Post || Type == HttpRequestType.Put)
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(Parameters);
            WebRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
        }
        WebRequest.downloadHandler = new DownloadHandlerBuffer();
    }
}