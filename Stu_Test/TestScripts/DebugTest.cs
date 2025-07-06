using UnityEngine;

public class DebugTest : MonoBehaviour
{
    public LogConfig logConfig;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        PLoger.InitLog(new LogConfig
        {
            logHeadFix = "标识1"
        });
        PLoger.Log("<color=red>打印</color><color=yellow>测试</color>");
        PLoger.LogFormat("{0},{1},{2}", "<color=red>输出1</color>", "<color=yellow>输出2</color>", "<color=blue>输出3</color>");
        PLoger.LogWarning("<color=red>打印</color><color=yellow>测试</color>");
        PLoger.LogWarningFormat("{0},{1},{2}", "<color=red>输出1</color>", "<color=yellow>输出2</color>", "<color=blue>输出3</color>");
        PLoger.LogError("<color=red>打印</color><color=yellow>测试</color>");
        PLoger.LogErrorFormat("{0},{1},{2}", "<color=red>输出1</color>", "<color=yellow>输出2</color>", "<color=blue>输出3</color>");
    }

    // Update is called once per frame
    void Update()
    {

    }
}
