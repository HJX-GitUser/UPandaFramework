using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class DataConfig 
{
    //[Tooltip("AB包打包的文件夹位置")]
    public static string CurrentABFilePath = Application.dataPath + "/AssetBundle/PC/";
    //[Tooltip("上传AB包地址")]
    public static string UpABURL = "ftp://192.168.31.48/AB/PC/";
    //[Tooltip("下载AB包地址")]
    public static string DownLoadURL = "ftp://192.168.31.48/AB/PC/";
    //FTP通信凭证
    //ftp用户名
    public static string Ftp_UserName = "Admin";
    //ftp密码
    public static string Ftp_Password = "Admin123";
}
