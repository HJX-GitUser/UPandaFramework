using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
namespace UPandaGF.RunTime.InteractiveTaskScoringSystem
{
    [CustomEditor(typeof(OperationCheckBase))]
    public class OperationCheckBaseEditor : Editor
    {
        OperationCheckBase component;
        private void OnEnable()
        {
            if (component == null) component = target as OperationCheckBase;
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            if (component.stepData.correctTargetId != 0 && component.TargetEntity == null)
            {
                if (GUILayout.Button("≤È’“"))
                {
                    component.TargetEntity = OperationCheckEntityTool.GetEntity(component.stepData.correctTargetId);
                }
            }
        }
    }
}


