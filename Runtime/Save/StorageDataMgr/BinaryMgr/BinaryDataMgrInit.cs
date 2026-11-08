using System.IO;
using UnityEngine;

namespace UPandaGF
{
    public class BinaryDataMgrInit : MonoBehaviour
    {
        private BinaryDataMgr binaryDataMgr;
        /// <summary>
        /// Excel 表的运行时目录（相对 persistentDataPath，或以 / 开头的路径会被当作绝对路径）。
        /// </summary>
        public string savePath = "/Data/BinaryData/";

        /// <summary>
        /// 存储文件后缀
        /// </summary>
        public string extension = ExcelBinaryFormat.FileExtension;

        /// <summary>桌面/主机首次启动时把 StreamingAssets 中的表复制到可写目录；移动端/WebGL 需走各自的安装流程。</summary>
        public bool installFromStreamingAssets = true;

        public BinaryDataMgr BinaryDataMgr { get => binaryDataMgr; }

        public void Init()
        {
            savePath = ResolveDirectory(savePath);
            if (installFromStreamingAssets) TryInstallFromStreamingAssets();
            Directory.CreateDirectory(savePath);

            binaryDataMgr = BinaryDataMgr.Instance;
            binaryDataMgr.InitData(savePath, extension);
        }

        private static string ResolveDirectory(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return ExcelBinaryFormat.PersistentDirectory;
            if (Path.IsPathRooted(value)) return value;
            return Path.Combine(Application.persistentDataPath, value.TrimStart('/', '\\'));
        }

        private void TryInstallFromStreamingAssets()
        {
#if UNITY_ANDROID || UNITY_WEBGL
            PLogger.LogWarning("[BinaryDataMgrInit] 当前平台 StreamingAssets 不是普通目录，跳过自动安装；请用索引清单/AB 把表安装到：" + savePath);
#else
            try
            {
                string source = ExcelBinaryFormat.StreamingAssetsDirectory;
                if (!Directory.Exists(source)) return;
                Directory.CreateDirectory(savePath);
                int copied = 0;
                foreach (string file in Directory.GetFiles(source, "*" + extension, SearchOption.TopDirectoryOnly))
                {
                    string target = Path.Combine(savePath, Path.GetFileName(file));
                    if (File.Exists(target) && new FileInfo(target).Length == new FileInfo(file).Length) continue;
                    File.Copy(file, target, true);
                    copied++;
                }
                if (copied > 0) PLogger.Log($"[BinaryDataMgrInit] 已从 StreamingAssets 安装 {copied} 个 Excel 表到 {savePath}");
            }
            catch (System.Exception e)
            {
                PLogger.LogError("[BinaryDataMgrInit] 安装 Excel 表失败：" + e.Message);
            }
#endif
        }
    }
}
