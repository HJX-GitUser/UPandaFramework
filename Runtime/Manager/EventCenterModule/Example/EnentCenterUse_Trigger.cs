using UnityEngine;
using UPandaGF;

public class EnentCenterUse_Trigger : MonoBehaviour
{
    public float triggerInterval = 1f;
    private float _nextPrintTime = 0;
    private void Update()
    {
        if (Time.time >= _nextPrintTime)
        {
            _nextPrintTime = Time.time + triggerInterval;
            EventCenter.Instance.EventTrigger(new EnentTest1($"¥•∑¢≤‚ ‘ {_nextPrintTime}"));
        }
    }
}
