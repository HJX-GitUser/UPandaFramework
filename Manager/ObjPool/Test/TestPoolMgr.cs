using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TestPoolMgr : MonoBehaviour
{
    List<GameObject> cubes = new List<GameObject>();
    List<GameObject> spheres= new List<GameObject>();
    // Start is called before the first frame update
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
        if(Input.GetKeyDown(KeyCode.P))
        {
            foreach (GameObject item in cubes)
            {
                PoolMgr.Instance.PushObj("Perfabs/Cube", item);
            }
            foreach (GameObject item in spheres)
            {
                PoolMgr.Instance.PushObj("Perfabs/Sphere", item);
            }
        }
        if (Input.GetKey(KeyCode.Q))
        {
            PoolMgr.Instance.GetObj("Perfabs/Cube", (obj) =>
            {
                obj.transform.position = new Vector3(Random.Range(0, 11), Random.Range(0, 11), Random.Range(0, 11));
                cubes.Add(obj);
            });
        }

        if (Input.GetKey(KeyCode.W))
        {
            PoolMgr.Instance.GetObj("Perfabs/Sphere", (obj) =>
            {
                obj.transform.position = new Vector3(Random.Range(0, 11), Random.Range(0, 11), Random.Range(0, 11));
                spheres.Add(obj);
            });
        }
    }
}
