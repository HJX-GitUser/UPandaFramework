using UnityEditor;
using UnityEngine;

namespace UPandaGF.RunTime.InteractiveTaskScoringSystem
{
    [CustomEditor(typeof(TaskDataManager))]
    public class TaskDataManagerEditor : Editor
    {
        private TaskDataManager component;
        private void OnEnable()
        {
            if (component == null) component = target as TaskDataManager;
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            if (component.taskSteps != null && !EditorApplication.isPlaying)
            {
                if (GUILayout.Button("创建任务节点"))
                {
                    TaskConfig arg = component.taskSteps;
                    Transform node = component.transform;
                    TaskStepBase[] children = component.GetComponentsInChildren<TaskStepBase>();
                    if (children != null && children.Length > 0)
                    {
                        for (int i = 0; i < arg.stepsConfig.Length; i++)
                        {
                            if (i < children.Length)
                            {
                                children[i].taskStepData = arg.stepsConfig[i];
                                children[i].transform.name = $"Task({children[i].taskStepData.stepID})";
                            }
                            else
                            {
                                CreatNode(arg.stepsConfig[i], node);
                            }
                        }
                    }
                    else
                    {
                        foreach (var item in arg.stepsConfig)
                        {
                            CreatNode(item, node);

                        }
                    }
                    EditorUtility.SetDirty(node);
                }
            }
            if (EditorApplication.isPlaying)
            {
                if (GUILayout.Button("步骤提示"))
                {
                    component.OperationInstructions();
                }

                if (GUILayout.Button("步骤跳过"))
                {
                    component.SkipTask();
                }
            }
        }

        private void CreatNode(TaskStepData arg, Transform node)
        {
            GameObject obj = new GameObject($"Task({arg.stepID})");
            obj.transform.SetParent(node);
            obj.transform.localPosition = Vector3.zero;
            obj.transform.localRotation = Quaternion.identity;
            TaskStepBase ts = obj.AddComponent<TaskStepBase>();
            ts.taskStepData = arg;
        }
    }
}

