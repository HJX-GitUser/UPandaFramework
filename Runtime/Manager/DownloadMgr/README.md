# DownloadMgr — 下载器模块（队列 / 并发 / 断点续传 / 超时重试）

> 位置：`Assets/Scripts/upanda-framework/Runtime/Manager/DownloadMgr/`
> 命名空间：`UPandaGF`
> 重构时间：2026-09（原文见文末「修复与优化记录」）

---

## 1. 文件组成

| 文件 | 类型 | 职责 |
| --- | --- | --- |
| `Downloader.cs` | `MonoBehaviour` | 下载器本体。内含 `DownloadItem`（任务）、`DownloadState`（状态枚举）、`Downloader`（队列调度 + 并发 + 事件 + `async` 包装） |
| `DownloadHandlerFile.cs` | `DownloadHandlerScript` | 落盘句柄。把收到的数据块写进文件，负责「延迟打开文件 / 追加 or 覆盖 / 异常兜底 / 句柄关闭」 |

两个类都在 `UPandaGF` 命名空间下，`DownloadHandlerFile` 与 Unity 内置的 `UnityEngine.Networking.DownloadHandlerFile` **同名**，同文件内同时使用时请写全名区分。

---

## 2. 架构与调用链

```
                    ┌─────────────────────────── 三种入口 ───────────────────────────┐
                    │                                                                │
  await DownloadAsync(url, path)      AddDownload(url, path)            事件订阅
        （单个任务 + 返回 bool）      AddBatchDownloads(List<DownloadItem>)  OnDownloadProgress...
                    │                                                                │
                    └──────────────► CreateItem（校验 + 补字段）◄────────────────────┘
                                            │
                                    BeginBatchIfIdle()      ← 完全空闲时开启新批次（清零统计）
                                            │
                                    Enqueue → pendingItems  ← 排队队列
                                            │
                                TryStartNextDownloads()     ← 按 maxConcurrentDownloads 拉起
                                            │
                                    activeItems + StartCoroutine(DownloadRoutine)
                                            │
   ┌────────────────────────────────────────┴────────────────────────────────────────┐
   │ DownloadRoutine：重试等待 → 建目录 → 续传判定 → 建请求+句柄 → SendWebRequest      │
   │                  每帧：暂停/取消/超时检查 → 200/206 校正 → 读 Content-Length      │
   │                        → 刷新进度 → 抛 OnDownloadProgress → yield return null     │
   │                  结束：结果判定 + 落盘大小校验                                     │
   └────────────────────────────────────────┬────────────────────────────────────────┘
                                            │
                                        FinishItem
        ┌───────────────┬──────────────────┼───────────────────┬────────────────┐
     Paused          Canceled          Completed            Failed(还有重试)   Failed(用尽)
   OnDownloadPause      无事件        OnDownloadComplete    回队尾重新排队     OnDownloadError
        └───────────────┴──────────────────┴───────────────────┴────────────────┘
                                            │
                                ReleaseRequest（Abort + Close 文件 + Dispose）
                                            │
                                RemoveActive → 拉起下一个任务
                                            │
                                    CheckAllFinished（三队列全空才算结束）
                                    ├─ OnAllDownloadsComplete      （无参，兼容旧代码）
                                    └─ OnAllDownloadsFinished(bool)（新增，告知是否全部成功）
```

要点：

- **队列是唯一的真源**：`pendingItems`（排队）/ `activeItems`（下载中）/ `pausedItems`（暂停）/ `failedItems`（失败记录）。
- **进度由下载器统一计算**：句柄的 `GetProgress()` 固定返回 `0f`，避免与「续传起点」冲突。
- **所有回调都在主线程**（协程驱动），可以在回调里直接操作 UI。
- 即使 `maxConcurrentDownloads = 1`（默认），也是走同一条队列路径，没有"单独任务"的特例分支。

---

## 3. 下载生命周期（`DownloadRoutine` 逐步说明）

| 步骤 | 行为 | 关键点 |
| --- | --- | --- |
| 1 | `attempt++`，若 `attempt > 1` 且 `retryDelay > 0` 则等待 | 重试延迟；首次不等待 |
| 2 | 创建落地目录 | 失败 → 直接判失败（可重试） |
| 3 | 续传判定（见 §6.2） | 默认不续传，**整文件重下** |
| 4 | 建 `UnityWebRequest`：`timeout`、可选 `Range` 头、`DownloadHandlerFile(path, append, shouldAppend)` | `shouldAppend` 回调在句柄内部判断 206 |
| 5 | `SendWebRequest()` | `try/catch` 兜底（同步异常不会打断迭代器） |
| 6 | 每帧循环：暂停/取消 → 超时检查 → 200/206 校正 → 读 `Content-Length` 算总大小 → 刷新进度并抛事件 → `yield return null` | 循环体只做「判断 + 赋值 + 抛事件」，**不做阻塞 IO** |
| 7 | 结果判定：`item.error` 为空 && 请求成功 && 落盘大小匹配预期 | 三步都过才算成功 |
| 8 | `FinishItem` 收尾 | 暂停/取消/成功/失败分支 + 释放请求 + 批次统计 + 拉起下一个 |

