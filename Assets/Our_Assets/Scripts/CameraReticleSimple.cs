using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Camera-centered reticle with simple customization and optional object highlight.
/// Modes for drawing the reticle:
/// - Generated: ring drawn in OnGUI (no assets)
/// - CustomTexture: user-provided Texture2D drawn at screen center
///
/// Visibility: Always / OnHover / WhileActionPressed.
/// Optional: show while pressed without a target (dimmed), and suppress right after pickup starts.
/// Optional highlight of the hovered object (SwapMaterial or Emission), with suppression to avoid flashing on pickup.
/// Attach to Main Camera.
/// </summary>
[DisallowMultipleComponent]
public class CameraReticleSimple : MonoBehaviour
{
    public enum Visibility { Always, OnHover, WhileActionPressed }
    public enum RenderMode { Generated, CustomTexture }
    public enum HighlightMode { SwapMaterial, Emission }
    public enum DisplayMode { ScreenSpace, VR3D }

    [Header("Detection")]
    [Tooltip("Max distance for the gaze ray")][Min(0.1f)] public float gazeMaxDistance = 20f;
    [Tooltip("Physics layers considered for the gaze ray (default: everything)")] public LayerMask raycastMask = ~0;
    [Tooltip("Only consider colliders belonging to an object with ForcedPerspectiveFromPickup")] public bool requirePickupComponent = true;

    [Header("Obstacles")]
    [Tooltip("Layers that block gaze even if the collider is a trigger")] public LayerMask obstacleLayers = ~0;


    [Header("Input (for WhileActionPressed)")]
    public InputActionReference holdActionLeft;
    public InputActionReference holdActionRight;

    [Header("Render")]
    public RenderMode renderMode = RenderMode.Generated;
    [Tooltip("ScreenSpace = OnGUI (desktop), VR3D = 3D Quad in front of camera (VR)")]
    public DisplayMode displayMode = DisplayMode.ScreenSpace;
    [Tooltip("Distance from camera for VR3D mode (meters)")]
    [Min(0.1f)] public float vrDistance = 2f;
    [Tooltip("Size of reticle in VR3D mode (meters)")]
    [Min(0.001f)] public float vrSize = 0.01f;

    [Header("Generated Ring")]
    [Min(8)] public int sizePx = 32;
    [Min(1)] public int thicknessPx = 3;
    [Min(0f)] public float featherPx = 1.5f;
    public Color color = new Color(0.6226415f, 0.6226415f, 0.6226415f);

    [Header("Custom Texture")]
    [Tooltip("Texture to draw at screen center when RenderMode = CustomTexture")] public Texture2D customTexture;
    public Color customTextureTint = Color.white;
    [Min(8)] public int customTextureSizePx = 48;

    [Header("Highlight (Hovered Object)")]
    public bool enableHighlight = true;
    [Tooltip("Tie highlight to reticle visibility; otherwise highlight strictly on hover")]
    public bool syncHighlightToReticle = true;
    [Tooltip("When syncing to reticle, also require gaze hit to enable highlight")]
    public bool highlightRequireHoverWhenSynced = true;
    public HighlightMode highlightMode = HighlightMode.SwapMaterial;
    [Tooltip("Used when HighlightMode = SwapMaterial")] public Material glowMaterial;
    [Tooltip("Used when HighlightMode = Emission")] public Color emissionColor = new(0.6226415f, 0.6226415f, 0.6226415f);
    [Tooltip("Used when HighlightMode = Emission")][Min(0f)] public float emissionIntensity = 1.5f;

    [Header("Behavior")]
    public bool enabledReticle = true;
    public Visibility visibility = Visibility.WhileActionPressed;
    [Tooltip("Show reticle while action is pressed even if no target under gaze")] public bool showRingWithoutTarget = true;
    [Range(0f, 1f)] public float noTargetAlpha = 0.5f;
    [Tooltip("Temporarily suppress reticle/highlight after pickup starts to avoid flash")] public bool suppressOnPickup = true;
    [Min(0f)] public float pickupSuppressSeconds = 0.25f;

