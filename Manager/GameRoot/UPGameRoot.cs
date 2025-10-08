using UnityEngine;

/// <summary>
/// 资源加载方式
/// </summary>
public enum AssetLoaddingMethod
{
    Editor,//编辑器环境下加载资源
    Assetbundles,//使用AssetBundle加载资源
}

public class UPGameRoot : MonoBehaviour
{
    [Header("从StreamingAssets路径下加载资源")]
    public bool LoadAssetOnStreamingAssets = false;
    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }
}
