using UnityEngine;

namespace UPandaGF.RunTime.TaskSystem
{
    /// <summary>
    /// 极简玩家控制器（WASD + 空格跳），用于演示"跑到某个区域触发到达目标"。
    /// 依赖 CharacterController，不需要 Rigidbody；CharacterController 进入 Trigger 时会正常触发 OnTriggerEnter。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class DemoPlayerController : MonoBehaviour
    {
        public float moveSpeed = 6f;
        public float jumpHeight = 1.5f;
        public float gravity = -20f;
        public float turnSpeed = 720f;

        private CharacterController controller;
        private float verticalSpeed;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
        }

        private void Update()
        {
            float horizontal = Input.GetAxisRaw("Horizontal");
            float vertical = Input.GetAxisRaw("Vertical");

            Vector3 move = new Vector3(horizontal, 0f, vertical);
            if (move.sqrMagnitude > 1f) move.Normalize();

            if (move.sqrMagnitude > 0.0001f)
            {
                // 朝向移动方向平滑转身
                Quaternion target = Quaternion.LookRotation(move);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * Time.deltaTime);
            }

            if (controller.isGrounded && verticalSpeed < 0f) verticalSpeed = -2f;   // 贴地
            if (controller.isGrounded && Input.GetKeyDown(KeyCode.Space))
            {
                verticalSpeed = Mathf.Sqrt(-2f * gravity * jumpHeight);
            }
            verticalSpeed += gravity * Time.deltaTime;

            Vector3 velocity = move * moveSpeed + Vector3.up * verticalSpeed;
            controller.Move(velocity * Time.deltaTime);
        }
    }
}
