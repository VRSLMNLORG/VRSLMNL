using UnityEngine;
using UnityEngine.InputSystem;

public partial class ForcedPerspectiveFromPickup
{
    // Cache gaze (camera-forward) raycast once per frame so that layer changes from the
    // first grabbed object do not affect other instances in this frame.
    private static void RefreshGazeCache(Transform cam, float maxDist)
    {
        if (s_gazeFrame == Time.frameCount) return;
        s_cachedTopHit = null;
        if (cam != null && Physics.Raycast(cam.position, cam.forward, out var hit, maxDist, ~0, QueryTriggerInteraction.Ignore))
            s_cachedTopHit = hit.collider;
        s_gazeFrame = Time.frameCount;
    }

    // Two-hand simultaneous press helper
    private bool BothHandsPressed()
    {
        bool left = holdActionLeft?.action?.IsPressed() ?? false;
        bool right = holdActionRight?.action?.IsPressed() ?? false;
        return left && right;
    }

    void Update()
    {
        CacheCamera();
        if (_cameraTransform == null) return;

        // Обновляем кэш попадания по взгляду один раз на кадр и сверяемся только с ним,
        // чтобы избежать гонки при переключении слоёв у взятого объекта.
        RefreshGazeCache(_cameraTransform, gazeMaxDistance);

        // Под прицелом считаемся только если закэшированный коллайдер принадлежит нам
        _isUnderGaze = false;
        if (s_cachedTopHit != null)
        {
            foreach (var c in _selfColliders)
            {
                if (c != null && s_cachedTopHit == c) { _isUnderGaze = true; break; }
            }
        }

        // Захват стартует при одновременном нажатии двух триггеров (оба вжатые) — детектим фронт
        bool bothNow = BothHandsPressed();
        bool startEdge = bothNow && !_bothPressedPrevFrame;
        bool endEdge = !bothNow && _bothPressedPrevFrame;

        if (s_current == null && !isHeld && startEdge && _isUnderGaze)
            StartHolding();
        else if (isHeld && endEdge)
            ReleaseHolding();

        _bothPressedPrevFrame = bothNow;
    }

    // Called right before rendering. Keeps held object in sync with latest HMD pose.
    void OnBeforeRenderCallback()
    {
        if (!isHeld) return;
        CacheCamera();
        if (_cameraTransform == null) return;
        UpdateOrientationSmooth();
        // Обновляем силуэтную сетку при изменении экранного якоря/позы камеры
        SetupShapedGrid(GetBoundingBoxPoints());
        MoveInFrontOfObstacles();
        UpdateScale();
        MaintainViewportAnchor(); // обеспечиваем фиксированную viewport‑позицию X/Y
    }

    // Optional legacy helpers
    bool WasPressedThisFrame() =>
        (holdActionLeft?.action?.WasPressedThisFrame() ?? false) ||
        (holdActionRight?.action?.WasPressedThisFrame() ?? false);

    bool WasReleasedThisFrame() =>
        (holdActionLeft?.action?.WasReleasedThisFrame() ?? false) ||
        (holdActionRight?.action?.WasReleasedThisFrame() ?? false);
}
