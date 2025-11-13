using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Rotating door that uses a referenced persistent obstacle (CarryBlocker) in the doorway.
/// - The visual/physical moving part rotates around a pivot (either a Transform or a virtual hinge point).
/// - A referenced CarryBlocker object stays in the doorway and is managed by CarryBlockerToggleOnGrab.
/// - The blocker becomes solid when the door is closed, or (optionally via its own settings) while any object is held,
///   to block teleport rays and prevent carrying objects through.
///
/// Notes:
/// - Assign doorPivot (optional) to define which Transform will rotate. If null, falls back to rotateTarget if assigned,
///   otherwise this transform.
/// - If you want a classic hierarchy-based pivot, set rotateTarget to the hinge Transform (typically a parent with visuals
///   under it) and leave useHingeOffset = false. The script will interpolate localRotation on that Transform.
/// - If you don't want to restructure hierarchy, enable useHingeOffset and set hingeLocalPoint. The script will rotate
///   around the specified hinge point in world space, updating both rotation and position so the hinge stays fixed.
/// - Assign carryBlocker to the object that has CarryBlockerToggleOnGrab; it will NOT be reparented.
/// </summary>
[DisallowMultipleComponent]
public class RotatingDoorWithCarryBlocker : MonoBehaviour
{
    [Header("Pivot & Motion")]
    [Tooltip("Optional. What transform should rotate. If null, uses rotateTarget if assigned, otherwise this transform.")]
    public Transform doorPivot;

    [Tooltip("Classic pivot object (hierarchy-based). When useHingeOffset is false, we rotate this localRotation.")]
    public Transform rotateTarget;

    [Tooltip("Local axis to rotate around (normalized automatically). Typical is Y = up.")]
    public Vector3 localAxis = Vector3.up;

    [Tooltip("Degrees to rotate from CLOSED to OPEN (positive is right-hand rule around localAxis).")]
    public float openAngle = 90f;

    [Tooltip("Seconds to fully open/close")]
    [Min(0.01f)]
    public float moveTime = 0.7f;

