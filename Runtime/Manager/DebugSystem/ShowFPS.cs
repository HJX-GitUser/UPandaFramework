using UnityEngine;
using System.Collections;
namespace UPandaGF
{
    public class ShowFPS : MonoBehaviour
    {
        float deltaTime = 0.0f;

        GUIStyle mStyle;
        void Awake()
        {
            mStyle = new GUIStyle();
            mStyle.alignment = TextAnchor.UpperLeft;
            mStyle.normal.background = null;
            mStyle.fontSize = 35;
            mStyle.normal.textColor = Color.white;
        }

        void Update()
        {
            deltaTime += (Time.deltaTime - deltaTime) * 0.1f;
        }
        Rect rect = new Rect(0, 0, 500, 300);
        Rect buttonRect = new Rect(0, 0, 200, 50);
        void OnGUI()
        {


            float fps = 1.0f / deltaTime;
            string text = string.Format(" FPS:{0:N0} ", fps);
            if (UPGameRoot.Instance.EnableDebugModel)
            {
                if (UPGameRoot.Instance.reporter != null && !UPGameRoot.Instance.reporter.show)
                {
                    if (GUI.Button(buttonRect, text))
                    {
                        UPGameRoot.Instance.reporter.ShowLogWindows();
                    }
                }
            }
            else
            {
                GUI.Label(rect, text, mStyle);
            }
            Rect appInfoRect = new Rect(Screen.width - 400, Screen.height - 30, 500, 300);
        }
    }
}


