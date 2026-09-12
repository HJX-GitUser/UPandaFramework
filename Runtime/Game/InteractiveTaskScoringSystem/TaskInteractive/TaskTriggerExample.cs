using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UPandaGF.RunTime.InteractiveTaskScoringSystem;

/// <summary>
/// 交互触发案例，获取interactiveTrigger接口，根据需求触发
/// </summary>
public class TaskTriggerExample : MonoBehaviour
{
    protected InteractiveTrigger interactiveTrigger;
    protected virtual void Awake()
    {
        interactiveTrigger = GetComponent<InteractiveTrigger>();
        if (interactiveTrigger == null) Debug.LogError($"{name} 上未找到 InteractiveTrigger 实现，鼠标事件将无法转发");   // 修复：给出明确提示
    }
    void OnMouseDown()
    {
        if (interactiveTrigger != null) interactiveTrigger.OnSelect();   // 修复：空引用保护
    }

    void OnMouseUp()
    {
        if (interactiveTrigger != null) interactiveTrigger.OnSelectExit();   // 修复：空引用保护
    }

    private void OnMouseOver()
    {
        if (interactiveTrigger != null) interactiveTrigger.OnStay();   // 修复：空引用保护
    }

    void OnMouseEnter()
    {

        if (interactiveTrigger != null) interactiveTrigger.OnEnter();   // 修复：空引用保护
    }

    void OnMouseExit()
    {

        if (interactiveTrigger != null) interactiveTrigger.OnExit();   // 修复：空引用保护
    }
}