---

## 4. API 参考

### 4.1 `DownloadItem`（任务）

调用方需要设置的字段：

| 字段 | 说明 |
| --- | --- |
| `url` | 下载地址（必填） |
| `savePath` | 完整文件路径；若传的是**已存在的目录**或以 `/` `\` 结尾，会自动拼接 `fileName` |
| `fileName` | 文件名；为空时从 url 推导（自动去掉 `?query` 与 `#fragment`） |
| `fileSize` | 预期大小（字节，0 = 未知）。用于进度分母与完整性校验 |
| `timeout` | 超时秒数：`-1` = 用 `Downloader.defaultTimeout`；`0` = 不超时 |
| `retryCount` | 重试次数（不含首次）：`-1` = 用 `Downloader.defaultRetryCount` |
| `enableResume` | 是否允许续传：`null` = 用 `Downloader.enableResume` |

运行时状态（只读为主）：

| 成员 | 说明 |
| --- | --- |
| `id` | 任务唯一 id（`DL-1`、`DL-2`…），`AddDownload` 会返回它 |
| `downloadedSize` | 已下载字节（**含续传起点**） |
| `progress` | 0~1；总大小未知时保持 0，完成时置 1 |
| `state` | `DownloadState`：`Waiting / Downloading / Completed / Failed / Paused / Canceled` |
| `error` | 失败原因（成功时为 null） |
| `request` | 当前 `UnityWebRequest`（下载中有效，结束后置 null） |

### 4.2 `Downloader` 配置字段

| 字段 | 默认 | 说明 |
| --- | --- | --- |
| `maxConcurrentDownloads` | `1` | 同时下载数上限 |
| `defaultTimeout` | `30` | 默认超时（秒），`0` = 不超时 |
| `defaultRetryCount` | `0` | 默认重试次数 |
| `retryDelay` | `1` | 重试前等待（秒） |
| `enableResume` | `false` | 是否开启断点续传（**默认关闭，原因见 §6.2**） |

### 4.3 事件

| 事件 | 签名 | 触发时机 |
| --- | --- | --- |
| `OnDownloadStart` | `Action<DownloadItem>` | 开始下载（重试也会触发） |
| `OnDownloadProgress` | `Action<DownloadItem>` | 每帧进度更新 |
| `OnDownloadComplete` | `Action<DownloadItem>` | 单个任务成功 |
| `OnDownloadError` | `Action<DownloadItem>` | 单个任务失败（**重试耗尽才触发一次**） |
| `OnDownloadPause` | `Action<DownloadItem>` | 单个任务被暂停 |
| `OnAllDownloadsComplete` | `Action` | 一批全部结束（旧签名，兼容 `UPGameRoot`） |
| `OnAllDownloadsFinished` | `Action<bool>` | 一批全部结束，参数 = 是否全部成功 |

> 「暂停中」的任务会让批次保持未结束状态（`IsIdle == false`），继续或取消后才会收尾。

### 4.4 方法

| 方法 | 说明 |
| --- | --- |
| `Task<bool> DownloadAsync(string url, string savePath, IProgress<float> progress = null)` | 下载单个文件并等待结果；超时也会返回 `false` |
| `string AddDownload(string url, string savePath, string fileName = null)` | 加入队列，返回任务 id（参数非法返回 null） |
| `void AddBatchDownloads(List<DownloadItem> items)` | 批量入队并自动补齐运行时字段 |
| `void PauseDownload(DownloadItem item)` | 暂停任务（下载中的本帧结束后中断，排队中的直接移出） |
| `void PauseCurrentDownload()` | 暂停当前所有进行中 + 排队任务 |
| `void ResumeDownload(DownloadItem item)` | 继续已暂停任务（复位尝试次数，可续传则从落盘处继续） |
| `bool ResumeDownload(string savePath)` | 按路径继续，返回是否找到（旧接口语义已修正） |
| `void CancelDownload(DownloadItem item)` | 取消任务（**不触发** `OnDownloadError`） |
| `void CancelAllDownloads()` | 取消全部（排队/暂停的直接丢弃，下载中的由协程统一释放） |
| `float GetTotalProgress()` | 总体进度（已完成计 1，进行中计各自进度，排队计 0） |
| `void ClearFailedItems()` | 清空失败记录（新批次开始时会自动清空） |

