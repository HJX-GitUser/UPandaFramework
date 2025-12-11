using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

public static class StreamingAssetsLoader
{
    public static string StreamingAssetsPath => GetStreamingAssetsPath();

    /// <summary>
    /// 获取StreamingAssets的路径（适用于所有平台）
    /// </summary>
    /// <param name="fileName"></param>
    /// <returns></returns>
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

    /// <summary>
    /// 异步加载文本文件
    /// </summary>
    /// <param name="relativePath">相对于StreamingAssets的路径，如 "Config/settings.json"</param>
    /// <returns>文本内容</returns>
    public static async Task<string> LoadTextFileAsync(string relativePath)
    {
        string fullPath = Path.Combine(Application.streamingAssetsPath, relativePath);
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
                    await Task.Yield(); // 异步等待下一帧
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
                data = await Task.Run(() => File.ReadAllText(fullPath));
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to read file at {fullPath}: {e.Message}");
            }
        }
        return data;
    }

    /// <summary>
    /// 异步加载二进制数据（如图片、音频文件）
    /// </summary>
    public static async Task<byte[]> LoadBinaryDataAsync(string relativePath)
    {
        string fullPath = Path.Combine(Application.streamingAssetsPath, relativePath);

        if (Application.platform == RuntimePlatform.Android ||
            Application.platform == RuntimePlatform.WebGLPlayer)
        {
            using (UnityWebRequest www = UnityWebRequest.Get(fullPath))
            {
                www.downloadHandler = new DownloadHandlerBuffer();
                var operation = www.SendWebRequest();

                while (!operation.isDone)
                {
                    await Task.Yield();
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
                return await Task.Run(() => File.ReadAllBytes(fullPath));
            }
            catch (Exception e)
            {
                throw new Exception($"Failed to read binary data at {fullPath}: {e.Message}");
            }
        }
    }
}
