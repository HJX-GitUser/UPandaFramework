# HTTP 请求

`HttpManager` 提供业务 HTTP 请求入口，内部 `HttpClient` 管理队列与原生请求。`HttpPack` 保存单次请求的配置、回调和状态。文件下载及分块落盘使用 [Downloader](../DownloadMgr/README.md)。

## 快速接入

通过 `HttpManager.Instance.Get<T>`、`Post<T>`、`Put<T>`、`Delete<T>` 发起请求。`T` 可为 `string` 或 `JsonUtility` 支持的引用类型 DTO。成功回调可选用带 HTTP 状态码的重载。GET 和 DELETE 的参数字典会编码成查询串，POST 和 PUT 可传 JSON 字符串或由 `JsonUtility` 编码的对象。

```csharp
using UnityEngine;

public sealed class ProfileRequest : MonoBehaviour
{
    private GenericHttpPack<string> request;

    public void Fetch(string url)
    {
        request?.Cancel();
        request = HttpManager.Instance.Get<string>(
            url, parameters: null,
            onSuccess: (body, status) => Debug.Log($"HTTP {status}: {body}"),
            onFailure: Debug.LogError);
    }

    private void OnDisable()
    {
        request?.Cancel();
        request = null;
    }
}
```

单次请求需要配置超时、重试、进度或请求头时，先调用 `CreateRequest<T>`，设置数据包，再调用 `Send(pack)`。一个包只能提交一次。`Send` 返回是否成功入队，便捷接口在未能入队时可返回 `null`。

```csharp
var manager = HttpManager.Instance;
var pack = manager.CreateRequest<string>(HttpRequestType.Post, url, json);
pack.Headers["Authorization"] = "Bearer " + token;
pack.Timeout = 10;
pack.RetryCount = 0;
pack.OnSuccess = (body, status) => Debug.Log(status);
pack.OnFailure = Debug.LogError;
bool queued = manager.Send(pack);
```

## 队列、响应与取消

- 所有调用和回调放在 Unity 主线程。`MaxConcurrentRequests` 默认 1。底层用 `DownloadHandlerBuffer` 存放整个响应体。
- 每次请求默认超时 15 秒，失败默认可重试 3 次，间隔 0.5 秒。重试重建原生请求并以真实时间等待，`OnRetry` 收到剩余次数和错误文本。
- `T == string` 返回原始正文。其他 `T` 遇到空正文或 JSON 解析失败会执行 `OnFailure`。预期 `204 No Content` 的接口可使用字符串重载，通过状态码判断成功。
- `HttpPack.Cancel()` 取消一个请求。`HttpClient.ClearQueue()` 取消排队项，`CancelAll()` 同时取消排队与在途项。取消后不会触发成功、失败或重试通知。业务自建 Task 包装时需要自行结束取消等待。
- 停用或销毁 `HttpClient` 会清理自己的请求。框架先更新所有权与队列状态，再通知业务回调。回调的异常会单独处理。

## 请求头与重试边界

`SetGlobalHeader` 对该管理器的后续请求生效，同名单次请求头优先。全局头适用于该管理器发出的所有 URL。请求多个域名时，授权头放在对应请求的 `pack.Headers` 上。`BodyRaw` 优先于 JSON 文本，发送二进制时显式设置内容类型。

当前重试依据请求是否成功，未根据 HTTP 方法或状态码区分。POST 等有副作用的请求如果在服务端已执行、客户端却超时，自动重试可能重复执行。无服务端幂等保证时设 `RetryCount = 0`，或按服务端约定传幂等键。

[HttpManager.cs](HttpManager.cs) [HttpClient.cs](HttpClient.cs) [HttpPack.cs](HttpPack.cs)

用 Unity Test Runner 运行 [HTTP 协调回归](../../../Tests/PlayMode/CoordinationRegressionTests.cs)。该套件包含请求头优先级、重复提交、取消、销毁及回调异常等路径。发布前再核对实际服务端的认证、空响应、重试与错误格式。
