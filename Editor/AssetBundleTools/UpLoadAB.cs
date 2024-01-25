using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 上传更新AB包
/// </summary>
public class UpLoadAB
{
    [MenuItem("Tools/AB包工具/上传AB包和对比文件")]
    private static void UpLoadAllABFile()
    {
        DirectoryInfo directory = Directory.CreateDirectory(DataConfig.CurrentABFilePath);
        FileInfo[] fileInfos = directory.GetFiles();
        foreach (FileInfo fileInfo in fileInfos)
        {
            if (fileInfo.Extension == "" || fileInfo.Extension == ".txt")
            {
                FtpUploadFile(fileInfo.FullName, fileInfo.Name);
            }
        }
    }
    private async static void FtpUploadFile(string filePath, string fileName)
    {
        await Task.Run(() =>
        {
            try
            {
                //1.创建一个FTP连接 用于上传
                FtpWebRequest req = FtpWebRequest.Create(new Uri(DataConfig.UpABURL + fileName)) as FtpWebRequest;
                //2.设置一个通信凭证 这样才能上传
                NetworkCredential n = new NetworkCredential(DataConfig.Ftp_UserName, DataConfig.Ftp_Password);
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
                    //循环完毕后 证明上传结束
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

    [MenuItem("Tools/AB包工具/打印persistentDataPath路径")]
    private static void DebugPath()
    {
        Debug.Log(Application.persistentDataPath);
    }
}
