using System;
using System.Collections;
using UnityEngine;

namespace UPandaGF.RunTime.RobotCCDIK
{
    /// <summary>
    /// CCD 逆运动学控制器（串联机械臂，每个关节只绕单一局部轴旋转）
    /// 能力：位置 + 姿态双目标求解、关节限位、平滑插值、姿态录制/回放支持
    /// 约定：所有关节角都是"相对复位姿态（Awake 时记录的姿态）、绕 rotationAxis 的增量角"
    /// 依赖：纯 Unity API，插值由协程自包含实现，不依赖任何第三方补间插件
    /// 说明：实现原理与使用方式见同目录 README.md
    /// </summary>
    public class CCDIKController : MonoBehaviour
    {
        #region 嵌套类型

        /// <summary>
        /// 单个关节的配置与运行时状态
        /// </summary>
        [Serializable]
        public class JointInfo
        {
            public Transform joint;                             // 关节Transform
            public Axis rotationAxis;                           // 旋转轴（关节局部坐标系）
            public float minAngle = -180f;                      // 最小角度（相对复位姿态）
            public float maxAngle = 180f;                       // 最大角度（相对复位姿态）
            [HideInInspector] public float currentAngle = 0f;    // 当前角度（相对复位姿态的增量角）
        }

        /// <summary>
        /// 关节的旋转轴（关节局部坐标系下的单一轴）
        /// </summary>
        public enum Axis { X, Y, Z }

        #endregion

        #region 配置字段

        [Header("机器人关节配置")]
        public JointInfo[] joints = new JointInfo[6]; // 关节链：索引0为基座，末尾最接近末端

        [Header("末端执行器")]
        public Transform endEffector;

        [Header("IK参数")]
        public int maxIterations = 30;                 // 最大迭代次数
        public float positionTolerance = 0.001f;       // 位置容差
        public float rotationTolerance = 0.1f;         // 姿态容差（度）
        public float stepFactor = 0.5f;                // 步长因子（0~1）：实际单次旋转上限 = maxStepAngle * stepFactor
        public bool useAdaptiveStep = true;            // 自适应步长：误差大时步长大，误差小时步长小
        public float maxStepAngle = 5f;                // 单次旋转角度上限的基数（度）
        public int orientationJointCount = 3;          // 参与姿态修正的末端关节数量，0 表示只解位置

        [Header("调试")]
        public bool drawDebugLines = true;
        public Color debugColor = Color.green;

        #endregion

        #region 内部状态

        // 旋转轴向量映射，索引与 Axis 枚举一致
        private static readonly Vector3[] axisVectors = { Vector3.right, Vector3.up, Vector3.forward };

        private Vector3[] jointPositions;      // 关节世界坐标缓存（一次求解内复用，减少Transform访问）
        private Pose[] jointResetPose;         // 各关节的复位姿态（角度零位）
        private Coroutine angleLerpRoutine;    // 正在运行的角度插值协程

        #endregion

        #region 生命周期

        private void Awake()
        {
            if (joints == null) joints = new JointInfo[0];

            // JointInfo 是引用类型，Inspector 新增的元素可能为 null
            for (int i = 0; i < joints.Length; i++)
            {
                if (joints[i] == null)
                {
                    Debug.LogError($"关节 {i} 的配置项未实例化！");
                    joints[i] = new JointInfo();
                }
                if (joints[i].joint == null)
                {
                    Debug.LogError($"关节 {i} 的 joint 未赋值！");
                }
            }
            if (endEffector == null)
            {
                Debug.LogError("末端执行器未赋值！");
            }

            // 分配缓存并记录复位姿态（复位姿态即角度零位）
            EnsureCacheSize();
        }

        private void Start()
        {
            // 避免场景中序列化残留的 currentAngle 生效
            SyncCurrentAnglesFromPose();
            RefreshJointPositions();
        }

        private void OnDestroy()
        {
            // 组件销毁时停掉插值协程，避免回调打到已销毁的对象上
            StopAngleLerp();
        }

        private void Update()
        {
            if (drawDebugLines)
            {
                DrawDebugLines();
            }
        }

        #endregion

        #region 外部接口

