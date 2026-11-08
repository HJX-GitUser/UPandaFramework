using UnityEngine;

namespace UPandaGF
{

/// <summary>单一 IMGUI 入口，显示/隐藏复用组件，不反复创建和销毁。</summary>
[RequireComponent(typeof(Reporter))]
[DisallowMultipleComponent]
public class ReporterGUI : MonoBehaviour
{
    private Reporter reporter;
    private void Awake() { reporter = GetComponent<Reporter>(); }
    private void OnGUI()
    {
        if (reporter != null && reporter.isActiveAndEnabled) reporter.OnGUIDraw();
    }
}

}