using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using System.Collections;

/// <summary>
/// XR Socket Interactor with object size validation.
/// Only allows objects whose size is within the specified range to be placed.
/// Uses transform.localScale to determine size (same approach as TeleportationOnStationary).
/// </summary>
[DisallowMultipleComponent]
public class SizeRestrictedSocketInteractor : XRSocketInteractor
{
    [Header("Size Restrictions")]
    [Tooltip("Enable size check. If disabled, all objects are allowed.")]
    public bool enableSizeCheck = true;

    [Tooltip("Minimum object size (largest axis in Unity local units).")]
    [Min(0f)] public float minSize = 0.1f;

    [Tooltip("Maximum object size (largest axis in Unity local units).")]
    [Min(0f)] public float maxSize = 1.0f;

    [Header("Object Filter")]
    [Tooltip("Enable object whitelist. If enabled, only objects in the whitelist are allowed.")]
    public bool useObjectWhitelist = false;

    [Tooltip("List of allowed objects. If empty and useObjectWhitelist is true, no objects are allowed.")]
    public GameObject[] allowedObjects = new GameObject[0];

    [Header("Hover Mesh Size")]
    [Tooltip("Use fixed size for hover mesh regardless of object size.")]
    public bool useFixedHoverSize = false;

    [Tooltip("Fixed size for hover mesh (largest axis in Unity local units). Only used if Use Fixed Hover Size is enabled.")]
    [Min(0.01f)] public float fixedHoverSize = 0.5f;

    [Header("Debug")]
    [Tooltip("Show size check information in console.")]
    public bool debugLog = false;

    // Cache for hover mesh visual objects
    private System.Collections.Generic.Dictionary<UnityEngine.XR.Interaction.Toolkit.Interactables.IXRHoverInteractable, Transform> _hoverMeshVisuals = new System.Collections.Generic.Dictionary<UnityEngine.XR.Interaction.Toolkit.Interactables.IXRHoverInteractable, Transform>();

    /// <summary>
    /// Checks if the given object can be hovered by this socket.
    /// Returns true ONLY for objects from whitelist (if enabled) that DON'T meet size requirements.
    /// This way "Hover Mesh Material" will only show for whitelisted objects that are invalid.
    /// Objects not in whitelist won't show hover mesh at all.
    /// </summary>
    public override bool CanHover(UnityEngine.XR.Interaction.Toolkit.Interactables.IXRHoverInteractable interactable)
    {
        // First check if base allows hover
        bool baseCanHover = base.CanHover(interactable);
        if (!baseCanHover)
            return false;

        // Get GameObject from interactable
        GameObject obj = GetGameObjectFromHoverInteractable(interactable);
        if (obj == null)
            return false;

        // If whitelist is enabled, only show hover for objects in whitelist
        if (useObjectWhitelist)
        {
            bool inWhitelist = IsObjectInWhitelist(obj);
            if (!inWhitelist)
            {
                if (debugLog)
                {
                    Debug.Log($"[SizeRestrictedSocketInteractor] CanHover: hiding hover mesh for '{obj.name}' (NOT in whitelist)", this);
                }
                return false; // Don't show hover for objects not in whitelist
            }
        }

        // Check if object meets size requirements
        bool isAllowed = IsObjectAllowed(obj);

        // Return true ONLY if object is in whitelist (if enabled) but doesn't meet size requirements
        // This will show hover mesh with "Hover Mesh Material" as a warning
        bool shouldShowHover = !isAllowed;

        if (debugLog)
        {
            if (shouldShowHover)
            {
                Debug.Log($"[SizeRestrictedSocketInteractor] CanHover: showing hover mesh for '{obj.name}' (in whitelist but NOT ALLOWED by size - will use 'Hover Mesh Material' as warning)", this);
            }
            else
            {
                Debug.Log($"[SizeRestrictedSocketInteractor] CanHover: hiding hover mesh for '{obj.name}' (ALLOWED - no hover mesh needed)", this);
            }
        }

        return shouldShowHover;
    }

    /// <summary>
    /// Checks if the given object can be selected by this socket.
    /// Overrides base check, adding size validation.
    /// When this returns false while CanHover returns true, "Can't Hover" material is shown.
    /// </summary>
    public override bool CanSelect(UnityEngine.XR.Interaction.Toolkit.Interactables.IXRSelectInteractable interactable)
    {
        // First check base conditions
        bool baseCanSelect = base.CanSelect(interactable);

        if (debugLog)
        {
            Debug.Log($"[SizeRestrictedSocketInteractor] CanSelect called for {interactable}. base.CanSelect = {baseCanSelect}", this);
        }

        if (!baseCanSelect)
        {
            if (debugLog)
            {
                Debug.LogWarning($"[SizeRestrictedSocketInteractor] Base CanSelect returned false for {interactable}", this);
            }
            return false;
        }

        // Get GameObject from interactable
        GameObject obj = GetGameObjectFromInteractable(interactable);

        if (obj == null)
        {
            if (debugLog)
            {
                Debug.LogWarning($"[SizeRestrictedSocketInteractor] Failed to get GameObject from {interactable}", this);
            }
            return false;
        }

        bool isAllowed = IsObjectAllowed(obj);

        if (debugLog)
        {
            if (isAllowed)
            {
                Debug.Log($"[SizeRestrictedSocketInteractor] ✓ Object '{obj.name}' is ALLOWED - can be selected", this);
            }
            else
            {
                Debug.LogWarning($"[SizeRestrictedSocketInteractor] ✗ Object '{obj.name}' is NOT ALLOWED - cannot be selected", this);
            }
        }

        return isAllowed;
    }