        /// <summary>
        /// 核心IK求解：迭代逼近目标位置与姿态
        /// </summary>
        /// <param name="targetPosition">目标位置（世界坐标）</param>
        /// <param name="targetRotation">目标姿态（世界旋转；orientationJointCount 为0时忽略）</param>
        /// <returns>是否在容差内收敛；迭代耗尽返回 false，此时关节停在最后一次迭代结果</returns>
        public bool SolveIK(Vector3 targetPosition, Quaternion targetRotation)
        {
            if (joints == null || joints.Length == 0 || endEffector == null) return false;

            EnsureCacheSize();

            // 刷新缓存，并把 currentAngle 与真实姿态对齐（关节可能被外部改动过）
            RefreshJointPositions();
            SyncCurrentAnglesFromPose();

            int orientationCount = Mathf.Clamp(orientationJointCount, 0, joints.Length);

            for (int iter = 0; iter < maxIterations; iter++)
            {
                // 位置IK：从末端关节往基座逐关节修正
                for (int i = joints.Length - 1; i >= 0; i--)
                {
                    UpdateJointForPosition(i, targetPosition);
                }

                // 姿态IK：只修正末端 orientationCount 个关节
                for (int i = joints.Length - 1; i >= joints.Length - orientationCount; i--)
                {
                    UpdateJointForRotation(i, targetRotation);
                }

                // 两阶段都改完再统一判定误差，避免用"修正前"的误差误判收敛
                bool positionReached = Vector3.Distance(endEffector.position, targetPosition) < positionTolerance;
                bool rotationReached = orientationCount <= 0
                    || Quaternion.Angle(endEffector.rotation, targetRotation) < rotationTolerance;

                if (positionReached && rotationReached)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 平滑移动：在 duration 秒内让末端沿直线/球面插值逼近目标，并逐帧求解IK
        /// </summary>
        public void MoveTo(Vector3 targetPosition, Quaternion targetRotation, float duration = 1f)
        {
            StartCoroutine(MoveToCoroutine(targetPosition, targetRotation, duration));
        }

        private IEnumerator MoveToCoroutine(Vector3 targetPosition, Quaternion targetRotation, float duration)
        {
            // duration<=0 时直接求解，避免除零
            if (duration <= 0f)
            {
                SolveIK(targetPosition, targetRotation);
                yield break;
            }

            Vector3 startPos = endEffector.position;
            Quaternion startRot = endEffector.rotation;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;

                Vector3 currentTarget = Vector3.Lerp(startPos, targetPosition, t);
                Quaternion currentRot = Quaternion.Slerp(startRot, targetRotation, t);

                SolveIK(currentTarget, currentRot);

                yield return null;
            }

            // 最终精确求解
            SolveIK(targetPosition, targetRotation);
        }

        /// <summary>
        /// 直接设置关节角度（立即生效，会打断正在进行的插值）
        /// </summary>
        /// <param name="angles">长度必须与 joints 一致，单位：度（相对复位姿态的增量角）</param>
        public void SetJointAngles(float[] angles)
        {
            if (joints == null || angles == null || angles.Length != joints.Length)
            {
                Debug.LogError($"角度数组长度不匹配: 需要{(joints == null ? 0 : joints.Length)}, 得到{(angles == null ? 0 : angles.Length)}");
                return;
            }

            StopAngleLerp();
            EnsureCacheSize();

            for (int i = 0; i < joints.Length; i++)
            {
                if (!HasJoint(i)) continue;

                float clamped = Mathf.Clamp(angles[i], joints[i].minAngle, joints[i].maxAngle);
                joints[i].currentAngle = clamped;
                ApplyJointAngle(i, clamped);
            }
            RefreshJointPositions();
        }

        /// <summary>
        /// 平滑插值关节角度（增量角语义，与 currentAngle / GetJointAngles 一致）
        /// 所有关节同时开始、同时结束，整体耗时 time 秒，结束后触发 callback
        /// </summary>
        /// <param name="targetAngles">长度必须与 joints 一致，单位：度；会被钳位到各关节限位</param>
        /// <param name="time">插值时长（秒），&lt;=0 时下一帧到位</param>
        /// <param name="callback">插值结束回调（被新的插值/SetJointAngles/ResetAngle 打断时不触发）</param>
        public void AngleLerp(float[] targetAngles, float time, Action callback = null)
        {
            if (joints == null || targetAngles == null || targetAngles.Length != joints.Length)
            {
                Debug.LogError($"角度数组长度不匹配: 需要{(joints == null ? 0 : joints.Length)}, 得到{(targetAngles == null ? 0 : targetAngles.Length)}");
                return;
            }

            EnsureCacheSize();
            SyncCurrentAnglesFromPose();
            StopAngleLerp();

            // 起止值先算好，避免插值过程中被外部改动影响
            int count = joints.Length;
            float[] fromAngles = new float[count];
            float[] toAngles = new float[count];
            for (int i = 0; i < count; i++)
            {
                if (joints[i] == null) continue;

                fromAngles[i] = joints[i].currentAngle;
                toAngles[i] = Mathf.Clamp(targetAngles[i], joints[i].minAngle, joints[i].maxAngle);
            }

            if (gameObject.activeInHierarchy)
            {
                angleLerpRoutine = StartCoroutine(AngleLerpRoutine(fromAngles, toAngles, time, callback));
            }
            else
            {
                // 对象未激活时协程无法运行，改为瞬时到位
                ApplyJointAngles(fromAngles, toAngles, 1f);
                RefreshJointPositions();
                callback?.Invoke();
            }
        }

        /// <summary>
        /// 平滑插值到绝对局部欧拉角（用于回放录制好的姿态）
        /// 播放结束后会自动同步 currentAngle
        /// </summary>
        /// <param name="targetAngles">长度必须与 joints 一致，各关节的局部欧拉角（绝对姿态）</param>
        /// <param name="time">插值时长（秒），&lt;=0 时下一帧到位</param>
        /// <param name="callback">插值结束回调（被打断时不触发）</param>
        public void AngleLerp(Vector3[] targetAngles, float time, Action callback = null)
        {
            if (joints == null || targetAngles == null || targetAngles.Length != joints.Length)
            {
                Debug.LogError($"欧拉角数组长度不匹配: 需要{(joints == null ? 0 : joints.Length)}, 得到{(targetAngles == null ? 0 : targetAngles.Length)}");
                return;
            }

            StopAngleLerp();

            // 起止值先算好，避免插值过程中被外部改动影响
            int count = joints.Length;
            Quaternion[] fromRots = new Quaternion[count];
            Quaternion[] toRots = new Quaternion[count];
            for (int i = 0; i < count; i++)
            {
                if (!HasJoint(i))
                {
                    fromRots[i] = Quaternion.identity;
                    toRots[i] = Quaternion.identity;
                    continue;
                }

                fromRots[i] = joints[i].joint.localRotation;
                toRots[i] = Quaternion.Euler(targetAngles[i]);
            }

            if (gameObject.activeInHierarchy)
            {
                angleLerpRoutine = StartCoroutine(AngleLerpEulerRoutine(fromRots, toRots, time, callback));
            }
            else
            {
                // 对象未激活时协程无法运行，改为瞬时到位
                ApplyJointRotations(fromRots, toRots, 1f);
                SyncCurrentAnglesFromPose();
                RefreshJointPositions();
                callback?.Invoke();
            }
        }

        /// <summary>
        /// 获取当前关节角度（相对复位姿态的增量角，度）
        /// </summary>
        public float[] GetJointAngles()
        {
            int count = (joints != null) ? joints.Length : 0;
            float[] angles = new float[count];
            for (int i = 0; i < count; i++)
            {
                angles[i] = (joints[i] != null) ? joints[i].currentAngle : 0f;
            }
            return angles;
        }

        /// <summary>
        /// 获取当前各关节的局部欧拉角（绝对姿态，用于录制/回放）
        /// </summary>
        public Vector3[] GetJointEulerAngles()
        {
            int count = (joints != null) ? joints.Length : 0;
            Vector3[] angles = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                angles[i] = (HasJoint(i)) ? joints[i].joint.localEulerAngles : Vector3.zero;
            }
            return angles;
        }

