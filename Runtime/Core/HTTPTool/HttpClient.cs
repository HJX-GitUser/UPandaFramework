using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UPandaGF;

namespace UPandaGF
{

/// <summary>主线程 HTTP 队列。数据包只能提交一次，停用/销毁结束本宿主的全部请求。</summary>
public class HttpClient : MonoBehaviour
{
    public int MaxConcurrentRequests { get; set; } = 1;
    private readonly Queue<HttpPack> _sendQueue = new Queue<HttpPack>();
    private readonly HashSet<HttpPack> _runningRequests = new HashSet<HttpPack>();
    private readonly HashSet<HttpPack> owned = new HashSet<HttpPack>();
    private int _runningCount;
    private bool destroyed;

    private void Update()
    {
        while (!destroyed && isActiveAndEnabled && _runningCount < Mathf.Max(1, MaxConcurrentRequests) && _sendQueue.Count > 0)
        {
            var pack = _sendQueue.Dequeue();
            if (pack.IsCanceled) { Complete(pack); continue; }
            _runningRequests.Add(pack); _runningCount = _runningRequests.Count;
            StartCoroutine(ProcessRequest(pack));
        }
    }
    public void SendRequest(HttpPack pack) { TrySendRequest(pack); }
    internal bool TrySendRequest(HttpPack pack)
    {
        if (pack == null) return false;
        if (pack.IsCanceled && !pack.IsSubmitted) { pack.MarkCompleted(); SafeDispose(pack); return false; }
        if (!pack.TrySubmit()) return false;
        if (destroyed || this == null || !isActiveAndEnabled)
        { pack.Cancel(); pack.MarkCompleted(); SafeDispose(pack); return false; }
        owned.Add(pack);
        try
        {
            if (pack.WebRequest == null) pack.CreateWebRequest();
            if (!Valid(pack)) { Complete(pack); return false; }
            if (pack.WebRequest == null) throw new InvalidOperationException("CreateWebRequest 未创建请求");
            _sendQueue.Enqueue(pack); return true;
        }
        catch (Exception e) { Fail(pack, "HTTP 请求创建失败：" + e.Message); return false; }
    }
    public void ClearQueue()
    {
        var queued = _sendQueue.ToArray(); _sendQueue.Clear();
        foreach (var pack in queued) { pack.Cancel(); Complete(pack); }
    }
    public void CancelAll()
    {
        var pending = new List<HttpPack>(owned);
        // 先提交旧批次状态；清理重入产生的新请求不属于这批取消。
        owned.Clear(); _sendQueue.Clear(); _runningRequests.Clear(); _runningCount = 0;
        foreach (var pack in pending) { pack.Cancel(); pack.MarkCompleted(); SafeDispose(pack); }
    }
    private void OnDisable() { CancelAll(); base.StopAllCoroutines(); }
    private void OnDestroy() { destroyed = true; CancelAll(); }

