using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "ABURL",menuName = "ScriptableObject/AB包配置")]
public class ABURL : ScriptableObject
{
    [Tooltip("AB包打包的文件夹位置")]
    public string CurrentABFilePath = Application.streamingAssetsPath + "/PC/";
    [Tooltip("上传AB包地址")]
    public string UpABURL = "LoacalHost";
    [Tooltip("下载AB包地址")]
    public string DownLoadURL = "LoacalHost";
    
}
