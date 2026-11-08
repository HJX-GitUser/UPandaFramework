using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace UPandaGF.GFEditor
{

[System.Serializable]
public class FTPUpLoadABConfig
{
    //[Tooltip("上传AB包地址")]
    public string UpABURL;

    /// <summary>
    /// 资源清单（assetData.assetref）的上传地址：约定为 {remoteURL}/AssetBundles/，与 UPGameRoot.UpdateAssets 的读取位置一致
    /// </summary>
    public string ManifestABURL;

    //FTP通信凭证
    //ftp用户名
    public string Ftp_UserName;
    //ftp密码（只在内存中使用，不写入 EditorPrefs；可通过环境变量 UPANDA_FTP_PASSWORD 提供）
    public string Ftp_Password;

    public FTPUpLoadABConfig(string BuidTarget)
    {
        UpABURL = $"ftp://127.0.0.1/AssetBundles/{BuidTarget}/";
        ManifestABURL = "ftp://127.0.0.1/AssetBundles/";
        Ftp_UserName = "Admin";
        Ftp_Password = string.Empty;
    }
}

/// <summary>批量上传结果：成功/失败文件数与第一条错误，供上传页做“全部成功才提交清单”的判断。</summary>
public struct FTPUploadResult
{
    public int SuccessCount;
    public int FailedCount;
    public string FirstError;
    public bool AllSucceeded => FailedCount == 0 && SuccessCount > 0;
}

public class UpLoadFTP
{
    private const string UrlKey = "UpLoadABEditor_UpABURL";
    private const string ManifestUrlKey = "UpLoadABEditor_ManifestABURL";
    private const string UserNameKey = "UpLoadABEditor_Ftp_UserName";
    /// <summary>旧版本明文密码键：只用于一次性迁移读取，读取后立即删除。</summary>
    private const string LegacyPasswordKey = "UpLoadABEditor_Ftp_Password";
    /// <summary>密码不落盘；需要免输入时用该环境变量提供。</summary>
    public const string PasswordEnvironmentVariable = "UPANDA_FTP_PASSWORD";

    private FTPUpLoadABConfig Config;

    public UpLoadFTP(string BuidTarget)
    {
        if (Config == null)
        {
            Config = new FTPUpLoadABConfig(BuidTarget);
            Config.UpABURL = EditorPrefs.GetString(UrlKey, Config.UpABURL);
            Config.ManifestABURL = EditorPrefs.GetString(ManifestUrlKey, Config.ManifestABURL);
            Config.Ftp_UserName = EditorPrefs.GetString(UserNameKey, Config.Ftp_UserName);

            // 迁移：旧版本把 FTP 密码明文写进 EditorPrefs（注册表/plist）。读到本次会话内存后删除该键。
            string legacyPassword = EditorPrefs.GetString(LegacyPasswordKey, string.Empty);
            if (!string.IsNullOrEmpty(legacyPassword))
            {
                Config.Ftp_Password = legacyPassword;
                EditorPrefs.DeleteKey(LegacyPasswordKey);
                Debug.LogWarning("[UpLoadFTP] 检测到旧版本保存在 EditorPrefs 的 FTP 明文密码，已迁移到本次会话内存并删除该键。" +
                                 "后续请每次输入，或使用环境变量 " + PasswordEnvironmentVariable + "。");
            }
            else
            {
                Config.Ftp_Password = Environment.GetEnvironmentVariable(PasswordEnvironmentVariable) ?? string.Empty;
            }
        }
    }

    public FTPUpLoadABConfig GetConfig => Config;

    /// <summary>用户名和密码都为空时 FTP 服务器通常只允许匿名；UI 用该属性给出提示。</summary>
    public bool HasCredentials => !string.IsNullOrEmpty(Config.Ftp_UserName) && !string.IsNullOrEmpty(Config.Ftp_Password);

    public void ResetData(string BuidTarget)
    {
        Config = new FTPUpLoadABConfig(BuidTarget);
        SaveData();
    }

    /// <summary>只持久化地址与用户名；密码永远不写入 EditorPrefs，并顺手清理旧版本的明文键。</summary>
    public void SaveData()
    {
        EditorPrefs.SetString(UrlKey, Config.UpABURL);
        EditorPrefs.SetString(ManifestUrlKey, Config.ManifestABURL);
        EditorPrefs.SetString(UserNameKey, Config.Ftp_UserName);
        EditorPrefs.DeleteKey(LegacyPasswordKey);
        Debug.Log("FTP配置已保存（密码不落盘；可用环境变量 " + PasswordEnvironmentVariable + " 提供）");
    }

    /// <summary>
    /// 顺序上传目录内的 AB 包与对比文件，全部完成后才返回；调用方在成功后上传清单。
    /// </summary>
    /// <param name="localPath">本地 AB 输出目录</param>
    /// <param name="onProgress">(已处理数, 总数, 文件名)；在主线程回调</param>
    public async Task<FTPUploadResult> UpLoadAllABFileAsync(string localPath, Action<int, int, string> onProgress = null, CancellationToken token = default)
    {
        var result = new FTPUploadResult();
        if (!Directory.Exists(localPath))
        {
            result.FirstError = "路径不存在：" + localPath;
            Debug.LogError(result.FirstError);
            return result;
        }

        FileInfo[] files = new DirectoryInfo(localPath).GetFiles();
        int total = 0;
        foreach (FileInfo file in files) if (IsABAssets(file)) total++;

        int processed = 0;
        foreach (FileInfo file in files)
        {
            if (!IsABAssets(file)) continue;
            if (token.IsCancellationRequested) break;
            onProgress?.Invoke(processed, total, file.Name);
            string error = await UploadAsync(file.FullName, file.Name, Config.UpABURL);
            if (error == null) result.SuccessCount++;
            else
            {
                result.FailedCount++;
                if (result.FirstError == null) result.FirstError = file.Name + "：" + error;
                Debug.LogError("[UpLoadFTP] 上传失败 " + file.Name + "：" + error);
            }
            processed++;
            onProgress?.Invoke(processed, total, file.Name);
        }

        Debug.Log($"[UpLoadFTP] AB 上传结束：成功 {result.SuccessCount}，失败 {result.FailedCount}，目标 {Config.UpABURL}");
        return result;
    }

    /// <summary>上传单个文件到指定地址（用于资源清单 assetData.assetref）；返回 true 表示成功。</summary>
    public async Task<bool> UpLoadFileAsync(string localFilePath, string remoteURL)
    {
        if (string.IsNullOrEmpty(localFilePath) || !File.Exists(localFilePath))
        {
            Debug.LogError($"文件不存在，上传已跳过：{localFilePath}\n请先在资源分类窗口点「更新配置」生成清单。");
            return false;
        }
        if (string.IsNullOrEmpty(remoteURL))
        {
            Debug.LogError("上传地址为空，上传已跳过");
            return false;
        }

        string error = await UploadAsync(localFilePath, Path.GetFileName(localFilePath), remoteURL);
        if (error != null)
        {
            Debug.LogError("[UpLoadFTP] 清单上传失败：" + error);
            return false;
        }
        Debug.Log("[UpLoadFTP] 清单上传成功：" + remoteURL);
        return true;
    }

    /// <summary>旧接口：不等待结果直接返回，清单可能在 AB 未传完时提交。保留仅为源码兼容。</summary>
    [Obsolete("请使用 UpLoadAllABFileAsync 并在全部成功后提交清单。", false)]
    public void UpLoadAllABFile(string LocalABPath)
    {
        _ = UpLoadAllABFileAsync(LocalABPath);
    }

    /// <summary>旧接口：不等待结果直接返回。保留仅为源码兼容。</summary>
    [Obsolete("请使用 UpLoadFileAsync 等待上传结果。", false)]
    public void UpLoadFile(string localFilePath, string remoteURL)
    {
        _ = UpLoadFileAsync(localFilePath, remoteURL);
    }

    private static bool IsABAssets(FileInfo fileInfo)
    {
        bool isABAssets = false;
        if (fileInfo.Extension == "" || fileInfo.Extension == ".txt")
        {
            isABAssets = true;
        }
        return isABAssets;
    }

    /// <summary>
    /// 在后台线程执行 FTP 上传（同步 FtpWebRequest），返回 null 表示成功，否则返回错误信息。
    /// 避免 async void 导致调用方无法等待“AB 全部完成”。
    /// </summary>
    private Task<string> UploadAsync(string filePath, string fileName, string remoteURL)
    {
        string baseUrl = remoteURL;
        if (!baseUrl.EndsWith("/")) baseUrl += "/";
        return Task.Run(() =>
        {
            try
            {
                FtpWebRequest request = (FtpWebRequest)WebRequest.Create(new Uri(baseUrl + fileName));
                request.Credentials = new NetworkCredential(Config.Ftp_UserName, Config.Ftp_Password);
                request.Proxy = null;
                request.KeepAlive = false;
                request.Method = WebRequestMethods.Ftp.UploadFile;
                request.UseBinary = true;
                using (FileStream file = File.OpenRead(filePath))
                using (Stream upload = request.GetRequestStream())
                {
                    file.CopyTo(upload, 81920);
                }
                using (FtpWebResponse response = (FtpWebResponse)request.GetResponse())
                {
                    return null;
                }
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        });
    }
}

}