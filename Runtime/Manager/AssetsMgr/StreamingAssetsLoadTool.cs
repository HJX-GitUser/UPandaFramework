using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
using UPandaGF;

/// <summary>
/// StreamingAssets加载工具
/// </summary>
public class StreamingAssetsLoader
{
    private static string streamingAssetsPath = GetStreamingAssetsPath();
    public static string StreamingAssetsPath => streamingAssetsPath;

    /// <summary>
    /// 获取StreamingAssets的路径（适用于所有平台）
    /// </summary>
    private static string GetStreamingAssetsPath()
    {
        string path = "";
#if UNITY_EDITOR
        path = Application.streamingAssetsPath;
#else
            // 判断平台类型，获取对应的路径
            if (Application.platform == RuntimePlatform.Android)
            {
                // Android平台上，StreamingAssets路径是通过jar包访问的
                path = "jar:file://" + Application.dataPath + "!/assets";
            }
            else if (Application.platform == RuntimePlatform.IPhonePlayer)
            {
                // iOS平台，StreamingAssets在应用包的根目录
                path = "file://" + Path.Combine(Application.dataPath, "Raw");
            }
            else
            {
                // 其他平台（Windows, Mac, Linux, WebGL等），可以直接使用Application.streamingAssetsPath
                path = Application.streamingAssetsPath;
            }
#endif
        return path;
    }

    private static string CombineRelativePath(string relativePath)
    {

        if (relativePath[0] != '/')
        {
            //Debug.LogWarning($"path[0] is not '/'.  path[0]:{relativePath[0]}");
            relativePath = '/' + relativePath;
        }
        return streamingAssetsPath + relativePath;
    }


    /// <summary>
    /// 加载文本文件
    /// </summary>
    /// <param name="relativePath"></param>
    /// <returns></returns>
    public static string LoadTextFile(string relativePath)
    {
        string fullPath = CombineRelativePath(relativePath);
        if (Application.platform == RuntimePlatform.Android || Application.platform == RuntimePlatform.WebGLPlayer)
        {
            using (var request = UnityWebRequest.Get(fullPath))
            {
                request.SendWebRequest();

                while (!request.isDone)
                {
                    // 等待文件加载完成
                }

                if (request.result == UnityWebRequest.Result.Success)
                {
                    return request.downloadHandler.text;
                }
                else
                {
                    Debug.LogError("Failed to load file from Android StreamingAssets: " + request.error);
                    return null;
                }
            }
        }
        else
        {
            return File.ReadAllText(fullPath);
        }

    }

