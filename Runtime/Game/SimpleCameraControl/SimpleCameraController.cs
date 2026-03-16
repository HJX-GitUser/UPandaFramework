using UnityEngine;

[RequireComponent(typeof(Camera))]
public class SimpleCameraController : MonoBehaviour
{
    [Header("移动设置")]
    [SerializeField] private float moveSpeed = 10f;
    [SerializeField] private float fastMoveMultiplier = 3f;

    [Header("旋转设置")]
    [SerializeField] private float rotationSpeed = 5f;
    [SerializeField] private float smoothTime = 0.1f;

    [Header("缩放设置")]
    [SerializeField] private float zoomSpeed = 10f;
    [SerializeField] private float minZoom = 1f;
    [SerializeField] private float maxZoom = 100f;

    [Header("按键设置")]
    [SerializeField] private KeyCode rotateKey = KeyCode.Mouse1;  // 右键旋转
    [SerializeField] private KeyCode moveKey = KeyCode.Mouse1;  // 右键移动
    [SerializeField] private KeyCode panKey = KeyCode.Mouse2;     // 中键平移

    // 私有变量
    private Vector3 targetPosition;
    private Quaternion targetRotation;
    private Vector3 rotationVelocity = Vector3.zero;
    private Vector3 positionVelocity = Vector3.zero;
    public float currentZoom = 10f;

    private void Start()
    {
        targetPosition = transform.position;
        targetRotation = transform.rotation;
        currentZoom = Vector3.Distance(transform.position, Vector3.zero);
    }

    public void SetPose(Transform arg)
    {
        targetPosition = arg.position;
        targetRotation = arg.rotation;
    }

    private void Update()
    {
        HandleRotation();
        HandlePan();
        HandleZoom();
        HandleWASDMovement();

        ApplySmoothMovement();
    }

    private void HandleRotation()
    {
        if (Input.GetKey(rotateKey))
        {
            float mouseX = Input.GetAxis("Mouse X") * rotationSpeed;
            float mouseY = Input.GetAxis("Mouse Y") * rotationSpeed;

            // Scene视图风格的旋转
            Vector3 euler = targetRotation.eulerAngles;
            euler.x -= mouseY;
            euler.y += mouseX;
            euler.z = 0; // 保持z轴为0，防止倾斜

            targetRotation = Quaternion.Euler(euler);
        }
    }

    private void HandlePan()
    {
        if (Input.GetKey(panKey))
        {
            float mouseX = Input.GetAxis("Mouse X");
            float mouseY = Input.GetAxis("Mouse Y");

            // 根据当前视角计算平移方向
            Vector3 right = targetRotation * Vector3.right;
            Vector3 up = targetRotation * Vector3.up;

            Vector3 panOffset = (-right * mouseX - up * mouseY) * currentZoom * 0.1f;
            targetPosition += panOffset;
        }
    }
    float scroll;
    private void HandleZoom()
    {
        scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.01f)
        {
            // Scene视图风格的缩放：沿视线方向前进/后退
            Vector3 zoomDirection = targetRotation * Vector3.forward;
            currentZoom = Mathf.Clamp(currentZoom - scroll * zoomSpeed, minZoom, maxZoom);

            // 更新目标位置
            Vector3 lookAtPoint = targetPosition + zoomDirection * currentZoom;
            targetPosition = lookAtPoint - zoomDirection * currentZoom;
        }
    }

    private void HandleWASDMovement()
    {
        if (Input.GetKey(moveKey))
        {
            float speed = moveSpeed * Time.deltaTime;
            if (Input.GetKey(KeyCode.LeftShift))
                speed *= fastMoveMultiplier;

            Vector3 moveInput = new Vector3(
                (Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0),
                (Input.GetKey(KeyCode.E) ? 1 : 0) - (Input.GetKey(KeyCode.Q) ? 1 : 0),
                (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0)
            );

            if (moveInput.magnitude > 0)
            {
                Vector3 forward = targetRotation * Vector3.forward;
                Vector3 right = targetRotation * Vector3.right;
                Vector3 up = targetRotation * Vector3.up;

                // 移除垂直分量，保持水平移动
                forward.y = 0;
                forward.Normalize();
                right.y = 0;
                right.Normalize();

                Vector3 movement = (forward * moveInput.z + right * moveInput.x + up * moveInput.y) * speed;
                targetPosition += movement;
            }
        }

        // 聚焦到物体 (F键)
        if (Input.GetKeyDown(KeyCode.F) && SelectionHasGameObject())
        {
            FocusOnSelection();
        }
    }

    private void ApplySmoothMovement()
    {
        transform.position = Vector3.SmoothDamp(transform.position, targetPosition, ref positionVelocity, smoothTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 10f);
    }

    private bool SelectionHasGameObject()
    {
#if UNITY_EDITOR
        return UnityEditor.Selection.activeGameObject != null;
#else
        // 运行时版本，这里需要替换为自己的选择逻辑
        return false;
#endif
    }

    private void FocusOnSelection()
    {
#if UNITY_EDITOR
        GameObject selected = UnityEditor.Selection.activeGameObject;
        if (selected != null)
        {
            Bounds bounds = CalculateBounds(selected);
            Vector3 direction = (transform.position - bounds.center).normalized;
            float distance = Mathf.Max(bounds.size.magnitude, 5f);

            targetPosition = bounds.center + direction * distance;
            targetRotation = Quaternion.LookRotation(-direction);
            currentZoom = distance;
        }
#endif
    }

    private Bounds CalculateBounds(GameObject gameObject)
    {
        Renderer renderer = gameObject.GetComponent<Renderer>();
        if (renderer != null)
            return renderer.bounds;

        return new Bounds(gameObject.transform.position, Vector3.one * 2f);
    }

    // 重置相机位置
    public void ResetCamera()
    {
        targetPosition = Vector3.zero + Vector3.back * 10f;
        targetRotation = Quaternion.identity;
        currentZoom = 10f;
    }
}