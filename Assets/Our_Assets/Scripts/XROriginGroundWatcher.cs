using UnityEngine;

/// <summary>
/// Tracks which ForcedPerspectiveFromPickup (if any) is directly underneath the XR Origin.
/// Raycasts downward each frame and exposes the current target via event + property.
/// </summary>
[DisallowMultipleComponent]
public class XROriginGroundWatcher : MonoBehaviour
{
    [Header("Ground Check")]
    [Tooltip("Physics layers considered when raycasting downward from the XR Origin.")]
    public LayerMask groundMask = ~0;

    [Tooltip("Vertical offset applied upward before casting downward (to start ray from inside rig).")]
    [Min(0f)] public float upOffset = 0.05f;

    [Tooltip("Ray length when checking below the rig (meters).")]
    [Min(0.05f)] public float downDistance = 1.5f;

    [Header("Debug")]
    [Tooltip("If enabled, logs when the ground target changes.")]
    public bool debugLogChanges = false;

    public ForcedPerspectiveFromPickup CurrentTarget { get; private set; }

    public event System.Action<ForcedPerspectiveFromPickup> GroundTargetChanged;

    void Update()
    {
        ForcedPerspectiveFromPickup newTarget = null;
        Vector3 origin = transform.position + Vector3.up * upOffset;

        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, downDistance, groundMask, QueryTriggerInteraction.Ignore))
            newTarget = hit.collider != null ? hit.collider.GetComponentInParent<ForcedPerspectiveFromPickup>() : null;

        if (CurrentTarget == newTarget)
            return;

        CurrentTarget = newTarget;
        if (debugLogChanges)
        {
            string name = CurrentTarget != null ? CurrentTarget.name : "<none>";
            Debug.Log($"[XROriginGroundWatcher] ground target -> {name}", this);
        }
        GroundTargetChanged?.Invoke(CurrentTarget);
    }
}