    /// <summary>
    /// 异步加载文本文件
    /// </summary>
    /// <param name="relativePath">相对于StreamingAssets的路径，如 "Config/settings.json"</param>
    /// <returns>文本内容</returns>
    public static async Task<string> LoadTextFileAsync(string relativePath)
    {
        string fullPath = CombineRelativePath(relativePath);
        string data = null;
        // 判断平台，选择加载方式
        if (Application.platform == RuntimePlatform.Android ||
            Application.platform == RuntimePlatform.WebGLPlayer)
        {
            // Android和WebGL平台使用UnityWebRequest
            using (UnityWebRequest www = UnityWebRequest.Get(fullPath))
            {
                var operation = www.SendWebRequest();

                while (!operation.isDone)
                {
                    await System.Threading.Tasks.Task.Yield(); // 异步等待下一帧
                }

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"Failed to load file at {fullPath}: {www.error}");
                }
                else
                {
                    data = www.downloadHandler.text;
                }


            }
        }
        else
        {
            try
            {
                data = await System.Threading.Tasks.Task.Run(() => File.ReadAllText(fullPath));
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to read file at {fullPath}: {e.Message}");
            }
        }
        return data;
    }
    /// <summary>
    /// 加载文本文件 协程异步
    /// </summary>
    /// <param name="relativePath"></param>
    /// <param name="callback"></param>
    /// <returns></returns>
    public static IEnumerator LoadTextFile(string relativePath, UnityAction<string> callback)
    {
        string fullPath = CombineRelativePath(relativePath);
        string text = null;
        if (Application.platform == RuntimePlatform.Android ||
            Application.platform == RuntimePlatform.WebGLPlayer)
        {
            using (UnityWebRequest request = UnityWebRequest.Get(fullPath))
            {
                request.SendWebRequest();
                yield return request;
                if (request.result == UnityWebRequest.Result.Success)
                {
                    text = request.downloadHandler.text;
                }
                else
                {
                    PLoger.LogError("Failed to load file from WebGL StreamingAssets: " + request.error);
                }
            }
        }
        else
        {
            try
            {
                text = File.ReadAllText(fullPath);
            }
            catch (Exception e)
            {
                PLoger.LogError($"Failed to read binary data at {fullPath}: {e.Message}");
            }
        }
        callback?.Invoke(text);

    }

    /// <summary>
    /// 加载二进制文件，【警告！】别在WebGL平台使用该方法
    /// </summary>
    /// <param name="relativePath"></param>
    /// <returns></returns>
    public static byte[] LoadBinaryData(string relativePath)
    {
        string fullPath = CombineRelativePath(relativePath);
        if (Application.platform == RuntimePlatform.Android || Application.platform == RuntimePlatform.WebGLPlayer)
        {
            using (var request = UnityWebRequest.Get(fullPath))
            {
                request.SendWebRequest();

                while (!request.isDone)
                {
                    // 等待文件加载完成
                }

                if (request.result == UnityWebRequest.Result.Success)
                {
                    return request.downloadHandler.data;
                }
                else
                {
                    Debug.LogError("Failed to load file from Android StreamingAssets: " + request.error);
                    return null;
                }
            }
        }
        else
        {
            try
            {
                return File.ReadAllBytes(fullPath);
            }
            catch (Exception e)
            {
                PLoger.LogError($"Failed to read binary data at {fullPath}: {e.Message}");
                return null;
            }
        }

    }

    /// <summary>
    /// 异步加载二进制数据（如图片、音频文件）
    /// </summary>
    public static async Task<byte[]> LoadBinaryDataAsync(string relativePath)
    {
        string fullPath = CombineRelativePath(relativePath);
        if (Application.platform == RuntimePlatform.Android ||
            Application.platform == RuntimePlatform.WebGLPlayer)
        {
            using (UnityWebRequest www = UnityWebRequest.Get(fullPath))
            {
                www.downloadHandler = new DownloadHandlerBuffer();
                var operation = www.SendWebRequest();

                while (!operation.isDone)
                {
                    await System.Threading.Tasks.Task.Yield();
                }

                if (www.result != UnityWebRequest.Result.Success)
                {
                    throw new Exception($"Failed to load binary data at {fullPath}: {www.error}");
                }

                return www.downloadHandler.data;
            }
        }
        else
        {
            try
            {
                return await System.Threading.Tasks.Task.Run(() => File.ReadAllBytes(fullPath));
            }
            catch (Exception e)
            {
                throw new Exception($"Failed to read binary data at {fullPath}: {e.Message}");
            }
        }


    }

    /// <summary>
    /// 加载二进制文件 协程异步
    /// </summary>
    /// <param name="relativePath"></param>
    /// <param name="callback"></param>
    /// <returns></returns>
    public static IEnumerator LoadBinaryData(string relativePath, UnityAction<byte[]> callback)
    {
        string fullPath = CombineRelativePath(relativePath);
        byte[] bytes = null;
        if (Application.platform == RuntimePlatform.Android ||
            Application.platform == RuntimePlatform.WebGLPlayer)
        {
            using (UnityWebRequest www = UnityWebRequest.Get(fullPath))
            {
                www.downloadHandler = new DownloadHandlerBuffer();
                var operation = www.SendWebRequest();
                yield return operation;

                if (www.result != UnityWebRequest.Result.Success)
                {
                    PLoger.Log($"Failed to load binary data at {fullPath}: {www.error}");
                }
                else
                {
                    bytes = www.downloadHandler.data;
                }
            }
        }
        else
        {
            try
            {
                bytes = File.ReadAllBytes(fullPath);
            }
            catch (Exception e)
            {
                PLoger.LogError($"Failed to read binary data at {fullPath}: {e.Message}");
            }
        }
        callback?.Invoke(bytes);
    }

    /// <summary>
    /// 拼接路径
    /// </summary>
    /// <param name="path">相对路径</param>
    /// <returns></returns>
    public static string CombinePath(string path)
    {
        return CombineRelativePath(path);
    }
}

