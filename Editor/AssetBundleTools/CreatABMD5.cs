using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 对比文件格式：
/// 文件名 文件大小 MD5码|文件名 文件大小 MD5码
/// </summary>
public class CreatABMD5
{
    [MenuItem("Tools/AB包工具/创建对比文件")]
    public static void CreateABCompareFile()
    {
        //获取文件夹信息
        DirectoryInfo directory = Directory.CreateDirectory(DataConfig.CurrentABFilePath);
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
        abCompareInfo = abCompareInfo.Substring(0, abCompareInfo.Length - 1);
        //存储拼接好的 AB包资源信息
        File.WriteAllText(DataConfig.CurrentABFilePath + "/ABCompareInfo.txt", abCompareInfo);
        AssetDatabase.Refresh();
        Debug.Log("AB包对比文件生成成功,路径：" + DataConfig.CurrentABFilePath + "/ABCompareInfo.txt");

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
