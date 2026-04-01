using AssetBundleBrowser;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UPandaGF.GFEditor;
/// <summary>
/// 上传更新AB包
/// </summary>
internal class UpLoadABEditor
{
    public enum ContactMethod
    {
        None = 0,
        FTP = 1,
        HTTP = 2,
    }
    [System.Serializable]
    public class UpLoadABEditorConfig
    {
        public ContactMethod contactMethod = ContactMethod.None;
        public string targetDirectory = "";
    }


    private UpLoadFTP upLoadFTP;
    //public UpLoadHTTP upLoadHTTP;
    [SerializeField]
    private Vector2 m_ScrollPosition;

    private AssetBundleBrowserMain abMainE;

    //本地AB包路径
    public string LocalABPath;

    public string configName = "UpLoadABEditorConfig";
    public UpLoadABEditorConfig config;

    public void OnEnable(AssetBundleBrowserMain bm)
    {
        abMainE = bm;
        config = UPandaGFConfig.LoadJsonConfig<UpLoadABEditorConfig>(configName);
        LocalABPath = abMainE.m_BuildTabData.m_OutputPath;
        Debug.Log($"UpLoadABEditor Enable!!{config.targetDirectory}");

    }

    private FTPUpLoadABConfig GetFTPConfig()
    {
        if (config.contactMethod == ContactMethod.FTP && upLoadFTP == null)
        {
            upLoadFTP = new UpLoadFTP(abMainE.m_BuildTabData.m_BuildTarget.ToString());
        }
        return upLoadFTP.GetConfig;
    }

    public void OnGUI()
    {
        m_ScrollPosition = EditorGUILayout.BeginScrollView(m_ScrollPosition);
        var centeredStyle = new GUIStyle(GUI.skin.GetStyle("Label"));
        centeredStyle.alignment = TextAnchor.UpperCenter;
        GUILayout.Label(new GUIContent("资源包上传页签"), centeredStyle);
        EditorGUILayout.Space();
        GUILayout.BeginVertical();
        if (!LocalABPath.Equals(abMainE.m_BuildTabData.m_OutputPath))
            LocalABPath = abMainE.m_BuildTabData.m_OutputPath;
        EditorGUILayout.LabelField("本地资源路径", LocalABPath);
        //if (GUILayout.Button("创建AB包对比文件"))
        //{
        //    CreateABCompareFile();
        //}
        EditorGUILayout.Space(10);
        config.contactMethod = (ContactMethod)EditorGUILayout.EnumPopup("上传方式", config.contactMethod);
        switch (config.contactMethod)
        {
            case ContactMethod.FTP:
                FTPGUI();
                break;
            case ContactMethod.HTTP:
                HTTPGUI();
                break;
        }
        EditorGUILayout.Space(30);
        if (GUILayout.Button("复制资源到StreamingAssets"))
        {
            //string savePath = LocalABPath.Substring(LocalABPath.IndexOf("Assets") + "Assets".Length);//AssetBundle放到Assets路径下用这个
            string savePath = $"/{LocalABPath}";
            savePath = Application.streamingAssetsPath + savePath;
            if (!Directory.Exists(savePath))
            {
                Directory.CreateDirectory(savePath);
            }
            MoveABToStreamingAssets(savePath);
        }
        EditorGUILayout.Space(30);
        EditorGUILayout.LabelField("目标路径：", config.targetDirectory);
        if (GUILayout.Button("选择目标路径"))
        {
            config.targetDirectory = EditorUtility.OpenFolderPanel("目标路径选择", config.targetDirectory, string.Empty);
        }
        if (GUILayout.Button("复制资源到目标路径"))
        {
            if (!Directory.Exists(LocalABPath))
            {
                Debug.LogError($"AssetBundle源目录不存在: {LocalABPath}");
                return;
            }

            if (!Directory.Exists(config.targetDirectory))
            {
                Debug.LogError($"目标路径不存在！: {config.targetDirectory}");
                return;
            }

            Debug.Log($"开始复制AssetBundle: {LocalABPath} -> {config.targetDirectory}");
            ClearTargetDirectory();
            // 复制所有文件和子目录
            CopyDirectory(LocalABPath, config.targetDirectory);

            Debug.Log($"AssetBundle复制完成！目标位置: {config.targetDirectory}");

            // 刷新资源数据库
            AssetDatabase.Refresh();

            // 打开目标目录
            EditorUtility.RevealInFinder(config.targetDirectory);
        }

        EditorGUILayout.Space(20);
        if (GUILayout.Button("打印persistentDataPath路径"))
        {
            DebugPath();
        }
        if (GUILayout.Button("保存"))
        {
            SaveData();
        }
        GUILayout.EndVertical();
        EditorGUILayout.EndScrollView();
    }

