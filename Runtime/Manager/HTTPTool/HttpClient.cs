using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UPandaGF;

/// <summary>
/// HTTP 客户端：维护发送队列并按并发上限发送请求。
/// 由 HttpManager 在单例物体上动态添加，靠 Update 驱动队列，所有回调都在主线程。
/// </summary>
public class HttpClient : MonoBehaviour
{
    /// <summary>并发上限：1 = 串行（默认）；大于 1 时同时发送多个请求</summary>
    public int MaxConcurrentRequests { get; set; } = 1;

    private readonly Queue<HttpPack> _sendQueue = new Queue<HttpPack>();      // 等待发送
    private readonly HashSet<HttpPack> _runningRequests = new HashSet<HttpPack>();  // 正在发送
    private int _runningCount;

    private void Update()
    {
        int limit = Mathf.Max(1, MaxConcurrentRequests);

        while (_runningCount < limit && _sendQueue.Count > 0)
        {
            HttpPack pack = _sendQueue.Dequeue();
            if (pack == null) continue;

            // 排队期间被取消：直接释放，不发送也不回调
            if (pack.IsCanceled)
            {
                pack.DisposeWebRequest();
                continue;
            }

            _runningCount++;
            _runningRequests.Add(pack);
            StartCoroutine(ProcessRequest(pack));
        }
    }

    /// <summary>
    /// 请求入队（内部确保底层 UnityWebRequest 已创建）
    /// </summary>
    public void SendRequest(HttpPack pack)
    {
        if (pack == null) return;

        if (pack.IsCanceled)
        {
            pack.DisposeWebRequest();
            return;
        }

        if (pack.WebRequest == null) pack.CreateWebRequest();

        _sendQueue.Enqueue(pack);
    }

    /// <summary>清空等待队列（不影响正在发送的请求，取消它们请用 CancelAll）</summary>
    public void ClearQueue()
    {
        while (_sendQueue.Count > 0)
        {
            HttpPack pack = _sendQueue.Dequeue();
            if (pack == null) continue;

            pack.Cancel();
            pack.DisposeWebRequest();
        }
    }

    /// <summary>取消全部请求：等待队列被清空，进行中的请求会被 Abort（不会触发任何回调）</summary>
    public void CancelAll()
    {
        ClearQueue();

        if (_runningRequests.Count == 0) return;

        // 复制一份再取消，避免遍历时集合被修改
        var running = new List<HttpPack>(_runningRequests);
        for (int i = 0; i < running.Count; i++)
        {
            running[i]?.Cancel();
        }
    }

    /// <summary>
    /// 请求协程：等待重试间隔 -> 发起 -> 等待（可选上报进度）-> 统一收尾。
    /// 注意：迭代器内不能写 try/catch，所以"可能抛异常"的发起动作放在
    /// TryStartRequest（普通方法）里，保证任何异常都不会跳过收尾流程。
    /// </summary>
    private IEnumerator ProcessRequest(HttpPack pack)
    {
        // 重试前等待（首次发送不等待），避免网络异常时瞬间反复重试
        if (pack.AttemptCount > 0 && pack.RetryDelay > 0f)
        {
            yield return new WaitForSeconds(pack.RetryDelay);
        }

        pack.AttemptCount++;

        UnityWebRequestAsyncOperation operation;
        if (TryStartRequest(pack, out operation))
        {
            if (pack.OnProgress == null)
            {
                yield return operation;
            }
            else
            {
                // 仅在有进度回调时逐帧轮询，避免无谓开销
                while (!operation.isDone)
                {
                    pack.ReportProgress();
                    yield return null;
                }
                pack.ReportProgress();
            }
        }

        FinishRequest(pack);
    }

    /// <summary>
    /// 发起请求（普通方法，内部捕获异常）；返回是否成功拿到异步操作
    /// </summary>
    private bool TryStartRequest(HttpPack pack, out UnityWebRequestAsyncOperation operation)
    {
        operation = null;

        if (pack == null || pack.WebRequest == null)
        {
            PLogger.LogError("[HttpClient] 请求对象为空，无法发送");
            return false;
        }

        try
        {
            pack.WebRequest.timeout = pack.Timeout;
            operation = pack.WebRequest.SendWebRequest();
            return operation != null;
        }
        catch (Exception e)
        {
            PLogger.LogError($"[HttpClient] 发起请求失败。URL: {pack.Url}, Message: {e}");
            return false;
        }
    }

