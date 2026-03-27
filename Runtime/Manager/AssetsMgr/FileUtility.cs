using System;
using System.IO;
using UnityEngine;

public static class FileUtility
{
    /// <summary>
    /// 将文件另存到新位置，并删除原文件（类似移动，但先复制后删除）
    /// </summary>
    /// <param name="sourcePath">源文件路径</param>
    /// <param name="destPath">目标文件路径</param>
    /// <param name="overwrite">如果目标文件已存在，是否覆盖</param>
    /// <returns>是否成功</returns>
    public static bool SaveAsAndDeleteOriginal(string sourcePath, string destPath, bool overwrite = false)
    {
        // 1. 检查源文件是否存在
        if (!File.Exists(sourcePath))
        {
            Debug.LogError($"源文件不存在: {sourcePath}");
            return false;
        }

        // 2. 确保目标目录存在
        string destDir = Path.GetDirectoryName(destPath);
        if (!Directory.Exists(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        try
        {
            // 3. 复制文件（如果目标文件存在且 overwrite 为 false，则 File.Copy 会抛出异常）
            File.Copy(sourcePath, destPath, overwrite);

            // 4. 复制成功后删除原文件
            File.Delete(sourcePath);

            Debug.Log($"文件另存成功: {sourcePath} -> {destPath}");
            return true;
        }
        catch (IOException ex)
        {
            Debug.LogError($"文件操作失败（IO异常）: {ex.Message}");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.LogError($"文件操作失败（权限不足）: {ex.Message}");
            return false;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"文件操作失败（未知异常）: {ex.Message}");
            return false;
        }
    }

}