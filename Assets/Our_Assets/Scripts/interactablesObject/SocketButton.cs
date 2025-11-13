using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>
/// "Кнопка" от XR Socket Interactor. Срабатывает только по XR Interaction Layer Mask.
/// Режимы:
/// - HoldWhileSeated: OnPressed при вставке, OnReleased при извлечении
/// - ToggleOnSeat: каждый раз при вставке переключает состояние (извлечение не влияет)
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(UnityEngine.XR.Interaction.Toolkit.Interactors.XRSocketInteractor))]
public class SocketButton : MonoBehaviour
{
    public enum Mode { HoldWhileSeated, ToggleOnSeat }

    [Header("Socket")]
    public UnityEngine.XR.Interaction.Toolkit.Interactors.XRSocketInteractor socket; // если null, возьмём с этого объекта

    [Header("Mode")] 
    public Mode mode = Mode.HoldWhileSeated;

    [Header("Filter")]
    [Tooltip("Требуемые XR Interaction Layers. 0 — игнорировать фильтр по interaction layers.")]
    public UnityEngine.XR.Interaction.Toolkit.InteractionLayerMask requiredInteractionLayers = 0;

    [Header("Events")] 
    public UnityEvent OnPressed;
    public UnityEvent OnReleased;

    private bool _isPressed;

    void Reset()
    {
        socket = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.XRSocketInteractor>();
    }

    void Awake()
    {
        if (socket == null) socket = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.XRSocketInteractor>();
    }

    void OnEnable()
    {
        if (socket != null)
        {
            socket.selectEntered.AddListener(OnSelectEntered);
            socket.selectExited.AddListener(OnSelectExited);
        }
    }

    void OnDisable()
    {
        if (socket != null)
        {
            socket.selectEntered.RemoveListener(OnSelectEntered);
            socket.selectExited.RemoveListener(OnSelectExited);
        }
    }

    private void OnSelectEntered(SelectEnterEventArgs args)
    {
        if (!IsAllowed(args)) return;

        if (mode == Mode.HoldWhileSeated)
        {
            if (_isPressed) return;
            _isPressed = true;
            OnPressed?.Invoke();
        }
        else // ToggleOnSeat
        {
            _isPressed = !_isPressed;
            if (_isPressed) OnPressed?.Invoke(); else OnReleased?.Invoke();
        }
    }

    private void OnSelectExited(SelectExitEventArgs args)
    {
        if (mode != Mode.HoldWhileSeated) return;
        if (!_isPressed) return;
        if (!IsAllowed(args)) return; // проверяем, что уезжает допустимый объект
        _isPressed = false;
        OnReleased?.Invoke();
    }

    private bool IsAllowed(BaseInteractionEventArgs args)
    {
        var ixr = args.interactableObject; // IXRSelectInteractable
        if (ixr == null) return false;

        if (requiredInteractionLayers != 0)
        {
            var layers = ixr.interactionLayers; // InteractionLayerMask
            if ((requiredInteractionLayers.value & layers.value) == 0)
                return false;
        }

        return true;
    }
}
