using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace UPandaGF
{
    public class DownloadItem
    {
        public string url;          // 下载地址
        public string savePath;    // 保存路径
        public string fileName;    // 文件名
        public long fileSize;      // 文件大小
        public long downloadedSize; // 已下载大小
        public float progress;     // 下载进度
        public DownloadState state; // 下载状态
        public string error;       // 错误信息
        public UnityWebRequest request; // 请求对象
    }

    public enum DownloadState
    {
        Waiting,    // 等待中
        Downloading,// 下载中
        Completed,  // 完成
        Failed,     // 失败
        Paused      // 暂停
    }


    /*
    功能特性：
        断点续传：支持网络异常后的断点续传
        队列管理：支持多任务队列下载
        进度回调：实时下载进度反馈
        异步支持：提供async/await接口
        事件驱动：完整的事件通知系统
        错误处理：完善的错误处理和状态管理
        内存优化：分块下载，避免大文件内存占用
    注意事项：
        需要在Unity中启用网络权限
        大文件下载建议使用分块下载
        注意处理网络异常和磁盘空间不足的情况
        在WebGL平台有限制，需要配置CORS

    后续待优化：添加重试机制、速度限制、优先级管理。
     */
    public class Downloader : MonoBehaviour
    {
        // 下载队列
        private Queue<DownloadItem> downloadQueue = new Queue<DownloadItem>();
        private DownloadItem currentItem;
        private bool isDownloading = false;

        [Header("最大同时下载数")]
        public int maxConcurrentDownloads = 1;// 
        private int activeDownloadCount = 0;

        // 回调事件
        public event Action<DownloadItem> OnDownloadStart;
        public event Action<DownloadItem> OnDownloadProgress;
        public event Action<DownloadItem> OnDownloadComplete;
        public event Action<DownloadItem> OnDownloadError;
        public event Action<DownloadItem> OnDownloadPause;
        public event Action OnAllDownloadsComplete;

        protected  void Awake()
        {
            Debug.Log("Downloader Init!!!");
        }

        /// <summary>
        /// 添加下载任务
        /// </summary>
        public string AddDownload(string url, string savePath, string fileName = null)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                fileName = Path.GetFileName(url);
            }

            DownloadItem item = new DownloadItem
            {
                url = url,
                savePath = savePath,
                fileName = fileName,
                state = DownloadState.Waiting,
                progress = 0f,
                downloadedSize = 0,
                fileSize = 0
            };

            downloadQueue.Enqueue(item);
            TryStartNextDownload();

            return $"{savePath}/{fileName}";
        }

        /// <summary>
        /// 批量添加下载任务
        /// </summary>
        public void AddBatchDownloads(List<DownloadItem> items)
        {
            foreach (var item in items)
            {
                item.state = DownloadState.Waiting;
                downloadQueue.Enqueue(item);
            }
            TryStartNextDownload();
        }

        /// <summary>
        /// 开始下载
        /// </summary>
        private void TryStartNextDownload()
        {
            if (activeDownloadCount >= maxConcurrentDownloads || downloadQueue.Count == 0)
                return;

            StartCoroutine(StartDownload());
        }

        /// <summary>
        /// 下载协程
        /// </summary>
        private IEnumerator StartDownload()
        {
            if (downloadQueue.Count == 0) yield break;

            activeDownloadCount++;
            DownloadItem item = downloadQueue.Dequeue();
            currentItem = item;
            item.state = DownloadState.Downloading;

            // 确保目录存在
            string directory = Path.GetDirectoryName(item.savePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // 检查是否支持断点续传
            long startSize = 0;
            if (File.Exists(item.savePath))
            {
                FileInfo fileInfo = new FileInfo(item.savePath);
                startSize = fileInfo.Length;
            }

            // 创建请求
            UnityWebRequest request;
            if (startSize > 0)
            {
                // 断点续传
                request = UnityWebRequest.Get(item.url);
                request.SetRequestHeader("Range", $"bytes={startSize}-");
            }
            else
            {
                request = UnityWebRequest.Get(item.url);
            }

            request.downloadHandler = new DownloadHandlerFile(item.savePath, startSize > 0);
            item.request = request;

            // 发送请求
            OnDownloadStart?.Invoke(item);
            var operation = request.SendWebRequest();

            // 进度更新
            while (!operation.isDone)
            {
                if (item.state == DownloadState.Paused)
                {
                    request.Abort();
                    OnDownloadPause?.Invoke(item);
                    activeDownloadCount--;
                    yield break;
                }
#if UNITY_2020_1_OR_NEWER
                if (request.result == UnityWebRequest.Result.ProtocolError ||
                    request.result == UnityWebRequest.Result.ConnectionError)
#else
                if (request.isHttpError || request.isNetworkError)
#endif
                {
                    break;
                }

                item.downloadedSize = startSize + (long)(request.downloadedBytes);
                if (request.downloadedBytes > 0)
                {
                    item.progress = (float)item.downloadedSize / ((float)item.downloadedSize + (float)request.downloadedBytes);
                }

                OnDownloadProgress?.Invoke(item);
                yield return null;
            }

            // 处理结果
#if UNITY_2020_1_OR_NEWER
                if (request.result == UnityWebRequest.Result.ProtocolError ||
                    request.result == UnityWebRequest.Result.ConnectionError)
#else
            if (request.isHttpError || request.isNetworkError)
#endif
            {
                item.state = DownloadState.Failed;
                item.error = request.error;
                OnDownloadError?.Invoke(item);
            }
            else
            {
                item.state = DownloadState.Completed;
                item.progress = 1f;

                // 获取文件大小
                if (File.Exists(item.savePath))
                {
                    FileInfo fileInfo = new FileInfo(item.savePath);
                    item.fileSize = fileInfo.Length;
                    item.downloadedSize = item.fileSize;
                }

                OnDownloadComplete?.Invoke(item);
            }

            request.Dispose();
            currentItem = null;
            activeDownloadCount--;

            // 检查是否所有下载完成
            if (downloadQueue.Count == 0 && activeDownloadCount == 0)
            {
                OnAllDownloadsComplete?.Invoke();
            }
            else
            {
                // 开始下一个下载
                TryStartNextDownload();
            }
        }

        /// <summary>
        /// 暂停当前下载
        /// </summary>
        public void PauseCurrentDownload()
        {
            if (currentItem != null && currentItem.state == DownloadState.Downloading)
            {
                currentItem.state = DownloadState.Paused;
            }
        }

        /// <summary>
        /// 继续下载
        /// </summary>
        public void ResumeDownload(string savePath)
        {
            // 重新添加到队列头部
            var items = new List<DownloadItem>(downloadQueue.ToArray());
            downloadQueue.Clear();

            // 创建续传任务
            DownloadItem resumeItem = new DownloadItem
            {
                savePath = savePath,
                state = DownloadState.Waiting
            };

            downloadQueue.Enqueue(resumeItem);
            foreach (var item in items)
            {
                downloadQueue.Enqueue(item);
            }

            TryStartNextDownload();
        }

        /// <summary>
        /// 取消所有下载
        /// </summary>
        public void CancelAllDownloads()
        {
            downloadQueue.Clear();
            if (currentItem != null && currentItem.request != null)
            {
                currentItem.request.Abort();
            }
            activeDownloadCount = 0;
        }

        /// <summary>
        /// 获取当前下载进度
        /// </summary>
        public float GetTotalProgress()
        {
            if (downloadQueue.Count == 0 && currentItem == null)
                return 1f;

            // 计算总体进度
            float totalProgress = 0f;
            int itemCount = downloadQueue.Count + (currentItem != null ? 1 : 0);

            if (currentItem != null)
            {
                totalProgress += currentItem.progress;
            }

            // 等待中的任务进度为0
            return totalProgress / Mathf.Max(1, itemCount);
        }

        /// <summary>
        /// 异步下载方法
        /// </summary>
        public async Task<bool> DownloadAsync(string url, string savePath, IProgress<float> progress = null)
        {
            var tcs = new TaskCompletionSource<bool>();

            string taskId = AddDownload(url, savePath);

            OnDownloadComplete += (item) =>
            {
                if ($"{item.savePath}/{item.fileName}" == taskId)
                    tcs.TrySetResult(true);
            };

            OnDownloadError += (item) =>
            {
                if ($"{item.savePath}/{item.fileName}" == taskId)
                    tcs.TrySetResult(false);
            };

            OnDownloadProgress += (item) =>
            {
                if ($"{item.savePath}/{item.fileName}" == taskId)
                    progress?.Report(item.progress);
            };

            return await tcs.Task;
        }

        protected  void OnDestroy()
        {
            CancelAllDownloads();
        }
    }
}
