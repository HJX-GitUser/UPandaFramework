using Codice.Client.BaseCommands.Fileinfo;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public class SelectABToStrFolder
{
    [MenuItem("Tools/AB包工具/移动选中的AB包资源到StreamingAssets")]
    private static void MoveABToStreamingAssets()
    {
        //通过编辑器Selection类中的方法 获取再Project窗口中选中的资源 
        Object[] selectedAsset = Selection.GetFiltered(typeof(Object), SelectionMode.DeepAssets);
        //如果一个资源都没有选择 就没有必要处理后面的逻辑了
        if (selectedAsset.Length == 0)
        {
            Debug.Log("没有选择文件");
            return;
        }

        //用于拼接本地默认AB包资源信息的字符串
        string abCompareInfo = "";
        //遍历选中的资源对象
        foreach (Object asset in selectedAsset)
        {
            //通过Assetdatabase类 获取 资源的路径
            string assetPath = AssetDatabase.GetAssetPath(asset);
            //判断选取的资源是不是AB包文件夹下的，不是的话报错
            string judge_fileName = assetPath.Substring(0, assetPath.LastIndexOf('/'));
            if (judge_fileName != "Assets/AssetBundle/PC")
            {
                Debug.LogError($"请不要选取Assets/AssetBundle/PC路径以外的文件");
                return;
            }
            //截取路径当中的文件名 用于作为 StreamingAssets中的文件名
            string fileName = assetPath.Substring(assetPath.LastIndexOf('/'));

            // 判断是否有.符号 如果有 证明有后缀 不处理
            if (fileName.IndexOf('.') != -1)
                continue;//也可以在拷贝之前去获取全路径，然后通过FileInfo去获取后缀来判断 这样更准确

            //利用AssetDatabase中的API 将选中文件 复制到目标路径
            AssetDatabase.CopyAsset(assetPath, "Assets/StreamingAssets" + fileName);

            //获取拷贝到StreamingAssets文件夹中的文件的全部信息
            System.IO.FileInfo fileInfo = new System.IO.FileInfo(Application.streamingAssetsPath + fileName);
            //拼接AB包信息到字符串中
            abCompareInfo += fileInfo.Name + " " + fileInfo.Length + " " + CreatABMD5.GetMD5(fileInfo.FullName);
            //用一个符号隔开多个AB包信息
            abCompareInfo += "|";
        }
        //去掉最后一个|符号 为了之后拆分字符串方便
        abCompareInfo = abCompareInfo.Substring(0, abCompareInfo.Length - 1);
        //将本地默认资源的对比信息 存入文件
        File.WriteAllText(Application.streamingAssetsPath + "/ABCompareInfo.txt", abCompareInfo);
        AssetDatabase.Refresh();
    }

}
