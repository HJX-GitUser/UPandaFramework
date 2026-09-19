using UnityEngine;

/// <summary>
/// 视角管理器：支持第三人称、第一人称、环绕三种视角。
/// C 键循环切换，1/2/3 数字键直达；切换时有 0.5 秒平滑过渡。
/// 挂载在场景任意独立物体上，引用目标车辆与相机。
/// </summary>
public class CameraModeManager : MonoBehaviour
{
    /// <summary>视角类型（枚举值即 1/2/3 数字键对应的序号）</summary>
    public enum CameraMode
    {
        ThirdPerson = 0,  // 第三人称
        FirstPerson = 1,  // 第一人称
        Orbit = 2         // 环绕
    }

    [Header("目标")]
    [SerializeField] private Transform target;           // 被跟随的车辆
    [SerializeField] private Transform cameraTransform;  // 相机（留空自动使用主相机）

    [Header("输入")]
    [SerializeField] private VehicleInput input;         // 输入组件（留空自动查找）

    [Header("第三人称")]
    [SerializeField] private Vector3 thirdPersonOffset = new Vector3(0f, 3.5f, -7f); // 相对车辆的偏移
    [SerializeField] private float thirdPersonLookHeight = 1.2f;                      // 相机看向车辆的高度点

    [Header("第一人称")]
    [SerializeField] private Vector3 firstPersonOffset = new Vector3(0f, 1.2f, 0.4f); // 车内视角偏移

    [Header("环绕")]
    [SerializeField] private float orbitDistance = 8f;          // 环绕半径
    [SerializeField] private float orbitPitch = 25f;            // 初始俯仰角
    [SerializeField] private float orbitMouseSensitivity = 3f;  // 鼠标灵敏度
    [SerializeField] private float orbitMinPitch = 5f;          // 最小俯仰角
    [SerializeField] private float orbitMaxPitch = 80f;         // 最大俯仰角

    [Header("切换")]
    [SerializeField] private float transitionTime = 0.5f;       // 视角切换过渡时长（秒）

    private CameraMode mode = CameraMode.ThirdPerson;
    private float orbitYaw;
    private float orbitPitchCurrent;

    // 平滑过渡状态
    private bool transitioning;
    private float transitionTimer;
    private Vector3 transitionStartPos;
    private Quaternion transitionStartRot;

    void Awake()
    {
        // 自动查找相机与输入
        if (cameraTransform == null)
        {
            Camera cam = Camera.main;
            if (cam != null) cameraTransform = cam.transform;
        }
        if (input == null) input = GetComponent<VehicleInput>();
        if (input == null) input = GetComponentInChildren<VehicleInput>();
        if (input == null) input = FindObjectOfType<VehicleInput>();

        // 环绕初始角度：从车辆后方开始
        orbitYaw = target != null ? target.eulerAngles.y : 0f;
        orbitPitchCurrent = orbitPitch;
    }

    void LateUpdate()
    {
        if (target == null || cameraTransform == null) return;

        HandleCameraSwitch();

        // 计算当前视角的目标位姿
        Vector3 targetPos;
        Quaternion targetRot;
        ComputeDesiredTransform(out targetPos, out targetRot);

        if (transitioning)
        {
            // 平滑过渡：用 smoothstep 让起止更柔和
            transitionTimer += Time.deltaTime;
            float t = Mathf.Clamp01(transitionTimer / Mathf.Max(transitionTime, 0.0001f));
            t = t * t * (3f - 2f * t);
            cameraTransform.position = Vector3.Lerp(transitionStartPos, targetPos, t);
            cameraTransform.rotation = Quaternion.Slerp(transitionStartRot, targetRot, t);
            if (t >= 1f) transitioning = false;
        }
        else
        {
            // 非过渡期间持续跟随
            cameraTransform.position = targetPos;
            cameraTransform.rotation = targetRot;
        }
    }

    /// <summary>读取输入并切换视角</summary>
    private void HandleCameraSwitch()
    {
        if (input == null) return;

        int direct = input.DirectCamera;
        if (direct >= 0 && direct <= 2)
        {
            // 数字键直达
            SwitchMode((CameraMode)direct);
        }
        else if (input.CycleCamera)
        {
            // C 键循环
            SwitchMode((CameraMode)(((int)mode + 1) % 3));
        }
    }

    /// <summary>切换到指定视角，并开始平滑过渡</summary>
    private void SwitchMode(CameraMode newMode)
    {
        if (newMode == mode && !transitioning) return;

        transitionStartPos = cameraTransform.position;
        transitionStartRot = cameraTransform.rotation;
        transitionTimer = 0f;
        transitioning = true;
        mode = newMode;
    }

    /// <summary>计算当前视角下相机的目标位置与朝向</summary>
    private void ComputeDesiredTransform(out Vector3 pos, out Quaternion rot)
    {
        switch (mode)
        {
            case CameraMode.FirstPerson:
                // 第一人称：位于车辆前方稍高处，朝向车辆前进方向
                pos = target.position + target.TransformDirection(firstPersonOffset);
                rot = Quaternion.LookRotation(target.forward, Vector3.up);
                break;

            case CameraMode.Orbit:
                // 环绕：鼠标控制水平/垂直环绕，距离固定，始终看向车辆
                UpdateOrbitInput();
                Vector3 orbitDir = Quaternion.Euler(orbitPitchCurrent, orbitYaw, 0f) * Vector3.back;
                pos = target.position - orbitDir * orbitDistance;
                rot = Quaternion.LookRotation(target.position - pos, Vector3.up);
                break;

            default: // ThirdPerson
                // 第三人称：位于车辆后方上方，看向车辆
                pos = target.position + target.TransformDirection(thirdPersonOffset);
                rot = Quaternion.LookRotation((target.position + Vector3.up * thirdPersonLookHeight) - pos, Vector3.up);
                break;
        }
    }

    /// <summary>环绕视角的鼠标输入</summary>
    private void UpdateOrbitInput()
    {
        orbitYaw += Input.GetAxis("Mouse X") * orbitMouseSensitivity;
        orbitPitchCurrent -= Input.GetAxis("Mouse Y") * orbitMouseSensitivity;
        orbitPitchCurrent = Mathf.Clamp(orbitPitchCurrent, orbitMinPitch, orbitMaxPitch);
    }
}