查询属性：`PendingCount` / `ActiveCount` / `PausedCount` / `FailedCount` / `FailedItems` / `IsIdle`。

---

## 5. 使用示例

### 5.1 单个文件 + 进度（等结果）

```csharp
Downloader downloader = UPGameRoot.Instance.Downloader;

bool ok = await downloader.DownloadAsync(
    "https://example.com/a.bundle",
    Path.Combine(Application.persistentDataPath, "a.bundle"),
    new Progress<float>(p => Debug.Log($"进度 {p:P0}")));

Debug.Log(ok ? "下载成功" : "下载失败");
```

### 5.2 批量下载 + 全部完成

```csharp
List<DownloadItem> list = new List<DownloadItem>
{
    new DownloadItem { url = url1, savePath = path1, fileName = "a.bundle", fileSize = 1024 },
    new DownloadItem { url = url2, savePath = path2, fileName = "b.bundle", fileSize = 2048 },
};

TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
Action<bool> onFinished = allSuccess => tcs.TrySetResult(allSuccess);

downloader.OnAllDownloadsFinished += onFinished;
downloader.AddBatchDownloads(list);

bool allOk = await tcs.Task;

downloader.OnAllDownloadsFinished -= onFinished;
// 失败明细
foreach (DownloadItem bad in downloader.FailedItems) Debug.LogError($"{bad.fileName}: {bad.error}");
```

> 兼容旧代码：只订阅 `OnAllDownloadsComplete`（无参）依旧可用，但它**不携带成败信息**；需要判断结果请用 `OnAllDownloadsFinished` 或 `FailedCount`。

### 5.3 暂停 / 继续 / 取消

```csharp
string id = downloader.AddDownload(url, savePath);      // 入队即开始
DownloadItem item = /* 自己持有引用，或由事件回调拿到 */;

downloader.PauseDownload(item);                          // 暂停
downloader.ResumeDownload(item);                         // 继续（attempt 会被复位）
downloader.ResumeDownload(savePath);                     // 按路径继续
downloader.CancelDownload(item);                         // 取消（无错误回调）
```

### 5.4 并发与重试

```csharp
downloader.maxConcurrentDownloads = 3;
downloader.defaultRetryCount = 2;   // 失败自动重试 2 次
downloader.retryDelay = 1f;         // 重试间隔 1 秒
downloader.defaultTimeout = 60;     // 60 秒无进展判超时

// 单任务覆盖默认值
DownloadItem item = new DownloadItem
{
    url = bigUrl,
    savePath = bigPath,
    timeout = 0,        // 这个大文件不设超时
    retryCount = 5,
};
downloader.AddBatchDownloads(new List<DownloadItem> { item });
```

---

## 6. 关键设计说明

### 6.1 进度口径（修复"永远卡在 50%"）

旧公式是 `(已下载 + 总大小) / (总大小 + 2 × 总大小)`，全新下载时恒等于 0.5。现在：

```
总大小 totalSize     = 续传起点 + 响应头 Content-Length   （Content-Length 未到前为 0）
已下载 downloadedSize = 续传起点 + request.downloadedBytes
progress             = totalSize > 0 ? clamp01(downloadedSize / totalSize) : 0
```

- 分母在**拿到响应头后**才确定，所以进度严格单调 `0 → 1`。
- 完成时强制 `progress = 1`。
- 服务端不给 `Content-Length`（分块传输）时总大小未知，进度保持 0，但下载仍能正常完成。

### 6.2 断点续传为什么默认关闭

框架的增量更新（`UPGameRoot`）是**按 MD5 对比**生成下载清单的，因此清单里的文件在本地可能是：

- 不存在；
- 存在但大小不同；
- **存在且大小相同、但内容不同（旧版本）**。

第三种情况下若按「本地已有 N 字节」去追加写入，会把新内容拼到旧文件后面，得到一个**损坏的文件**。所以：

- `enableResume` 默认 `false`（一律覆盖重下）；
- 即使显式开启，也要求三重条件才续传：
  1. `IsResumeEnabled(item)` 为真（任务级 `enableResume` 优先于下载器默认值）；
  2. `item.fileSize > 0 && localSize > 0 && localSize < fileSize`（本地大小合理且未完成）；
  3. 服务端**确实返回 206**：`shouldAppend` 回调未通过时句柄改为覆盖写入，同时把统计起点归零（`HandleResumeMismatch`）。