        /// <summary>
        /// 依据"复位姿态"与关节当前局部旋转，重新计算各关节的 currentAngle
        /// 关节旋转被外部（动画 / Inspector / 逐帧写入）改动过之后调用它即可恢复一致性
        /// </summary>
        public void SyncCurrentAnglesFromPose()
        {
            if (joints == null || jointResetPose == null) return;

            for (int i = 0; i < joints.Length; i++)
            {
                if (!HasJoint(i)) continue;

                Vector3 axis = GetAxisVector(joints[i].rotationAxis);
                Quaternion baseRot = (i < jointResetPose.Length) ? jointResetPose[i].rotation : Quaternion.identity;

                // 取旋转在关节轴上的有符号投影，得到 [-180,180] 的增量角
                Quaternion delta = Quaternion.Inverse(baseRot) * joints[i].joint.localRotation;
                delta.ToAngleAxis(out float angle, out Vector3 axisLocal);
                float sign = Vector3.Dot(axisLocal, axis) < 0f ? -1f : 1f;

                joints[i].currentAngle = NormalizeAngle(angle) * sign;
            }
        }

        /// <summary>
        /// 复位：所有关节回到复位姿态，角度归一零（会打断正在进行的插值）
        /// </summary>
        public void ResetAngle()
        {
            StopAngleLerp();
            EnsureCacheSize();

            for (int i = 0; i < joints.Length; i++)
            {
                if (!HasJoint(i)) continue;

                joints[i].joint.localPosition = jointResetPose[i].position;
                joints[i].joint.localRotation = jointResetPose[i].rotation;
                joints[i].currentAngle = 0f; // 复位后角度归零，保持 currentAngle 与真实姿态一致
            }
            RefreshJointPositions();
        }

