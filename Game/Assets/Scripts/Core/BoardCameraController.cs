using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// 第三人称相机：热座跟随当前行动单位；AI 对战/管理员锁定玩家角色（避免 AI 回合切镜头暴露位置）。
/// 右键拖移；R 逆时针 / Shift+R 顺时针；滚轮推近拉远；Shift+滚轮调俯仰。开局对齐玩家后方。
/// </summary>
public class BoardCameraController : MonoBehaviour
{
    public static BoardCameraController Instance;

    private const float FieldOfView = 50f;
    private const float LookHeight = 0.55f;
    private const float FollowLerp = 12f;
    /// <summary>按住 R / Shift+R 时的转速（度/秒）。</summary>
    private const float RotateSpeedDegrees = 45f;
    private const float PitchMin = 12f;
    private const float PitchMax = 70f;
    /// <summary>默认天空（清晨晴）；对局中由 AtmosphereVisual 覆盖。</summary>
    private static readonly Color DefaultSky = new Color(0.95f, 0.72f, 0.58f);

    [SerializeField] private float panThresholdPx = 6f;
    [SerializeField] private float zoomSensitivity = 0.12f;
    [SerializeField] private float pitchSensitivity = 3.5f;
    [SerializeField] private float maxPanDistance = 10f;

    private Camera cam;
    private Vector3 followPoint;
    private Vector3 panOffset;
    private UnitActor lastFollowUnit;
    private float yaw = 0f;
    private float pitch = 18f;
    private float distance = 5.5f;
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

        distMin = 2.8f;
        // 限制拉远，减少穿出密林外圈的机会
        distMax = Mathf.Clamp(boardMax * 0.55f, 9f, 14f);
        distance = 5.5f;
        pitch = 18f;
        yaw = 0f;
        panOffset = Vector3.zero;
        lastFollowUnit = null;
        maxPanDistance = Mathf.Clamp(boardMax * 0.55f, 8f, 14f);

        followPoint = GetAnchorPoint() + Vector3.up * LookHeight;
        ApplyOutdoorClearColor();
        ApplyTransform(true);
    }

    /// <summary>开局：相机落到跟随单位后方，朝向棋盘中心（第三人称跟背视角）。</summary>
    public void SnapBehindFollowUnit()
    {
        panOffset = Vector3.zero;
        lastFollowUnit = GetFollowUnit();
        Vector3 anchor = GetAnchorPoint();
        followPoint = anchor + Vector3.up * LookHeight;

        Vector3 boardCenter = new Vector3(boardW * 0.5f, 0f, boardD * 0.5f);
        Vector3 toCenter = boardCenter - anchor;
        toCenter.y = 0f;
        if (toCenter.sqrMagnitude > 0.01f)
            yaw = Mathf.Atan2(toCenter.x, toCenter.z) * Mathf.Rad2Deg;
        else
            yaw = 0f;

        pitch = 18f;
        distance = 5.5f;
        ApplyOutdoorClearColor();
        ApplyTransform(true);
    }

    public void ApplyClearColorForTable(TableStyle style)
        => ApplyOutdoorClearColor();

    public void ApplyOutdoorClearColor()
    {
        if (AtmosphereVisual.Instance != null)
            ApplySkyColor(AtmosphereVisual.Instance.CurrentSkyColor);
        else
            ApplySkyColor(DefaultSky);
    }

    public void ApplySkyColor(Color sky)
    {
        if (cam == null)
            cam = Camera.main;
        if (cam != null)
            cam.backgroundColor = sky;
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

    /// <summary>
    /// AI 对战/管理员：始终跟玩家角色，与迷雾视角一致；热座：跟当前行动者。
    /// </summary>
    private static UnitActor GetFollowUnit()
    {
        if (MatchConfig.IsAiBattle)
        {
            var human = VisibilityService.FindUnit(MatchConfig.HumanRole);
            if (human != null && !human.IsDead)
                return human;
            return null;
        }

        return TurnManager.Instance?.CurrentUnit;
    }

    private Vector3 GetAnchorPoint()
    {
        var unit = GetFollowUnit();
        if (unit != null && !unit.IsDead)
            return unit.transform.position;
        return new Vector3(boardW * 0.5f, 0f, boardD * 0.5f);
    }

    private void UpdateFollowTarget()
    {
        var unit = GetFollowUnit();
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

        // 俯视下：R 逆时针（yaw 减），Shift+R 顺时针（yaw 加）
        float delta = RotateSpeedDegrees * Time.deltaTime;
        yaw += IsShiftHeld() ? delta : -delta;
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

        // Shift+滚轮：调俯仰；普通滚轮：推近拉远
        if (IsShiftHeld())
            pitch = Mathf.Clamp(pitch - steps * pitchSensitivity, PitchMin, PitchMax);
        else
            distance = Mathf.Clamp(distance * (1f - steps * zoomSensitivity), distMin, distMax);
    }

    private static bool IsShiftHeld()
    {
        if (Keyboard.current == null)
            return false;
        return Keyboard.current.shiftKey.isPressed
            || Keyboard.current.leftShiftKey.isPressed
            || Keyboard.current.rightShiftKey.isPressed;
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
        cam.farClipPlane = 200f;

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
