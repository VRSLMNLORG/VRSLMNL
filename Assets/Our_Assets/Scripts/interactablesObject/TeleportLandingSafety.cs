using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Prevents the XR Origin's CharacterController from ending up intersecting a door/blocker
/// after a teleport that lands too close to an obstacle. Detects sudden large rig movement
/// (teleport) and, for a few frames, computes a minimal push-out away from nearby colliders,
/// keeping a safety margin from obstacles.
///
/// Attach to the XR Origin (object that has the CharacterController).
/// </summary>
[DisallowMultipleComponent]
public class TeleportLandingSafety : MonoBehaviour
{
    [Header("Obstacle Filter")]
    [Tooltip("Layers considered solid for landing separation (e.g., Default, Obstacle, Environment)")]
    public LayerMask obstacleMask = ~0;

    [Header("Parameters")] 
    [Tooltip("Horizontal safety margin from obstacles around the capsule radius (meters).")]
    [Range(0.0f, 0.25f)] public float safeMargin = 0.06f;

    [Tooltip("If rig moves further than this within one frame, treat it as a teleport (meters).")]
    [Range(0.05f, 2f)] public float teleportDeltaThreshold = 0.5f;

    [Tooltip("How many frames to run separation after a teleport.")]
    [Range(1, 8)] public int settleFrames = 3;

    [Tooltip("Max push distance per frame (meters) to avoid large pops.")]
    [Range(0.01f, 0.5f)] public float maxPushPerFrame = 0.2f;

    CharacterController _cc;
    Vector3 _prevPos;
    int _framesToSettle;

    void Reset()
    {
        // Default to Obstacle + Default layers if they exist
        int obstacle = LayerMask.NameToLayer("Obstacle");
        if (obstacle >= 0)
            obstacleMask = (1 << obstacle) | (1 << 0); // Obstacle | Default
    }

    void Awake()
    {
        _cc = GetComponent<CharacterController>();
        if (_cc == null)
        {
            Debug.LogWarning("TeleportLandingSafety: CharacterController not found on this object.");
        }
        _prevPos = transform.position;
    }

    void LateUpdate()
    {
        if (_cc == null || !_cc.enabled) { _prevPos = transform.position; return; }

        float delta = (transform.position - _prevPos).magnitude;
        if (delta > teleportDeltaThreshold)
            _framesToSettle = settleFrames;

        if (_framesToSettle > 0)
        {
            ResolvePenetrationAndMargin();
            _framesToSettle--;
        }

        _prevPos = transform.position;
    }

    void ResolvePenetrationAndMargin()
    {
        // Compute capsule endpoints in world space
        Vector3 centerWorld = transform.TransformPoint(_cc.center);
        float half = Mathf.Max(0f, _cc.height * 0.5f - _cc.radius);
        Vector3 pTop = centerWorld + Vector3.up * half;
        Vector3 pBot = centerWorld - Vector3.up * half;

        float targetRadius = _cc.radius + safeMargin;

        // Check candidates intersecting an expanded capsule
        Collider[] candidates = Physics.OverlapCapsule(pTop, pBot, targetRadius, obstacleMask, QueryTriggerInteraction.Collide);
        if (candidates == null || candidates.Length == 0)
            return;

        // Sample points along capsule (center, near top, near bottom)
        Vector3 sCenter = centerWorld;
        Vector3 sTop = pTop - Vector3.up * _cc.radius;
        Vector3 sBottom = pBot + Vector3.up * _cc.radius;

        Vector3 push = Vector3.zero;
        float maxNeeded = 0f;

        foreach (var col in candidates)
        {
            if (col == null || col.attachedRigidbody == _cc) continue;
            // For each sample, compute closest point and accumulate the most constraining push
            AccumulatePush(col, sCenter, targetRadius, ref push, ref maxNeeded);
            AccumulatePush(col, sTop, targetRadius, ref push, ref maxNeeded);
            AccumulatePush(col, sBottom, targetRadius, ref push, ref maxNeeded);
        }

        if (maxNeeded <= 1e-5f) return;

        // Limit and move via CharacterController to respect ground snapping
        Vector3 pushDir = push.sqrMagnitude > 1e-8f ? push.normalized : Vector3.zero;
        float pushLen = Mathf.Min(maxNeeded, maxPushPerFrame);
        if (pushLen > 0f && pushDir != Vector3.zero)
            _cc.Move(pushDir * pushLen);
    }

    static void AccumulatePush(Collider col, Vector3 sample, float targetRadius, ref Vector3 pushSum, ref float maxNeeded)
    {
        Vector3 cp = col.ClosestPoint(sample);
        Vector3 toOutside = sample - cp;
        float d = toOutside.magnitude;
        float need = targetRadius - d;
        if (need <= 0f) return;

        Vector3 dir = d > 1e-5f ? (toOutside / d) : Vector3.up; // fallback upward
        // Track the direction with the largest required correction
        if (need > maxNeeded)
        {
            maxNeeded = need;
            pushSum = dir; // orient to the tightest constraint
        }
    }
}
