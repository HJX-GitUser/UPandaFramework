using UnityEngine;

public class DebugTest : MonoBehaviour
{
    public LogConfig logConfig;
    public bool debug = true;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        Application.targetFrameRate = 60;
        PLoger.InitLog(logConfig);

        PLoger.Log("<color=red>´òÓ¡</color><color=yellow>²âÊÔ</color>");
        PLoger.LogFormat("{0},{1},{2}", "<color=red>Êä³ö1</color>", "<color=yellow>Êä³ö2</color>", "<color=blue>Êä³ö3</color>");
        PLoger.LogWarning("<color=red>´òÓ¡</color><color=yellow>²âÊÔ</color>");
        PLoger.LogWarningFormat("{0},{1},{2}", "<color=red>Êä³ö1</color>", "<color=yellow>Êä³ö2</color>", "<color=blue>Êä³ö3</color>");
        PLoger.LogError("<color=red>´òÓ¡</color><color=yellow>²âÊÔ</color>");
        PLoger.LogErrorFormat("{0},{1},{2}", "<color=red>Êä³ö1</color>", "<color=yellow>Êä³ö2</color>", "<color=blue>Êä³ö3</color>");
    }
    float t = 0;
    // Update is called once per frame
    void Update()
    {
        if (debug)
        {
            t += Time.deltaTime;
            if (t >= 0.5f)
            {
                t = 0;
                PLoger.Log("<color=red>´òÓ¡</color><color=yellow>²âÊÔ</color>");
                PLoger.LogFormat("{0},{1},{2}", "<color=red>Êä³ö1</color>", "<color=yellow>Êä³ö2</color>", "<color=blue>Êä³ö3</color>");
                PLoger.LogWarning("<color=red>´òÓ¡</color><color=yellow>²âÊÔ</color>");
                PLoger.LogWarningFormat("{0},{1},{2}", "<color=red>Êä³ö1</color>", "<color=yellow>Êä³ö2</color>", "<color=blue>Êä³ö3</color>");
                PLoger.LogError("<color=red>´òÓ¡</color><color=yellow>²âÊÔ</color>");
                PLoger.LogErrorFormat("{0},{1},{2}", "<color=red>Êä³ö1</color>", "<color=yellow>Êä³ö2</color>", "<color=blue>Êä³ö3</color>");
            }
        }
    }
}
