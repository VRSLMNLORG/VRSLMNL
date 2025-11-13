using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Forced‑Perspective удержание объекта с «силуэтной сеткой» и управлением через Input System.
/// Раздроблено на частичные классы для улучшения читаемости и сопровождения.
/// Части:
/// - ForcedPerspectiveFromPickup.Core (этот файл): события, поля, жизненный цикл, Start/Release, корутины
/// - ForcedPerspectiveFromPickup.InputAndUpdate: кэш взгляда, Update, OnBeforeRenderCallback
/// - ForcedPerspectiveFromPickup.Orientation: расчёт ориентации и сглаживание
/// - ForcedPerspectiveFromPickup.Grid: построение «силуэтной» сетки
/// - ForcedPerspectiveFromPickup.PlacementAndScale: позиционирование у поверхности и поддержание масштаба/якоря
/// </summary> 
[RequireComponent(typeof(Collider))]
[DisallowMultipleComponent]
public partial class ForcedPerspectiveFromPickup : MonoBehaviour
{
    // Global events to notify external systems (e.g., camera UI) about holding state changes
    public static event System.Action<ForcedPerspectiveFromPickup> HoldingStarted;
    public static event System.Action<ForcedPerspectiveFromPickup> HoldingEnded;

    [Header("References")]
    [SerializeField] private Transform _cameraTransform; // если не задано, берётся Camera.main

    [Header("Gaze Control (Input System)")]
    [Tooltip("Действие удержания (левая рука)")]
    public InputActionReference holdActionLeft;
    [Tooltip("Действие удержания (правая рука)")]
    public InputActionReference holdActionRight;
    [Tooltip("Макс. дистанция взгляда для наведения на объект")]
    [Min(0.1f)]
    public float gazeMaxDistance = 20f;

    [Header("Collision / Walls")]
    [Tooltip("Слои препятствий (стены/сцена), перед которыми должен останавливаться объект")]
    [SerializeField] private LayerMask _wallLayers = ~0; // по умолчанию всё


    [Header("Silhouette Grid")]
    [SerializeField] private int NUMBER_OF_GRID_ROWS = 10;
    [SerializeField] private int NUMBER_OF_GRID_COLUMNS = 10;
    [Tooltip("Использовать полярную (круг/эллипс) сетку вместо прямоугольной")]
    [SerializeField] private bool usePolarGrid = true;
    [SerializeField, Range(3, 32)] private int polarRings = 8;
    [SerializeField, Range(8, 64)] private int polarSectors = 24;
    [Tooltip("Смещение плотности к краям ( >1 — больше точек у контура)")]
    [SerializeField, Range(0.25f, 2f)] private float edgeBias = 1.2f;
    private const float SCALE_MARGIN = .0001f;
    private bool _smoothingActive = true;

    // ---- Global synchronization to avoid multi-object grab races ----
    internal static int s_gazeFrame = -1;                 // frame index of cached gaze raycast
    internal static Collider s_cachedTopHit = null;        // top-most collider seen by camera this frame
    internal static ForcedPerspectiveFromPickup s_current; // current exclusive holder

    [Header("Orientation")]
    [SerializeField] private bool keepUpright = true;
    [SerializeField] private bool followCameraYaw = true;
    [SerializeField, Tooltip("Скорость следования по рысканью (0 = мгновенно)"), Range(0f, 30f)] private float yawFollowSpeed = 0f;

    [Header("Centering on Start")]
    [Tooltip("Выполнить плавную отцентровку в момент начала захвата")]
    [SerializeField] private bool centerOnPickup = true;
    [SerializeField, Tooltip("Длительность анимации отцентровки, сек"), Range(0f, 3f)] private float centeringDuration = 0.35f;
    [SerializeField, Tooltip("Целевая viewport‑позиция (0..1) по X/Y")] private Vector2 targetViewportOnPickup = new Vector2(0.5f, 0.5f);
    [SerializeField] private AnimationCurve centeringEase = AnimationCurve.EaseInOut(0, 0, 1, 1);

