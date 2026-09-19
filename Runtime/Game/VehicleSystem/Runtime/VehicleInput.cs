using UnityEngine;

/// <summary>
/// 车辆输入：统一读取键盘输入，供 VehicleController 和 CameraModeManager 使用。
/// 挂载在车辆根物体（或其他任意物体）上即可。
/// </summary>
public class VehicleInput : MonoBehaviour
{
    /// <summary>油门：W / 上箭头 = 1，松开 = 0</summary>
    public float Throttle { get; private set; }

    /// <summary>倒车：S / 下箭头 = 1，松开 = 0</summary>
    public float Reverse { get; private set; }

    /// <summary>转向：A / D 或左右箭头，-1（左）~ 1（右）</summary>
    public float Steering { get; private set; }

    /// <summary>手刹：空格键按住</summary>
    public bool Handbrake { get; private set; }

    /// <summary>循环切换视角：C 键按下当帧为 true</summary>
    public bool CycleCamera { get; private set; }

    /// <summary>直达视角：1/2/3 键按下当帧返回 0/1/2，否则返回 -1</summary>
    public int DirectCamera { get; private set; }

    void Update()
    {
        float vertical = Input.GetAxisRaw("Vertical");     // W/S 或 上/下箭头
        float horizontal = Input.GetAxisRaw("Horizontal"); // A/D 或 左/右箭头

        Throttle = Mathf.Clamp01(vertical);  // 只取前进部分
        Reverse = Mathf.Clamp01(-vertical);  // 只取后退部分
        Steering = horizontal;
        Handbrake = Input.GetKey(KeyCode.Space);

        CycleCamera = Input.GetKeyDown(KeyCode.C);

        DirectCamera = -1;
        if (Input.GetKeyDown(KeyCode.Alpha1)) DirectCamera = 0;
        else if (Input.GetKeyDown(KeyCode.Alpha2)) DirectCamera = 1;
        else if (Input.GetKeyDown(KeyCode.Alpha3)) DirectCamera = 2;
    }
}
