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
        /// Excel 表的运行时目录（相对 <see cref="Application.persistentDataPath"/>）。
        /// <para>历史写法以 "/" 开头（例如 "/Data/BinaryData/"）时仍按相对路径处理：
        /// Android/iOS 的根分区是只读的，"/Data/..." 会被系统拒绝（Read-only file system）。</para>
        /// <para>只有 Windows 盘符（C:\...）与 UNC（\\server\share）才当作绝对路径。</para>
        /// </summary>
        public string savePath = "Data/BinaryData";

        /// <summary>
        /// 存储文件后缀
        /// </summary>
        public string extension = ExcelBinaryFormat.FileExtension;

        /// <summary>
        /// 首次启动把 StreamingAssets 中的表安装到可写目录。
        /// <para>Android/WebGL 由 <see cref="InitAsync"/> 通过索引清单 + UnityWebRequest 安装；桌面端同步复制。</para>
        /// </summary>
        public bool installFromStreamingAssets = true;

        /// <summary>实际使用的可写目录；<see cref="Init"/> / <see cref="InitAsync"/> 之后有效。</summary>
        public string SaveDirectory { get; private set; }

        public BinaryDataMgr BinaryDataMgr { get => binaryDataMgr; }

        /// <summary>
        /// 同步初始化入口（保留给桌面/测试）。Android/WebGL 请使用 <see cref="InitAsync"/>，否则无法读取包内 StreamingAssets。
        /// </summary>
        public void Init()
        {
            PrepareDirectory();
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
            PrepareDirectory();
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

        /// <summary>
        /// 解析配置目录并创建它。可写目录只来自 persistentDataPath：
        /// 以免在移动端把 "/Data/..." 当成系统根目录（只读）而中止启动。
        /// </summary>
        private void PrepareDirectory()
        {
            SaveDirectory = ResolveSaveDirectory(savePath);
            try
            {
                Directory.CreateDirectory(SaveDirectory);
            }
            catch (Exception e)
            {
                throw new IOException(
                    $"无法创建 Excel 表目录：{SaveDirectory}（savePath = \"{savePath}\"）。"
                    + "savePath 应使用相对路径（推荐 \"Data/BinaryData\"），目录必须位于 Application.persistentDataPath 下。", e);
            }
        }

        private void FinishInit()
        {
            binaryDataMgr = BinaryDataMgr.Instance;
            binaryDataMgr.InitData(SaveDirectory, extension);
        }

        /// <summary>
        /// 把 savePath 解析成可写目录。
        /// <para>相对路径、以 "/" 开头或以反斜杠开头的历史写法都相对 persistentDataPath；
        /// 仅 Windows 盘符 / UNC 路径保持绝对。已经是 persistentDataPath 绝对路径的值原样返回（幂等）。</para>
        /// </summary>
        public static string ResolveSaveDirectory(string configuredPath)
        {
            if (string.IsNullOrWhiteSpace(configuredPath)) return ExcelBinaryFormat.PersistentDirectory;

            string value = configuredPath.Trim();
            if (IsWindowsAbsolutePath(value)) return value;
            if (IsUnderPersistentDataPath(value)) return value;

            string relative = value.Replace('\\', '/').TrimStart('/');
            if (relative.Length == 0) return ExcelBinaryFormat.PersistentDirectory;

            string[] segments = relative.Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                if (segments[i] == "." || segments[i] == "..")
                    throw new ArgumentException("savePath 不能包含 . 或 .. 路径段：" + configuredPath, nameof(configuredPath));
            }

            return Path.Combine(Application.persistentDataPath, string.Join(Path.DirectorySeparatorChar.ToString(), segments));
        }

        private static bool IsWindowsAbsolutePath(string value)
        {
            // C:\... / C:/... / \\server\share
            if (value.Length >= 3 && value[1] == ':' && char.IsLetter(value[0])
                && (value[2] == '\\' || value[2] == '/')) return true;
            return value.Length >= 2 && value[0] == '\\' && value[1] == '\\';
        }

        private static bool IsUnderPersistentDataPath(string value)
        {
            string root = Application.persistentDataPath;
            if (string.IsNullOrEmpty(root)) return false;
            StringComparison comparison = Application.platform == RuntimePlatform.WindowsPlayer
                                          || Application.platform == RuntimePlatform.WindowsEditor
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (!value.StartsWith(root, comparison)) return false;
            if (value.Length == root.Length) return true;
            char next = value[root.Length];
            return next == '/' || next == '\\';
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
                Directory.CreateDirectory(SaveDirectory);
                int copied = 0;
                foreach (string file in Directory.GetFiles(source, "*" + extension, SearchOption.TopDirectoryOnly))
                {
                    string target = Path.Combine(SaveDirectory, Path.GetFileName(file));
                    if (File.Exists(target) && new FileInfo(target).Length == new FileInfo(file).Length) continue;
                    File.Copy(file, target, true);
                    copied++;
                }
                if (copied > 0) Debug.Log($"[BinaryDataMgrInit] 已从 StreamingAssets 安装 {copied} 个 Excel 表到 {SaveDirectory}");
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
                    string target = Path.Combine(SaveDirectory, fileName);
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

                Debug.Log($"[BinaryDataMgrInit] 已从 StreamingAssets 安装 {copied} 个 Excel 表（跳过 {skipped} 个未变化）到 {SaveDirectory}"
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
