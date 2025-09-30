using UnityEngine;
namespace UPandaGF
{
    public class DebugerInit : MonoBehaviour
    {
        public LogConfig logConfig;
        private void Awake()
        {
            PLoger.InitLog(logConfig);
        }
    }
}

