using UnityEngine;

/// <summary>
/// Tracks the nearest ForcedPerspectiveFromPickup under the XR Origin using OverlapSphere.
/// Place on XR Origin; it checks a sphere below with layer/tag filters and raises an event on change.
/// Can block ForcedPerspectiveFromPickup when object is detected under the player.
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

    [Header("Blocking Behavior")]
    [Tooltip("Block ForcedPerspectiveFromPickup when object is detected under XR Origin.")]
    public bool blockForcedPerspectiveWhenDetected = true;

    [Tooltip("Disable ForcedPerspectiveFromPickup component when blocked.")]
    public bool disableComponentWhenBlocked = true;

    [Tooltip("Change object layer when blocked. Leave empty to keep original layer.")]
    public string blockedLayerName = "";

    [Tooltip("Layer to use when object is blocked (if blockedLayerName is empty, this is used as fallback).")]
    public int blockedLayer = 0;

    [Header("Debug")]
    public bool debugLogChanges = false;

    public ForcedPerspectiveFromPickup CurrentTarget { get; private set; }
    public event System.Action<ForcedPerspectiveFromPickup> GroundTargetChanged;

    readonly Collider[] _hits = new Collider[16];

    // Track blocked objects to restore them when unblocked
    readonly System.Collections.Generic.Dictionary<ForcedPerspectiveFromPickup, BlockedState> _blockedObjects =
        new System.Collections.Generic.Dictionary<ForcedPerspectiveFromPickup, BlockedState>();

    private class BlockedState
    {
        public bool wasEnabled;
        public int originalLayer;
        public bool wasBlockedByTeleport;
    }

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

        // Handle blocking/unblocking
        if (blockForcedPerspectiveWhenDetected)
        {
            // Block new target if it changed
            if (newTarget != null && newTarget != CurrentTarget)
            {
                BlockForcedPerspective(newTarget);
            }

            // Unblock previous target if it's no longer current
            if (CurrentTarget != null && CurrentTarget != newTarget)
            {
                UnblockForcedPerspective(CurrentTarget);
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

    void OnDisable()
    {
        // Restore all blocked objects when this component is disabled
        var keys = new System.Collections.Generic.List<ForcedPerspectiveFromPickup>(_blockedObjects.Keys);
        foreach (var pickup in keys)
        {
            UnblockForcedPerspective(pickup);
        }
        _blockedObjects.Clear();
    }

    void BlockForcedPerspective(ForcedPerspectiveFromPickup pickup)
    {
        if (pickup == null) return;
        if (_blockedObjects.ContainsKey(pickup)) return; // Already blocked

        // Release object if it's currently being held
        if (pickup.isHeld)
        {
            pickup.ReleaseHolding();
        }

        var state = new BlockedState
        {
            wasEnabled = pickup.enabled,
            originalLayer = pickup.gameObject.layer,
            wasBlockedByTeleport = pickup.blockedByTeleport
        };
        _blockedObjects[pickup] = state;

        // Set blockedByTeleport flag (prevents StartHolding)
        pickup.blockedByTeleport = true;

        // Disable component if requested
        if (disableComponentWhenBlocked)
        {
            pickup.enabled = false;
        }

        // Change layer if requested
        if (!string.IsNullOrEmpty(blockedLayerName))
        {
            int layer = LayerMask.NameToLayer(blockedLayerName);
            if (layer != -1)
            {
                SetObjectLayer(pickup.gameObject, layer);
            }
        }
        else if (blockedLayer >= 0 && blockedLayer < 32)
        {
            SetObjectLayer(pickup.gameObject, blockedLayer);
        }

        if (debugLogChanges)
        {
            Debug.Log($"[XROriginGroundOverlapWatcher] Blocked ForcedPerspective on '{pickup.name}'", this);
        }
    }

    void UnblockForcedPerspective(ForcedPerspectiveFromPickup pickup)
    {
        if (pickup == null) return;
        if (!_blockedObjects.TryGetValue(pickup, out var state)) return;

        // Restore blockedByTeleport flag
        pickup.blockedByTeleport = state.wasBlockedByTeleport;

        // Restore component enabled state
        if (disableComponentWhenBlocked)
        {
            pickup.enabled = state.wasEnabled;
        }

        // Restore original layer
        SetObjectLayer(pickup.gameObject, state.originalLayer);

        _blockedObjects.Remove(pickup);

        if (debugLogChanges)
        {
            Debug.Log($"[XROriginGroundOverlapWatcher] Unblocked ForcedPerspective on '{pickup.name}'", this);
        }
    }

    void SetObjectLayer(GameObject obj, int layer)
    {
        if (obj == null) return;
        obj.layer = layer;
        // Also set layer for all children
        foreach (Transform child in obj.transform)
        {
            SetObjectLayer(child.gameObject, layer);
        }
    }
}

