using UnityEngine;
using UPandaGF;

public class EventCenterUse_AddListener : MonoBehaviour
{

    private void OnEnable()
    {
        EventCenter.Instance.AddEventListener<EnentTest1>(TestEvent1);//事件注册
    }

    private void OnDisable()
    {
        EventCenter.Instance.RemoveEventListener<EnentTest1>(TestEvent1);//事件注销
    }
    private void OnDestroy()
    {
        EventCenter.Instance.RemoveEventListener<EnentTest1>(TestEvent1);
    }

    private void TestEvent1(EventArgBase arg0)
    {
        EnentTest1 arg = arg0 as EnentTest1;
        PLogger.Log($"事件触发1 参数：{arg.arg0}");
    }
}

public class EnentTest1 : EventArgBase//声名事件
{
    public string arg0 { get;private set; }
    public EnentTest1(string arg)
    {
        arg0 = arg;
    }
}