    private IEnumerator ProcessRequest(HttpPack pack)
    {
        bool retryQueued = false;
        try
        {
            // 游戏暂停不冻结网络重试。等待也可被取消，不额外计为一次发送。
            while (Valid(pack) && Time.realtimeSinceStartup < pack.NextAttemptAt) yield return null;
            if (!Valid(pack)) yield break;
            UnityWebRequestAsyncOperation operation;
            string startError;
            if (!TryStart(pack, out operation, out startError))
            { Fail(pack, startError); yield break; }
            if (pack.OnProgress == null) yield return operation;
            else
            {
                while (Valid(pack) && !operation.isDone)
                {
                    pack.ReportProgress();
                    if (!Valid(pack)) yield break;
                    yield return null;
                }
            }
            if (!Valid(pack)) yield break;
            pack.ReportProgress();
            if (Valid(pack)) retryQueued = FinishAttempt(pack);
        }
        finally
        {
            _runningRequests.Remove(pack); _runningCount = _runningRequests.Count;
            if (!retryQueued && owned.Contains(pack)) { pack.Cancel(); Complete(pack); }
        }
    }
    private bool Valid(HttpPack pack) { return !destroyed && !pack.IsCanceled && owned.Contains(pack); }
    private bool TryStart(HttpPack pack, out UnityWebRequestAsyncOperation operation, out string error)
    {
        operation = null; error = null;
        try
        {
            if (pack.WebRequest == null) pack.CreateWebRequest();
            if (!Valid(pack)) { Complete(pack); error = "HTTP 请求已取消"; return false; }
            if (pack.WebRequest == null) throw new InvalidOperationException("CreateWebRequest 未创建请求");
            pack.WebRequest.timeout = pack.Timeout;
            pack.AttemptCount++;
            operation = pack.WebRequest.SendWebRequest();
            if (operation == null) throw new InvalidOperationException("SendWebRequest 未返回操作");
            return true;
        }
        catch (Exception e) { error = "HTTP 请求启动失败：" + e.Message; return false; }
    }
    private bool FinishAttempt(HttpPack pack)
    {
        if (pack.WebRequest != null && pack.WebRequest.result == UnityWebRequest.Result.Success)
        {
            // 原生响应仍在，先撤销管理器所有权与槽位，再进入解析和用户回调。
            MarkCompleted(pack);
            try { pack.HandleResponse(); }
            catch (Exception e) { pack.NotifyFailure("HTTP 响应处理失败：" + e.Message); }
            finally { SafeDispose(pack); }
            return false;
        }
        string error = BuildErrorMessage(pack.WebRequest);
        bool retryable = IsRetryable(pack);
        SafeDispose(pack);
        if (pack.RetryCount <= 0 || !retryable)
        {
            if (pack.RetryCount > 0) PLogger.LogWarning("[HttpClient] 本次失败不可安全重试（方法/状态码）：" + error);
            Fail(pack, error);
            return false;
        }
        pack.RetryCount--;
        _runningRequests.Remove(pack); _runningCount = _runningRequests.Count;
        PLogger.LogWarning("[HttpClient] 请求失败，准备重试。URL: " + pack.Url + ", 原因: " + error);
        pack.NotifyRetry(pack.RetryCount, error);
        if (!Valid(pack)) { Complete(pack); return false; }
        pack.NextAttemptAt = Time.realtimeSinceStartup + Mathf.Max(0, pack.RetryDelay);
        _sendQueue.Enqueue(pack);
        return true;
    }
    private void Fail(HttpPack pack, string error)
    {
        if (!owned.Contains(pack)) return;
        bool canceled = pack.IsCanceled;
        Complete(pack);
        if (canceled) return;
        PLogger.LogError("[HttpClient] 请求失败（重试已用尽）。URL: " + pack.Url + ", 原因: " + error);
        pack.NotifyFailure(error);
    }
    private void MarkCompleted(HttpPack pack)
    {
        owned.Remove(pack); _runningRequests.Remove(pack); _runningCount = _runningRequests.Count;
        pack.MarkCompleted();
    }
    private void Complete(HttpPack pack) { MarkCompleted(pack); SafeDispose(pack); }
    private static void SafeDispose(HttpPack pack)
    {
        try { pack.DisposeWebRequest(); }
        catch (Exception e)
        {
            // 即使扩展数据包的释放方法抛异常，也释放仍暴露的原生请求。
            var native = pack.WebRequest; pack.WebRequest = null;
            try { native?.Dispose(); } catch (Exception cleanup) { Debug.LogException(cleanup); }
            Debug.LogException(e);
        }
    }
    /// <summary>
    /// 是否允许自动重试：网络/超时/429/5xx 可重试；4xx（除 408/429）不重试；
    /// POST/PUT 默认不重试，除非显式开启 <see cref="HttpPack.RetryNonIdempotent"/>。
    /// </summary>
    private static bool IsRetryable(HttpPack pack)
    {
        if (pack == null) return false;
        if ((pack.Type == HttpRequestType.Post || pack.Type == HttpRequestType.Put) && !pack.RetryNonIdempotent)
            return false;

        UnityWebRequest request = pack.WebRequest;
        long code = request != null ? request.responseCode : 0;
        if (code >= 400 && code < 500) return code == 408 || code == 429;
        return true;
    }

    private static string BuildErrorMessage(UnityWebRequest request)
    {
        if (request == null) return "请求对象为空";
        string error = "HTTP Request Failed. URL: " + request.url + ", Code: " + request.responseCode + ", Error: " + request.error;
        try
        {
            string body = request.downloadHandler?.text;
            if (!string.IsNullOrEmpty(body)) error += ", Body: " + (body.Length > 512 ? body.Substring(0, 512) + "..." : body);
        }
        catch (Exception) { }
        return error;
    }
}

}