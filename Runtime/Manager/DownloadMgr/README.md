# 文件下载队列

`Downloader` 用 UnityWebRequest 分块写入文件，提供并发、超时、重试、暂停与取消。可使用 `UPGameRoot.Downloader`，也可在自有 GameObject 上挂载组件。调用与通知由 Unity 主线程驱动。用于业务 JSON 请求时见 [HTTPTool](../HTTPTool/README.md)，用于启动内容更新时见 [热更服务](../GameRoot/HotUpdate/README.md)。

## 下载单个文件

```csharp
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UPandaGF;

public static class FileDownload
{
    public static async Task<bool> RunAsync(UPGameRoot root, string url)
    {
        await root.WaitUntilReadyAsync();
        string path = Path.Combine(Application.persistentDataPath, "content.bundle");
        return await root.Downloader.DownloadAsync(url, path);
    }
}
```

`DownloadAsync(string url, string savePath, IProgress<float> progress = null)` 等待本任务终态。`true` 表示任务完成，失败、取消或等待超时返回 `false`。组件停用或销毁会终止队列并释放请求。`savePath` 可传完整文件路径。传入已存在目录或以目录分隔符结尾的路径时，追加文件名。

## 队列、状态与事件

| 入口或设置 | 行为 |
| --- | --- |
| `AddDownload(url, savePath, fileName)` | 添加单个任务，返回任务 ID |
| `AddBatchDownloads(List<DownloadItem>)` | 入队调用方构造的对象，可观察各自的 `state`、`error` 与 `progress` |
| `PauseDownload`、`PauseCurrentDownload` | 暂停指定或当前任务，暂停任务仍占据批次生命周期 |
| `ResumeDownload(item)`、`ResumeDownload(savePath)` | 重新入队并重置尝试次数，是否追加由续传配置决定 |
| `CancelDownload`、`CancelAllDownloads` | 取消单项或全部任务，取消不发 `OnDownloadError` |
| `maxConcurrentDownloads` | 默认 1，至少 1 |
| `defaultTimeout`、`defaultRetryCount`、`retryDelay` | 默认 30 秒、0 次重试、间隔 1 秒 |

`DownloadItem.timeout` 和 `retryCount` 的 `-1` 继承组件配置，超时 `0` 表示不限时。`fileSize` 是预期大小，`maximumBytes` 是写入的硬上限，`0` 表示不设该限制。`enableResume` 为空时继承组件配置。每次提交新任务对象，批量输入前确保 URL 和最终路径有效。

`OnDownloadStart`、`OnDownloadProgress`、`OnDownloadComplete`、`OnDownloadError`、`OnDownloadPause` 携带任务对象。`OnAllDownloadsComplete` 与 `OnAllDownloadsFinished(bool)` 是整个下载器空闲时的通知。后者的布尔值来自失败列表是否为空，取消任务不进入该列表，业务批次应单独检查每个任务的 `Completed` 状态。取消或停用的收尾也不应依赖全局事件必定触发。 [Downloader.cs](Downloader.cs)

## 写入与完整性

`DownloadHandlerFile` 是框架自己的 `DownloadHandlerScript`。收到第一块数据时打开文件，支持没有 `Content-Length` 的分块响应。每个写入块受 `maximumBytes` 限制，落盘失败通过 `WriteFailed` 和 `LastError` 交给调度器。默认下载直接写指定路径，失败或取消可留下部分文件。需要保留可用旧版本时，先下载至独立暂存文件，验完后再提交。

续传默认关闭。显式开启且存在比 `fileSize` 小的本地文件时发送 `Range`。只有响应码 `206` 才会追加，响应码 `200` 会覆盖原文件。当前续传未核对 `Content-Range` 起点或 ETag，最终长度又优先使用响应得到的长度。要求内容完整时，用可信清单独立验证最终长度和摘要。启动热更已禁用续传并在提交前复验。WebGL 需要跨刷新保留普通下载文件时，在主线程等待 `PlatformStorage.FlushAsync()`。 [DownloadHandlerFile.cs](DownloadHandlerFile.cs) [热更校验](../GameRoot/HotUpdate/README.md) [平台存储](../AssetsMgr/README.md)

回归入口为 [框架回归](../../../Tests/PlayMode/FrameworkRegressionTests.cs) 和 [热更回归](../../../Tests/PlayMode/HotUpdateRegressionTests.cs)。业务接入可用测试服务器验证分块、拒写上限、禁用时的终态及超时重试。
