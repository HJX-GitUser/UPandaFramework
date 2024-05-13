using System.Collections;
using System.Collections.Generic;
using UnityEngine;
/// <summary>
/// ResMgr使用案列
/// </summary>
public class UseResMgrExample : MonoBehaviour
{
    public enum AssetsLoadMode
    {
        Load_T,//同步 泛型方式加载
        Load_Type,//同步 type方式加载
        LoadAsync_T,//异步 泛型方式加载
        LoadAsync_Type//异步 type方式加载
    }
    public AssetsLoadMode assetsLoadMode;
    private GameObject obj;
    private GameObject obj2;
    private string objPath = "Obj1";
    private string obj2Path = "Obj2";
    void Start()
    {
        switch (assetsLoadMode)
        {
            case AssetsLoadMode.Load_T:
                obj = Instantiate(ResMgr.Instance.Load<GameObject>(objPath));
                obj2 = Instantiate(ResMgr.Instance.Load<GameObject>(obj2Path));
                obj2.transform.position = obj.transform.position + Vector3.right * 2;
                break;
            case AssetsLoadMode.Load_Type:
                obj = Instantiate(ResMgr.Instance.Load(objPath, typeof(GameObject)) as GameObject);
                obj2 = Instantiate(ResMgr.Instance.Load(obj2Path, typeof(GameObject)) as GameObject);
                obj2.transform.position = obj.transform.position + Vector3.right * 2;
                break;
            case AssetsLoadMode.LoadAsync_T:
                ResMgr.Instance.LoadAsync<GameObject>(objPath, (o) =>
                {
                    obj = Instantiate(o);
                });
                ResMgr.Instance.LoadAsync<GameObject>(obj2Path, (o) =>
                {
                    obj2 = Instantiate(o);
                    obj2.transform.position = obj.transform.position + Vector3.right * 2;
                });
                break;
            case AssetsLoadMode.LoadAsync_Type:
                ResMgr.Instance.LoadAsync(objPath, typeof(GameObject), (o) =>
                 {
                     obj = Instantiate(o as GameObject);
                 });
                ResMgr.Instance.LoadAsync(obj2Path, typeof(GameObject), (o) =>
                {
                    obj2 = Instantiate(o as GameObject);
                    obj2.transform.position = obj.transform.position + Vector3.right * 2;
                });
                break;
        }
        

    }
}
