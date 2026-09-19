using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace UPandaGF
{
    /// <summary>
    /// 单个下载任务。
    /// 常用字段（url / savePath / fileName / fileSize）可直接赋值，其余为运行时状态。
    /// </summary>
    public class DownloadItem
    {
        // ==================== 由调用方设置 ====================

        /// <summary>下载地址</summary>
        public string url;

        /// <summary>保存路径【完整文件路径】；若传的是已存在的目录（或以 / \ 结尾），会自动拼接 fileName</summary>
        public string savePath;

        /// <summary>文件名；为空时自动从 url 推导（会去掉 ?query 与 #fragment）</summary>
        public string fileName;

        /// <summary>预期文件大小（字节，0 = 未知）。由清单提供时可用于进度计算与下载完整性校验</summary>
        public long fileSize;

        /// <summary>超时时间（秒）：-1 = 使用 Downloader.defaultTimeout；0 = 不超时</summary>
        public int timeout = -1;

        /// <summary>失败重试次数（不含首次）：-1 = 使用 Downloader.defaultRetryCount</summary>
        public int retryCount = -1;

        /// <summary>是否允许断点续传：null = 使用 Downloader.enableResume</summary>
        public bool? enableResume = null;

        // ==================== 运行时状态 ====================

        /// <summary>已下载大小（含续传起点）</summary>
        public long downloadedSize;

        /// <summary>下载进度（0~1）；总大小未知时保持 0，完成时置 1</summary>
        public float progress;

        /// <summary>当前状态</summary>
        public DownloadState state;

        /// <summary>错误信息（失败时写入）</summary>
        public string error;

        /// <summary>当前请求对象（仅在下载中有效，结束后置空）</summary>
        public UnityWebRequest request;

        /// <summary>任务唯一 id（由 Downloader 分配，用于回调匹配）</summary>
        public string id;

        // ==================== 内部运行时字段 ====================

        internal int attempt;               // 已尝试次数（1 表示首次）
        internal long resumeSize;           // 本次续传起点（本地已有字节数）
        internal long totalSize;            // 总大小（续传起点 + 响应 Content-Length），0 = 未知
        internal bool pauseRequested;       // 外部请求暂停
        internal bool cancelRequested;      // 外部请求取消

        public override string ToString()
        {
            return $"{id} {fileName} [{state}] {progress:P0}";
        }
    }

    /// <summary>下载状态</summary>
    public enum DownloadState
    {
        Waiting,        // 排队等待
        Downloading,    // 下载中
        Completed,      // 已完成
        Failed,         // 失败（重试已用尽）
        Paused,         // 已暂停（可 ResumeDownload 继续）
        Canceled        // 已取消
    }

    /// <summary>
    /// 下载器（MonoBehaviour）：队列 + 并发下载 + 事件通知 + async 包装。
    ///
    /// 特性：
    ///   - 队列与并发：maxConcurrentDownloads 控制同时下载数（默认 1 = 串行）
    ///   - 进度：优先用「响应 Content-Length + 续传起点」计算，进度严格单调 0 → 1
    ///   - 超时：默认 30 秒（可配置；0 = 不超时），避免服务器无响应时永久等待
    ///   - 重试：可配置次数与间隔，重试期间不产生错误事件；重试耗尽才回调 OnDownloadError
    ///   - 完整性校验：完成后比对落盘大小与预期/Content-Length，不一致视为失败
    ///   - 断点续传：默认关闭（见 enableResume 说明），开启时会校验服务器是否真的返回 206
    ///   - 暂停 / 继续 / 取消：任务级 API，暂停后可通过 ResumeDownload 继续
    ///   - 批量结果：OnAllDownloadsFinished(bool allSuccess) 告知是否全部成功
    ///
    /// 所有回调都在主线程触发。
    /// </summary>
    public class Downloader : MonoBehaviour
    {
        [Header("下载设置")]
        [Tooltip("最大同时下载数")]
        public int maxConcurrentDownloads = 1;

        [Tooltip("单个任务的默认超时时间（秒），0 = 不超时")]
        public int defaultTimeout = 30;

        [Tooltip("单个任务的默认重试次数（不含首次）")]
        public int defaultRetryCount = 0;

        [Tooltip("重试前的等待时间（秒）")]
        public float retryDelay = 1f;

        [Tooltip("是否允许断点续传。注意：只有确定「本地文件是同一内容的未完成下载」时才安全；" +
                 "框架的 MD5 增量更新场景下本地文件可能是旧版本，因此默认关闭。开启后仍会校验 206 响应码")]
        public bool enableResume = false;

        // ==================== 事件 ====================

        /// <summary>开始下载一个任务（重试时也会触发）</summary>
        public event Action<DownloadItem> OnDownloadStart;

        /// <summary>下载进度更新（每帧）</summary>
        public event Action<DownloadItem> OnDownloadProgress;

        /// <summary>单个任务下载成功</summary>
        public event Action<DownloadItem> OnDownloadComplete;

        /// <summary>单个任务失败（重试已用尽，整个任务只触发一次）</summary>
        public event Action<DownloadItem> OnDownloadError;

        /// <summary>单个任务被暂停</summary>
        public event Action<DownloadItem> OnDownloadPause;

        /// <summary>一批下载全部结束（兼容旧签名：不携带成功与否）</summary>
        public event Action OnAllDownloadsComplete;

        /// <summary>一批下载全部结束（参数 = 是否全部成功）</summary>
        public event Action<bool> OnAllDownloadsFinished;

        // ==================== 内部状态 ====================

        private readonly List<DownloadItem> pendingItems = new List<DownloadItem>();
        private readonly List<DownloadItem> activeItems = new List<DownloadItem>();
        private readonly List<DownloadItem> pausedItems = new List<DownloadItem>();
        private readonly List<DownloadItem> failedItems = new List<DownloadItem>();

        private int downloadIdSeed;
        private int batchCompleted;

        // ==================== 查询接口 ====================

        /// <summary>排队等待中的任务数</summary>
        public int PendingCount { get { return pendingItems.Count; } }

        /// <summary>正在下载的任务数</summary>
        public int ActiveCount { get { return activeItems.Count; } }

        /// <summary>已暂停的任务数</summary>
        public int PausedCount { get { return pausedItems.Count; } }

        /// <summary>本批次失败的任务数</summary>
        public int FailedCount { get { return failedItems.Count; } }

        /// <summary>本批次失败的任务列表</summary>
        public IReadOnlyList<DownloadItem> FailedItems { get { return failedItems; } }

        /// <summary>是否已空闲（无排队、无下载中、无暂停）</summary>
        public bool IsIdle
        {
            get { return pendingItems.Count == 0 && activeItems.Count == 0 && pausedItems.Count == 0; }
        }

        /// <summary>清空失败记录（新批次开始时自动清空）</summary>
        public void ClearFailedItems()
        {
            failedItems.Clear();
        }

        // ==================== 添加任务 ====================

        /// <summary>
        /// 添加单个下载任务
        /// </summary>
        /// <param name="url">下载地址</param>
        /// <param name="savePath">保存路径（完整文件路径；传目录会自动拼接文件名）</param>
        /// <param name="fileName">文件名，为空则从 url 推导</param>
        /// <returns>任务 id（可用于事件匹配）；参数非法时返回 null</returns>
        public string AddDownload(string url, string savePath, string fileName = null)
        {
            DownloadItem item = CreateItem(url, savePath, fileName);
            if (item == null) return null;

            BeginBatchIfIdle();
            Enqueue(item);
            return item.id;
        }

        /// <summary>
        /// 批量添加下载任务（会补齐各任务的运行时字段）
        /// </summary>
        public void AddBatchDownloads(List<DownloadItem> items)
        {
            if (items == null || items.Count == 0) return;

            BeginBatchIfIdle();

            for (int i = 0; i < items.Count; i++)
            {
                DownloadItem item = items[i];
                if (item == null) continue;

                if (string.IsNullOrEmpty(item.url))
                {
                    PLogger.LogError("[Downloader] 批量任务中存在 url 为空的项，已跳过");
                    continue;
                }

                PrepareItem(item);
                item.state = DownloadState.Waiting;
                pendingItems.Add(item);
            }

            TryStartNextDownloads();
        }

        // ==================== 暂停 / 继续 / 取消 ====================

        /// <summary>暂停指定任务：下载中的会在本帧结束后中断并释放请求，排队中的直接移出队列</summary>
        public void PauseDownload(DownloadItem item)
        {
            if (item == null) return;

            if (activeItems.Contains(item))
            {
                item.pauseRequested = true;      // 由下载协程统一收尾
                return;
            }

            if (pendingItems.Remove(item))
            {
                item.state = DownloadState.Paused;
                pausedItems.Add(item);
                OnDownloadPause?.Invoke(item);
            }
        }

        /// <summary>
        /// 暂停当前所有正在进行的任务。
        /// （旧实现只能暂停唯一的一个"当前任务"，这里在保持语义的同时支持并发）
        /// </summary>
        public void PauseCurrentDownload()
        {
            for (int i = 0; i < activeItems.Count; i++)
            {
                DownloadItem item = activeItems[i];
                if (item != null) item.pauseRequested = true;
            }

            // 还在排队的任务也一起暂停，避免"暂停了但后面还在偷偷下"
            while (pendingItems.Count > 0)
            {
                DownloadItem item = pendingItems[0];
                pendingItems.RemoveAt(0);
                if (item == null) continue;

                item.state = DownloadState.Paused;
                pausedItems.Add(item);
                OnDownloadPause?.Invoke(item);
            }
        }

        /// <summary>继续一个已暂停的任务（会从已落盘的部分继续，依赖 enableResume）</summary>
        public void ResumeDownload(DownloadItem item)
        {
            if (item == null) return;
            if (!pausedItems.Remove(item)) return;

            item.pauseRequested = false;
            item.state = DownloadState.Waiting;

            // 重新排队时把尝试次数复位，否则会因为"重试次数已用尽"直接判失败
            item.attempt = 0;
            item.error = null;

            pendingItems.Add(item);
            TryStartNextDownloads();
        }

        /// <summary>按保存路径继续一个已暂停的任务，返回是否找到</summary>
        public bool ResumeDownload(string savePath)
        {
            if (string.IsNullOrEmpty(savePath)) return false;

            for (int i = 0; i < pausedItems.Count; i++)
            {
                DownloadItem item = pausedItems[i];
                if (item == null) continue;

                if (item.savePath == savePath || ResolveSavePath(item) == savePath)
                {
                    ResumeDownload(item);
                    return true;
                }
            }

            PLogger.LogWarning($"[Downloader] 没有找到已暂停的任务：{savePath}");
            return false;
        }

        /// <summary>取消指定任务（取消不会触发 OnDownloadError）</summary>
        public void CancelDownload(DownloadItem item)
        {
            if (item == null) return;

            item.cancelRequested = true;

            if (pendingItems.Remove(item))
            {
                item.state = DownloadState.Canceled;
                CheckAllFinished();
            }

            if (pausedItems.Remove(item))
            {
                item.state = DownloadState.Canceled;
                CheckAllFinished();
            }
        }

        /// <summary>取消全部下载：排队/暂停的直接丢弃，下载中的交给协程统一释放</summary>
        public void CancelAllDownloads()
        {
            for (int i = 0; i < pendingItems.Count; i++)
            {
                DownloadItem item = pendingItems[i];
                if (item != null) item.state = DownloadState.Canceled;
            }
            pendingItems.Clear();

            for (int i = 0; i < pausedItems.Count; i++)
            {
                DownloadItem item = pausedItems[i];
                if (item != null) item.state = DownloadState.Canceled;
            }
            pausedItems.Clear();

            // 正在下载的任务只打标记，由 DownloadRoutine 统一 Abort + Dispose + 复位
            for (int i = 0; i < activeItems.Count; i++)
            {
                DownloadItem item = activeItems[i];
                if (item != null) item.cancelRequested = true;
            }
        }

        // ==================== 进度 ====================

        /// <summary>
        /// 总体进度（0~1）：已完成任务计 1，下载中/暂停中任务计各自进度，排队任务计 0。
        /// </summary>
        public float GetTotalProgress()
        {
            int total = batchCompleted + activeItems.Count + pausedItems.Count + pendingItems.Count;
            if (total <= 0) return IsIdle ? 1f : 0f;

            float sum = batchCompleted;
            for (int i = 0; i < activeItems.Count; i++)
            {
                DownloadItem item = activeItems[i];
                if (item != null) sum += Mathf.Clamp01(item.progress);
            }
            for (int i = 0; i < pausedItems.Count; i++)
            {
                DownloadItem item = pausedItems[i];
                if (item != null) sum += Mathf.Clamp01(item.progress);
            }

            return Mathf.Clamp01(sum / total);
        }

        // ==================== async 接口 ====================

        /// <summary>
        /// 下载单个文件并等待结果（内部用事件完成，返回结果时会自动退订，不会泄漏事件处理器）
        /// </summary>
        /// <param name="url">下载地址</param>
        /// <param name="savePath">保存路径（完整文件路径）</param>
        /// <param name="progress">可选进度回调</param>
        /// <returns>是否下载成功</returns>
        public async Task<bool> DownloadAsync(string url, string savePath, IProgress<float> progress = null)
        {
            DownloadItem item = CreateItem(url, savePath, null);
            if (item == null) return false;

            // RunContinuationsAsynchronously：避免等待方在"完成事件分发"中途同步续跑
            // （否则 await 之后的热更流程会嵌在下载器的 FinishItem 里执行，容易引发重入）
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            Action<DownloadItem> onComplete = (i) => { if (ReferenceEquals(i, item)) tcs.TrySetResult(true); };
            Action<DownloadItem> onError = (i) => { if (ReferenceEquals(i, item)) tcs.TrySetResult(false); };
            Action<DownloadItem> onProgress = (i) => { if (ReferenceEquals(i, item)) progress.Report(i.progress); };

            OnDownloadComplete += onComplete;
            OnDownloadError += onError;
            if (progress != null) OnDownloadProgress += onProgress;

            try
            {
                BeginBatchIfIdle();
                Enqueue(item);

                // 兜底：即使传输层超时未生效，也不会让调用方永远挂起
                int timeout = item.timeout < 0 ? defaultTimeout : item.timeout;
                if (timeout > 0)
                {
                    float deadline = Time.realtimeSinceStartup + timeout + 5f;
                    while (!tcs.Task.IsCompleted && Time.realtimeSinceStartup < deadline)
                    {
                        await Task.Yield();
                    }

                    if (!tcs.Task.IsCompleted)
                    {
                        PLogger.LogError($"[Downloader] 等待下载完成超时（{timeout + 5} 秒）：{item.url}");
                        CancelDownload(item);
                        tcs.TrySetResult(false);
                    }
                }
                else
                {
                    await tcs.Task;
                }

                return tcs.Task.Result;
            }
            finally
            {
                // 必须退订，否则每次调用都会永久堆积事件处理器
                OnDownloadComplete -= onComplete;
                OnDownloadError -= onError;
                if (progress != null) OnDownloadProgress -= onProgress;
            }
        }

        // ==================== 生命周期 ====================

        private void OnDestroy()
        {
            CancelAllDownloads();

            // 对象销毁时协程会被强制终止，这里直接释放仍在使用的请求，避免文件句柄泄漏
            for (int i = 0; i < activeItems.Count; i++)
            {
                DownloadItem item = activeItems[i];
                if (item == null) continue;

                ReleaseRequest(item);
            }
            activeItems.Clear();
        }

        // ==================== 内部实现 ====================

        /// <summary>创建并校验任务（不排队）</summary>
        private DownloadItem CreateItem(string url, string savePath, string fileName)
        {
            if (string.IsNullOrEmpty(url))
            {
                PLogger.LogError("[Downloader] url 为空，无法添加下载任务");
                return null;
            }

            DownloadItem item = new DownloadItem
            {
                url = url,
                savePath = savePath,
                fileName = string.IsNullOrEmpty(fileName) ? GetFileNameFromUrl(url) : fileName,
                state = DownloadState.Waiting
            };

            PrepareItem(item);
            return item;
        }

        /// <summary>补齐外部创建的任务的运行时字段</summary>
        private void PrepareItem(DownloadItem item)
        {
            if (string.IsNullOrEmpty(item.id)) item.id = $"DL-{++downloadIdSeed}";
            if (string.IsNullOrEmpty(item.fileName)) item.fileName = GetFileNameFromUrl(item.url);
            if (string.IsNullOrEmpty(item.savePath))
            {
                item.savePath = Path.Combine(Application.persistentDataPath, item.fileName);
            }

            item.error = null;
            item.progress = 0f;
            item.state = DownloadState.Waiting;
        }

        /// <summary>一批任务开始时（当前完全空闲）重置批次统计</summary>
        private void BeginBatchIfIdle()
        {
            if (!IsIdle) return;

            batchCompleted = 0;
            failedItems.Clear();
        }

        /// <summary>任务入队并尝试启动</summary>
        private void Enqueue(DownloadItem item)
        {
            item.state = DownloadState.Waiting;
            pendingItems.Add(item);
            TryStartNextDownloads();
        }

        /// <summary>按并发上限尽可能多地启动下载</summary>
        private void TryStartNextDownloads()
        {
            int max = Mathf.Max(1, maxConcurrentDownloads);

            while (activeItems.Count < max && pendingItems.Count > 0)
            {
                DownloadItem item = pendingItems[0];
                pendingItems.RemoveAt(0);
                if (item == null) continue;

                if (item.cancelRequested)
                {
                    item.state = DownloadState.Canceled;
                    continue;
                }

                activeItems.Add(item);
                StartCoroutine(DownloadRoutine(item));
            }
        }

        /// <summary>下载主流程</summary>
        private IEnumerator DownloadRoutine(DownloadItem item)
        {
            item.attempt++;

            // 重试等待（首次不等待）
            if (item.attempt > 1 && retryDelay > 0f)
            {
                yield return new WaitForSeconds(retryDelay);
            }

            if (item.cancelRequested || item.pauseRequested)
            {
                FinishItem(item, false);
                yield break;
            }

            int timeout = item.timeout < 0 ? defaultTimeout : item.timeout;
            string savePath = ResolveSavePath(item);

            // 目录
            try
            {
                string directory = Path.GetDirectoryName(savePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
            }
            catch (Exception e)
            {
                item.error = $"创建目录失败：{e.Message}";
                FinishItem(item, false);
                yield break;
            }

            // 续传起点：只有显式开启、且本地文件小于预期大小时才续传
            // （框架的 MD5 增量更新场景下，本地文件可能是"完整但过期"的旧版本，
            //   此时追加写入会把新文件内容拼到旧文件后面 → 必须禁止续传）
            long resumeSize = 0;
            if (IsResumeEnabled(item) && item.fileSize > 0 && File.Exists(savePath))
            {
                try
                {
                    long localSize = new FileInfo(savePath).Length;
                    if (localSize > 0 && localSize < item.fileSize) resumeSize = localSize;
                }
                catch (Exception e)
                {
                    PLogger.LogWarning($"[Downloader] 读取本地文件大小失败，将整文件重下：{savePath}，{e.Message}");
                    resumeSize = 0;
                }
            }

            item.resumeSize = resumeSize;
            item.totalSize = 0;
            item.downloadedSize = resumeSize;
            item.progress = 0f;
            item.state = DownloadState.Downloading;

            // 创建请求
            UnityWebRequest request = UnityWebRequest.Get(item.url);
            request.timeout = timeout > 0 ? timeout : 0;
            if (resumeSize > 0)
            {
                request.SetRequestHeader("Range", $"bytes={resumeSize}-");
            }
            request.downloadHandler = new DownloadHandlerFile(
                savePath,
                resumeSize > 0,
                resumeSize > 0 ? (Func<bool>)(() => request.responseCode == 206) : null);
            item.request = request;

            OnDownloadStart?.Invoke(item);

            UnityWebRequestAsyncOperation operation = null;
            try
            {
                operation = request.SendWebRequest();
            }
            catch (Exception e)
            {
                item.error = $"发起请求失败：{e.Message}";
            }

            // accountingResume：用于"进度分母"和"大小校验"的续传起点。
            // 服务器忽略 Range 直接返回 200 时会被归零（此时句柄已改为整文件覆盖写入）
            long accountingResume = resumeSize;

            if (operation != null)
            {
                float startTime = Time.realtimeSinceStartup;
                bool contentLengthRead = false;

                while (!operation.isDone)
                {
                    if (item.cancelRequested || item.pauseRequested) break;

                    if (timeout > 0 && Time.realtimeSinceStartup - startTime > timeout)
                    {
                        item.error = $"下载超时（{timeout} 秒）";
                        break;
                    }

                    // 检测"请求续传但服务器不支持"（返回码不是 206），此时要按整文件重下统计
                    accountingResume = HandleResumeMismatch(request, item, accountingResume, ref contentLengthRead);

                    // 响应头到达后才能拿到 Content-Length，用它确定总大小（进度分母）
                    if (!contentLengthRead)
                    {
                        UpdateTotalSize(item, request.GetResponseHeader("Content-Length"), accountingResume);
                        contentLengthRead = true;
                    }

                    RefreshProgress(item, accountingResume);
                    OnDownloadProgress?.Invoke(item);

                    yield return null;
                }

                // 循环因暂停/取消/超时提前退出时可能还没读到响应头，这里补一次
                if (!contentLengthRead)
                {
                    UpdateTotalSize(item, request.GetResponseHeader("Content-Length"), accountingResume);
                }
            }

            bool success = string.IsNullOrEmpty(item.error) && IsRequestSuccess(request);

            // 未提示错误时再校验落盘大小，避免"被截断的下载"被当作成功
            if (success)
            {
                success = VerifyFileSize(item, savePath);
            }

            if (!success && string.IsNullOrEmpty(item.error))
            {
                item.error = string.IsNullOrEmpty(request.error) ? "下载失败" : request.error;
            }

            if (success)
            {
                item.progress = 1f;
                if (item.totalSize > 0) item.downloadedSize = item.totalSize;
            }

            RefreshProgress(item, accountingResume);
            FinishItem(item, success);
        }

        /// <summary>任务收尾：重试判定、事件通知、请求释放、批次统计与启动下一个</summary>
        /// <param name="item">任务</param>
        /// <param name="success">是否成功</param>
        private void FinishItem(DownloadItem item, bool success)
        {
            // ---------- 暂停 ----------
            if (item.pauseRequested && !item.cancelRequested)
            {
                ReleaseRequest(item);
                item.state = DownloadState.Paused;
                RemoveActive(item);
                pausedItems.Add(item);
                OnDownloadPause?.Invoke(item);
                CheckAllFinished();
                return;
            }

            // ---------- 取消 ----------
            if (item.cancelRequested)
            {
                ReleaseRequest(item);
                item.state = DownloadState.Canceled;
                RemoveActive(item);
                CheckAllFinished();
                return;
            }

            // ---------- 成功 ----------
            if (success)
            {
                ReleaseRequest(item);

                try
                {
                    string finalPath = ResolveSavePath(item);
                    if (File.Exists(finalPath))
                    {
                        item.fileSize = new FileInfo(finalPath).Length;
                        item.downloadedSize = item.fileSize;
                    }
                }
                catch (Exception e)
                {
                    PLogger.LogWarning($"[Downloader] 读取下载结果大小失败：{item.savePath}，{e.Message}");
                }

                item.state = DownloadState.Completed;
                item.progress = 1f;
                RemoveActive(item);
                batchCompleted++;

                // 先移除当前任务再抛事件，保证回调里看到的队列状态是准确的
                OnDownloadComplete?.Invoke(item);
                CheckAllFinished();
                return;
            }

            // ---------- 失败：先看是否还能重试 ----------
            int retryCount = item.retryCount < 0 ? defaultRetryCount : item.retryCount;
            if (item.attempt <= retryCount)
            {
                ReleaseRequest(item);
                PLogger.LogWarning($"[Downloader] 下载失败，准备第 {item.attempt} 次重试（最多 {retryCount} 次）：{item.url}，原因：{item.error}");

                item.error = null;
                item.progress = 0f;
                item.state = DownloadState.Waiting;
                RemoveActive(item);          // 内部会尝试启动后续任务
                pendingItems.Add(item);      // 放回队尾，避免阻塞其它任务
                TryStartNextDownloads();     // 若有空档则立即安排重试（协程内会等待重试间隔）
                return;
            }

            ReleaseRequest(item);
            item.state = DownloadState.Failed;
            RemoveActive(item);
            failedItems.Add(item);

            PLogger.LogError($"[Downloader] 下载失败（重试已用尽）：{item.url}，原因：{item.error}");
            OnDownloadError?.Invoke(item);
            CheckAllFinished();
        }

        /// <summary>全部结束后触发批次完成事件（有任务被暂停时不算结束）</summary>
        private void CheckAllFinished()
        {
            if (!IsIdle) return;

            bool allSuccess = failedItems.Count == 0;
            OnAllDownloadsComplete?.Invoke();
            OnAllDownloadsFinished?.Invoke(allSuccess);
        }

        /// <summary>从进行中列表移除并启动后续任务</summary>
        private void RemoveActive(DownloadItem item)
        {
            activeItems.Remove(item);
            TryStartNextDownloads();
        }

        /// <summary>中断并释放请求（连同落盘句柄，避免文件被占用）</summary>
        private void ReleaseRequest(DownloadItem item)
        {
            if (item == null || item.request == null) return;

            UnityWebRequest request = item.request;
            item.request = null;

            try
            {
                if (!request.isDone) request.Abort();
            }
            catch (Exception) { /* Abort 失败不影响后续释放 */ }

            try
            {
                // 先把自定义句柄的文件流关掉，再释放请求，确保文件句柄一定被关闭
                DownloadHandlerFile handler = request.downloadHandler as DownloadHandlerFile;
                if (handler != null) handler.Close();

                request.Dispose();
            }
            catch (Exception e)
            {
                PLogger.LogWarning($"[Downloader] 释放请求失败：{item.url}，{e.Message}");
            }
        }

        /// <summary>计算并刷新进度（分母 = 续传起点 + Content-Length，保证单调递增）</summary>
        private static void RefreshProgress(DownloadItem item, long resumeSize)
        {
            item.downloadedSize = resumeSize + (long)item.request.downloadedBytes;

            if (item.totalSize > 0)
            {
                item.progress = Mathf.Clamp01((float)item.downloadedSize / item.totalSize);
            }
        }

        /// <summary>用响应头里的 Content-Length 计算总大小</summary>
        private static void UpdateTotalSize(DownloadItem item, string contentLengthHeader, long resumeSize)
        {
            long remain;
            if (long.TryParse(contentLengthHeader, out remain) && remain > 0)
            {
                item.totalSize = resumeSize + remain;
            }
        }

        /// <summary>
        /// 检测"请求了续传但服务器不支持"的情况（返回码既不是 0 也不是 206）：
        /// 此时句柄已按覆盖方式整文件写入，进度分母与大小校验的起点必须归零。
        /// </summary>
        /// <returns>修正后的续传起点（正常续传时原样返回）</returns>
        private static long HandleResumeMismatch(UnityWebRequest request, DownloadItem item, long accountingResume, ref bool contentLengthRead)
        {
            if (accountingResume <= 0) return accountingResume;

            long code = request.responseCode;
            if (code == 0 || code == 206) return accountingResume;

            PLogger.LogWarning($"[Downloader] 服务器未响应 206（实际 {code}），已改为整文件重新下载：{item.url}");

            item.resumeSize = 0;
            item.totalSize = 0;
            contentLengthRead = false;   // 让外层用新的 Content-Length 重新计算总大小
            return 0;
        }

        /// <summary>校验落盘大小是否与预期一致（预期未知时不校验）</summary>
        private bool VerifyFileSize(DownloadItem item, string savePath)
        {
            long expected = item.totalSize > 0 ? item.totalSize : item.fileSize;
            if (expected <= 0) return true;

            try
            {
                long actual = new FileInfo(savePath).Length;
                if (actual != expected)
                {
                    item.error = $"文件大小不匹配（预期 {expected}，实际 {actual}）";
                    return false;
                }
            }
            catch (Exception e)
            {
                item.error = $"校验文件大小失败：{e.Message}";
                return false;
            }

            return true;
        }

        /// <summary>是否对该任务开启断点续传</summary>
        private bool IsResumeEnabled(DownloadItem item)
        {
            return item.enableResume.HasValue ? item.enableResume.Value : enableResume;
        }

        /// <summary>请求是否成功</summary>
        private static bool IsRequestSuccess(UnityWebRequest request)
        {
            if (request == null) return false;

#if UNITY_2020_2_OR_NEWER
            return request.result == UnityWebRequest.Result.Success;
#else
            return !request.isHttpError && !request.isNetworkError;
#endif
        }

        /// <summary>解析实际落盘路径：savePath 为目录时拼接文件名</summary>
        private static string ResolveSavePath(DownloadItem item)
        {
            string path = item.savePath;

            if (string.IsNullOrEmpty(path))
            {
                return Path.Combine(Application.persistentDataPath, item.fileName);
            }

            if (path.EndsWith("/") || path.EndsWith("\\") || Directory.Exists(path))
            {
                string name = string.IsNullOrEmpty(item.fileName) ? GetFileNameFromUrl(item.url) : item.fileName;
                return Path.Combine(path, name);
            }

            return path;
        }

        /// <summary>从 url 推导文件名（去掉 ?query 与 #fragment）</summary>
        private static string GetFileNameFromUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return "download";

            string path = url;

            int queryIndex = path.IndexOf('?');
            if (queryIndex >= 0) path = path.Substring(0, queryIndex);

            int fragmentIndex = path.IndexOf('#');
            if (fragmentIndex >= 0) path = path.Substring(0, fragmentIndex);

            string name = Path.GetFileName(path);
            return string.IsNullOrEmpty(name) ? "download" : name;
        }
    }
}