    /// <summary>
    /// 统一收尾：判定成功/失败、按需重试、释放请求、复位并发计数。
    /// 这里绝不能抛异常出去，否则队列会永久停摆，因此整体包在 try/catch/finally 中。
    /// </summary>
    private void FinishRequest(HttpPack pack)
    {
        if (pack == null)
        {
            _runningCount = Mathf.Max(0, _runningCount - 1);
            return;
        }

        bool willRetry = false;

        try
        {
            // 被取消：静默结束（不回调、不重试）
            if (pack.IsCanceled) return;

            if (IsRequestSuccess(pack.WebRequest))
            {
                pack.HandleResponse();
            }
            else
            {
                willRetry = TryScheduleRetry(pack);
            }
        }
        catch (Exception e)
        {
            PLogger.LogError($"[HttpClient] 处理响应异常。URL: {pack.Url}, Message: {e}");
            pack.OnFailure?.Invoke($"HTTP Handle Exception. URL: {pack.Url}, Message: {e.Message}");
        }
        finally
        {
            if (willRetry)
            {
                // 重建请求：上一次的 WebRequest 已不可复用（这也修复了原来重试时
                // WebRequest 为 null 导致的空引用）
                pack.CreateWebRequest();
                _sendQueue.Enqueue(pack);
            }
            else
            {
                pack.DisposeWebRequest();
            }

            _runningRequests.Remove(pack);
            _runningCount = Mathf.Max(0, _runningCount - 1);
        }
    }

    /// <summary>
    /// 成功判定（兼容新旧 Unity 版本）
    /// </summary>
    private bool IsRequestSuccess(UnityWebRequest request)
    {
        if (request == null) return false;

#if UNITY_2020_3_OR_NEWER
        return request.result == UnityWebRequest.Result.Success;
#else
        return !request.isHttpError && !request.isNetworkError;
#endif
    }

    /// <summary>
    /// 失败处理：还有重试次数就返回 true（由调用方重新入队），否则回调一次 OnFailure 并返回 false。
    /// 注意：OnFailure 只在放弃时触发一次，重试过程通过 OnRetry / 日志体现。
    /// </summary>
    private bool TryScheduleRetry(HttpPack pack)
    {
        string error = BuildErrorMessage(pack.WebRequest);

        if (pack.RetryCount > 0)
        {
            pack.RetryCount--;
            PLogger.LogWarning($"[HttpClient] 请求失败，准备重试。URL: {pack.Url}, 剩余重试次数: {pack.RetryCount}, 原因: {error}");
            pack.OnRetry?.Invoke(pack.RetryCount, error);
            return true;
        }

        PLogger.LogError($"[HttpClient] 请求失败（重试已用尽）。URL: {pack.Url}, 原因: {error}");
        pack.OnFailure?.Invoke(error);
        return false;
    }

    /// <summary>
    /// 组装错误信息：地址、状态码、错误原因，并尽量附上服务端返回的错误体（4xx/5xx 常见）
    /// </summary>
    private static string BuildErrorMessage(UnityWebRequest request)
    {
        if (request == null) return "请求对象为空";

        string message = $"HTTP Request Failed. URL: {request.url}, Code: {request.responseCode}, Error: {request.error}";

        try
        {
            if (request.downloadHandler != null)
            {
                string body = request.downloadHandler.text;
                if (!string.IsNullOrEmpty(body))
                {
                    message += $", Body: {Truncate(body, 512)}";
                }
            }
        }
        catch (Exception)
        {
            // 读取响应体失败不影响错误上报
        }

        return message;
    }

    /// <summary>按最大长度截断文本（错误体可能很长）</summary>
    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength) return text;

        return text.Substring(0, maxLength) + "...";
    }
}
