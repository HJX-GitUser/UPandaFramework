using UnityEngine;

/// <summary>
/// 资源加载方式
/// </summary>
public enum AssetLoaddingMethod
{
    Editor,//编辑器环境下加载资源
    Assetbundles,//使用AssetBundle加载资源
}

public class UPGameRoot : BaseMonoSingleton<UPGameRoot>
{
    [Header("资源加载方式")]
    public AssetLoaddingMethod method;
    [Header("从StreamingAssets路径下加载资源")]
    public bool LoadAssetOnStreamingAssets = false;
    [Header("资源路径")]
    public string LoadAssetPath = "AB/PC/";
   
}
