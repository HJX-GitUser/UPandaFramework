using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UPandaGF;

public class ABUpdataUI : MonoBehaviour
{
    ABUpdataMgr aBUpdataMgr;
    public Text text;
    // Start is called before the first frame update
    void Awake()
    {
        aBUpdataMgr = GetComponentInParent<ABUpdataMgr>();
        aBUpdataMgr.updataMessage += UpdataMessage;
        text = transform.GetComponentInChildren<Text>();
        text.text = "";
        aBUpdataMgr.updataSuccessEvent += UpdataSuccessEvent;
    }

    private void UpdataSuccessEvent()
    {
        ABLoadMgr.Instance.LoadResAsync<GameObject>("cube", "Cube", (o) =>
        {
            GameObject obj = Instantiate(o);
            obj.transform.position = Vector3.zero;
        });
    }

    private void UpdataMessage(string arg0)
    {
        string[] temp = arg0.Split('*');
        if (temp.Length >= 2)
        {
            text.text = temp[0] + "..........." + temp[1];
            if (temp[1] == "10")
            {
                text.text = "";
            }
        }
        else
        {
            PLoger.Log(arg0);
        }
    }
    
    private void OnDestroy()
    {
        aBUpdataMgr.updataMessage -= UpdataMessage;
    }
}