    private void FTPGUI()
    {
        FTPUpLoadABConfig FTPConfig = GetFTPConfig();
        FTPConfig.UpABURL = EditorGUILayout.TextField("上传地址", FTPConfig.UpABURL);
        EditorGUILayout.LabelField("FTP通信凭证:");
        FTPConfig.Ftp_UserName = EditorGUILayout.TextField("ftp用户名", FTPConfig.Ftp_UserName);
        FTPConfig.Ftp_Password = EditorGUILayout.TextField("ftp密码", FTPConfig.Ftp_Password);
        if (GUILayout.Button("上传AB包和对比文件"))
        {
            DirectoryInfo directory = Directory.CreateDirectory(LocalABPath);
            if (config.contactMethod == ContactMethod.FTP)
            {
                upLoadFTP.UpLoadAllABFile(LocalABPath);
            }
        }
    }

    private void HTTPGUI()
    {
        //if (upLoadHTTP == null) upLoadHTTP = new UpLoadHTTP();
        ////upLoadHTTP.HTTPConfig.serverUrl = EditorGUILayout.TextField("上传地址", upLoadHTTP. HTTPConfig.serverUrl);
        //upLoadHTTP.OnGUI();
        if (GUILayout.Button("HTTP 上传工具"))
        {
            NginxUploader.ShowWindow();
        }
    }

    private bool IsABAssets(FileInfo fileInfo)
    {
        bool isABAssets = false;
        if (fileInfo.Extension == "" || fileInfo.Extension == ".txt")
        {
            isABAssets = true;
        }
        return isABAssets;
    }
    /// <summary>
    /// 选择路径
    /// </summary>
    private void BrowseForFolder()
    {
        var newPath = EditorUtility.OpenFolderPanel("Bundle Folder", LocalABPath, string.Empty);
        if (!string.IsNullOrEmpty(newPath))
        {
            var gamePath = System.IO.Path.GetFullPath(".");
            gamePath = gamePath.Replace("\\", "/");
            if (newPath.StartsWith(gamePath) && newPath.Length > gamePath.Length)
                newPath = newPath.Remove(0, gamePath.Length + 1);
            LocalABPath = newPath;
        }
    }

    /// <summary>
    /// 重置数据
    /// </summary>
    public void ResetPathToDefault()
    {
        if (config.contactMethod == ContactMethod.FTP)
        {
            upLoadFTP.ResetData(abMainE.m_BuildTabData.m_BuildTarget.ToString());
            Debug.Log("FTP已重置");
        }
    }

    /// <summary>
    /// 保存数据
    /// </summary>
    public void SaveData()
    {
        UPandaGFConfig.SaveJsonConfig(config, configName);
        if (config.contactMethod == ContactMethod.FTP) upLoadFTP.SaveData();
    }

    /// <summary>
    /// 打印persistentDataPath路径
    /// </summary>
    private void DebugPath()
    {
        Debug.Log(Application.persistentDataPath);
    }

    private void SelectAB()
    {
        //通过编辑器Selection类中的方法 获取再Project窗口中选中的资源 
        UnityEngine.Object[] selectedAsset = Selection.GetFiltered(typeof(UnityEngine.Object), SelectionMode.DeepAssets);
        //如果一个资源都没有选择 就没有必要处理后面的逻辑了
        if (selectedAsset.Length == 0)
        {
            Debug.Log("请先选择资源文件");
        }
        else
        {
            string savePath = EditorUtility.OpenFolderPanel("复制路径选择", Application.streamingAssetsPath, string.Empty);
            if (!string.IsNullOrEmpty(savePath))
            {
                SelectABToStreamingAssets(savePath, selectedAsset);
            }
        }
    }
    /// <summary>
    /// 选择资源到StreamingAssets
    /// </summary>
    private void SelectABToStreamingAssets(string savePath, UnityEngine.Object[] selectedAsset)
    {
        //用于拼接本地默认AB包资源信息的字符串
        string abCompareInfo = "";
        //遍历选中的资源对象
        foreach (UnityEngine.Object asset in selectedAsset)
        {
            //通过Assetdatabase类 获取 资源的路径
            string assetPath = AssetDatabase.GetAssetPath(asset);
            //判断选取的资源是不是AB包文件夹下的，不是的话报错
            string judge_fileName = assetPath.Substring(0, assetPath.LastIndexOf('/'));
            if (judge_fileName != LocalABPath)
            {
                if (judge_fileName != LocalABPath.Substring(0, LocalABPath.LastIndexOf('/')))
                    Debug.LogError($"（{judge_fileName}）无法复制，你只能选\"{LocalABPath}\"路径下的资源");
                continue;
            }
            //截取路径当中的文件名 用于作为 StreamingAssets中的文件名
            string fileName = assetPath.Substring(assetPath.LastIndexOf('/'));
            // 判断是否有.符号 如果有 证明有后缀 不处理
            if (fileName.IndexOf('.') != -1)
                continue;//也可以在拷贝之前去获取全路径，然后通过FileInfo去获取后缀来判断 这样更准确
            string copyPath = $"{savePath}/{fileName}";
            Debug.Log(copyPath);
            //利用AssetDatabase中的API 将选中文件 复制到目标路径
            AssetDatabase.CopyAsset(assetPath, copyPath);

            //获取拷贝到StreamingAssets文件夹中的文件的全部信息
            System.IO.FileInfo fileInfo = new System.IO.FileInfo(copyPath);
            //拼接AB包信息到字符串中
            abCompareInfo += fileInfo.Name + " " + fileInfo.Length + " " + GetMD5(fileInfo.FullName);
            //用一个符号隔开多个AB包信息
            abCompareInfo += "|";
        }
        //去掉最后一个|符号 为了之后拆分字符串方便
        if (abCompareInfo != "")
        {
            abCompareInfo = abCompareInfo.Substring(0, abCompareInfo.Length - 1);
            //将本地默认资源的对比信息 存入文件
            File.WriteAllText(savePath + "/ABCompareInfo.txt", abCompareInfo);
        }
        else
        {
            Debug.Log("无法生成对比文件，请选择资源文件进行移动");
        }
        AssetDatabase.Refresh();
    }