    private BoxCollider _proxyCollider;
    private Vector3 _proxySize;
    private Vector3 _originalLocalScale;

    [Header("Proxy Settings")]
    [SerializeField, Min(0f)] private float proxyPadding = 0.02f; // относительный запас к размерам прокси

    // Состояние удержания
    public bool isHeld { get; private set; }
    private Transform _originalParent;
    private Rigidbody _rb;
    private Collider[] _selfColliders;

    // Ориентация (upright)
    private Quaternion _uprightYawOffset = Quaternion.identity;
    private Quaternion _fixedUprightYaw = Quaternion.identity;
    private Vector3 _lastFlatFwd = Vector3.forward;

    // Камерное локальное пространство, 4 крайние точки прямоугольника проекции объекта
    private Vector3 _left;
    private Vector3 _right;
    private Vector3 _top;
    private Vector3 _bottom;

    // Отношение исходной дистанции к масштабу и текущая позиция-якорь в viewport
    private float _orgDistanceToScaleRatio;
    private Vector3 _orgViewportPos;

    // Сетка в локальном пространстве камеры
    private readonly List<Vector3> _shapedGrid = new List<Vector3>();

    // Плавная отцентровка
    private bool _centeringActive = false;

    // Служебное
    bool _isUnderGaze;
    bool _bothPressedPrevFrame; // edge detection for simultaneous two-hand press
    readonly System.Collections.Generic.List<(Collider col, int originalLayer)> _savedColliderLayers = new();

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        if (_rb == null) _rb = gameObject.AddComponent<Rigidbody>();
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        _selfColliders = GetComponentsInChildren<Collider>(true);
    }

    void OnEnable()
    {
        holdActionLeft?.action?.Enable();
        holdActionRight?.action?.Enable();
        Application.onBeforeRender += OnBeforeRenderCallback;
    }

    void OnDisable()
    {
        holdActionLeft?.action?.Disable();
        holdActionRight?.action?.Disable();
        Application.onBeforeRender -= OnBeforeRenderCallback;
    }

    void CacheCamera()
    {
        if (_cameraTransform == null)
        {
            var cam = Camera.main;
            if (cam != null) _cameraTransform = cam.transform;
        }
    }

    public void StartHolding()
    {
        if (isHeld) return;
        CacheCamera();
        if (_cameraTransform == null) return;

        // Блокируем старт, если нет прямой видимости (между камерой и объектом есть препятствие/проём)
        if (!HasLineOfSightToSelf()) return;

        if (s_current != null && s_current != this) return; // эксклюзивная блокировка
        s_current = this;

        // Проверка равномерности масштаба
        float s = transform.localScale.x;
        if (Mathf.Abs(s - transform.localScale.y) > SCALE_MARGIN || Mathf.Abs(s - transform.localScale.z) > SCALE_MARGIN)
            throw new System.Exception("Wrong Pickupable object's scale! Use uniform scale.");

        _originalParent = transform.parent;
        isHeld = true;

        // Notify listeners
        try { HoldingStarted?.Invoke(this); } catch { }

        _smoothingActive = true;
        StartCoroutine(SmoothStartCoroutine());

        // Rigidbody в режим удержания
        if (_rb != null)
        {
            _rb.useGravity = false;
            _rb.isKinematic = true;
            _rb.constraints = RigidbodyConstraints.FreezeAll;
        }

        // Скрыть коллайдеры от Physics.Raycast, чтобы не мешали внешним лучам
        _savedColliderLayers.Clear();
        foreach (var c in _selfColliders)
        {
            if (c == null) continue;
            _savedColliderLayers.Add((c, c.gameObject.layer));
            c.gameObject.layer = 2; // Ignore Raycast (raycasts), физические столкновения мы отключим иначе
        }

        // Полностью отключаем участие Rigidbody в столкновениях на время удержания
        if (_rb != null) _rb.detectCollisions = false;

        // Отношение дистанции к масштабу для дальнейшего авто‑масштабирования
        _orgDistanceToScaleRatio = (_cameraTransform.position - transform.position).magnitude / s;
        _orgViewportPos = Camera.main != null ? Camera.main.WorldToViewportPoint(transform.position) : new Vector3(0.5f, 0.5f, 0f);

        // Делаем дочерним камеры (удобно для лок. системы камеры)
        transform.SetParent(_cameraTransform, true);

        _originalLocalScale = transform.localScale;

        // --- Создание/обновление ProxyCollider на основе КОМПОЗИТНЫХ ЛОКАЛЬНЫХ границ (не мировых AABB) ---
        Bounds compositeLocal = CalculateCompositeLocalBounds(transform);
        if (compositeLocal.size.sqrMagnitude > 0f)
        {
            // Создаем при первом удержании или переиспользуем существующий
            if (_proxyCollider == null || _proxyCollider.gameObject.name != "ProxyCollider")
            {
                GameObject proxyObj = new GameObject("ProxyCollider");
                proxyObj.transform.SetParent(transform, false);
                proxyObj.layer = LayerMask.NameToLayer("Ignore Raycast");
                _proxyCollider = proxyObj.AddComponent<BoxCollider>();
                _proxyCollider.isTrigger = true;
            }

            // Центр и размер в ЛОКАЛЕ корневого объекта, с небольшим запасом
            _proxyCollider.center = compositeLocal.center;
            _proxyCollider.size = compositeLocal.size * (1f + proxyPadding);
            _proxyCollider.transform.localPosition = Vector3.zero;
            _proxyCollider.transform.localRotation = Quaternion.identity;
            _proxyCollider.transform.localScale = Vector3.one;
        }

        // Ориентация: зафиксировать вертикаль и вычислить смещение по рысканью
        if (keepUpright)
        {
            Vector3 camFlat = Vector3.ProjectOnPlane(_cameraTransform.forward, Vector3.up);
            if (camFlat.sqrMagnitude < 1e-6f) camFlat = (_lastFlatFwd.sqrMagnitude > 0f ? _lastFlatFwd : Vector3.forward);
            _lastFlatFwd = camFlat.normalized;

            Vector3 objFlat = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (objFlat.sqrMagnitude < 1e-6f) objFlat = _lastFlatFwd;

            Quaternion camYaw = Quaternion.LookRotation(_lastFlatFwd, Vector3.up);
            Quaternion objYaw = Quaternion.LookRotation(objFlat.normalized, Vector3.up);

            _fixedUprightYaw = objYaw;
            _uprightYawOffset = Quaternion.Inverse(camYaw) * objYaw;
        }

        // Однократная плавная отцентровка по экрану
        if (centerOnPickup)
            StartCoroutine(CenterViewportCoroutine());

        // Построение силуэтной сетки
        Vector3[] bbPoints = GetBoundingBoxPoints();
        SetupShapedGrid(bbPoints);
    }

    public void ReleaseHolding()
    {
        if (!isHeld) return;
        isHeld = false;
        _centeringActive = false;
        if (s_current == this) s_current = null; // снять глобальную блокировку

        // Notify listeners
        try { HoldingEnded?.Invoke(this); } catch { }

        transform.SetParent(_originalParent, true);

        if (_rb != null)
        {
            _rb.constraints = RigidbodyConstraints.None;
            _rb.isKinematic = false;
            _rb.useGravity = true;
        }
        // Удаляем временный ProxyCollider
        if (_proxyCollider != null)
        {
            Destroy(_proxyCollider.gameObject);
            _proxyCollider = null;
        }

        // Вернуть слои коллайдерам
        foreach (var pair in _savedColliderLayers)
        {
            if (pair.col != null) pair.col.gameObject.layer = pair.originalLayer;
        }
        _savedColliderLayers.Clear();

        // Восстановить детекцию столкновений у Rigidbody
        if (_rb != null) _rb.detectCollisions = true;
    }

    private IEnumerator SmoothStartCoroutine(float duration = 0.2f)
    {
        float timer = 0f;
        while (timer < duration)
        {
            timer += Time.deltaTime;
            yield return null;
        }
        _smoothingActive = false; // отключаем сглаживание после плавного входа
    }

    private IEnumerator CenterViewportCoroutine()
    {
        _centeringActive = true;

        Vector2 startViewport = new Vector2(_orgViewportPos.x, _orgViewportPos.y);
        float t = 0f;
        float dur = Mathf.Max(0f, centeringDuration);

        if (dur <= 0f)
        {
            // Мгновенно
            _orgViewportPos = targetViewportOnPickup;
            _centeringActive = false;
            yield break;
        }

        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            float e = centeringEase != null ? centeringEase.Evaluate(k) : k;

            // X/Y центрирование: только меняем якорь, перемещения выполнит OnBeforeRenderCallback
            _orgViewportPos = Vector2.Lerp(startViewport, targetViewportOnPickup, e);
            yield return null;
        }

        // Финальная фиксация
        _orgViewportPos = targetViewportOnPickup;
        _centeringActive = false;
    }

    // Public debug accessors
    public System.Collections.Generic.IReadOnlyList<Vector3> ShapedGridCameraLocal => _shapedGrid;
    public Transform CameraTransform => _cameraTransform;
    public bool UsePolarGrid => usePolarGrid;
    public int PolarRings => polarRings;
    public int PolarSectors => polarSectors;
    public float EdgeBias => edgeBias;
    public Vector2 CurrentViewportAnchor => _orgViewportPos;
    public bool CenterOnPickupEnabled => centerOnPickup;

    // Проверка прямой видимости между камерой и текущим объектом, с учётом триггеров и «Obstacle»
    private bool HasLineOfSightToSelf()
    {
        var rend = GetComponentInChildren<Renderer>();
        if (rend == null)
            return true;

        Vector3 origin = (_cameraTransform != null) ? _cameraTransform.position : transform.position + Vector3.forward * 0.001f;
        Vector3 target = rend.bounds.center;
        Vector3 dir = target - origin;
        float dist = dir.magnitude;
        if (dist <= 1e-5f)
            return true;
        dir /= dist;

        // Берём ВСЕ попадания (включая триггеры), сортируем по дистанции и проверяем порядок
        var hits = Physics.RaycastAll(origin, dir, dist, ~0, QueryTriggerInteraction.Collide);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var h in hits)
        {
            if (h.distance < 0.001f) continue; // пропуск точки старта

            if (IsSelfCollider(h.collider))
            {
                // Наши коллайдеры встретились раньше препятствий — чистая видимость
                return true;
            }

            if (IsObstacleCollider(h.collider))
            {
                // Нашли препятствие (включая триггерные стены/проёмы) раньше, чем себя — блокируем
                return false;
            }

            // Иные посторонние коллайдеры (не «Obstacle» и не self) не считаем препятствием
            // и продолжаем искать дальше по лучу
        }

        // Не нашли ни self, ни препятствий — считаем, что видимость есть
        return true;
    }

    // Возвращает true, если коллайдер принадлежит этому объекту
    private bool IsSelfCollider(Collider c)
    {
        if (c == null) return false;
        for (int i = 0; i < _selfColliders.Length; i++)
        {
            var sc = _selfColliders[i];
            if (sc != null && sc == c) return true;
        }
        return false;
    }

    // Возвращает true, если коллайдер относится к препятствиям (по слою)
    private bool IsObstacleCollider(Collider c)
    {
        if (c == null) return false;
        int layerBit = 1 << c.gameObject.layer;
        return (_wallLayers.value & layerBit) != 0;
    }
}
