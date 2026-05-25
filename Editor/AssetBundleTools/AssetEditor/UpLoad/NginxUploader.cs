using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Net;
using System.IO;
using System.Text;
using System.Collections.Generic;
using UnityEngine.Networking;

public class NginxUploader : EditorWindow
{
    [System.Serializable]
    public class UploadResult
    {
        public bool success;
        public string message;
        public string fileUrl;
    }

    private List<string> filePaths = new List<string>();
    private string serverUrl = "http://localhost:8090/upload";
    private string uploadPath = "/uploads/";
    private float uploadProgress = 0f;
    private bool isUploading = false;
    private string status = "";
    private List<string> uploadLog = new List<string>();

    //[MenuItem("Tools/Advanced Nginx Uploader")]
    public static void ShowWindow()
    {
        GetWindow<NginxUploader>("Advanced Uploader");
    }

    private void OnGUI()
    {
        DrawHeader();
        DrawServerSettings();
        DrawFileList();
        DrawActions();
        DrawProgress();
        DrawLog();
    }

    private void DrawHeader()
    {
        GUILayout.Label("Nginx 上传工具", EditorStyles.boldLabel);
        EditorGUILayout.Space();
    }

    private void DrawServerSettings()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUILayout.Label("服务器设置", EditorStyles.boldLabel);
        serverUrl = EditorGUILayout.TextField("服务器URL:", serverUrl);
        uploadPath = EditorGUILayout.TextField("上传路径:", uploadPath);
        EditorGUILayout.EndVertical();
        EditorGUILayout.Space();
    }

    private void DrawFileList()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUILayout.Label("文件列表", EditorStyles.boldLabel);

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(150));

        for (int i = 0; i < filePaths.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(Path.GetFileName(filePaths[i]));
            if (GUILayout.Button("移除", GUILayout.Width(60)))
            {
                filePaths.RemoveAt(i);
                break;
            }
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();

        if (GUILayout.Button("添加文件"))
        {
            string paths = EditorUtility.OpenFilePanel("选择文件", "", "*");
            if (paths != null && paths.Length > 0)
            {
                filePaths.AddRange(new string[] { paths });
            }
        }

        if (GUILayout.Button("添加文件夹"))
        {
            string path = EditorUtility.OpenFolderPanel("选择文件夹", "", "");
            if (!string.IsNullOrEmpty(path))
            {
                AddFilesFromFolder(path);
            }
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.Space();
    }

    // 添加取消功能
    private bool cancelUpload = false;

    private void DrawActions()
    {
        EditorGUI.BeginDisabledGroup(isUploading || filePaths.Count == 0);

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("清空列表", GUILayout.Height(30)))
        {
            filePaths.Clear();
        }

        if (isUploading)
        {
            if (GUILayout.Button("取消上传", GUILayout.Height(30), GUILayout.Width(100)))
            {
                cancelUpload = true;
            }
        }
        else
        {
            if (GUILayout.Button("上传所有文件", GUILayout.Height(30), GUILayout.Width(100)))
            {
                cancelUpload = false;
                StartUploadAll();
            }
        }

        EditorGUILayout.EndHorizontal();
        EditorGUI.EndDisabledGroup();
    }

    private void DrawProgress()
    {
        if (isUploading)
        {
            EditorGUILayout.Space();
            Rect rect = GUILayoutUtility.GetRect(200, 20);
            EditorGUI.ProgressBar(rect, uploadProgress, $"上传进度: {uploadProgress:P0}");
        }
    }

    private void DrawLog()
    {
        if (uploadLog.Count > 0)
        {
            EditorGUILayout.Space();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("上传日志", EditorStyles.boldLabel);
            logScrollPosition = EditorGUILayout.BeginScrollView(logScrollPosition, GUILayout.Height(150));
            foreach (string log in uploadLog)
            {
                EditorGUILayout.LabelField(log, EditorStyles.wordWrappedMiniLabel);
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
            if (GUILayout.Button("清空日志"))
            {
                uploadLog.Clear();
            }
        }
    }

    private void AddFilesFromFolder(string folderPath)
    {
        if (Directory.Exists(folderPath))
        {
            string[] files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories);
            filePaths.AddRange(files);
        }
    }

    private async void StartUploadAll()
    {
        isUploading = true;
        uploadLog.Clear();

        int successCount = 0;
        int failCount = 0;

        for (int i = 0; i < filePaths.Count; i++)
        {
            if (!File.Exists(filePaths[i]))
            {
                AddLog($"文件不存在: {filePaths[i]}");
                failCount++;
                continue;
            }

            uploadProgress = (float)i / filePaths.Count;
            Repaint();

            bool result = await UploadFileAsync(filePaths[i]);
            if (result) successCount++;
            else failCount++;
        }

        AddLog($"\n上传完成! 成功: {successCount}, 失败: {failCount}");
        uploadProgress = 1f;
        isUploading = false;

        // 提示用户
        if (successCount > 0)
        {
            EditorUtility.DisplayDialog("上传完成",
                $"上传完成!\n成功: {successCount} 个文件\n失败: {failCount} 个文件", "确定");
        }

        Repaint();
    }


    private async System.Threading.Tasks.Task<bool> UploadFileAsync(string filePath)
    {
        try
        {
            string fileName = Path.GetFileName(filePath);
            AddLog($"开始上传: {fileName}");

            // 使用WWWForm简化
            WWWForm form = new WWWForm();

            // 分块读取文件，避免大文件内存问题
            byte[] fileData = File.ReadAllBytes(filePath);
            if (fileData.Length > 50 * 1024 * 1024) // 50MB以上警告
            {
                if (!EditorUtility.DisplayDialog("警告",
                    $"文件 {fileName} 大小 {fileData.Length / 1024f / 1024f:F2}MB，可能上传较慢，继续吗？",
                    "继续", "取消"))
                {
                    return false;
                }
            }

            form.AddBinaryData("file", fileData, fileName, "application/octet-stream");
            form.AddField("path", uploadPath);
            form.AddField("timestamp", System.DateTime.Now.Ticks.ToString());

            using (UnityWebRequest request = UnityWebRequest.Post(serverUrl, form))
            {
                request.timeout = 300; // 5分钟超时

                // 显示进度
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    await System.Threading.Tasks.Task.Yield();
                }

#if UNITY_2020_1_OR_NEWER
                if (request.result != UnityWebRequest.Result.Success)
#else
                if (request.isNetworkError || request.isHttpError)
#endif
                {
                    AddLog($"上传失败 {fileName}: {request.error}");
                    return false;
                }

                // 解析JSON响应
                string responseText = request.downloadHandler.text;
                try
                {
                    UploadResult result = JsonUtility.FromJson<UploadResult>(responseText);
                    if (result != null && result.success)
                    {
                        AddLog($"上传成功: {result.fileUrl}");
                        return true;
                    }
                }
                catch
                {
                    // 如果不是JSON，可能是其他格式
                    AddLog($"响应: {responseText}");

                    // 简单判断是否成功
                    if (responseText.Contains("success") || responseText.Contains("fileName"))
                    {
                        AddLog("上传成功（非标准响应）");
                        return true;
                    }
                }

                return false;
            }
        }
        catch (System.Exception e)
        {
            AddLog($"异常: {e.Message}");
            return false;
        }
    }

    private string GenerateMultipartFormData(List<IMultipartFormSection> formData, string boundary)
    {
        StringBuilder sb = new StringBuilder();

        foreach (var section in formData)
        {
            sb.AppendLine($"--{boundary}");
            sb.AppendLine($"Content-Disposition: form-data; name=\"{section.sectionName}\"");

            if (section is MultipartFormFileSection fileSection)
            {
                sb.AppendLine($"Content-Type: {fileSection.contentType}");
                sb.AppendLine();
                sb.AppendLine(Encoding.UTF8.GetString(fileSection.sectionData));
            }
            else
            {
                sb.AppendLine();
                sb.AppendLine(Encoding.UTF8.GetString(section.sectionData));
            }
        }

        sb.AppendLine($"--{boundary}--");
        return sb.ToString();
    }

    private void AddLog(string message)
    {
        uploadLog.Add($"[{System.DateTime.Now:HH:mm:ss}] {message}");
    }

    private Vector2 scrollPosition;
    private Vector2 logScrollPosition;
}