    // runtime
    Camera _cam;
    GameObject _currentTarget;
    ForcedPerspectiveFromPickup _currentPickup;

    // suppression flags
    float _suppressUntilUnscaled = 0f;
    bool _forceHideUntilRelease = false;
    static readonly System.Collections.Generic.HashSet<ForcedPerspectiveFromPickup> s_suppressedPickups = new();

    // ring cache (Generated)
    Texture2D _ringTex;
    int _ringTexSize;
    int _ringTexThick;
    float _ringTexFeather;
    Color _ringTexColor;

    // highlight cache
    readonly Dictionary<Renderer, Material[]> _originalMats = new();
    Renderer[] _currentRenderers = System.Array.Empty<Renderer>();

    // VR 3D reticle
    GameObject _vrReticleObject;
    MeshRenderer _vrReticleRenderer;
    Material _vrReticleMaterial;

    void OnEnable()
    {
        _cam = Camera.main;
        holdActionLeft?.action?.Enable();
        holdActionRight?.action?.Enable();
        ForcedPerspectiveFromPickup.HoldingStarted += OnPickupStarted;
        ForcedPerspectiveFromPickup.HoldingEnded += OnPickupEnded;
    }

    void OnDisable()
    {
        holdActionLeft?.action?.Disable();
        holdActionRight?.action?.Disable();
        ForcedPerspectiveFromPickup.HoldingStarted -= OnPickupStarted;
        ForcedPerspectiveFromPickup.HoldingEnded -= OnPickupEnded;
        DestroyRing();
        DestroyVRReticle();
        ClearHighlight();
    }

    void Update()
    {
        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return;

        UpdateHoverTarget();

        // Suppress window check
        bool inSuppress = suppressOnPickup && Time.unscaledTime < _suppressUntilUnscaled;

        // Highlight control
        if (enableHighlight && !inSuppress && !_forceHideUntilRelease)
        {
            if (syncHighlightToReticle)
            {
                bool reticleShow = IsReticleVisible();
                bool gatedHover = !highlightRequireHoverWhenSynced || _currentTarget != null;
                SetHighlight(reticleShow && gatedHover);
            }
            else
            {
                // Highlight purely on hover, but not while the object is held
                bool held = IsCurrentHeld();
                SetHighlight(_currentTarget != null && !held);
            }
        }
        else
        {
            SetHighlight(false);
        }

        // Update VR 3D reticle
        if (displayMode == DisplayMode.VR3D)
        {
            UpdateVRReticle();
        }
    }

    void UpdateHoverTarget()
    {
        GameObject newTarget = null;
        ForcedPerspectiveFromPickup newPickup = null;

        // ���������� ����� ��� ��������� �� ����� � ������������; ��������� ��������
        int mask = raycastMask | obstacleLayers;
        var hits = Physics.RaycastAll(_cam.transform.position, _cam.transform.forward, gazeMaxDistance, mask, QueryTriggerInteraction.Collide);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var h in hits)
        {
            var col = h.collider;
            if (col == null) continue;

            // Сначала проверяем ForcedPerspective (он может быть на "Obstacle" слое, но мы хотим иметь возможность пропустить его)
            var pickup = col.GetComponentInParent<ForcedPerspectiveFromPickup>();
            if (pickup != null)
            {
                if (IsPickupSuppressed(pickup))
                    continue; // пропускаем заблокированный объект и продолжаем искать дальше по лучу

                newPickup = pickup;
                newTarget = pickup.gameObject;
                break;
            }

            // ���� ������ ����������� ����������� � ������ �� ��������
            if (IsObstacleCollider(col))
            {
                newTarget = null;
                newPickup = null;
                break;
            }

            // ���� ��������� ����� ����� ������ � ���� ������ ���������� ������
            if (!requirePickupComponent)
            {
                newTarget = col.transform.root.gameObject;
                newPickup = null;
                break;
            }
        }

        if (newTarget == _currentTarget) return;