        #endregion

        #region 内部实现

        /// <summary>
        /// 判断指定索引的关节是否可安全访问（配置项与 Transform 都已赋值）
        /// </summary>
        private bool HasJoint(int index)
        {
            return joints != null
                && index >= 0
                && index < joints.Length
                && joints[index] != null
                && joints[index].joint != null;
        }

        /// <summary>
        /// 校验缓存数组长度与关节数量是否一致（支持运行时调整 joints 长度）
        /// </summary>
        private void EnsureCacheSize()
        {
            int count = (joints != null) ? joints.Length : 0;

            if (jointPositions == null || jointPositions.Length != count)
            {
                jointPositions = new Vector3[count];
            }
            if (jointResetPose == null || jointResetPose.Length != count)
            {
                jointResetPose = new Pose[count];
                CaptureResetPose();
            }
        }

        /// <summary>
        /// 记录各关节的复位姿态（作为角度零位）
        /// </summary>
        private void CaptureResetPose()
        {
            for (int i = 0; i < joints.Length; i++)
            {
                if (!HasJoint(i)) continue;

                jointResetPose[i] = new Pose(joints[i].joint.localPosition, joints[i].joint.localRotation);
            }
        }

        /// <summary>
        /// 刷新关节世界坐标缓存
        /// 关节旋转会带动其后所有关节移动，因此旋转索引 i 的关节后只需从 i 开始刷新
        /// </summary>
        /// <param name="startIndex">起始索引，默认刷新全部</param>
        private void RefreshJointPositions(int startIndex = 0)
        {
            for (int i = startIndex; i < joints.Length; i++)
            {
                if (!HasJoint(i)) continue;

                jointPositions[i] = joints[i].joint.position;
            }
        }

