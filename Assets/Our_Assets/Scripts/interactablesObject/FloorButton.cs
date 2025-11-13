using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Простая напольная кнопка (pressure plate), срабатывающая от объектов.
/// Требуется Collider с включённым IsTrigger на этом же объекте.
/// 
/// Возможности:
/// - Фильтр по слоям активатора
/// - Режимы активации: любой коллайдер либо только Rigidbody с минимальной массой
/// - События OnPressed / OnReleased
/// - Опциональная визуальная анимация нажатия (смещение верхней части)
/// </summary>
[RequireComponent(typeof(Collider))]
public class FloorButton : MonoBehaviour
{
    public enum ActivationMode
    {
        AnyCollider,
        RequireRigidbodyMinMass
    }

    [Header("Activation")]
    [SerializeField] private LayerMask activatorLayers = ~0; // какие слои могут нажимать кнопку
    [SerializeField] private ActivationMode activationMode = ActivationMode.AnyCollider;
    [SerializeField, Min(0f)] private float minMass = 0.1f; // для режима RequireRigidbodyMinMass

    [Header("Events")] 
    public UnityEvent OnPressed;
    public UnityEvent OnReleased;

    [Header("Visuals (optional)")]
    [Tooltip("Деталь, которая будет визуально утапливаться при нажатии. Если не задано — визуальная анимация отключена.")]
    [SerializeField] private Transform buttonTop;
    [Tooltip("Локальный сдвиг при нажатии (например, (0, -0.02, 0))")] 
    [SerializeField] private Vector3 pressedLocalOffset = new Vector3(0f, -0.02f, 0f);
    [SerializeField, Min(0f)] private float pressLerpSpeed = 12f;

    private readonly HashSet<Collider> _contacts = new HashSet<Collider>();
    private bool _isPressed;
    private Vector3 _topInitialLocalPos;
    private Collider _ownTrigger;

    void Reset()
    {
        // Гарантируем IsTrigger на своём коллайдере
        var col = GetComponent<Collider>();
        if (col) col.isTrigger = true;
    }

    void Awake()
    {
        _ownTrigger = GetComponent<Collider>();
        if (_ownTrigger != null && !_ownTrigger.isTrigger)
            _ownTrigger.isTrigger = true;
        if (buttonTop != null)
            _topInitialLocalPos = buttonTop.localPosition;
    }

    void Update()
    {
        // Поддерживаем визуальную анимацию
        if (buttonTop != null)
        {
            var target = _topInitialLocalPos + (_isPressed ? pressedLocalOffset : Vector3.zero);
            buttonTop.localPosition = Vector3.Lerp(buttonTop.localPosition, target, 1f - Mathf.Exp(-pressLerpSpeed * Time.deltaTime));
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (!IsValidActivator(other)) return;
        _contacts.Add(other);
        RefreshState();
    }

    void OnTriggerExit(Collider other)
    {
        if (_contacts.Remove(other))
            RefreshState();
    }

    private bool IsValidActivator(Collider other)
    {
        if (((1 << other.gameObject.layer) & activatorLayers) == 0)
            return false;

        if (activationMode == ActivationMode.RequireRigidbodyMinMass)
        {
            var rb = other.attachedRigidbody;
            if (rb == null || rb.mass < minMass) return false;
        }

        // Игнорируем собственный триггер
        if (other == _ownTrigger) return false;
        return true;
    }

    private void RefreshState()
    {
        bool shouldBePressed = _contacts.Count > 0;
        if (shouldBePressed == _isPressed) return;
        _isPressed = shouldBePressed;
        if (_isPressed) OnPressed?.Invoke(); else OnReleased?.Invoke();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        var col = GetComponent<Collider>();
        if (col && !col.isTrigger) col.isTrigger = true;
        if (buttonTop != null && Application.isPlaying == false)
            _topInitialLocalPos = buttonTop.localPosition;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.1f, 0.9f, 0.1f, 0.25f);
        var c = GetComponent<Collider>() as BoxCollider;
        if (c != null)
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(c.center, c.size);
        }
    }
#endif
}
