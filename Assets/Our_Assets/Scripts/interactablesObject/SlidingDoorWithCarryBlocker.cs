using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Sliding door with an externally referenced persistent obstacle (CarryBlocker).
/// - The moving part slides open/closed.
/// - The CarryBlocker object remains in the doorway and is controlled by CarryBlockerToggleOnGrab.
///   This collider is intended to be hit by raycasts (e.g., ForcedPerspectiveFromPickup)
///   and prevents scalable/held objects from being carried through when the rules deem it solid.
///
/// Notes:
/// - The CarryBlocker component owns its trigger/solid logic and layer assignment.
/// - Physics setting "Raycasts Hit Triggers" must be enabled (default) for trigger state.
/// </summary>
[DisallowMultipleComponent]
public class SlidingDoorWithCarryBlocker : MonoBehaviour
{
    [Header("Moving Part")]
    [Tooltip("What transform should slide. If null, this transform is moved.")]
    public Transform movingPart;

    [Tooltip("Local offset from CLOSED to OPEN state.")]
    public Vector3 openLocalOffset = new Vector3(1.2f, 0f, 0f);

    [Tooltip("Seconds to fully open/close")]
    [Min(0.01f)] public float moveTime = 0.7f;

    [Tooltip("Easing curve time->alpha for motion")]
    public AnimationCurve ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Tooltip("Automatically close after X seconds. 0 = never auto-close")]
    public float autoCloseDelay = 0f;

    [Header("Visual toggles (optional)")]
    [Tooltip("Disable MeshRenderers while OPEN")] public bool hideRenderersWhenOpen = false;
    [Tooltip("Set door colliders to isTrigger while OPEN")] public bool setDoorCollidersTriggerWhenOpen = true;

    [Header("CarryBlocker Reference")]
    [Tooltip("Reference to an external CarryBlocker object that has CarryBlockerToggleOnGrab.")]
    public Transform carryBlocker;

    [Header("Events")]
    public UnityEvent OnOpened;
    public UnityEvent OnClosed;

    // --- Runtime state ---
    public bool IsOpen { get; private set; }

    Transform _moveT;
    Vector3 _closedLocalPos;
    Vector3 _openLocalPos;
    Coroutine _motion;

    Renderer[] _renderers;
    Collider[] _doorColliders;
    CarryBlockerToggleOnGrab _blockerCtrl;

    void Reset()
    {
        movingPart = transform;
    }

    void Awake()
    {
        _moveT = movingPart != null ? movingPart : transform;
        _closedLocalPos = _moveT.localPosition;
        _openLocalPos = _closedLocalPos + openLocalOffset;

        CacheDoorComponents();
        ResolveBlockerController();
        NotifyBlockerOfState();
    }

    void OnValidate()
    {
        if (movingPart == null) movingPart = transform;
        _moveT = movingPart;
        _closedLocalPos = _moveT != null ? _moveT.localPosition : Vector3.zero;
        _openLocalPos = _closedLocalPos + openLocalOffset;

        if (Application.isPlaying) return;
        CacheDoorComponents();
        ResolveBlockerController();
        NotifyBlockerOfState();
    }

    void ResolveBlockerController()
    {
        _blockerCtrl = null;
        if (carryBlocker != null)
            _blockerCtrl = carryBlocker.GetComponent<CarryBlockerToggleOnGrab>();
        if (carryBlocker != null && _blockerCtrl == null)
            Debug.LogWarning($"[{name}] SlidingDoorWithCarryBlocker: 'carryBlocker' has no CarryBlockerToggleOnGrab component.");
    }

    void CacheDoorComponents()
    {
        _renderers = GetComponentsInChildren<Renderer>(true);
        _doorColliders = GetComponentsInChildren<Collider>(true);
    }

    // ---------------- Public API ----------------
    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true;
        StartMove(_openLocalPos);
        if (autoCloseDelay > 0f)
            StartCoroutine(AutoCloseAfter(autoCloseDelay));
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        StartMove(_closedLocalPos);
    }

    public void Toggle()
    {
        if (IsOpen) Close(); else Open();
    }

    void StartMove(Vector3 target)
    {
        if (_motion != null) StopCoroutine(_motion);
        _motion = StartCoroutine(MoveTo(target));
    }

    IEnumerator MoveTo(Vector3 target)
    {
        var p0 = _moveT.localPosition;
        float t = 0f;
        float dur = Mathf.Max(0.0001f, moveTime);

        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            float e = ease != null ? ease.Evaluate(k) : k;
            _moveT.localPosition = Vector3.LerpUnclamped(p0, target, e);
            yield return null;
        }

        _moveT.localPosition = target;
        _motion = null;

        bool opened = (target - _openLocalPos).sqrMagnitude <= 1e-6f;
        ApplyVisualAndColliderState(opened);

        try { if (opened) OnOpened?.Invoke(); else OnClosed?.Invoke(); } catch { }
    }

    void ApplyVisualAndColliderState(bool opened)
    {
        if (hideRenderersWhenOpen && _renderers != null)
        {
            foreach (var r in _renderers)
                if (r) r.enabled = !opened;
        }

        if (setDoorCollidersTriggerWhenOpen && _doorColliders != null)
        {
            foreach (var c in _doorColliders)
            {
                if (!c) continue;
                c.isTrigger = opened;
                if (!c.enabled) c.enabled = true;
            }
        }

        // Inform the blocker
        NotifyBlocker(opened);
    }

    IEnumerator AutoCloseAfter(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        Close();
    }

    void NotifyBlocker(bool opened)
    {
        if (_blockerCtrl == null)
            ResolveBlockerController();
        if (_blockerCtrl != null)
            _blockerCtrl.SetDoorOpen(opened);
    }

    void NotifyBlockerOfState()
    {
        NotifyBlocker(IsOpen);
    }
}
