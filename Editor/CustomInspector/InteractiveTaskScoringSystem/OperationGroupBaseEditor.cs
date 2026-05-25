using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UPandaGF.RunTime.InteractiveTaskScoringSystem
{
    [CustomEditor(typeof(OperationGroupBase), true)]
    public class OperationGroupBaseEditor : Editor
    {
        private OperationGroupBase component;
        private bool isSeriesOperationGroup;
        private void OnEnable()
        {
            if (component == null) component = target as OperationGroupBase;
            isSeriesOperationGroup = component is SeriesOperationGroup;
        }
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            if (!EditorApplication.isPlaying)
            {
                GUILayout.Space(20);

                if (GUILayout.Button("Add 基础任务检查"))
                {
                    int _count = component.transform.childCount;
                    GameObject obj = CreatNode($"OperationCheck({_count})");
                    OperationCheckBase oc = obj.AddComponent<OperationCheckBase>();
                    Undo.RecordObject(obj, "Add 基础任务检查");
                }
                if (GUILayout.Button("Add 操作检查组（串联）"))
                {
                    GameObject obj = CreatNode("SeriesOperationGroup");
                    obj.AddComponent<SeriesOperationGroup>();
                    Undo.RecordObject(obj, "Add 操作检查组（串联）");
                }
                if (GUILayout.Button("Add 操作检查组（并联）"))
                {
                    GameObject obj = CreatNode("ParallelOperationGroup");
                    obj.AddComponent<ParallelOperationGroup>();
                    Undo.RecordObject(obj, "Add 操作检查组（并联）");
                }

                if (isSeriesOperationGroup)
                {
                    EditorGUILayout.HelpBox("【串联检查组】\n子节点任务检查依次启动，直至全部完成", MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox("【并联检查组】\n子节点任务检查全部启动，直至完成数量满足CompleteCount。\nCompleteCount等于0且步骤数量大于0时，这需要任务全部完成", MessageType.Info);
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

