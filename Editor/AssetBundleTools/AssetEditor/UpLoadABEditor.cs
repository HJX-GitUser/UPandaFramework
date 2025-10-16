using AssetBundleBrowser;
using GluonGui.WorkspaceWindow.Views.WorkspaceExplorer;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using static AssetBundleBrowser.AssetBundleBuildTab;

/// <summary>
/// 上传更新AB包
/// </summary>
internal class UpLoadABEditor
{
    public enum ContactMethod
    {
        None = 0,
        FTP = 1,
    }
    //通讯方式
    public ContactMethod contactMethod;

    public FTPUpLoadABConfig Config;
    [SerializeField]
    private Vector2 m_ScrollPosition;

    private AssetBundleBrowserMain abMainE;

    //本地AB包路径
    public string LocalABPath;

    public void OnEnable(AssetBundleBrowserMain bm)
    {
        abMainE = bm;
        int c = EditorPrefs.GetInt("UpLoadABEditor_contactMethod", (int)contactMethod);
        contactMethod = (ContactMethod)c;
        LocalABPath = abMainE.m_BuildTabData.m_OutputPath;
        GetFTPConfig();
    }

    private void GetFTPConfig()
    {
        if (contactMethod == ContactMethod.FTP && Config == null)
        {
            Config = new FTPUpLoadABConfig(abMainE.m_BuildTabData.m_BuildTarget.ToString());
            Config.UpABURL = EditorPrefs.GetString("UpLoadABEditor_UpABURL", Config.UpABURL);
            Config.Ftp_UserName = EditorPrefs.GetString("UpLoadABEditor_Ftp_UserName", Config.Ftp_UserName);
            Config.Ftp_Password = EditorPrefs.GetString("UpLoadABEditor_Ftp_Password", Config.Ftp_Password);
        }
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
        if (GUILayout.Button("创建AB包对比文件"))
        {
            CreateABCompareFile();
        }
        EditorGUILayout.Space(10);
        contactMethod = (ContactMethod)EditorGUILayout.EnumPopup("上传方式", contactMethod);
        switch (contactMethod)
        {
            case ContactMethod.FTP:
                FTPGUI();
                break;
        }
        EditorGUILayout.Space(30);
        if (GUILayout.Button("复制资源到StreamingAssets"))
        {
            //string savePath = EditorUtility.OpenFolderPanel("复制路径选择", Application.streamingAssetsPath, string.Empty);
            string savePath = LocalABPath.Substring(LocalABPath.IndexOf("Assets") + "Assets".Length);
            savePath = Application.streamingAssetsPath + savePath;
            if (!Directory.Exists(savePath))
            {
                Directory.CreateDirectory(savePath);
            }
            MoveABToStreamingAssets(savePath);
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
        GetFTPConfig();
        Config.UpABURL = EditorGUILayout.TextField("上传地址", Config.UpABURL);
        EditorGUILayout.LabelField("FTP通信凭证:");
        Config.Ftp_UserName = EditorGUILayout.TextField("ftp用户名", Config.Ftp_UserName);
        Config.Ftp_Password = EditorGUILayout.TextField("ftp密码", Config.Ftp_Password);
        if (GUILayout.Button("上传AB包和对比文件"))
        {
            UpLoadAllABFile();
        }
    }


    /// <summary>
    /// 上传
    /// </summary>
    private void UpLoadAllABFile()
    {
        Debug.Log($"开始上传：{Config.UpABURL}");
        DirectoryInfo directory = Directory.CreateDirectory(LocalABPath);
        FileInfo[] fileInfos = directory.GetFiles();
        foreach (FileInfo fileInfo in fileInfos)
        {
            if (IsABAssets(fileInfo))
            {
                FtpUploadFile(fileInfo.FullName, fileInfo.Name);
            }
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
        if (contactMethod == ContactMethod.FTP)
        {
            Config = new FTPUpLoadABConfig(abMainE.m_BuildTabData.m_BuildTarget.ToString());
        }
        Debug.Log("已重置");
        SaveData();
    }

    /// <summary>
    /// 保存数据
    /// </summary>
    public void SaveData()
    {
        EditorPrefs.SetInt("UpLoadABEditor_contactMethod", (int)contactMethod);
        if (contactMethod == ContactMethod.FTP)
        {
            EditorPrefs.SetString("UpLoadABEditor_UpABURL", Config.UpABURL);
            EditorPrefs.SetString("UpLoadABEditor_Ftp_UserName", Config.Ftp_UserName);
            EditorPrefs.SetString("UpLoadABEditor_Ftp_Password", Config.Ftp_Password);
        }
        Debug.Log("界面信息已保存");
    }

    /// <summary>
    /// 上传AB包和对比文件
    /// </summary>
    /// <param name="filePath"></param>
    /// <param name="fileName"></param>
    private async void FtpUploadFile(string filePath, string fileName)
    {
        await Task.Run(() =>
        {
            try
            {
                //1.创建一个FTP连接 用于上传
                FtpWebRequest req = FtpWebRequest.Create(new Uri(Config.UpABURL + fileName)) as FtpWebRequest;
                //2.设置一个通信凭证 这样才能上传
                NetworkCredential n = new NetworkCredential(Config.Ftp_UserName, Config.Ftp_Password);
                req.Credentials = n;
                //3.其它设置
                //  设置代理为null
                req.Proxy = null;
                //  请求完毕后 是否关闭控制连接
                req.KeepAlive = false;
                //  操作命令-上传
                req.Method = WebRequestMethods.Ftp.UploadFile;
                //  指定传输的类型 2进制
                req.UseBinary = true;
                //4.上传文件
                //  ftp的流对象
                Stream upLoadStream = req.GetRequestStream();
                //  读取文件信息 写入该流对象
                using (FileStream file = File.OpenRead(filePath))
                {
                    //一点一点的上传内容
                    byte[] bytes = new byte[1024];
                    //返回值 代表读取了多少个字节
                    int contentLength = file.Read(bytes, 0, bytes.Length);
                    while (contentLength != 0)
                    {
                        //写入到上传流中
                        upLoadStream.Write(bytes, 0, contentLength);
                        //写完再读
                        contentLength = file.Read(bytes, 0, bytes.Length);
                    }
                    //循环完毕后 上传结束
                    file.Close();
                    upLoadStream.Close();

                }
                Debug.Log(fileName + "上传成功");
            }
            catch (Exception ex)
            {
                Debug.Log(fileName + "上传失败，错误信息：" + ex.Message);
            }
        });
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
        string assetsKeyword = "Assets";
        int startIndex = savePath.IndexOf(assetsKeyword); // 定位"Assets"的起始位置
        string result = savePath.Substring(startIndex); // 从"Assets"开始截取


        if (startIndex == -1)
        {
            Debug.LogError("路径不在Assets路径内");
            return;
        }

        DirectoryInfo directory = Directory.CreateDirectory(LocalABPath);
        FileInfo[] fileInfos = directory.GetFiles();
        foreach (var file in fileInfos)
        {
            if (IsABAssets(file))
            {
                int startIndex1 = file.FullName.IndexOf(assetsKeyword);
                string result1 = file.FullName.Substring(startIndex1); // 从"Assets"开始截取
                AssetDatabase.CopyAsset(result1, result + $"/{file.Name}");
                Debug.Log(result1 + " 已拷贝至：" + result + $"/{file.Name}");
            }
        }
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
}

[System.Serializable]
public class FTPUpLoadABConfig
{
    //[Tooltip("上传AB包地址")]
    public string UpABURL;

    //FTP通信凭证
    //ftp用户名
    public string Ftp_UserName;
    //ftp密码
    public string Ftp_Password;

    public FTPUpLoadABConfig(string BuidTarget)
    {
        UpABURL = $"ftp://127.0.0.1/AssetBundles/{BuidTarget}/";
        Ftp_UserName = "Admin";
        Ftp_Password = "Admin123";
    }
}


