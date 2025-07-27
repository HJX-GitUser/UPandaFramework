using UnityEngine;

public class DebugerInit : MonoBehaviour
{
    public LogConfig logConfig;
    private void Awake()
    {
        PLoger.InitLog(logConfig);
    }
}
