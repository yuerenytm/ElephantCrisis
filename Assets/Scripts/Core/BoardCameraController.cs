using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// 第三人称相机：跟随当前行动单位；右键拖移视角；R 逆时针旋转；滚轮推近拉远。
/// </summary>
public class BoardCameraController : MonoBehaviour
{
    public static BoardCameraController Instance;

    private const float FieldOfView = 50f;
    private const float LookHeight = 0.55f;
    private const float FollowLerp = 12f;
    /// <summary>按住 R 时的逆时针转速（度/秒）。</summary>
    private const float RotateSpeedDegrees = 45f;

    [SerializeField] private float panThresholdPx = 6f;
    [SerializeField] private float zoomSensitivity = 0.12f;
    [SerializeField] private float maxPanDistance = 10f;

    private Camera cam;
    private Vector3 followPoint;
    private Vector3 panOffset;
    private UnitActor lastFollowUnit;
    private float yaw = 0f;
    private float pitch = 34f;
    private float distance = 7.5f;
    private float distMin = 3.2f;
    private float distMax = 16f;
    private float boardW = 18f;
    private float boardD = 18f;

    private bool rightHeld;
    private bool pannedThisGesture;
    private Vector2 lastScreen;
    private Vector3 panGrabGround;

    /// <summary>本次右键手势是否已用于拖移（用于区分「取消瞄准」短按）。</summary>
    public bool PannedThisGesture => pannedThisGesture;

    private void Awake()
    {
        Instance = this;
        cam = GetComponent<Camera>();
        if (cam == null)
            cam = Camera.main;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void FrameBoard(float width, float depth)
    {
        boardW = width;
        boardD = depth;
        float boardMax = Mathf.Max(width, depth);

        distMin = 3f;
        distMax = Mathf.Clamp(boardMax * 0.85f, 12f, 22f);
        distance = Mathf.Clamp(boardMax * 0.42f, 6f, 10f);
        pitch = 34f;
        yaw = 0f;
        panOffset = Vector3.zero;
        lastFollowUnit = null;
        maxPanDistance = Mathf.Clamp(boardMax * 0.55f, 8f, 14f);

        followPoint = GetAnchorPoint() + Vector3.up * LookHeight;
        ApplyClearColorForTable(TableSurface.Current);
        ApplyTransform(true);
    }

    public void ApplyClearColorForTable(TableStyle style)
    {
        if (cam == null)
            cam = Camera.main;
        if (cam != null)
            cam.backgroundColor = TableSurface.GetClearColor(style);
    }

    private void LateUpdate()
    {
        if (cam == null)
        {
            cam = Camera.main;
            if (cam == null)
                return;
        }

        if (!IsGameplayCameraActive())
            return;

        HandleRotate();
        UpdateFollowTarget();
        HandlePan();
        HandleZoom();
        ApplyTransform(rightHeld && pannedThisGesture);
    }

    private static bool IsGameplayCameraActive()
    {
        if (MainMenuUI.Instance != null && MainMenuUI.Instance.IsVisible)
            return false;
        return TurnManager.Instance != null
            && GridManager.Instance != null
            && MapVisual.Instance != null;
    }

    private Vector3 GetAnchorPoint()
    {
        var unit = TurnManager.Instance?.CurrentUnit;
        if (unit != null && !unit.IsDead)
            return unit.transform.position;
        return new Vector3(boardW * 0.5f, 0f, boardD * 0.5f);
    }

    private void UpdateFollowTarget()
    {
        var unit = TurnManager.Instance?.CurrentUnit;
        if (unit != lastFollowUnit)
        {
            panOffset = Vector3.zero;
            lastFollowUnit = unit;
        }

        Vector3 desired = GetAnchorPoint() + Vector3.up * LookHeight + panOffset;
        if (rightHeld && pannedThisGesture)
        {
            followPoint = desired;
            return;
        }

        float t = 1f - Mathf.Exp(-FollowLerp * Time.deltaTime);
        followPoint = Vector3.Lerp(followPoint, desired, t);
    }

    private void HandleRotate()
    {
        if (Keyboard.current == null)
            return;
        if (IsPointerOverUi())
            return;
        if (!Keyboard.current.rKey.isPressed)
            return;

        // 俯视下逆时针 = Unity Yaw 减小；按住持续慢转
        yaw -= RotateSpeedDegrees * Time.deltaTime;
    }

    private void HandleZoom()
    {
        if (Mouse.current == null)
            return;
        if (IsPointerOverUi())
            return;

        float scroll = Mouse.current.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) < 0.01f)
            return;

        float steps = scroll / 120f;
        if (Mathf.Abs(steps) < 0.01f)
            steps = Mathf.Sign(scroll);

        distance = Mathf.Clamp(distance * (1f - steps * zoomSensitivity), distMin, distMax);
    }

    private void HandlePan()
    {
        if (Mouse.current == null)
            return;

        if (Mouse.current.rightButton.wasPressedThisFrame)
        {
            if (IsPointerOverUi())
            {
                rightHeld = false;
                return;
            }
            rightHeld = true;
            pannedThisGesture = false;
            lastScreen = Mouse.current.position.ReadValue();
            TryScreenToGround(lastScreen, out panGrabGround);
        }

        if (rightHeld && Mouse.current.rightButton.isPressed)
        {
            Vector2 screen = Mouse.current.position.ReadValue();
            if (!pannedThisGesture && (screen - lastScreen).sqrMagnitude >= panThresholdPx * panThresholdPx)
                pannedThisGesture = true;

            if (pannedThisGesture && TryScreenToGround(screen, out var nowGround))
            {
                Vector3 delta = panGrabGround - nowGround;
                delta.y = 0f;
                panOffset += delta;
                if (panOffset.sqrMagnitude > maxPanDistance * maxPanDistance)
                    panOffset = panOffset.normalized * maxPanDistance;
                followPoint = GetAnchorPoint() + Vector3.up * LookHeight + panOffset;
            }
            lastScreen = screen;
        }

        if (Mouse.current.rightButton.wasReleasedThisFrame)
            rightHeld = false;
    }

    private bool TryScreenToGround(Vector2 screen, out Vector3 ground)
    {
        ground = followPoint;
        Ray ray = cam.ScreenPointToRay(screen);
        if (Mathf.Abs(ray.direction.y) < 1e-5f)
            return false;
        float t = (0f - ray.origin.y) / ray.direction.y;
        if (t < 0f)
            return false;
        ground = ray.origin + ray.direction * t;
        ground.y = 0f;
        return true;
    }

    private void ApplyTransform(bool instant)
    {
        if (cam == null)
            return;

        cam.orthographic = false;
        cam.fieldOfView = FieldOfView;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 120f;

        var rot = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 eye = followPoint - rot * Vector3.forward * distance;
        eye.y = Mathf.Max(eye.y, 0.35f);

        if (instant)
            cam.transform.SetPositionAndRotation(eye, rot);
        else
        {
            float t = 1f - Mathf.Exp(-FollowLerp * Time.deltaTime);
            cam.transform.position = Vector3.Lerp(cam.transform.position, eye, t);
            cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, rot, t);
        }

        Vector3 sort = cam.transform.forward;
        sort.y = 0f;
        if (sort.sqrMagnitude < 1e-4f)
            sort = Vector3.forward;
        cam.transparencySortMode = TransparencySortMode.CustomAxis;
        cam.transparencySortAxis = sort.normalized;
    }

    private static bool IsPointerOverUi()
    {
        if (EventSystem.current == null)
            return false;
        return EventSystem.current.IsPointerOverGameObject();
    }
}
