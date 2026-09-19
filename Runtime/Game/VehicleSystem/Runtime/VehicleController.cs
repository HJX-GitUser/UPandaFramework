using UnityEngine;

/// <summary>
/// 车辆控制器：基于 Rigidbody + WheelCollider 的简易汽车物理。
/// 挂载在车体根物体（带 Rigidbody）上；WheelCollider 作为其子物体。
/// 前轮负责转向，后轮负责驱动与手刹，实现前进/后退/漂移。
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class VehicleController : MonoBehaviour
{
    [Header("输入")]
    [SerializeField] private VehicleInput input;   // 输入组件（留空自动查找）

    [Header("车轮")]
    [SerializeField] private WheelCollider[] frontWheels;   // 前轮（转向 + 刹车）
    [SerializeField] private WheelCollider[] rearWheels;    // 后轮（驱动 + 刹车 + 手刹）
    [SerializeField] private Transform[] frontWheelMeshes;  // 前轮视觉模型（同步旋转）
    [SerializeField] private Transform[] rearWheelMeshes;   // 后轮视觉模型（同步旋转）

    [Header("动力")]
    [SerializeField] private float maxMotorTorque = 1500f;  // 最大驱动扭矩（后轮）
    [SerializeField] private float maxBrakeTorque = 3000f;  // 最大刹车扭矩（四轮）
    [SerializeField] private float handbrakeTorque = 6000f; // 手刹扭矩（只作用后轮，用于漂移）
    [SerializeField] private float maxSteerAngle = 30f;     // 最大转向角（度）
    [SerializeField] private float topSpeed = 40f;          // 前进最高速度（米/秒）
    [SerializeField] private float reverseTopSpeed = 12f;   // 倒车最高速度（米/秒）

    [Header("稳定")]
    [SerializeField] private Vector3 centerOfMassOffset = new Vector3(0f, -0.6f, 0f); // 重心偏移（压低重心更稳）

    private Rigidbody rb;
    private float currentSteerAngle;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.centerOfMass = centerOfMassOffset;

        // 自动查找输入组件
        if (input == null) input = GetComponent<VehicleInput>();
        if (input == null) input = GetComponentInChildren<VehicleInput>();
    }

    void FixedUpdate()
    {
        if (input == null) return;

        // 沿车辆前进方向的速度（正=前进，负=后退）
        float forwardSpeed = Vector3.Dot(rb.velocity, transform.forward);

        // 1. 转向
        currentSteerAngle = maxSteerAngle * input.Steering;
        for (int i = 0; i < frontWheels.Length; i++)
            if (frontWheels[i] != null) frontWheels[i].steerAngle = currentSteerAngle;

        // 2. 驱动 / 刹车 / 倒车
        float motor = 0f;
        float brake = 0f;

        if (input.Throttle > 0.01f)
        {
            // 前进：倒车中按 W 先刹停，否则加速
            if (forwardSpeed < -0.5f) brake = maxBrakeTorque;
            else if (forwardSpeed < topSpeed) motor = maxMotorTorque * input.Throttle;
        }
        else if (input.Reverse > 0.01f)
        {
            // 后退：前进中按 S 先刹停，否则倒车
            if (forwardSpeed > 0.5f) brake = maxBrakeTorque;
            else if (forwardSpeed > -reverseTopSpeed) motor = -maxMotorTorque * input.Reverse;
        }

        // 3. 手刹：只锁死后轮，后轮失去抓地产生漂移
        float handbrake = input.Handbrake ? handbrakeTorque : 0f;

        for (int i = 0; i < rearWheels.Length; i++)
        {
            if (rearWheels[i] == null) continue;
            rearWheels[i].motorTorque = motor;
            rearWheels[i].brakeTorque = brake + handbrake;
        }
        for (int i = 0; i < frontWheels.Length; i++)
        {
            if (frontWheels[i] == null) continue;
            frontWheels[i].brakeTorque = brake;
        }
    }

    void Update()
    {
        // 视觉车轮同步：让轮子模型跟随 WheelCollider 的真实位姿转动
        SyncWheels(frontWheels, frontWheelMeshes);
        SyncWheels(rearWheels, rearWheelMeshes);
    }

    /// <summary>把 WheelCollider 的位姿同步到对应的视觉模型</summary>
    private void SyncWheels(WheelCollider[] colliders, Transform[] meshes)
    {
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] == null || i >= meshes.Length || meshes[i] == null) continue;

            Vector3 pos;
            Quaternion rot;
            colliders[i].GetWorldPose(out pos, out rot);
            meshes[i].position = pos;
            meshes[i].rotation = rot;
        }
    }
}