    /// <summary>
    /// Checks if object is allowed (whitelist and size check).
    /// </summary>
    private bool IsObjectAllowed(GameObject obj)
    {
        if (obj == null)
            return false;

        // Check whitelist first
        if (useObjectWhitelist)
        {
            bool inWhitelist = IsObjectInWhitelist(obj);
            if (!inWhitelist)
            {
                if (debugLog)
                {
                    Debug.LogWarning($"[SizeRestrictedSocketInteractor] Object '{obj.name}' is NOT in whitelist", this);
                }
                return false;
            }
        }

        // Check size if enabled
        if (enableSizeCheck)
        {
            bool sizeValid = CheckObjectSize(obj);
            if (!sizeValid)
            {
                if (debugLog)
                {
                    Debug.LogWarning($"[SizeRestrictedSocketInteractor] Object '{obj.name}' FAILS size check", this);
                }
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Checks if object is in the whitelist.
    /// </summary>
    private bool IsObjectInWhitelist(GameObject obj)
    {
        if (allowedObjects == null || allowedObjects.Length == 0)
            return false;

        foreach (GameObject allowedObj in allowedObjects)
        {
            if (allowedObj == null)
                continue;

            // Check direct reference
            if (allowedObj == obj)
                return true;

            // Check if obj is a child of allowed object
            if (obj.transform.IsChildOf(allowedObj.transform))
                return true;

            // Check if allowed object is a child of obj
            if (allowedObj.transform.IsChildOf(obj.transform))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Gets GameObject from IXRHoverInteractable.
    /// </summary>
    private GameObject GetGameObjectFromHoverInteractable(UnityEngine.XR.Interaction.Toolkit.Interactables.IXRHoverInteractable interactable)
    {
        if (interactable == null)
            return null;

        // Try to get through MonoBehaviour
        if (interactable is MonoBehaviour monoBehaviour)
        {
            return monoBehaviour.gameObject;
        }

        // Try to get through transform
        if (interactable.transform != null)
        {
            return interactable.transform.gameObject;
        }

        // Try to get through object as Component
        if (interactable is Component component)
        {
            return component.gameObject;
        }

        return null;
    }

    /// <summary>
    /// Gets GameObject from IXRSelectInteractable.
    /// </summary>
    private GameObject GetGameObjectFromInteractable(UnityEngine.XR.Interaction.Toolkit.Interactables.IXRSelectInteractable interactable)
    {
        if (interactable == null)
            return null;

        // Try to get through MonoBehaviour
        if (interactable is MonoBehaviour monoBehaviour)
        {
            return monoBehaviour.gameObject;
        }

        // Try to get through transform
        if (interactable.transform != null)
        {
            return interactable.transform.gameObject;
        }

        // Try to get through object as Component
        if (interactable is Component component)
        {
            return component.gameObject;
        }

        return null;
    }

    /// <summary>
    /// Checks object size and returns true if it's within the allowed range.
    /// Uses approach from TeleportationOnStationary: checks largest axis of localScale.
    /// </summary>
    private bool CheckObjectSize(GameObject obj)
    {
        if (obj == null)
        {
            if (debugLog)
            {
                Debug.LogWarning($"[SizeRestrictedSocketInteractor] Object is null during size check", this);
            }
            return false;
        }

        float extent = GetLargestExtentWorld(obj);

        if (debugLog)
        {
            Debug.Log($"[SizeRestrictedSocketInteractor] Checking object '{obj.name}': size = {extent}, localScale = {obj.transform.localScale}, range: {minSize} - {maxSize}", this);
        }

        bool isValid = extent >= minSize && extent <= maxSize;

        if (debugLog)
        {
            if (isValid)
            {
                Debug.Log($"[SizeRestrictedSocketInteractor] ✓ Object '{obj.name}' PASSES size check: {extent} (range: {minSize} - {maxSize})", this);
            }
            else
            {
                Debug.LogWarning($"[SizeRestrictedSocketInteractor] ✗ Object '{obj.name}' FAILS size check: {extent} (range: {minSize} - {maxSize})", this);
            }
        }

        return isValid;
    }

    /// <summary>
    /// Gets the largest extent of the object by localScale (same as TeleportationOnStationary).
    /// </summary>
    private float GetLargestExtentWorld(GameObject obj)
    {
        if (obj == null)
        {
            if (debugLog)
            {
                Debug.LogWarning($"[SizeRestrictedSocketInteractor] GetLargestExtentWorld: obj == null", this);
            }
            return 0f;
        }

        if (obj.transform == null)
        {
            if (debugLog)
            {
                Debug.LogWarning($"[SizeRestrictedSocketInteractor] GetLargestExtentWorld: obj.transform == null for '{obj.name}'", this);
            }
            return 0f;
        }

        Vector3 local = obj.transform.localScale;
        float extent = Mathf.Max(local.x, local.y, local.z);

        if (debugLog)
        {
            Debug.Log($"[SizeRestrictedSocketInteractor] GetLargestExtentWorld for '{obj.name}': localScale = {local}, extent = {extent}", this);
        }

        return extent;
    }

    /// <summary>
    /// Called when an object enters hover state.
    /// </summary>
    protected override void OnHoverEntered(HoverEnterEventArgs args)
    {
        base.OnHoverEntered(args);

        if (useFixedHoverSize)
        {
            // Delay to allow base class to create hover mesh visual
            StartCoroutine(UpdateHoverMeshSizeCoroutine(args.interactableObject));
        }
    }

    /// <summary>
    /// Called when an object exits hover state.
    /// </summary>
    protected override void OnHoverExited(HoverExitEventArgs args)
    {
        // Clean up cached visual
        if (_hoverMeshVisuals.ContainsKey(args.interactableObject))
        {
            _hoverMeshVisuals.Remove(args.interactableObject);
        }

        base.OnHoverExited(args);
    }

    /// <summary>
    /// Coroutine to update hover mesh size after base class creates it.
    /// </summary>
    private System.Collections.IEnumerator UpdateHoverMeshSizeCoroutine(UnityEngine.XR.Interaction.Toolkit.Interactables.IXRHoverInteractable interactable)
    {
        // Wait a frame for base class to create hover mesh visual
        yield return null;

        // Try to find and update hover mesh visual
        UpdateHoverMeshSize(interactable);
    }

    /// <summary>
    /// Updates the size of hover mesh visual to fixed size.
    /// </summary>
    private void UpdateHoverMeshSize(UnityEngine.XR.Interaction.Toolkit.Interactables.IXRHoverInteractable interactable)
    {
        if (interactable == null || !useFixedHoverSize)
            return;

        // Try to find hover mesh visual in children
        // XR Socket Interactor creates hover mesh as a child object
        Transform hoverVisual = FindHoverMeshVisual(interactable);

        if (hoverVisual != null)
        {
            // Calculate scale factor to achieve fixed size
            GameObject obj = GetGameObjectFromHoverInteractable(interactable);
            if (obj != null)
            {
                float objectSize = GetLargestExtentWorld(obj);
                if (objectSize > 0.001f)
                {
                    float scaleFactor = fixedHoverSize / objectSize;
                    hoverVisual.localScale = Vector3.one * scaleFactor;

                    if (debugLog)
                    {
                        Debug.Log($"[SizeRestrictedSocketInteractor] Updated hover mesh size for '{obj.name}': objectSize={objectSize}, fixedSize={fixedHoverSize}, scaleFactor={scaleFactor}", this);
                    }
                }
            }

            _hoverMeshVisuals[interactable] = hoverVisual;
        }
        else if (debugLog)
        {
            Debug.LogWarning($"[SizeRestrictedSocketInteractor] Could not find hover mesh visual for {interactable}", this);
        }
    }

    /// <summary>
    /// Finds the hover mesh visual transform created by XR Socket Interactor.
    /// </summary>
    private Transform FindHoverMeshVisual(UnityEngine.XR.Interaction.Toolkit.Interactables.IXRHoverInteractable interactable)
    {
        // XR Socket Interactor creates hover mesh visuals as children of the socket
        // They are usually named with "Hover" or contain mesh renderer with hover material

        // Check cached visual first
        if (_hoverMeshVisuals.ContainsKey(interactable))
        {
            Transform cached = _hoverMeshVisuals[interactable];
            if (cached != null)
                return cached;
        }

        // Search in children for mesh renderer with hover material
        foreach (Transform child in transform)
        {
            if (child.name.Contains("Hover") || child.name.Contains("hover"))
            {
                Renderer renderer = child.GetComponent<Renderer>();
                if (renderer != null && renderer.enabled)
                {
                    return child;
                }
            }
        }

        // Alternative: search for any child with renderer that might be hover mesh
        foreach (Transform child in transform)
        {
            Renderer renderer = child.GetComponent<Renderer>();
            if (renderer != null && renderer.enabled && child.gameObject.activeInHierarchy)
            {
                // Check if it's likely a hover mesh (small, recently created, etc.)
                if (child.localScale.magnitude < 10f) // Reasonable size check
                {
                    return child;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Validates values in inspector.
    /// </summary>
    private new void OnValidate()
    {
        // Call base validation if it exists
        base.OnValidate();

        // Ensure minimum size is not greater than maximum
        if (minSize > maxSize)
            minSize = maxSize;

        // Ensure values are positive
        minSize = Mathf.Max(0.001f, minSize);
        maxSize = Mathf.Max(minSize, maxSize);

        // Ensure fixed hover size is positive
        fixedHoverSize = Mathf.Max(0.01f, fixedHoverSize);
    }
}

