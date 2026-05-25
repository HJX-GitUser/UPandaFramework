using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UPandaGF.RunTime.InteractiveTaskScoringSystem
{
    [CustomEditor(typeof(TaskStepBase))]
    public class TaskStepBaseEditor : Editor
    {
        private TaskStepBase component;
        private bool operationsCheck = false; //确保任务节点下只有一个任务检查组
        private void OnEnable()
        {
            //Debug.Log("TaskStepBase Inspector OnEnable");
            if (component == null) component = target as TaskStepBase;
            List<OperationGroupBase> operations = new List<OperationGroupBase>();
            Transform c = component.transform;
            for (int i = 0; i < c.childCount; i++)
            {
                OperationGroupBase temp = c.GetChild(i).GetComponent<OperationGroupBase>();
                if (temp != null) operations.Add(temp);
            }
            operationsCheck = operations.Count > 1;
            if (operationsCheck)
            {
                Debug.LogError("任务节点下只允许有一个操作检查组，你可以在任务组节点下继续创建其他任务组节点。");
            }
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            if (operationsCheck)
            {
                EditorGUILayout.HelpBox("任务节点下只允许有一个操作检查组,你可以在任务组节点下继续创建其他任务组节点。", MessageType.Error);
            }
            else
            {
                if (component.operationGroup == null && !EditorApplication.isPlaying)
                {
                    GUILayout.Space(10);
                    if (GUILayout.Button("Add 操作检查组（串联）"))
                    {
                        GameObject obj = CreatNode("SeriesOperationGroup");
                        component.operationGroup = obj.AddComponent<SeriesOperationGroup>();
                        Undo.RecordObject(obj, "SeriesOperationGroup");
                    }
                    if (GUILayout.Button("Add 操作检查组（并联）"))
                    {
                        GameObject obj = CreatNode("ParallelOperationGroup");
                        component.operationGroup = obj.AddComponent<ParallelOperationGroup>();
                        Undo.RecordObject(obj, "ParallelOperationGroup");
                    }
                }
            }

        }

        public GameObject CreatNode(string name)
        {
            GameObject temp = new GameObject(name);
            temp.transform.SetParent(component.transform);
            temp.transform.localPosition = Vector3.zero;
            return temp;
        }

    }
}