    private void MoveABToStreamingAssets(string savePath)
    {
        CopyDirectory(LocalABPath, savePath);
        AssetDatabase.Refresh();
    }



    /// <summary>
    /// 创建对比文件
    /// </summary>
    public void CreateABCompareFile()
    {
        //获取文件夹信息
        DirectoryInfo directory = Directory.CreateDirectory(LocalABPath);
        //获取该目录下的所有文件信息
        FileInfo[] fileInfos = directory.GetFiles();

        //用于存储信息的 字符串
        string abCompareInfo = "";

        foreach (FileInfo item in fileInfos)
        {
            //没有后缀的 才是AB包 这里只想要AB包的信息
            if (item.Extension == "")
            {
                //拼接一个AB包的信息
                abCompareInfo += item.Name + " " + item.Length + " " + GetMD5(item.FullName);
                abCompareInfo += "|";
            }
        }
        //因为循环完毕后 会在最后有一个 | 符号 所以 把它去掉
        if (abCompareInfo.Length == 0)
        {
            Debug.LogError("对比文件创建失败！请检查资源路径：" + LocalABPath);
            return;
        }
        abCompareInfo = abCompareInfo.Substring(0, abCompareInfo.Length - 1);
        //存储拼接好的 AB包资源信息
        int _index = LocalABPath.IndexOf('/', 1);
        string rawPath = LocalABPath.Substring(_index, LocalABPath.Length - _index);
        string nPath = $"{Application.dataPath}{rawPath}/ABCompareInfo.txt";
        File.WriteAllText(nPath, abCompareInfo);
        AssetDatabase.Refresh();
        Debug.Log("AB包对比文件生成成功,路径：" + nPath);
    }
    /// <summary>
    /// 得到文件的MD5码
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns></returns>
    public static string GetMD5(string filePath)
    {
        using (FileStream file = new FileStream(filePath, FileMode.Open))
        {
            //声明一个MD5对象 用于生成MD5码
            MD5 md5 = new MD5CryptoServiceProvider();
            //利用API 得到数据的MD5码 16个字节 数组
            byte[] md5Info = md5.ComputeHash(file);
            //关闭文件流
            file.Close();
            //把16个字节转换为 16进制 拼接成字符串 为了减小md5码的长度
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < md5Info.Length; i++)
            {
                sb.Append(md5Info[i].ToString("x2"));
            }
            return sb.ToString();
        }

    }

    /// <summary>
    /// 清空目标目录
    /// </summary>
    private void ClearTargetDirectory()
    {
        if (Directory.Exists(config.targetDirectory))
        {
            Debug.Log($"清空目标目录: {config.targetDirectory}");

            // 获取所有文件
            string[] files = Directory.GetFiles(config.targetDirectory, "*", SearchOption.AllDirectories);
            foreach (string file in files)
            {
                File.Delete(file);
            }

            // 删除所有子目录（除了根目录）
            string[] directories = Directory.GetDirectories(config.targetDirectory);
            foreach (string dir in directories)
            {
                Directory.Delete(dir, true);
            }

            Debug.Log($"已删除 {files.Length} 个文件和 {directories.Length} 个目录");
        }
        else
        {
            // 如果目录不存在，创建它
            Directory.CreateDirectory(config.targetDirectory);
            Debug.Log($"创建目标目录: {config.targetDirectory}");
        }
    }

    /// <summary>
    /// 复制目录
    /// </summary>
    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        // 确保目标目录存在
        if (!Directory.Exists(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        // 复制所有文件
        string[] files = Directory.GetFiles(sourceDir);
        foreach (string file in files)
        {
            if (Path.GetExtension(file) == ".meta")
            {
                continue;
            }
            string fileName = Path.GetFileName(file);
            string destFile = Path.Combine(targetDir, fileName);
            File.Copy(file, destFile, true);
            Debug.Log($"复制文件: {fileName}");
        }

        // 递归复制子目录
        string[] subDirectories = Directory.GetDirectories(sourceDir);
        foreach (string subDir in subDirectories)
        {
            string dirName = Path.GetFileName(subDir);
            string destDir = Path.Combine(targetDir, dirName);
            CopyDirectory(subDir, destDir);
        }
    }
}