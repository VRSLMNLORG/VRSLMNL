using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Unity.XR.CoreUtils;
using System.Collections.Generic;

public class TeleportationOnStationary : MonoBehaviour
{
    [Header("References")]
    public Rigidbody targetRigidbody;
    public TeleportationArea teleportArea;
    public XRGrabInteractable grabInteractable;
    public float maxAllowedVelocity = 0.01f;

    [Header("Size-based mode")]
    [Tooltip("If true, small objects stay grabbable via XRGrab while larger ones remain teleport-only.")]
    public bool gateGrabBySize = false;
    [Tooltip("Largest world-space extent (meters) where XR grabbing stays enabled. Bigger objects become teleport-only.")]
    [Min(0f)] public float maxGrabExtent = 0.6f;
    [Header("Forced Perspective")]
    [Tooltip("ForcedPerspective component on the same object (auto-filled if present).")]
    public ForcedPerspectiveFromPickup forcedPerspective;
    [Tooltip("Optional XR Origin (rig) transform to detect when the player has stepped away.")]
    public Transform xrRigTransform;
    [Tooltip("Optional ground watcher that reports which ForcedPerspective object is under the XR Origin.")]
    public XROriginGroundWatcher groundWatcher;
    [Tooltip("Disable ForcedPerspective when teleporting onto this object.")]
    public bool disableForcedPerspectiveOnTeleport = true;

    bool _isGrabbed;
    bool _teleportAllowedBySize = true;
    float _lastExtent = -1f;
    bool _forcedPerspectiveDisabledByTeleport;
    bool _forcedPerspectiveWasEnabledBeforeTeleport = true;
    [Header("Debug")]
    public bool debugLogTeleportEvents = false;
    bool _groundWatcherSawSelfAfterTeleport;

    private void Reset()
    {
        if (targetRigidbody == null)
            targetRigidbody = GetComponent<Rigidbody>();
        if (teleportArea == null)
            teleportArea = GetComponentInChildren<TeleportationArea>();
        if (grabInteractable == null)
            grabInteractable = GetComponent<XRGrabInteractable>();
        if (forcedPerspective == null)
            forcedPerspective = GetComponent<ForcedPerspectiveFromPickup>();
        if (xrRigTransform == null)
        {
            var origin = FindFirstObjectByType<XROrigin>();
            if (origin != null)
                xrRigTransform = origin.transform;
        }

        EvaluateSizeGate();
        CacheGroundWatcher();
    }

    private void Update()
    {
        if (targetRigidbody == null || teleportArea == null)
            return;

        // Сначала пересчитываем размер (если включено), чтобы _teleportAllowedBySize был актуален
        if (gateGrabBySize)
            EvaluateSizeGate();

        // Проверяем разрешение по размеру ПОСЛЕ пересчёта
        if (!_teleportAllowedBySize)
        {
            if (teleportArea.enabled)
                teleportArea.enabled = false;
            return;
        }

        bool isStationary = targetRigidbody.linearVelocity.sqrMagnitude < maxAllowedVelocity * maxAllowedVelocity &&
                            targetRigidbody.angularVelocity.sqrMagnitude < maxAllowedVelocity * maxAllowedVelocity;
        teleportArea.enabled = isStationary && !_isGrabbed;

        TryReactivateForcedPerspective();
    }

    private void OnEnable()
    {
        if (grabInteractable != null)
        {
            grabInteractable.selectEntered.AddListener(OnGrab);
            grabInteractable.selectExited.AddListener(OnRelease);
        }
        if (teleportArea != null)
            teleportArea.teleporting.AddListener(OnTeleporting);
        
        // Отслеживаем Forced Perspective, чтобы сбрасывать _isGrabbed
        ForcedPerspectiveFromPickup.HoldingStarted += OnForcedPerspectiveStarted;
        ForcedPerspectiveFromPickup.HoldingEnded += OnForcedPerspectiveEnded;
        if (groundWatcher == null)
            CacheGroundWatcher();
        if (groundWatcher != null)
            groundWatcher.GroundTargetChanged += OnGroundTargetChanged;
    }

    private void OnDisable()
    {
        if (grabInteractable != null)
        {
            grabInteractable.selectEntered.RemoveListener(OnGrab);
            grabInteractable.selectExited.RemoveListener(OnRelease);
        }
        if (teleportArea != null)
            teleportArea.teleporting.RemoveListener(OnTeleporting);
        
        ForcedPerspectiveFromPickup.HoldingStarted -= OnForcedPerspectiveStarted;
        ForcedPerspectiveFromPickup.HoldingEnded -= OnForcedPerspectiveEnded;
        if (groundWatcher != null)
            groundWatcher.GroundTargetChanged -= OnGroundTargetChanged;
    }

    // Отключаем Area сразу при захвате
    private void OnGrab(SelectEnterEventArgs args)
    {
        _isGrabbed = true;
        if (teleportArea != null)
            teleportArea.enabled = false;
    }