    [Tooltip("Easing curve time->alpha for motion")]
    public AnimationCurve ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Tooltip("Automatically close after X seconds. 0 = never auto-close")]
    public float autoCloseDelay = 0f;

    [Header("Virtual Hinge (no hierarchy changes)")]
    [Tooltip("Rotate around an explicit local hinge point (relative to this door) instead of relying on a pivot Transform.")]
    public bool useHingeOffset = false;

    [Tooltip("Hinge point in this object's local space (used when useHingeOffset is true). World hinge = transform.TransformPoint(hingeLocalPoint).")]
    public Vector3 hingeLocalPoint = Vector3.zero;

    [Header("Visual toggles (optional)")]
    [Tooltip("Disable MeshRenderers while OPEN")] public bool hideRenderersWhenOpen = false;
    [Tooltip("Set door colliders to isTrigger while OPEN")] public bool setDoorCollidersTriggerWhenOpen = true;

    [Header("CarryBlocker reference")]
    [Tooltip("Reference to the persistent obstacle in the doorway. Should have CarryBlockerToggleOnGrab component.")]
    public Transform carryBlocker;

    [Header("Events")] public UnityEvent OnOpened; public UnityEvent OnClosed;

    // --- runtime ---
    public bool IsOpen { get; private set; }

    Transform _rot;                 // actual transform that rotates
    Quaternion _closedLocalRot;     // used when useHingeOffset == false
    Quaternion _openLocalRot;       // used when useHingeOffset == false

    // Cached for hinge mode
    Vector3 _hingeWorldClosed;      // world-space hinge computed in closed pose
    Vector3 _closedOffsetFromHinge; // (doorPivot.position - _hingeWorldClosed) captured in closed pose

    Coroutine _motion;

    Renderer[] _renderers;
    Collider[] _doorColliders;
    CarryBlockerToggleOnGrab _blockerCtrl;

    void Reset()
    {
        rotateTarget = transform;
        doorPivot = null; // will resolve to rotateTarget or transform at runtime
    }

    void Awake()
    {
        ResolveDoorPivotT();
        CacheDoorComponents();
        RecomputeRotations();
        CaptureClosedHingeData();
        ResolveBlockerController();
        NotifyBlockerOfState(); // initial state
    }

    void OnValidate()
    {
        ResolveDoorPivotT();
        if (!Application.isPlaying)
        {
            CacheDoorComponents();
            RecomputeRotations();
            CaptureClosedHingeData();
            ResolveBlockerController();
            NotifyBlockerOfState();
        }
    }

    void ResolveDoorPivotT()
    {
        _rot = doorPivot != null ? doorPivot : (rotateTarget != null ? rotateTarget : transform);
    }

    void ResolveBlockerController()
    {
        _blockerCtrl = null;
        if (carryBlocker != null)
            _blockerCtrl = carryBlocker.GetComponent<CarryBlockerToggleOnGrab>();
        if (carryBlocker != null && _blockerCtrl == null)
            Debug.LogWarning("RotatingDoorWithCarryBlocker: 'carryBlocker' has no CarryBlockerToggleOnGrab component.");
    }

    void CacheDoorComponents()
    {
        _renderers = GetComponentsInChildren<Renderer>(true);
        _doorColliders = GetComponentsInChildren<Collider>(true);
    }

    void RecomputeRotations()
    {
        if (_rot == null) return;
        Vector3 ax = (localAxis.sqrMagnitude < 1e-6f ? Vector3.up : localAxis.normalized);
        _closedLocalRot = _rot.localRotation;
        _openLocalRot = Quaternion.AngleAxis(openAngle, ax) * _closedLocalRot;
    }

    void CaptureClosedHingeData()
    {
        if (!useHingeOffset || _rot == null) return;
        _hingeWorldClosed = transform.TransformPoint(hingeLocalPoint);
        _closedOffsetFromHinge = _rot.position - _hingeWorldClosed;
    }

    // --------------- API -----------------
    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true;
        StartMove(true);
        if (autoCloseDelay > 0f)
            StartCoroutine(AutoCloseAfter(autoCloseDelay));
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        StartMove(false);
    }

    public void Toggle()
    {
        if (IsOpen) Close(); else Open();
    }

    void StartMove(bool toOpen)
    {
        if (_motion != null) StopCoroutine(_motion);
        _motion = useHingeOffset ? StartCoroutine(RotateAroundHinge(toOpen))
                                 : StartCoroutine(RotateLocal(toOpen ? _openLocalRot : _closedLocalRot));
    }

    IEnumerator RotateLocal(Quaternion targetLocalRot)
    {
        var r0 = _rot.localRotation;
        float t = 0f;
        float dur = Mathf.Max(0.0001f, moveTime);
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            float e = ease != null ? ease.Evaluate(k) : k;
            _rot.localRotation = Quaternion.SlerpUnclamped(r0, targetLocalRot, e);
            yield return null;
        }
        _rot.localRotation = targetLocalRot;
        _motion = null;

        bool opened = Quaternion.Angle(targetLocalRot, _openLocalRot) <= 0.01f;
        ApplyVisualAndColliderState(opened);
        try { if (opened) OnOpened?.Invoke(); else OnClosed?.Invoke(); } catch { }
    }

    IEnumerator RotateAroundHinge(bool toOpen)
    {
        // Prepare world-space start/end rotations derived from saved local closed/open rotations
        var parent = _rot.parent;
        Quaternion parentWorldRot = parent ? parent.rotation : Quaternion.identity;
        Quaternion worldClosed = parentWorldRot * _closedLocalRot;
        Quaternion worldOpen = parentWorldRot * _openLocalRot;

        // Interpolation direction expressed as [0..1] open fraction
        float t = 0f; float dur = Mathf.Max(0.0001f, moveTime);
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            float e = ease != null ? ease.Evaluate(k) : k;
            float openFrac = toOpen ? e : (1f - e);

            Quaternion rotW = Quaternion.SlerpUnclamped(worldClosed, worldOpen, openFrac);
            Quaternion delta = rotW * Quaternion.Inverse(worldClosed);

            // Keep the hinge point fixed in world space
            _rot.rotation = rotW;
            _rot.position = _hingeWorldClosed + (delta * _closedOffsetFromHinge);

            yield return null;
        }

        // Snap to exact end
        if (toOpen)
        {
            _rot.rotation = worldOpen;
            _rot.position = _hingeWorldClosed + (Quaternion.AngleAxis(openAngle, (_rot.TransformDirection(localAxis).sqrMagnitude < 1e-6f ? Vector3.up : _rot.TransformDirection(localAxis).normalized)) * _closedOffsetFromHinge);
        }
        else
        {
            _rot.rotation = worldClosed;
            _rot.position = _hingeWorldClosed + _closedOffsetFromHinge;
        }

        _motion = null;
        ApplyVisualAndColliderState(toOpen);
        try { if (toOpen) OnOpened?.Invoke(); else OnClosed?.Invoke(); } catch { }
    }

    void ApplyVisualAndColliderState(bool opened)
    {
        if (hideRenderersWhenOpen && _renderers != null)
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                var r = _renderers[i];
                if (!r) continue;
                r.enabled = !opened;
            }
        }
        if (setDoorCollidersTriggerWhenOpen && _doorColliders != null)
        {
            for (int i = 0; i < _doorColliders.Length; i++)
            {
                var c = _doorColliders[i];
                if (!c) continue;
                c.isTrigger = opened;
                if (!c.enabled) c.enabled = true;
            }
        }

        // Inform the blocker about the door state.
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
