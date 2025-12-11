using UnityEngine;

/// <summary>
/// Tracks the nearest ForcedPerspectiveFromPickup under the XR Origin using OverlapSphere.
/// Place on XR Origin; it checks a sphere below with layer/tag filters and raises an event on change.
/// </summary>
[DisallowMultipleComponent]
public class XROriginGroundWatcher : MonoBehaviour
{
    [Header("Overlap Settings")]
    [Tooltip("Physics layers considered as ground candidates.")]
    public LayerMask groundMask = ~0;

    [Tooltip("Optional tag filter. Leave empty to ignore tags.")]
    public string requiredTag = "";

    [Tooltip("Vertical offset applied upward before sampling the overlap center.")]
    [Min(0f)] public float upOffset = 0.05f;

    [Tooltip("Vertical drop from the origin to place the overlap center (meters).")]
    [Min(0f)] public float downOffset = 0.1f;

    [Tooltip("Radius of the overlap sphere.")]
    [Min(0.05f)] public float radius = 0.5f;

    [Header("Debug")]
    public bool debugLogChanges = false;

    public ForcedPerspectiveFromPickup CurrentTarget { get; private set; }
    public event System.Action<ForcedPerspectiveFromPickup> GroundTargetChanged;

    readonly Collider[] _hits = new Collider[16];

    void Update()
    {
        ForcedPerspectiveFromPickup newTarget = null;

        Vector3 center = transform.position + Vector3.up * upOffset - Vector3.up * downOffset;
        int count = Physics.OverlapSphereNonAlloc(center, radius, _hits, groundMask, QueryTriggerInteraction.Ignore);

        float bestDistSqr = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            var col = _hits[i];
            if (col == null) continue;

            if (!string.IsNullOrEmpty(requiredTag) && !col.CompareTag(requiredTag))
                continue;

            var candidate = col.GetComponentInParent<ForcedPerspectiveFromPickup>();
            if (candidate == null) continue;

            float d = (candidate.transform.position - center).sqrMagnitude;
            if (d < bestDistSqr)
            {
                bestDistSqr = d;
                newTarget = candidate;
            }
        }

        if (CurrentTarget == newTarget)
            return;

        CurrentTarget = newTarget;
        if (debugLogChanges)
        {
            string name = CurrentTarget != null ? CurrentTarget.name : "<none>";
            Debug.Log($"[XROriginGroundOverlapWatcher] ground target -> {name}", this);
        }
        GroundTargetChanged?.Invoke(CurrentTarget);
    }
}

