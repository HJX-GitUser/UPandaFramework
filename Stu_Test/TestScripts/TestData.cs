using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TestData : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        string a = "a/b/c/d/e";
        print(a.Substring(a.LastIndexOf('/') + 1));
    }

    // Update is called once per frame
    void Update()
    {

    }
}
