using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
namespace UPandaGF.RunTime.InteractiveTaskScoringSystem
{
    [CustomEditor(typeof(OperationCheckBase), true)]
    public class OperationCheckBaseEditor : Editor
    {
        private OperationCheckBase component;
        private void OnEnable()
        {
            component = (OperationCheckBase)target;
        }
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            if (!string.IsNullOrEmpty(component.OperatingStepID) && component.TargetEntity == null)
            {
                if (GUILayout.Button("查找对应实体"))
                {
                    TaskEntityBase[] args = FindObjectsOfType<TaskEntityBase>();
                    bool findSuccess = false;
                    foreach (TaskEntityBase arg in args)
                    {
                        if (arg.StepIDGroup != null && arg.StepIDGroup.Length > 0)
                        {
                            foreach (var item in arg.StepIDGroup)
                            {
                                if (item == component.GetID)
                                {
                                    findSuccess = true;
                                    component.TargetEntity = arg;
                                    //Selection.activeGameObject = arg.gameObject;
                                    //EditorGUIUtility.PingObject(arg.gameObject);
                                    EditorUtility.SetDirty(component);
                                    break;
                                }
                            }
                            if (findSuccess) break;
                        }
                    }
                    if (!findSuccess) Debug.LogWarning($"{component.GetID} 查找失败");
                }
            }
        }
    }

}