        /// <summary>
        /// 位置修正：让末端在"垂直于关节轴"的平面上更靠近目标
        /// 即求把 toEnd 转到 toTarget 所需绕关节世界轴的有符号角度，再受限位/步长约束施加
        /// </summary>
        private void UpdateJointForPosition(int jointIndex, Vector3 targetPosition)
        {
            if (!HasJoint(jointIndex)) return;

            JointInfo info = joints[jointIndex];
            Vector3 jointPos = jointPositions[jointIndex];

            Vector3 toEnd = endEffector.position - jointPos;
            Vector3 toTarget = targetPosition - jointPos;
            // 末端或目标与关节重合时无法确定旋转平面
            if (toEnd.sqrMagnitude < 1e-6f || toTarget.sqrMagnitude < 1e-6f) return;

            // 关节轴在世界空间的方向
            Vector3 axisWorld = info.joint.rotation * GetAxisVector(info.rotationAxis);

            // 投影到垂直于关节轴的平面
            Vector3 toEndProj = Vector3.ProjectOnPlane(toEnd, axisWorld);
            Vector3 toTargetProj = Vector3.ProjectOnPlane(toTarget, axisWorld);

            // 投影长度过小（末端或目标几乎落在关节轴上）说明该关节无法贡献位置调整
            if (toEndProj.sqrMagnitude < 1e-6f || toTargetProj.sqrMagnitude < 1e-6f) return;

            float angle = ApplyStepLimit(Vector3.SignedAngle(toEndProj, toTargetProj, axisWorld));
            if (Mathf.Abs(angle) < 0.01f) return;

            // 累加并限位，取实际可旋转量
            float newAngle = Mathf.Clamp(info.currentAngle + angle, info.minAngle, info.maxAngle);
            float actualRotation = newAngle - info.currentAngle;
            if (Mathf.Abs(actualRotation) < 0.01f) return;

            // 右乘局部轴旋转 = 绕关节自身世界轴旋转
            info.joint.localRotation *= Quaternion.AngleAxis(actualRotation, GetAxisVector(info.rotationAxis));
            info.currentAngle = newAngle;

            RefreshJointPositions(jointIndex);
        }

        /// <summary>
        /// 姿态修正：把末端当前姿态到目标姿态的增量，按该关节轴上的有效分量施加
        /// 关节轴与所需旋转轴越接近垂直，贡献越小（投影小于0.1视为无法贡献）
        /// </summary>
        private void UpdateJointForRotation(int jointIndex, Quaternion targetRotation)
        {
            if (!HasJoint(jointIndex)) return;

            JointInfo info = joints[jointIndex];

            // 末端姿态误差：target * inverse(current)
            Quaternion delta = targetRotation * Quaternion.Inverse(endEffector.rotation);
            delta.ToAngleAxis(out float angle, out Vector3 axisWorld);
            if (angle > 180f) angle -= 360f; // 归一化到[-180,180]
            if (Mathf.Abs(angle) < 0.01f) return;

            Vector3 jointAxisWorld = info.joint.rotation * GetAxisVector(info.rotationAxis);

            // 所需旋转轴在关节轴上的投影（带符号）
            float dot = Vector3.Dot(axisWorld, jointAxisWorld);
            if (Mathf.Abs(dot) < 0.1f) return;

            float rotationAmount = ApplyStepLimit(angle * Mathf.Sign(dot));
            if (Mathf.Abs(rotationAmount) < 0.01f) return;

            float newAngle = Mathf.Clamp(info.currentAngle + rotationAmount, info.minAngle, info.maxAngle);
            float actualRotation = newAngle - info.currentAngle;
            if (Mathf.Abs(actualRotation) < 0.01f) return;

            info.joint.localRotation *= Quaternion.AngleAxis(actualRotation, GetAxisVector(info.rotationAxis));
            info.currentAngle = newAngle;

            RefreshJointPositions(jointIndex);
        }

        /// <summary>
        /// 步长限制：单次旋转不超过 maxStepAngle，再乘 stepFactor
        /// </summary>
        private float ApplyStepLimit(float angle)
        {
            float cap = useAdaptiveStep ? Mathf.Min(Mathf.Abs(angle), maxStepAngle) : maxStepAngle;
            return Mathf.Clamp(angle, -cap, cap) * stepFactor;
        }

        /// <summary>
        /// 获取旋转轴向量（关节局部坐标系）
        /// </summary>
        private Vector3 GetAxisVector(Axis axis)
        {
            return axisVectors[(int)axis];
        }

        /// <summary>
        /// 把"相对复位姿态的增量角"应用到关节
        /// 局部旋转 = 复位旋转 * 绕轴旋转，保证关节基准姿态不被丢弃，且与CCD使用的局部轴一致
        /// </summary>
        private void ApplyJointAngle(int index, float angle)
        {
            if (!HasJoint(index)) return;

            Quaternion baseRot = (jointResetPose != null && index < jointResetPose.Length)
                ? jointResetPose[index].rotation
                : Quaternion.identity;

            joints[index].joint.localRotation = baseRot * Quaternion.AngleAxis(angle, GetAxisVector(joints[index].rotationAxis));
        }

