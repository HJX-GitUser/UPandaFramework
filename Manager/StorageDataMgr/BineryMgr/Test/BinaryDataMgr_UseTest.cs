using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class CustomDate_By_BinaryDataMgr_Use
{
    public int i;
    public float f;
    public string s;
}
public class BinaryDataMgr_UseTest : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        //Save("SaveDataName");

        CustomDate_By_BinaryDataMgr_Use cbu = BinaryDataMgr.Instance.Load<CustomDate_By_BinaryDataMgr_Use>("SaveDataName");
    }
    private void Save(string fileName)
    {
        CustomDate_By_BinaryDataMgr_Use cbu = new CustomDate_By_BinaryDataMgr_Use();
        cbu.i = 10;
        cbu.f = 20.20f;
        cbu.s = "TestData";
        BinaryDataMgr.Instance.Save(cbu, fileName);
    }

}
