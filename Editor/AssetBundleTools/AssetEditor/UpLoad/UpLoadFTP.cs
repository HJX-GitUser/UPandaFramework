using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

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
public class UpLoadFTP
{
    private FTPUpLoadABConfig Config;
    public UpLoadFTP(string BuidTarget)
    {
        if (Config == null)
        {
            Config = new FTPUpLoadABConfig(BuidTarget);
            Config.UpABURL = EditorPrefs.GetString("UpLoadABEditor_UpABURL", Config.UpABURL);
            Config.Ftp_UserName = EditorPrefs.GetString("UpLoadABEditor_Ftp_UserName", Config.Ftp_UserName);
            Config.Ftp_Password = EditorPrefs.GetString("UpLoadABEditor_Ftp_Password", Config.Ftp_Password);
        }
    }

    public FTPUpLoadABConfig GetConfig=> Config;

    public void ResetData(string BuidTarget)
    {
        Config = new FTPUpLoadABConfig(BuidTarget);
        SaveData();
    }

    public void SaveData()
    {
        EditorPrefs.SetString("UpLoadABEditor_UpABURL", Config.UpABURL);
        EditorPrefs.SetString("UpLoadABEditor_Ftp_UserName", Config.Ftp_UserName);
        EditorPrefs.SetString("UpLoadABEditor_Ftp_Password", Config.Ftp_Password);
        Debug.Log("FTP配置已保存");
    }

    /// <summary>
    /// 上传
    /// </summary>
    public void UpLoadAllABFile(string LocalABPath)
    {
        Debug.Log($"开始上传：{Config.UpABURL}");
        if(!Directory.Exists(LocalABPath))
        {
            Debug.LogError($"路径不存在：{LocalABPath}");
            return;
        }
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

}