- 续传时发送 `Range: bytes=N-`；若服务端忽略 Range 返回 200，会被识别出来并按整文件重下统计，不会产生"进度超过 100%"或"大小校验失败"的误判。

### 6.3 落盘句柄（`DownloadHandlerFile`）

- **延迟打开文件**：在收到第一块数据时才创建文件。旧实现只在 `ReceiveContentLengthHeader` 里打开，而分块传输不会触发该回调 → `fileStream` 一直是 null → 数据被全部丢弃（且返回 false 直接中断下载）。
- **写入异常兜底**：打开/写入失败会记录到 `WriteFailed` / `LastError`，并返回 `false` 让 Unity 中断下载，不会从下载回调里抛异常。
- **可主动关闭**：提供 `Close()`。取消/中断请求前 `Downloader` 会先 `Close()` 再 `Dispose()`，避免文件句柄泄漏、文件被占用。
- `GetProgress()` 返回 `0f`（进度由下载器统一算）。

### 6.4 超时（修复"永久挂起"）

旧实现没有任何超时，服务器不响应时 `DownloadAsync` 的 `TaskCompletionSource` 会永远不完成，进而卡死 `UPGameRoot` 的启动流程。现在：

- 请求级：`request.timeout = timeout`（`defaultTimeout = 30` 秒，`0` = 不超时）；
- 协程级：每帧检查 `Time.realtimeSinceStartup - startTime > timeout`，超时写入 `item.error` 并收尾；
- `DownloadAsync` 级：`timeout + 5` 秒兜底，超时主动取消任务并返回 `false`（即使传输层超时失效也不会挂起）。

### 6.5 请求与句柄的释放

统一走 `ReleaseRequest(item)`：`Abort()`（未完成时）→ `handler.Close()` → `request.Dispose()`。暂停、取消、成功、失败四条路径都会调用它，`OnDestroy` 也会对仍在进行中的请求做一次释放。

### 6.6 事件不泄漏

`DownloadAsync` 内部订阅 `OnDownloadComplete/OnDownloadError/OnDownloadProgress`，并在 `finally` 中**无条件退订**。旧实现订阅 3 个匿名委托后从不退订，每次调用都会永久堆积（内存泄漏 + 回调被重复触发）。

### 6.7 取消计数不再被破坏

旧 `CancelAllDownloads` 会把 `activeDownloadCount` 直接置 0，导致后续协程里的 `--` 把它减成负数。现在取消只打标记，计数由 `activeItems` 列表本身维护，协程收尾时统一移除。

### 6.8 批次结束判定

`CheckAllFinished()` 只在 `pendingItems`、`activeItems`、`pausedItems` **同时为空**时才触发批次事件，并给 `OnAllDownloadsFinished` 传入 `FailedCount == 0` 的判定结果。失败任务不会阻止批次结束（旧实现会把失败也当作"全部完成"且无处查原因，现在可查 `FailedItems`）。

批次回调发出后，`TaskCompletionSource` 使用 `TaskCreationOptions.RunContinuationsAsynchronously`：等待方（如 `UPGameRoot.DownloadBatchAndWait`）的后续流程会在事件分发**结束之后**才继续，不会嵌套在 `FinishItem` 内部同步执行，避免重入（例如在下载器收尾过程中立刻投递新一批任务）。

---

## 7. 修复与优化记录（2026-09）

