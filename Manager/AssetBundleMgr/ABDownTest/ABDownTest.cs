using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ABDownTest : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        ABUpdataMgr.Instance.CheckUpdata((isOver) =>
        {
            if(isOver)
            {
                print("更新结束");
            }
            else
            {
                print("更新失败，检查网络");
            }
        },
        (info) =>
        {
            print(info);
        });
    }


}
