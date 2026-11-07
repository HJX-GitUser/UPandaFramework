# 串联机械臂 CCD 逆运动学

`CCDIKController` 通过逐关节旋转，让末端执行器逼近给定的世界位置和姿态。`RobotAngleRecoder` 提供目标操作和姿态录制回放。组件属于 `UPandaGF.Extensions`，命名空间为 `UPandaGF.RunTime.RobotCCDIK`。使用 Unity Transform 和协程，无需 DOTween。 [IK 控制器](CCDIKController.cs) [姿态工具](RobotAngleRecoder.cs)

## 绑定模型并求解

1. 将关节按基座到末端放入 `joints`。索引 0 是基座侧，第一个关节的后代包含后续关节及 `endEffector`。
2. 为每个关节指定 `joint`、实际局部旋转轴 `rotationAxis` 以及 `minAngle`、`maxAngle`。限位以 Awake 记录的局部旋转为零位，是绕本轴的**增量角**。
3. 为 `endEffector` 指定实际的工具中心点。姿态约束由最后 `orientationJointCount` 个关节参与，设为 0 只求位置。
4. 需要即时求解时调用 `ik.SolveIK(target.position, target.rotation)`，检查返回值。需要沿插值目标移动时调用 `ik.MoveTo(target.position, target.rotation, duration: 1f)`。

每轮先从末端到基座修正位置，再修正末端姿态，最后同时检查距离与角度。调参时优先核对轴、零位和关节顺序，再调整 `maxIterations`、`positionTolerance`、`rotationTolerance` 和步长参数。收敛失败返回 `false`，关节停在最后一次迭代的姿态。

## 角度与插值

| 入口 | 输入和用途 |
| --- | --- |
| `SetJointAngles(float[] angles)`、`GetJointAngles()` | 写入、读取相对零位的增量角，写入时按限位裁剪 |
| `AngleLerp(float[] targetAngles, float time, Action callback = null)` | 对增量角做插值，目标受限位约束 |
| `AngleLerp(Vector3[] targetAngles, float time, Action callback = null)` | 对绝对局部欧拉角对应的四元数做插值，结束后同步增量角 |
| `GetJointEulerAngles()`、`SyncCurrentAnglesFromPose()`、`ResetAngle()` | 获取绝对局部姿态、将外部改过的姿态同步回增量角、恢复零位 |
| `RobotAngleRecoder.SetAngle(int index, Action callback = null)` | 播放一条已记录的绝对姿态 |

角度数组长度应与关节数一致。姿态录制同时保存相对角和局部欧拉角，回放采用绝对姿态重载，会绕过增量限位。同一个控制器的新 AngleLerp 会打断旧插值，已打断的回调不会再完成。

## 示例与限制

随附六轴场景保留了关节层级，两个 MonoBehaviour 的脚本 GUID 与当前 meta 不一致。用场景做布局参考，重新添加 `CCDIKController` 与 `RobotAngleRecoder` 并按前述步骤绑定。进入 Play 后可用 RobotAngleRecoder 面板切换控制方式，拖动 Target 检查末端位置与方向，再录制一组关节姿态并回放。 [六轴场景](Sample/Sample.unity)

CCD 可能在局部解和奇异位形停住，未提供独立的可达性与碰撞检测。`MoveTo` 每次启动独立协程，同一控制器应避免重叠调用。正时长移动前必须绑定 `endEffector`。现场验收应覆盖关节限位、不可达目标、相反朝向和业务中使用的目标序列。