| 级别 | 原问题 | 现状 |
| --- | --- | --- |
| 🔴 | 进度公式 `(S+D)/(S+2D)`，新下载恒为 0.5 | 分母 = 续传起点 + `Content-Length`，进度严格单调 |
| 🔴 | 续传不校验 206，完整响应被追加到旧文件 → 文件损坏 | `shouldAppend` 校验 206，未通过则覆盖写入并归零统计 |
| 🔴 | `ResumeDownload(string savePath)` 失效（用 savePath 造了个没有 url 的新任务） | 在 `pausedItems` 中查找后真正重新排队 |
| 🔴 | 全程无超时，`DownloadAsync` 可永久挂起（卡死启动流程） | 请求/协程/`DownloadAsync` 三级超时 |
| 🔴 | 暂停/取消只 `Abort()`，不关文件流 → 句柄泄漏、文件被占用 | `ReleaseRequest` = Abort + `Close()` + `Dispose()`；`OnDestroy` 兜底 |
| 🔴 | 分块传输（无 `Content-Length`）时文件根本没打开 → 数据全丢 | 句柄延迟到首块数据时打开文件 |
| 🟡 | `DownloadAsync` 订阅 3 个匿名委托且从不退订 → 泄漏 | `finally` 中无条件退订 |
| 🟡 | 批次没有成败信号（失败也算"全部完成"） | 新增 `OnAllDownloadsFinished(bool)` + `FailedItems`，旧事件保留 |
| 🟡 | 并发是假的（一次只起一个协程，且只起一次） | `TryStartNextDownloads` 按上限循环拉起 |
| 🟡 | `CancelAllDownloads` 把计数置 0，后续 `--` 变负 | 计数由列表维护 |
| 🟡 | 无落盘大小校验，被截断的下载算成功 | `VerifyFileSize` 比对 `totalSize`/`fileSize` |
| 🟡 | 写入/打开异常未处理 | 句柄内 `try/catch` + `WriteFailed`/`LastError` |
| 🟡 | 无 url / savePath 校验 | `CreateItem` 校验并记录日志，返回 null |
| 🟡 | 文件名直接取自 url，带 `?query` | `GetFileNameFromUrl` 去掉 `?query` 与 `#fragment` |
| 🟢 | 死代码 `isDownloading`、`Debug.Log("Downloader Init!!!")`、重复错误判断 | 已清除，日志统一走 `PLogger` |
| 🟢 | 缺少状态查询、重试、并发、超时等能力 | 新增 `IsIdle`/`FailedItems`/`GetTotalProgress`/重试/并发/超时 |

---

## 8. 与框架的关系

`UPGameRoot`（`Runtime/Manager/GameRoot/UPGameRoot.cs`）通过 `InitComponent<Downloader>()` 挂载下载器，并依赖以下契约（**重构后保持不变**）：

| 契约 | 用途 |
| --- | --- |
| `public Downloader Downloader { get; }` | 对外暴露下载器 |
| `Task<bool> DownloadAsync(string url, string savePath)` | `DownloadRemoteList` 等待远程清单下载完 |
| `event Action OnAllDownloadsComplete` | 旧的无参批结束事件，**签名必须保持无参**（仍在抛，供已有代码使用） |
| `event Action<bool> OnAllDownloadsFinished` | `DownloadBatchAndWait` 现在用它拿到「是否全部成功」 |
| `IReadOnlyList<DownloadItem> FailedItems` | `LogDownloadFailures` 用它打印失败清单 |
| `void AddBatchDownloads(List<DownloadItem>)` | 批量投递增量清单 |
| `DownloadItem.url / savePath / fileName / fileSize` | `GetDownInfo(fileName, size)` 用对象初始化器构造任务 |

`UPGameRoot` 侧已同步改造（2026-09）：

- `DownloadBatchAndWait` → `Task<bool>`，改订阅 `OnAllDownloadsFinished(bool)` 取真实结果；失败时由 `LogDownloadFailures` 输出红色汇总 + 最多 10 条失败明细（文件名 / url / 原因）。
- `DownloadFirstTime`、`DownloadIncrement` 一并返回 `Task<bool>`；`UpdateAssets` 用 `if (!await ...)` 判断，未全部成功时输出红色告警（**仍继续启动流程，不回滚**——是否中断启动属于产品决策）。

另外注意：`UPGameRoot` 的增量清单是按 **MD5 对比**筛出来的，这正是 §6.2 中「续传默认关闭」的根本原因——修改下载策略时务必保留这个前提。

---

## 9. 维护提示

1. **编码**：`Downloader.cs` / `DownloadHandlerFile.cs` 是 **GBK（代码页 936）+ CRLF**。直接改中文注释会乱码，建议先备份再改，改完检查是否出现 `U+FFFD`。
2. **迭代器约束**：`DownloadRoutine` 是 `IEnumerator`，**不要把 `yield return` 放进带 `catch` 的 `try` 块**（C# 不允许，会编译失败）。异常处理请放在不含 `yield` 的独立方法里（如 `ReleaseRequest` / `HandleResumeMismatch`）。
3. **改动后验证**：至少确认无编译错误，并检查括号配平；下载相关逻辑建议真机/编辑器各跑一次「正常 / 断网 / 慢速 / 暂停继续」四种场景。
4. **不要轻易打开 `enableResume`**：开启前提是「本地文件确定是同一内容的未完成下载」，否则会得到损坏的文件（见 §6.2）。
5. **契约优先**：`UPGameRoot` 依赖的事件签名与 `DownloadItem` 字段名不要随意修改，新增能力请用新成员扩展。
