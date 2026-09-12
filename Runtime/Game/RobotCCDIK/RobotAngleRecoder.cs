using System;
using System.Collections.Generic;
using System.ComponentModel;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UPandaGF.RunTime.RobotCCDIK
{
    public enum RobotAngleCtrModel
    {
        None = 0,
        RealTime,
        Click,
    }

    [RequireComponent(typeof(CCDIKController))]
    public class RobotAngleRecoder : MonoBehaviour
    {
        //[HideInInspector]
        public Transform Target;

        [System.Serializable]
        public class RobotAngleInfo
        {
            public string description;
            public float[] angles = new float[6];
            public Vector3[] localEulerAngle = new Vector3[6];
        }
        private CCDIKController optimizedCCDIK;

        public int anglePosIndex = 0;
        public List<RobotAngleInfo> anglePos;
        private Dictionary<string, RobotAngleInfo> anglePosDict = new Dictionary<string, RobotAngleInfo>();

        public RobotAngleInfo currentPos;

        public CCDIKController OptimizedCCDIK { get => optimizedCCDIK; }

        private RobotAngleCtrModel ctrModel;
        public RobotAngleCtrModel CtrModel
        {
            get => ctrModel;
            set
            {
                ctrModel = value;
                if (ctrModel == RobotAngleCtrModel.None)
                {
                    optimizedCCDIK.ResetAngle();
                    ResetRobotTarget();
                }
            }
        }

        public void ResetRobotTarget()
        {
            Target.position = OptimizedCCDIK.endEffector.position;
            Target.rotation = OptimizedCCDIK.endEffector.rotation;
        }
        private void Awake()
        {
            optimizedCCDIK = GetComponent<CCDIKController>();
            ctrModel = RobotAngleCtrModel.None;
        }

        private void Start()
        {
            currentPos = new RobotAngleInfo();
            foreach (var item in anglePos)
            {
                anglePosDict.Add(item.description, item);
            }
            CreatAimTarget();
        }

        private void Update()
        {
            switch (ctrModel)
            {
                case RobotAngleCtrModel.None:
                    break;
                case RobotAngleCtrModel.RealTime:
                    // 直接求解
                    bool arg = optimizedCCDIK.SolveIK(Target.transform.position, Target.transform.rotation);
                    break;
                case RobotAngleCtrModel.Click:
                    break;
            }
        }

        private void CreatAimTarget()
        {
            if (Target != null) return;
            Debug.Log("RobotAim Creat");
            Target = new GameObject("RobotAim").transform;
            Target.parent = transform.parent;
            Target.position = OptimizedCCDIK.endEffector.position;
            Target.rotation = OptimizedCCDIK.endEffector.rotation;
        }

        public void SetAngle(int index, Action callback = null)
        {
            if (anglePos.Count == 0) return;
            if (index < 0 || index >= anglePos.Count) return;
            optimizedCCDIK.AngleLerp(anglePos[index].localEulerAngle, 1, callback);
        }
    }


#if UNITY_EDITOR
    [CustomEditor(typeof(RobotAngleRecoder))]
    public class RobotAngleRecoderEditor : Editor
    {
        RobotAngleRecoder component;

        bool isEditorPlaying = false;
        RobotAngleCtrModel ctrMode = RobotAngleCtrModel.None;

        private void OnEnable()
        {
            component = target as RobotAngleRecoder;
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            DrawInspectorCtr();
        }

        private void DrawInspectorCtr()
        {
            isEditorPlaying = EditorApplication.isPlaying;
            if (isEditorPlaying)
            {
                if (GUILayout.Button("获取轴数据"))
                {
                    component.currentPos.angles = component.OptimizedCCDIK.GetJointAngles();
                    component.currentPos.localEulerAngle = component.OptimizedCCDIK.GetJointEulerAngles();
                }
                if (GUILayout.Button("轴数据添加到AnglePos"))
                {
                    if (string.IsNullOrEmpty(component.currentPos.description))
                    {
                        Debug.LogError("description 不能为null!");
                    }
                    else
                    {
                        component.anglePos.Add(component.currentPos);
                        component.currentPos = new RobotAngleRecoder.RobotAngleInfo();
                    }
                }

                ctrMode = (RobotAngleCtrModel)EditorGUILayout.EnumPopup("控制模式", ctrMode);
                if (component.CtrModel != ctrMode)
                {
                    component.CtrModel = ctrMode;
                }
                switch (ctrMode)
                {
                    case RobotAngleCtrModel.None:
                        break;
                    case RobotAngleCtrModel.RealTime:
                        EditorGUILayout.HelpBox("拖动Target控制机器人", MessageType.Info);
                        break;
                    case RobotAngleCtrModel.Click:
                        if (GUILayout.Button("姿态移动到Target"))
                        {
                            component.OptimizedCCDIK.MoveTo(component.Target.transform.position, component.Target.transform.rotation);
                        }
                        GUILayout.BeginHorizontal();
                        if (GUILayout.Button("上一个AnglePos"))
                        {
                            component.anglePosIndex--;
                            component.anglePosIndex = Mathf.Clamp(component.anglePosIndex, 0, component.anglePos.Count - 1);
                            component.SetAngle(component.anglePosIndex, () =>
                            {
                                component.ResetRobotTarget();
                            });
                        }
                        if (GUILayout.Button("下一个AnglePos"))
                        {
                            component.anglePosIndex++;
                            component.anglePosIndex = Mathf.Clamp(component.anglePosIndex, 0, component.anglePos.Count - 1);
                            component.SetAngle(component.anglePosIndex, () =>
                            {
                                component.ResetRobotTarget();
                            });
                        }
                        GUILayout.EndHorizontal();
                        if (GUILayout.Button("执行全部"))
                        {

                        }
                        break;
                }
            }
            else
            {
                EditorGUILayout.HelpBox("编辑器运行开始编辑数据", MessageType.Info);
            }
        }
    }
#endif
}
