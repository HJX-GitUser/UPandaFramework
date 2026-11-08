# HTTP 请求

`HttpManager` 实现 `IHttpService`，由 `GameServices` 创建并持有（不再有静态单例）。内部 `HttpClient` 管理队列与原生请求；`HttpPack` 保存单次请求的配置、回调和状态。业务优先使用 `services.Http` 的 Task 契约；需要逐帧进度、`OnRetry` 或自定义数据包时再使用具体 `HttpManager`。文件下载及分块落盘使用 [Downloader](../../Assets/DownloadMgr/README.md)。

## 快速接入（Task 契约）

`GetAsync<T>`、`PostAsync<T>`、`PutAsync<T>`、`DeleteAsync<T>` 返回 `HttpResult<T>`：失败不抛异常，取消以 Task 取消态表达。`T` 可为 `string`（响应原文）或 `JsonUtility` 支持的引用类型 DTO。GET/DELETE 的查询参数会 URL 编码；POST/PUT 的 `object body` 为 string 时视为 JSON 原文，其它类型由 `JsonUtility` 编码。

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using UPandaGF;

public sealed class ProfileRequest : MonoBehaviour, IGameServicesConsumer
{
    private IGameServices services;
    private CancellationTokenSource request;

    public void Configure(IGameServices value) { services = value; }

    public async Task Fetch(string url)
    {
        Cancel();
        request = new CancellationTokenSource();
        var result = await services.Http.GetAsync<string>(url, token: request.Token);
        if (result.Success) Debug.Log($"HTTP {result.StatusCode}: {result.Data}");
        else if (!string.IsNullOrEmpty(result.Error)) Debug.LogError(result.Error);
    }

    private void OnDisable() { Cancel(); }

    private void Cancel()
    {
        request?.Cancel();
        request?.Dispose();
        request = null;
    }
}
```

单次请求需要超时、重试、请求头或非幂等重试时，通过 `HttpRequestOptions` 传入：

```csharp
var options = new HttpRequestOptions
{
    RetryCount = 2,
    TimeoutSeconds = 10,
    RetryNonIdempotent = true,
    Headers = new Dictionary<string, string> { ["Authorization"] = "Bearer " + token }
};
var result = await services.Http.PostAsync<LoginResponse>(url, body, options);
```

## 高级入口（数据包 / 进度 / 重试回调）

需要逐帧进度或自定义数据包时，使用具体实现：

```csharp
var manager = (HttpManager)services.Http;
var pack = manager.CreateRequest<string>(HttpRequestType.Post, url, json);
pack.Headers["Authorization"] = "Bearer " + token;
pack.Timeout = 10;
pack.RetryCount = 0;
pack.OnProgress = p => Debug.Log(p);
pack.OnSuccess = (body, status) => Debug.Log(status);
pack.OnFailure = Debug.LogError;
bool queued = manager.Send(pack);
```

一个包只能提交一次。`Send` 返回是否成功入队，便捷接口在未能入队时可返回 `null`。

## 队列、响应与取消

- 所有调用和回调放在 Unity 主线程。`services.Http.MaxConcurrentRequests` 默认 1。底层用 `DownloadHandlerBuffer` 存放整个响应体。
- 每次请求默认超时 15 秒，失败默认可重试 3 次，间隔 0.5 秒。重试重建原生请求并以真实时间等待，`OnRetry` 收到剩余次数和错误文本。
- `T == string` 返回原始正文。其他 `T` 遇到空正文或 JSON 解析失败会以失败结果返回。预期 `204 No Content` 的接口可使用字符串重载，通过状态码判断成功。
- `HttpPack.Cancel()` 取消一个请求。`HttpClient.ClearQueue()` 取消排队项，`CancelAll()` 同时取消排队与在途项。取消后不会触发成功、失败或重试通知；契约 Task 进入取消态。
- 停用或销毁宿主会清理该 `HttpClient` 的请求。框架先更新所有权与队列状态，再通知业务回调。回调的异常会单独处理。

## 请求头与重试边界

`SetGlobalHeader` 对该管理器的后续请求生效，同名单次请求头优先。全局头适用于该管理器发出的所有 URL。请求多个域名时，授权头放在对应请求的 `pack.Headers` 上。`BodyRaw` 优先于 JSON 文本，发送二进制时显式设置内容类型。

当前只对网络/超时/408/429/5xx 重试，4xx（除 408/429）不重试；POST/PUT 默认不自动重试，需要时设置 `RetryNonIdempotent = true`。无服务端幂等保证时不要开启，或按服务端约定传幂等键。

[HttpManager.cs](HttpManager.cs) [HttpClient.cs](HttpClient.cs) [HttpPack.cs](HttpPack.cs)

用 Unity Test Runner 运行 [HTTP 协调回归](../../../Tests/PlayMode/CoordinationRegressionTests.cs)。该套件包含请求头优先级、重复提交、取消、销毁及回调异常等路径。发布前再核对实际服务端的认证、空响应、重试与错误格式。