    private void OnRelease(SelectExitEventArgs args)
    {
        _isGrabbed = false;
        if (targetRigidbody != null && teleportArea != null && _teleportAllowedBySize)
        {
            bool isStationary = targetRigidbody.linearVelocity.sqrMagnitude < maxAllowedVelocity * maxAllowedVelocity &&
                                targetRigidbody.angularVelocity.sqrMagnitude < maxAllowedVelocity * maxAllowedVelocity;
            teleportArea.enabled = isStationary;
        }
    }

    private void OnTeleporting(TeleportingEventArgs args)
    {
        if (targetRigidbody != null)
        {
            targetRigidbody.isKinematic = true;
            targetRigidbody.linearVelocity = Vector3.zero;
            targetRigidbody.angularVelocity = Vector3.zero;
        }

        if (!disableForcedPerspectiveOnTeleport || forcedPerspective == null)
            return;

        if (!_forcedPerspectiveDisabledByTeleport && forcedPerspective != null)
            _forcedPerspectiveWasEnabledBeforeTeleport = !forcedPerspective.blockedByTeleport;

        forcedPerspective.blockedByTeleport = true;
        _forcedPerspectiveDisabledByTeleport = true;
        CameraReticleSimple.SuppressPickup(forcedPerspective);
        _groundWatcherSawSelfAfterTeleport = false;
    }

    private void OnForcedPerspectiveStarted(ForcedPerspectiveFromPickup fp)
    {
        // Если это наш объект, блокируем телепорт
        if (fp != null && fp.gameObject == gameObject)
        {
            _isGrabbed = true;
            if (teleportArea != null)
                teleportArea.enabled = false;
        }
    }

    private void OnForcedPerspectiveEnded(ForcedPerspectiveFromPickup fp)
    {
        // Если это наш объект, разблокируем телепорт (если размер позволяет)
        if (fp != null && fp.gameObject == gameObject)
        {
            _isGrabbed = false;
            // Update() сам включит телепорт, если условия выполнены
        }
    }

    void OnValidate()
    {
        EvaluateSizeGate(true);
    }

    void EvaluateSizeGate(bool force = false)
    {
        if (!gateGrabBySize)
        {
            _teleportAllowedBySize = true;
            SetGrabComponentEnabled(true);
            return;
        }

        float extent = GetLargestExtentWorld();
        if (!force && Mathf.Approximately(extent, _lastExtent))
            return;
        _lastExtent = extent;

        bool allowGrab = extent <= maxGrabExtent;
        SetGrabComponentEnabled(allowGrab);
        _teleportAllowedBySize = !allowGrab;

        if (!_teleportAllowedBySize && teleportArea != null)
            teleportArea.enabled = false;
    }

    void SetGrabComponentEnabled(bool enable)
    {
        if (grabInteractable == null) return;
        if (grabInteractable.enabled == enable) return;
        grabInteractable.enabled = enable;
    }

    float GetLargestExtentWorld()
    {
        Vector3 local = transform.localScale;
        return Mathf.Max(local.x, local.y, local.z);
    }

    void TryReactivateForcedPerspective()
    {
        if (!_forcedPerspectiveDisabledByTeleport)
            return;
        if (forcedPerspective == null)
        {
            _forcedPerspectiveDisabledByTeleport = false;
            return;
        }
        if (!_forcedPerspectiveWasEnabledBeforeTeleport)
            return;
        if (groundWatcher != null)
        {
            if (!_groundWatcherSawSelfAfterTeleport)
            {
                if (groundWatcher.CurrentTarget != forcedPerspective)
                    return; // ждём пока watcher подтвердит, что стоим на себе
                _groundWatcherSawSelfAfterTeleport = true;
                return;
            }

            if (groundWatcher.CurrentTarget == forcedPerspective)
                return;
        }
        else if (xrRigTransform == null)
            return;

        forcedPerspective.blockedByTeleport = false;
        _forcedPerspectiveDisabledByTeleport = false;
        CameraReticleSimple.UnsuppressPickup(forcedPerspective);
        if (debugLogTeleportEvents)
            Debug.Log($"[TeleportationOnStationary] Re-enabled ForcedPerspective on '{gameObject.name}'.", this);
    }

    void CacheGroundWatcher()
    {
        if (groundWatcher != null)
            return;
        if (xrRigTransform == null)
            return;

        groundWatcher = xrRigTransform.GetComponentInChildren<XROriginGroundWatcher>(true);
    }

    void OnGroundTargetChanged(ForcedPerspectiveFromPickup pickup)
    {
        if (!_forcedPerspectiveDisabledByTeleport || forcedPerspective == null)
            return;

        if (pickup == forcedPerspective)
        {
            _groundWatcherSawSelfAfterTeleport = true;
            return;
        }

        if (_groundWatcherSawSelfAfterTeleport)
            TryReactivateForcedPerspective();
    }
}
