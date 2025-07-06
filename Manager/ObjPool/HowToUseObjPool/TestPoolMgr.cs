using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TestPoolMgr : MonoBehaviour
{
    List<GameObject> cubes = new List<GameObject>();
    List<GameObject> spheres = new List<GameObject>();

    public string obj1Path = "Obj1";
    public string obj2Path = "Obj2";
    // Start is called before the first frame update
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.P))
        {
            foreach (GameObject item in cubes)
            {
                item.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;
                PoolMgr.Instance.PushObj(obj1Path, item);
            }
            foreach (GameObject item in spheres)
            {
                item.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;
                PoolMgr.Instance.PushObj(obj2Path, item);
            }
            cubes.Clear();
            spheres.Clear();
        }
        if (Input.GetKeyDown(KeyCode.Q))
        {
            //PoolMgr.Instance.GetObjAsync(obj1Path, (obj) =>
            //{
            //    obj.transform.position = new Vector3(Random.Range(0, 11), Random.Range(0, 11), Random.Range(0, 11));
            //    cubes.Add(obj);
            //});
            GameObject obj = PoolMgr.Instance.GetObj(obj1Path);
            obj.transform.position = new Vector3(Random.Range(-11, 11), Random.Range(0, 11), Random.Range(0, 11));
            cubes.Add(obj);
        }

        if (Input.GetKeyDown(KeyCode.W))
        {
            //PoolMgr.Instance.GetObjAsync(obj2Path, (obj) =>
            //{
            //    obj.transform.position = new Vector3(Random.Range(0, 11), Random.Range(0, 11), Random.Range(0, 11));
            //    spheres.Add(obj);
            //});
            GameObject obj = PoolMgr.Instance.GetObj(obj2Path);
            obj.transform.position = new Vector3(Random.Range(-11, 11), Random.Range(0, 11), Random.Range(0, 11));
            spheres.Add(obj);
        }
    }
}
