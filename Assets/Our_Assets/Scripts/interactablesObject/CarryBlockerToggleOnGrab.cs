using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>
/// Centralized controller for a persistent doorway obstacle ("CarryBlocker").
///
/// Responsibilities:
/// - Ensures there is a collider (preferably BoxCollider) and, optionally, assigns the "Obstacle" layer.
/// - Decides when the blocker should be SOLID (isTrigger = false) vs a trigger (isTrigger = true).
/// - Listens to ForcedPerspectiveFromPickup global events to know when any scalable object is being held.
/// - Optionally also monitors XR Interactor selections as an additional holding signal.
/// - Exposes SetDoorOpen(bool) so doors can notify their open/closed state without duplicating logic.
///
/// Solid rules:
///   solid = (solidWhenDoorClosed && !doorOpen) || (solidWhileHolding && anyHolding)
///   isTrigger = !solid
///
/// Attach this component to the CarryBlocker object in the doorway.
/// Doors should call SetDoorOpen(IsOpen) when they finish opening/closing.
/// </summary>
[DisallowMultipleComponent]
public class CarryBlockerToggleOnGrab : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("Collider to toggle. If null, tries BoxCollider on this GameObject, otherwise any Collider, otherwise adds BoxCollider.")]
    [SerializeField] private Collider carryBlocker;

    [Header("Setup Helpers")]
    [Tooltip("If true and no BoxCollider present, add one and use it as the blocker.")]
    [SerializeField] private bool ensureBoxCollider = true;
    [Tooltip("If true, assign layer 'Obstacle' to this GameObject when available.")]
    [SerializeField] private bool setObstacleLayer = true;

    [Header("Door Integration")]
    [Tooltip("When the door is closed, make the blocker solid (isTrigger = false).")]
    [SerializeField] private bool solidWhenDoorClosed = true;

    [Header("Holding Detection")]
    [Tooltip("When any object is held (ForcedPerspectiveFromPickup), make the blocker solid.")]
    [SerializeField] private bool solidWhileHolding = true;
    [Tooltip("Subscribe to ForcedPerspectiveFromPickup.HoldingStarted/HoldingEnded events.")]
    [SerializeField] private bool useForcedPerspectiveEvents = true;
    [Tooltip("Also consider XR Interactor selections as 'holding'.")]
    [SerializeField] private bool alsoUseXRInteractorSelections = false;

    [Header("Interactors (optional)")]
    [Tooltip("Interactors to monitor. If empty and alsoUseXRInteractorSelections is true, auto-discovers all XRBaseInteractors in the scene (active and inactive).")]
    [SerializeField] private List<UnityEngine.XR.Interaction.Toolkit.Interactors.XRBaseInteractor> interactors = new List<UnityEngine.XR.Interaction.Toolkit.Interactors.XRBaseInteractor>();

    // State
    private bool doorOpen = false;
    private int heldCountFP = 0;             // ForcedPerspectiveFromPickup holding count
    private int activeSelections = 0;        // XR Interactor selections
    private bool originalIsTrigger = true;   // for restoration when disabled (optional)

    public void SetDoorOpen(bool isOpen)
    {
        doorOpen = isOpen;
        ApplyState();
    }

    private void Awake()
    {
        ResolveCarryBlockerCollider();
        if (carryBlocker == null)
        {
            Debug.LogError("CarryBlockerToggleOnGrab: No Collider found/assigned.");
            enabled = false;
            return;
        }
        originalIsTrigger = carryBlocker.isTrigger;
        MaybeAssignObstacleLayer();
    }

    private void OnEnable()
    {
        if (useForcedPerspectiveEvents)
        {
            ForcedPerspectiveFromPickup.HoldingStarted += OnHoldingStartedFP;
            ForcedPerspectiveFromPickup.HoldingEnded += OnHoldingEndedFP;
        }

        if (alsoUseXRInteractorSelections)
        {
            EnsureInteractors();
            SubscribeInteractors(true);
            RecountSelections();
        }

        ApplyState();
    }

    private void OnDisable()
    {
        if (useForcedPerspectiveEvents)
        {
            ForcedPerspectiveFromPickup.HoldingStarted -= OnHoldingStartedFP;
            ForcedPerspectiveFromPickup.HoldingEnded -= OnHoldingEndedFP;
        }

        if (alsoUseXRInteractorSelections)
        {
            SubscribeInteractors(false);
        }

        // Restore original if desired
        if (carryBlocker != null)
            carryBlocker.isTrigger = originalIsTrigger;
    }

    private void OnValidate()
    {
        if (!Application.isPlaying)
        {
            ResolveCarryBlockerCollider();
            MaybeAssignObstacleLayer();
        }
        ApplyState();
    }

    private void ResolveCarryBlockerCollider()
    {
        if (carryBlocker == null)
        {
            // Prefer BoxCollider
            var box = GetComponent<BoxCollider>();
            if (box == null)
            {
                // fallback to any collider
                carryBlocker = GetComponent<Collider>();
                if (ensureBoxCollider)
                {
                    // If no box exists, add one and prefer it
                    box = GetComponent<BoxCollider>();
                    if (box == null)
                        box = gameObject.AddComponent<BoxCollider>();
                }
            }
            if (box != null)
                carryBlocker = box;
        }
        else if (!(carryBlocker is BoxCollider) && ensureBoxCollider)
        {
            // Keep existing collider but also add a BoxCollider that we control
            var box = GetComponent<BoxCollider>();
            if (box == null)
                box = gameObject.AddComponent<BoxCollider>();
            carryBlocker = box;
        }

        if (carryBlocker != null)
            carryBlocker.enabled = true;
    }

    private void MaybeAssignObstacleLayer()
    {
        if (!setObstacleLayer) return;
        int obstacleLayer = LayerMask.NameToLayer("Obstacle");
        if (obstacleLayer != -1)
            gameObject.layer = obstacleLayer;
    }

    private void EnsureInteractors()
    {
        if (interactors == null)
            interactors = new List<UnityEngine.XR.Interaction.Toolkit.Interactors.XRBaseInteractor>();
        if (interactors.Count == 0)
        {
            var found = FindObjectsOfType<UnityEngine.XR.Interaction.Toolkit.Interactors.XRBaseInteractor>(includeInactive: true);
            foreach (var it in found)
                if (it != null && !interactors.Contains(it))
                    interactors.Add(it);
        }
    }

    private void SubscribeInteractors(bool add)
    {
        if (interactors == null) return;
        foreach (var it in interactors)
        {
            if (it == null) continue;
            if (add)
            {
                it.selectEntered.AddListener(OnSelectEntered);
                it.selectExited.AddListener(OnSelectExited);
            }
            else
            {
                it.selectEntered.RemoveListener(OnSelectEntered);
                it.selectExited.RemoveListener(OnSelectExited);
            }
        }
    }

    private void RecountSelections()
    {
        activeSelections = 0;
        if (interactors == null) return;
        foreach (var it in interactors)
            if (it != null && it.hasSelection)
                activeSelections++;
    }

    private void OnSelectEntered(SelectEnterEventArgs args)
    {
        activeSelections = Mathf.Max(0, activeSelections + 1);
        ApplyState();
    }

    private void OnSelectExited(SelectExitEventArgs args)
    {
        activeSelections = Mathf.Max(0, activeSelections - 1);
        ApplyState();
    }

    private void OnHoldingStartedFP(ForcedPerspectiveFromPickup _)
    {
        heldCountFP++;
        ApplyState();
    }

    private void OnHoldingEndedFP(ForcedPerspectiveFromPickup _)
    {
        heldCountFP = Mathf.Max(0, heldCountFP - 1);
        ApplyState();
    }

    private void ApplyState()
    {
        if (carryBlocker == null) return;

        bool anyHolding = (useForcedPerspectiveEvents && heldCountFP > 0)
                        || (alsoUseXRInteractorSelections && activeSelections > 0);

        bool solid = (solidWhenDoorClosed && !doorOpen)
                  || (solidWhileHolding && anyHolding);

        bool shouldBeTrigger = !solid;
        if (carryBlocker.isTrigger != shouldBeTrigger)
            carryBlocker.isTrigger = shouldBeTrigger;

        if (!carryBlocker.enabled)
            carryBlocker.enabled = true;
    }
}
