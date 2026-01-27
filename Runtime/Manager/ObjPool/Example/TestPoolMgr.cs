using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UPandaGF;

public class TestPoolMgr : MonoBehaviour
{
    List<GameObject> cubes = new List<GameObject>();
    List<GameObject> spheres = new List<GameObject>();
    public bool LoadAsync = false;
    public AssetLoadMethod loadMethod;
    public string obj1Path = "Obj1";
    public string obj2Path = "Obj2";


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
    }


    private async void CreatObj1()
    {
        if (LoadAsync)
        {
            GameObject obj = await GameObjectPoolMgr.Instance.GetObjAsync(obj1Path, loadMethod);
            obj.transform.position = new Vector3(Random.Range(0, 11), Random.Range(0, 11), Random.Range(0, 11));
            cubes.Add(obj);

        }
        else
        {
            GameObject obj = GameObjectPoolMgr.Instance.GetObj(obj1Path, loadMethod);
            obj.transform.position = new Vector3(Random.Range(-11, 11), Random.Range(0, 11), Random.Range(0, 11));
            cubes.Add(obj);
        }
    }

    private async void CreatObj2()
    {
        if (LoadAsync)
        {
            GameObject obj = await GameObjectPoolMgr.Instance.GetObjAsync(obj2Path, loadMethod);
            obj.transform.position = new Vector3(Random.Range(0, 11), Random.Range(0, 11), Random.Range(0, 11));
            spheres.Add(obj);
        }
        else
        {
            GameObject obj = GameObjectPoolMgr.Instance.GetObj(obj2Path, loadMethod);
            obj.transform.position = new Vector3(Random.Range(-11, 11), Random.Range(0, 11), Random.Range(0, 11));
            spheres.Add(obj);
        }
    }

    private void PushAll()
    {
        foreach (GameObject item in cubes)
        {
            item.GetComponent<Rigidbody>().velocity = Vector3.zero;
            GameObjectPoolMgr.Instance.PushObj(obj1Path, item);
        }
        foreach (GameObject item in spheres)
        {
            item.GetComponent<Rigidbody>().velocity = Vector3.zero;
            GameObjectPoolMgr.Instance.PushObj(obj2Path, item);
        }
        cubes.Clear();
        spheres.Clear();
    }

}
