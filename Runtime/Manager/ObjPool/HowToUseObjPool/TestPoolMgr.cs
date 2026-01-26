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
   
    // Update is called once per frame
    async void Update()
    {
        if (Input.GetKeyDown(KeyCode.P))
        {
            foreach (GameObject item in cubes)
            {
                item.GetComponent<Rigidbody>().velocity = Vector3.zero;
                PoolMgr.Instance.PushObj(obj1Path, item);
            }
            foreach (GameObject item in spheres)
            {
                item.GetComponent<Rigidbody>().velocity = Vector3.zero;
                PoolMgr.Instance.PushObj(obj2Path, item);
            }
            cubes.Clear();
            spheres.Clear();
        }
        if (Input.GetKeyDown(KeyCode.Q))
        {

            if (LoadAsync)
            {
                GameObject obj = await PoolMgr.Instance.GetObjAsync(obj1Path, loadMethod);
                obj.transform.position = new Vector3(Random.Range(0, 11), Random.Range(0, 11), Random.Range(0, 11));
                cubes.Add(obj);

            }
            else
            {
                GameObject obj = PoolMgr.Instance.GetObj(obj1Path, loadMethod);
                obj.transform.position = new Vector3(Random.Range(-11, 11), Random.Range(0, 11), Random.Range(0, 11));
                cubes.Add(obj);
            }

        }

        if (Input.GetKeyDown(KeyCode.W))
        {

            if (LoadAsync)
            {
                GameObject obj = await PoolMgr.Instance.GetObjAsync(obj2Path, loadMethod);
                obj.transform.position = new Vector3(Random.Range(0, 11), Random.Range(0, 11), Random.Range(0, 11));
                spheres.Add(obj);
            }
            else
            {
                GameObject obj = PoolMgr.Instance.GetObj(obj2Path, loadMethod);
                obj.transform.position = new Vector3(Random.Range(-11, 11), Random.Range(0, 11), Random.Range(0, 11));
                spheres.Add(obj);
            }

        }
    }

}
