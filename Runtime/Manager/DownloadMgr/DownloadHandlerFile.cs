using System.IO;
using UnityEngine.Networking;

namespace UPandaGF
{
    /// 扩展DownloadHandler支持断点续传
    public class DownloadHandlerFile : DownloadHandlerScript
    {
        private string filePath;
        private FileStream fileStream;
        private bool append;

        public DownloadHandlerFile(string path, bool append = false) : base(new byte[1024 * 8])
        {
            this.filePath = path;
            this.append = append;
        }

        protected override void ReceiveContentLengthHeader(ulong contentLength)
        {
            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            fileStream = new FileStream(filePath, append ? FileMode.Append : FileMode.Create, FileAccess.Write);
        }

        protected override bool ReceiveData(byte[] data, int dataLength)
        {
            if (data == null || data.Length == 0 || fileStream == null)
                return false;

            fileStream.Write(data, 0, dataLength);
            return true;
        }

        protected override void CompleteContent()
        {
            fileStream?.Close();
            fileStream = null;
        }

        protected override float GetProgress()
        {
            return 0f;
        }
    }
}

