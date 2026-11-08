using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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

        /// <summary>
        /// 首次启动把 StreamingAssets 中的表安装到可写目录。
        /// <para>Android/WebGL 由 <see cref="InitAsync"/> 通过索引清单 + UnityWebRequest 安装；桌面端同步复制。</para>
        /// </summary>
        public bool installFromStreamingAssets = true;

        public BinaryDataMgr BinaryDataMgr { get => binaryDataMgr; }

        /// <summary>
        /// 同步初始化入口（保留给桌面/测试）。Android/WebGL 请使用 <see cref="InitAsync"/>，否则无法读取包内 StreamingAssets。
        /// </summary>
        public void Init()
        {
            savePath = ResolveDirectory(savePath);
            Directory.CreateDirectory(savePath);
#if UNITY_ANDROID || UNITY_WEBGL
            if (installFromStreamingAssets)
                Debug.LogWarning("[BinaryDataMgrInit] 移动端/WebGL 不能同步安装 StreamingAssets；请由启动链调用 InitAsync()。");
#else
            if (installFromStreamingAssets) TryInstallFromStreamingAssets();
#endif
            FinishInit();
        }

        /// <summary>
        /// 启动链使用的异步初始化：Android/WebGL 会先按索引把 StreamingAssets 中的表安装到 persistentDataPath。
        /// </summary>
        public async Task InitAsync(CancellationToken token = default)
        {
            savePath = ResolveDirectory(savePath);
            Directory.CreateDirectory(savePath);
            if (installFromStreamingAssets)
            {
#if UNITY_ANDROID || UNITY_WEBGL
                await TryInstallFromStreamingAssetsAsync(token);
#else
                TryInstallFromStreamingAssets();
                await Task.CompletedTask;
#endif
            }
            token.ThrowIfCancellationRequested();
            FinishInit();
        }

        private void FinishInit()
        {
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
            Debug.LogWarning("[BinaryDataMgrInit] 当前平台请使用 InitAsync() 安装 StreamingAssets 表。");
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
                if (copied > 0) Debug.Log($"[BinaryDataMgrInit] 已从 StreamingAssets 安装 {copied} 个 Excel 表到 {savePath}");
            }
            catch (Exception e)
            {
                Debug.LogError("[BinaryDataMgrInit] 安装 Excel 表失败：" + e.Message);
            }
#endif
        }

#if UNITY_ANDROID || UNITY_WEBGL
        private async Task TryInstallFromStreamingAssetsAsync(CancellationToken token)
        {
            try
            {
                string index = await StreamingAssetsLoader.LoadTextFileAsync(ExcelBinaryFormat.IndexRelativePath);
                if (string.IsNullOrEmpty(index))
                {
                    Debug.LogWarning("[BinaryDataMgrInit] 未找到随包表索引 " + ExcelBinaryFormat.IndexRelativePath
                        + "。请在打包前运行 ExcelTool 的“重建二进制表索引”，或改用 AB 安装表。");
                    return;
                }

                int copied = 0, skipped = 0, failed = 0;
                string[] lines = index.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string raw in lines)
                {
                    token.ThrowIfCancellationRequested();
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    string[] parts = line.Split('|');
                    string fileName = Path.GetFileName(parts[0].Trim());
                    if (!fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) continue;

                    long expectedLength = -1;
                    if (parts.Length > 1) long.TryParse(parts[1].Trim(), out expectedLength);
                    string target = Path.Combine(savePath, fileName);
                    if (expectedLength >= 0 && File.Exists(target) && new FileInfo(target).Length == expectedLength)
                    {
                        skipped++;
                        continue;
                    }

                    byte[] bytes = await StreamingAssetsLoader.LoadBinaryDataAsync(
                        "/" + ExcelBinaryFormat.RelativeFolder + "/" + fileName);
                    if (bytes == null)
                    {
                        failed++;
                        Debug.LogWarning("[BinaryDataMgrInit] 表中缺少文件：" + fileName);
                        continue;
                    }
                    File.WriteAllBytes(target, bytes);
                    copied++;
                }

                if (Application.platform == RuntimePlatform.WebGLPlayer)
                    await PlatformStorage.FlushAsync();

                Debug.Log($"[BinaryDataMgrInit] 已从 StreamingAssets 安装 {copied} 个 Excel 表（跳过 {skipped} 个未变化）到 {savePath}"
                    + (failed > 0 ? "，失败 " + failed + " 个" : ""));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                Debug.LogError("[BinaryDataMgrInit] 安装 Excel 表失败：" + e);
            }
        }
#endif
    }
}