        /// <summary>
        /// 按插值系数 t 批量应用关节增量角（t>=1 时精确等于目标值）
        /// </summary>
        private void ApplyJointAngles(float[] fromAngles, float[] toAngles, float t)
        {
            for (int i = 0; i < joints.Length; i++)
            {
                if (!HasJoint(i)) continue;

                float angle = (t >= 1f) ? toAngles[i] : Mathf.Lerp(fromAngles[i], toAngles[i], t);
                joints[i].currentAngle = angle;
                ApplyJointAngle(i, angle);
            }
        }

        /// <summary>
        /// 按插值系数 t 批量应用关节局部旋转（t>=1 时精确等于目标值）
        /// </summary>
        private void ApplyJointRotations(Quaternion[] fromRots, Quaternion[] toRots, float t)
        {
            for (int i = 0; i < joints.Length; i++)
            {
                if (!HasJoint(i)) continue;

                joints[i].joint.localRotation = (t >= 1f) ? toRots[i] : Quaternion.Slerp(fromRots[i], toRots[i], t);
            }
        }

        /// <summary>
        /// 角度规范化到[-180,180]
        /// </summary>
        private float NormalizeAngle(float angle)
        {
            angle = angle % 360f;
            if (angle > 180f) angle -= 360f;
            if (angle < -180f) angle += 360f;
            return angle;
        }

        /// <summary>
        /// 停止当前角度插值（不触发其回调）
        /// </summary>
        private void StopAngleLerp()
        {
            if (angleLerpRoutine != null)
            {
                StopCoroutine(angleLerpRoutine);
                angleLerpRoutine = null;
            }
        }

        /// <summary>
        /// 增量角插值协程：线性插值，所有关节同时起止
        /// </summary>
        private IEnumerator AngleLerpRoutine(float[] fromAngles, float[] toAngles, float time, Action callback)
        {
            if (time > 0f)
            {
                float elapsed = 0f;
                while (elapsed < time)
                {
                    elapsed += Time.deltaTime;
                    ApplyJointAngles(fromAngles, toAngles, Mathf.Clamp01(elapsed / time));
                    yield return null;
                }
            }
            else
            {
                // time<=0 时下一帧到位，避免回调在调用栈内同步触发
                yield return null;
            }

            ApplyJointAngles(fromAngles, toAngles, 1f); // 收尾精确落位，消除累计误差
            RefreshJointPositions();

            angleLerpRoutine = null;
            callback?.Invoke();
        }

        /// <summary>
        /// 绝对局部旋转插值协程：四元数球面插值（最短路径）
        /// </summary>
        private IEnumerator AngleLerpEulerRoutine(Quaternion[] fromRots, Quaternion[] toRots, float time, Action callback)
        {
            if (time > 0f)
            {
                float elapsed = 0f;
                while (elapsed < time)
                {
                    elapsed += Time.deltaTime;
                    ApplyJointRotations(fromRots, toRots, Mathf.Clamp01(elapsed / time));
                    yield return null;
                }
            }
            else
            {
                // time<=0 时下一帧到位，避免回调在调用栈内同步触发
                yield return null;
            }

            ApplyJointRotations(fromRots, toRots, 1f); // 收尾精确落位，消除累计误差

            // 绝对旋转绕过了 currentAngle，结束后重新对齐
            SyncCurrentAnglesFromPose();
            RefreshJointPositions();

            angleLerpRoutine = null;
            callback?.Invoke();
        }

        #endregion

        #region 调试

        /// <summary>
        /// 在 Scene 视图画出关节链与末端连线
        /// </summary>
        private void DrawDebugLines()
        {
            if (joints == null || joints.Length < 2) return;

            for (int i = 0; i < joints.Length - 1; i++)
            {
                if (!HasJoint(i) || !HasJoint(i + 1)) continue;

                Debug.DrawLine(joints[i].joint.position, joints[i + 1].joint.position, debugColor);
            }

            int lastIndex = joints.Length - 1;
            if (HasJoint(lastIndex) && endEffector != null)
            {
                Debug.DrawLine(joints[lastIndex].joint.position, endEffector.position, Color.yellow);
            }
        }

        #endregion
    }
}