        // Switch highlight target
        ClearHighlight();
        _currentTarget = newTarget;
        _currentPickup = newPickup;
        if (_currentTarget != null)
        {
            _currentRenderers = _currentTarget.GetComponentsInChildren<Renderer>(true);
            foreach (var r in _currentRenderers)
            {
                if (r == null) continue;
                if (!_originalMats.ContainsKey(r))
                    _originalMats[r] = r.sharedMaterials;
            }
        }
        else
        {
            _currentRenderers = System.Array.Empty<Renderer>();
        }
    }

    bool IsObstacleCollider(Collider c)
    {
        if (c == null) return false;
        int layerBit = 1 << c.gameObject.layer;
        return (obstacleLayers.value & layerBit) != 0;
    }

    void OnGUI()
    {
        // OnGUI only works in ScreenSpace mode
        if (displayMode != DisplayMode.ScreenSpace) return;
        if (!enabledReticle) return;
        if (!IsReticleVisible()) return;

        // Select source
        Texture2D tex = null;
        int size = 0;
        Color tint = Color.white;

        if (renderMode == RenderMode.CustomTexture)
        {
            tex = customTexture;
            size = Mathf.Max(8, customTextureSizePx);
            tint = customTextureTint;
            if (tex == null) return;
        }
        else // Generated
        {
            int drawThickness = Mathf.Max(1, thicknessPx);
            EnsureRingTexture(drawThickness);
            if (_ringTex == null) return;
            tex = _ringTex;
            size = Mathf.Max(8, sizePx);
            tint = color;
        }

        var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        // Dim when no target and allowed to show
        Color prev = GUI.color;
        float a = tint.a;
        if (visibility == Visibility.WhileActionPressed && showRingWithoutTarget && _currentTarget == null)
            a *= Mathf.Clamp01(noTargetAlpha);
        GUI.color = new Color(tint.r, tint.g, tint.b, a);

        GUI.DrawTexture(new Rect(center.x - size * 0.5f, center.y - size * 0.5f, size, size), tex);
        GUI.color = prev;
    }

    bool IsReticleVisible()
    {
        if (!enabledReticle) return false;
        if (suppressOnPickup && Time.unscaledTime < _suppressUntilUnscaled) return false;
        if (_forceHideUntilRelease) return false;

        switch (visibility)
        {
            case Visibility.Always:
                return true;
            case Visibility.OnHover:
                return _currentTarget != null && !IsCurrentHeld();
            case Visibility.WhileActionPressed:
                return IsAnyHoldPressed() && (showRingWithoutTarget || _currentTarget != null) && !IsCurrentHeld();
        }
        return false;
    }

    bool IsCurrentHeld() => _currentPickup != null && _currentPickup.isHeld;

    bool IsAnyHoldPressed()
    {
        bool left = holdActionLeft != null && holdActionLeft.action != null && holdActionLeft.action.IsPressed();
        bool right = holdActionRight != null && holdActionRight.action != null && holdActionRight.action.IsPressed();
        return left || right;
    }

    void OnPickupStarted(ForcedPerspectiveFromPickup fp)
    {
        if (suppressOnPickup)
            _suppressUntilUnscaled = Time.unscaledTime + Mathf.Max(0f, pickupSuppressSeconds);
        _forceHideUntilRelease = true;

        // Immediately clear highlight and ensure emission disabled on the picked object
        if (fp != null)
        {
            DisableEmissionOnObject(fp.gameObject);
            if (_currentPickup == fp || (_currentTarget != null && _currentTarget == fp.gameObject))
                ClearHighlight();
        }
    }

    void OnPickupEnded(ForcedPerspectiveFromPickup fp)
    {
        _forceHideUntilRelease = false;
    }

    // Highlight impl
    void SetHighlight(bool enabled)
    {
        if (!enableHighlight) return;
        if (_currentTarget == null || _currentRenderers == null) { ClearHighlight(); return; }

        if (highlightMode == HighlightMode.SwapMaterial)
        {
            if (glowMaterial == null) return;
            foreach (var r in _currentRenderers)
            {
                if (r == null) continue;
                int count = r.sharedMaterials != null ? r.sharedMaterials.Length : 1;
                var mats = new Material[count];
                for (int i = 0; i < count; i++) mats[i] = enabled ? glowMaterial : (_originalMats.TryGetValue(r, out var orig) ? orig[Mathf.Clamp(i, 0, orig.Length - 1)] : r.sharedMaterial);
                r.materials = mats; // instance-level assignment
            }
        }
        else // Emission
        {
            Color final = enabled ? emissionColor * Mathf.LinearToGammaSpace(emissionIntensity) : Color.black;
            foreach (var r in _currentRenderers)
            {
                if (r == null) continue;
                var mats = r.materials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (enabled) m.EnableKeyword("_EMISSION"); else m.DisableKeyword("_EMISSION");
                    if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", final);
                }
            }
        }
    }

    void ClearHighlight()
    {
        if (_currentRenderers != null)
        {
            foreach (var r in _currentRenderers)
            {
                if (r == null) continue;
                // Ensure emission is fully disabled when clearing (even if not restoring mats)
                var mats = r.materials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;
                    if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.black);
                    m.DisableKeyword("_EMISSION");
                }
                if (_originalMats.TryGetValue(r, out var original))
                {
                    r.materials = (Material[])original.Clone();
                }
            }
        }
        _currentRenderers = System.Array.Empty<Renderer>();
    }

    static void DisableEmissionOnObject(GameObject go)
    {
        if (go == null) return;
        var rends = go.GetComponentsInChildren<Renderer>(true);
        foreach (var r in rends)
        {
            if (r == null) continue;
            var mats = r.materials;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null) continue;
                if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.black);
                m.DisableKeyword("_EMISSION");
            }
        }
    }

    // Generated ring
    void EnsureRingTexture(int thickness)
    {
        int size = Mathf.Max(8, sizePx);
        float feather = Mathf.Max(0f, featherPx);
        if (_ringTex != null && _ringTexSize == size && _ringTexThick == thickness &&
            Mathf.Approximately(_ringTexFeather, feather) && ColorsEqual(_ringTexColor, color))
            return;

        DestroyRing();

        _ringTex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        float cx = (size - 1) * 0.5f;
        float cy = (size - 1) * 0.5f;
        float radius = Mathf.Min(cx, cy) - 0.5f;
        float halfT = thickness * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - cx;
                float dy = y - cy;
                float d = Mathf.Sqrt(dx * dx + dy * dy);

                float a = BandAlpha(d, radius, halfT, feather);
                if (a <= 0f)
                {
                    _ringTex.SetPixel(x, y, new Color(0, 0, 0, 0));
                }
                else
                {
                    Color col = color;
                    col.a *= a;
                    _ringTex.SetPixel(x, y, col);
                }
            }
        }
        _ringTex.Apply();

        _ringTexSize = size;
        _ringTexThick = thickness;
        _ringTexFeather = feather;
        _ringTexColor = color;
    }

    static float BandAlpha(float dist, float bandRadius, float halfThickness, float feather)
    {
        float dd = Mathf.Abs(dist - bandRadius) - halfThickness;
        float a = 1f - Mathf.Clamp01(dd / Mathf.Max(0.0001f, feather));
        return Mathf.Clamp01(a);
    }

    static bool ColorsEqual(Color a, Color b)
    {
        return Mathf.Approximately(a.r, b.r) && Mathf.Approximately(a.g, b.g) && Mathf.Approximately(a.b, b.b) && Mathf.Approximately(a.a, b.a);
    }

    void DestroyRing()
    {
        if (_ringTex != null)
        {
            Destroy(_ringTex);
            _ringTex = null;
        }
    }

    // VR 3D Reticle
    void EnsureVRReticle()
    {
        if (_vrReticleObject != null) return;
        if (_cam == null) return;

        // Create Quad GameObject
        _vrReticleObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
        _vrReticleObject.name = "VRReticle";
        _vrReticleObject.transform.SetParent(_cam.transform, false);
        _vrReticleObject.transform.localPosition = Vector3.forward * vrDistance;
        _vrReticleObject.transform.localRotation = Quaternion.identity;
        _vrReticleObject.transform.localScale = Vector3.one * vrSize;

        // Remove collider (not needed for reticle)
        var col = _vrReticleObject.GetComponent<Collider>();
        if (col != null) Destroy(col);

        // Get renderer
        _vrReticleRenderer = _vrReticleObject.GetComponent<MeshRenderer>();
        if (_vrReticleRenderer == null) return;

        // Create material with standard shader (works in all Unity versions)
        Shader shader = Shader.Find("Unlit/Transparent");
        if (shader == null) shader = Shader.Find("Sprites/Default"); // Fallback
        if (shader == null) shader = Shader.Find("Standard"); // Last resort

        _vrReticleMaterial = new Material(shader);

        // Configure for transparency if using Unlit/Transparent
        if (shader.name.Contains("Transparent") || shader.name.Contains("Unlit"))
        {
            _vrReticleMaterial.SetFloat("_Mode", 3); // Transparent mode
            _vrReticleMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _vrReticleMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _vrReticleMaterial.SetInt("_ZWrite", 0);
            _vrReticleMaterial.DisableKeyword("_ALPHATEST_ON");
            _vrReticleMaterial.EnableKeyword("_ALPHABLEND_ON");
            _vrReticleMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            _vrReticleMaterial.renderQueue = 3000;
        }

        _vrReticleRenderer.material = _vrReticleMaterial;
        _vrReticleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _vrReticleRenderer.receiveShadows = false;

        // Set layer to UI or a layer that doesn't interact with physics
        _vrReticleObject.layer = LayerMask.NameToLayer("UI");
        if (_vrReticleObject.layer == -1) _vrReticleObject.layer = 5; // Default UI layer
    }

    void UpdateVRReticle()
    {
        if (!enabledReticle)
        {
            if (_vrReticleObject != null) _vrReticleObject.SetActive(false);
            return;
        }

        bool shouldShow = IsReticleVisible();
        if (!shouldShow)
        {
            if (_vrReticleObject != null) _vrReticleObject.SetActive(false);
            return;
        }

        EnsureVRReticle();
        if (_vrReticleObject == null || _vrReticleRenderer == null || _vrReticleMaterial == null) return;

        _vrReticleObject.SetActive(true);

        // Update position and scale
        if (_cam != null)
        {
            _vrReticleObject.transform.localPosition = Vector3.forward * vrDistance;
            _vrReticleObject.transform.localScale = Vector3.one * vrSize;
        }

        // Update texture
        Texture2D tex = null;
        Color tint = Color.white;

        if (renderMode == RenderMode.CustomTexture)
        {
            tex = customTexture;
            tint = customTextureTint;
        }
        else // Generated
        {
            int drawThickness = Mathf.Max(1, thicknessPx);
            EnsureRingTexture(drawThickness);
            tex = _ringTex;
            tint = color;
        }

        if (tex == null) return;

        // Apply texture to material
        _vrReticleMaterial.mainTexture = tex;

        // Calculate alpha
        float a = tint.a;
        if (visibility == Visibility.WhileActionPressed && showRingWithoutTarget && _currentTarget == null)
            a *= Mathf.Clamp01(noTargetAlpha);

        // Update color with alpha
        _vrReticleMaterial.color = new Color(tint.r, tint.g, tint.b, a);
    }

    void DestroyVRReticle()
    {
        if (_vrReticleObject != null)
        {
            Destroy(_vrReticleObject);
            _vrReticleObject = null;
        }
        if (_vrReticleMaterial != null)
        {
            Destroy(_vrReticleMaterial);
            _vrReticleMaterial = null;
        }
        _vrReticleRenderer = null;
    }

    internal static void SuppressPickup(ForcedPerspectiveFromPickup pickup)
    {
        if (pickup == null) return;
        s_suppressedPickups.Add(pickup);
    }

    internal static void UnsuppressPickup(ForcedPerspectiveFromPickup pickup)
    {
        if (pickup == null) return;
        s_suppressedPickups.Remove(pickup);
    }

    internal static bool IsPickupSuppressed(ForcedPerspectiveFromPickup pickup)
    {
        return pickup != null && s_suppressedPickups.Contains(pickup);
    }
}
