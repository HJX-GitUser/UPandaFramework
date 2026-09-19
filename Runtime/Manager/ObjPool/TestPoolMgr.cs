using System.Collections.Generic;
using UnityEngine;
using UPandaGF;

/// <summary>
/// 对象池使用示例：演示取对象（Task / 回调两种异步形式）、回收、预热与统计。
/// 配套场景：Example/PoolUseScene.unity；配套预制体：Resources/Obj1.prefab、Resources/Obj2.prefab
/// </summary>
public class TestPoolMgr : MonoBehaviour
{
    List<GameObject> cubes = new List<GameObject>();
    List<GameObject> spheres = new List<GameObject>();

    [Tooltip("true：使用 Task 形式异步取对象；false：使用回调形式")]
    public bool LoadAsync = true;

    public AssetLoadMethod loadMethod;
    public string obj1Path = "Obj1";
    public string obj2Path = "Obj2";

    [Tooltip("启动时每个预制体预先实例化多少个放入池中（0 = 不预热）")]
    public int prewarmCount = 0;

    private void Awake()
    {
        EventCenter.Instance.AddEventListener<GFLoadedEvent>(OnRootStarted);
        IAssetsLoader loader = UPGameRoot.Instance.GetAssetsLoader();
    }
    void OnDestroy()
    {
         EventCenter.Instance.RemoveEventListener<GFLoadedEvent>(OnRootStarted);
    }

    private async void OnRootStarted(GFLoadedEvent evt)
    {
        if (prewarmCount <= 0) return;

        // 预热：提前把对象放进池子，避免首次取对象时的加载尖峰
        await GameObjectPoolMgr.Instance.Prewarm(obj1Path, prewarmCount, loadMethod);
        await GameObjectPoolMgr.Instance.Prewarm(obj2Path, prewarmCount, loadMethod);
    }

    private void OnGUI()
    {
        if (GUI.Button(new Rect(10, 25, 100, 50), "创建Obj1"))
        {
            CreatObj1();
        }
        if (GUI.Button(new Rect(110, 25, 100, 50), "创建Obj2"))
        {
            CreatObj2();
        }

        if (GUI.Button(new Rect(10, 80, 200, 50), "回收所有"))
        {
            PushAll();
        }

        GUI.Label(new Rect(10, 135, 320, 25), $"池中空闲对象：{GameObjectPoolMgr.Instance.TotalPooledCount}");
    }

    private void CreatObj1()
    {
        if (LoadAsync)
        {
            CreatObj1ByTask();
        }
        else
        {
            // 回调形式：后续逻辑写在回调里
            GameObjectPoolMgr.Instance.GetObjAsync(obj1Path, SetupObj1, loadMethod);
        }
    }

    private async void CreatObj1ByTask()
    {
        GameObject obj = await GameObjectPoolMgr.Instance.GetObjAsync(obj1Path, loadMethod);
        SetupObj1(obj);
    }

    private void SetupObj1(GameObject obj)
    {
        // 加载失败（含超时）会以 null 返回/回调，必须判空
        if (obj == null)
        {
            Debug.LogError($"Obj1 创建失败：{obj1Path}");
            return;
        }

        obj.transform.position = new Vector3(Random.Range(-11, 11), Random.Range(0, 11), Random.Range(-11, 11));
        cubes.Add(obj);
    }

    private async void CreatObj2()
    {
        GameObject obj = await GameObjectPoolMgr.Instance.GetObjAsync(obj2Path, loadMethod);
        if (obj == null)
        {
            Debug.LogError($"Obj2 创建失败：{obj2Path}");
            return;
        }

        obj.transform.position = new Vector3(Random.Range(0, 11), Random.Range(0, 11), Random.Range(0, 11));
        spheres.Add(obj);
    }

    private void PushAll()
    {
        foreach (GameObject item in cubes)
        {
            ResetRigidbody(item);
            GameObjectPoolMgr.Instance.PushObj(obj1Path, item);
        }
        cubes.Clear();

        foreach (GameObject item in spheres)
        {
            ResetRigidbody(item);
            GameObjectPoolMgr.Instance.PushObj(obj2Path, item);
        }
        spheres.Clear();
    }

    /// <summary>
    /// 复用前复位状态。更推荐的做法：给预制体根节点挂一个实现 IPoolable 的组件，
    /// 在 OnDespawn / OnSpawn 里处理复位，这样任何取用方都不需要重复写这类代码。
    /// </summary>
    private static void ResetRigidbody(GameObject obj)
    {
        if (obj == null) return;

        Rigidbody body = obj.GetComponent<Rigidbody>();
        if (body == null) return;

        body.velocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
    }
}
