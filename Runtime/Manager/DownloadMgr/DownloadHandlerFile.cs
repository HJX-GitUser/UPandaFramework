using System;
using System.IO;
using UnityEngine.Networking;

namespace UPandaGF
{
    /// <summary>
    /// 下载落盘句柄（DownloadHandlerScript 扩展版）。
    ///
    /// 相比原始实现，解决了 4 个问题：
    /// 1) 延迟打开文件：在收到第一块数据时才创建文件。原实现依赖 ReceiveContentLengthHeader，
    ///    而服务端使用分块传输（chunked）或未返回 Content-Length 时该回调不会触发，
    ///    结果 fileStream 始终为 null，整份数据都被丢弃；
    /// 2) 续传安全性：可传入 shouldAppend 回调，只有服务端真的返回 206（Partial Content）时才追加写入，
    ///    否则改为覆盖写入，避免"服务器不支持 Range 却把完整响应追加到旧文件后面"导致文件损坏；
    /// 3) 异常安全：打开/写入失败全部捕获并记录（WriteFailed / LastError），
    ///    通过返回 false 让 Unity 中断下载，而不是让异常从下载回调里抛出；
    /// 4) 句柄可主动释放：提供 Close()，供外部在取消/中断请求前显式关闭文件流，避免文件被占用。
    /// </summary>
    public class DownloadHandlerFile : DownloadHandlerScript
    {
        /// <summary>落盘路径</summary>
        private readonly string filePath;

        /// <summary>是否允许以追加方式续传（最终是否追加还要看 shouldAppend 的返回值）</summary>
        private readonly bool append;

        /// <summary>续传确认回调：返回 true 才追加写入（通常用于判断响应码是否为 206）</summary>
        private readonly Func<bool> shouldAppend;

        /// <summary>文件流（延迟创建）</summary>
        private FileStream fileStream;

        /// <summary>是否已调用 Close()（之后不再接受数据）</summary>
        private bool closed;

        /// <summary>是否发生了打开/写入失败</summary>
        public bool WriteFailed { get; private set; }

        /// <summary>失败原因（无失败时为 null）</summary>
        public string LastError { get; private set; }

        /// <summary>本次已写入的字节数（续传时只统计本次新增部分）</summary>
        public long WrittenBytes { get; private set; }

        /// <summary>服务端返回的 Content-Length（分块传输时为 0）</summary>
        public long ContentLength { get; private set; }

        /// <summary>最终是否以追加方式写入（打开文件后才有意义）</summary>
        public bool AppendMode { get; private set; }

        /// <summary>落盘路径</summary>
        public string FilePath { get { return filePath; } }

        /// <param name="path">落盘路径（父目录不存在会自动创建）</param>
        /// <param name="append">是否允许续传（追加写入）</param>
        /// <param name="shouldAppend">续传确认回调，返回 false 时改为覆盖写入；为 null 表示不校验</param>
        public DownloadHandlerFile(string path, bool append = false, Func<bool> shouldAppend = null)
            : base(new byte[1024 * 8])
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("下载落盘路径不能为空", "path");
            }

            this.filePath = path;
            this.append = append;
            this.shouldAppend = shouldAppend;
        }

        /// <summary>服务端给出 Content-Length 时记录（注意：分块传输不会触发此回调）</summary>
        protected override void ReceiveContentLengthHeader(ulong contentLength)
        {
            ContentLength = (long)contentLength;
        }

        /// <summary>收到数据块：首次收到时才打开文件，然后写入</summary>
        protected override bool ReceiveData(byte[] data, int dataLength)
        {
            if (WriteFailed || closed) return false;
            if (data == null || dataLength <= 0) return true;   // 空块不算失败

            if (!OpenFile()) return false;

            try
            {
                fileStream.Write(data, 0, dataLength);
                WrittenBytes += dataLength;
                return true;
            }
            catch (Exception e)
            {
                WriteFailed = true;
                LastError = $"写入文件失败：{e.Message}";
                PLogger.LogError($"[DownloadHandlerFile] {LastError}（{filePath}）");
                return false;   // 返回 false 会让 Unity 中断下载
            }
        }

        /// <summary>下载正常结束</summary>
        protected override void CompleteContent()
        {
            Close();
        }

        /// <summary>
        /// 刷新并关闭文件句柄。可重复调用。
        /// 取消/中断下载前请显式调用，确保文件不会被占用。
        /// </summary>
        public void Close()
        {
            closed = true;

            if (fileStream == null) return;

            try
            {
                fileStream.Flush();
                fileStream.Close();
            }
            catch (Exception e)
            {
                PLogger.LogWarning($"[DownloadHandlerFile] 关闭文件失败：{e.Message}（{filePath}）");
            }
            finally
            {
                fileStream = null;
            }
        }

        /// <summary>
        /// 进度一律返回 0：进度由 Downloader 统一计算
        /// （续传时真实进度 = 续传起点 + 本次下载字节数，句柄自身拿不到续传起点）。
        /// </summary>
        protected override float GetProgress()
        {
            return 0f;
        }

        /// <summary>打开文件（父目录不存在时创建）；已打开则直接返回 true</summary>
        private bool OpenFile()
        {
            if (fileStream != null) return true;
            if (closed) return false;

            try
            {
                string directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // 只有"请求了续传"且"服务端确实按 Range 响应"时才追加写入，
                // 否则一律覆盖写入，防止完整响应被追加到旧文件后面
                bool doAppend = append && (shouldAppend == null || shouldAppend());
                AppendMode = doAppend;

                fileStream = new FileStream(
                    filePath,
                    doAppend ? FileMode.Append : FileMode.Create,
                    FileAccess.Write,
                    FileShare.Read,
                    1024 * 8);

                return true;
            }
            catch (Exception e)
            {
                WriteFailed = true;
                LastError = $"打开文件失败：{e.Message}";
                PLogger.LogError($"[DownloadHandlerFile] {LastError}（{filePath}）");
                fileStream = null;
                return false;
            }
        }
    }
}
