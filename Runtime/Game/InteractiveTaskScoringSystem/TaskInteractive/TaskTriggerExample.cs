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
    }
    void OnMouseDown()
    {
        interactiveTrigger.OnSelect();
    }

    void OnMouseUp()
    {
        interactiveTrigger.OnSelectExit();
    }

    private void OnMouseOver()
    {
        interactiveTrigger.OnStay();
    }

    void OnMouseEnter()
    {

        interactiveTrigger.OnEnter();
    }

    void OnMouseExit()
    {

        interactiveTrigger.OnExit();
    }
}
