using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// HTTP客户端，负责实际发送请求和管理队列
/// </summary>
public class HttpClient : MonoBehaviour
{
    private readonly Queue<HttpPack> _sendQueue = new Queue<HttpPack>();
    private readonly List<HttpPack> _pendingRequests = new List<HttpPack>();
    private bool _isProcessing = false;

    void Update()
    {
        // 如果没有正在处理的请求且发送队列不为空，则开始处理下一个
        if (!_isProcessing && _sendQueue.Count > 0)
        {
            _isProcessing = true;
            StartCoroutine(ProcessRequest(_sendQueue.Dequeue()));
        }
    }

    /// <summary>
    /// 添加请求到发送队列
    /// </summary>
    public void SendRequest(HttpPack pack)
    {
        _sendQueue.Enqueue(pack);
    }

    private System.Collections.IEnumerator ProcessRequest(HttpPack pack)
    {
        pack.WebRequest.timeout = pack.Timeout;
        pack.WebRequest.SetRequestHeader("Content-Type", "application/json;charset=utf-8");

        // 发送请求
        yield return pack.WebRequest.SendWebRequest();

        // 处理结果
        if (IsRequestSuccess(pack.WebRequest))
        {
            pack.HandleResponse();
        }
        else
        {
            HandleRequestError(pack);
        }

        // 清理资源
        if (pack.WebRequest != null)
        {
            pack.WebRequest.Dispose();
            pack.WebRequest = null;
        }
        _pendingRequests.Remove(pack);
        _isProcessing = false;
    }

    private bool IsRequestSuccess(UnityWebRequest request)
    {
#if UNITY_2020_3_OR_NEWER
        return request.result == UnityWebRequest.Result.Success;
#else
        return !request.isHttpError && !request.isNetworkError;
#endif
    }

    private void HandleRequestError(HttpPack pack)
    {
        string errorMsg = $"HTTP Request Failed. URL: {pack.Url}, Error: {pack.WebRequest.error}";
        pack.OnFailure?.Invoke(errorMsg);

        // 重试逻辑
        if (pack.RetryCount > 0)
        {
            pack.RetryCount--;
            _sendQueue.Enqueue(pack); // 重新加入队列
        }
    }
}