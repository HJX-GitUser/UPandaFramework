using UnityEngine;
using UPandaGF;

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
    [Header("主包名")]
    public string MainName = "PC";

    [Header("启动时检查资源更新")]
    public bool enableChecekOnStart = false;

    private DebugerInit debugerInit;
    private ABUpdataMgr abUpdataMgr;
    private ABLoadMgr abLoadMgr;


    private void Reset()
    {
        InitComponent();
    }
    protected override void Awake()
    {
        base.Awake();
        InitComponent();
        abUpdataMgr.localFilePath = LoadAssetPath;
        abUpdataMgr.enableChecekOnStart = enableChecekOnStart;
        string p = LoadAssetOnStreamingAssets ? Application.streamingAssetsPath : Application.persistentDataPath;
        abLoadMgr.PathUrl = p + "/" + LoadAssetPath;
        abLoadMgr.MainName = MainName;
        debugerInit.Init();
        abUpdataMgr.Init();
        abLoadMgr.LogInitMessage();
    }

    private void InitComponent()
    {
        debugerInit = InitComponent<DebugerInit>();
        abUpdataMgr = InitComponent<ABUpdataMgr>();
        abLoadMgr = InitComponent<ABLoadMgr>();
    }

    private T InitComponent<T>() where T : Component
    {
        T component = GetComponentInChildren<T>();
        if (component == null)
        {
            GameObject obj = new GameObject(typeof(T).Name);
            obj.transform.parent = transform;
            component = obj.AddComponent<T>();
        }
        return component;
    }

}
