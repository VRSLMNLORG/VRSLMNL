using UnityEngine;

public partial class ForcedPerspectiveFromPickup
{
    /// <summary>
    /// Ставит удерживаемый объект вплотную перед ближайшей поверхностью сцены.
    /// Трассирует лучи по всем точкам силуэта и берёт минимальную глубину попадания относительно камеры.
    /// Смещение на половину диагонали Bounds (в масштабе) не даёт коллайдеру войти в стену.
    /// </summary>
    private void MoveInFrontOfObstacles()
    {
        if (_shapedGrid.Count == 0)
            return; // fallback

        int hitCount = 0;
        float closestZ = float.MaxValue;

        // Ищем минимальную глубину по сетке силуэта
        for (int i = 0; i < _shapedGrid.Count; i++)
        {
            RaycastHit hit = CastTowardsGridPoint(_shapedGrid[i], _wallLayers);
            if (hit.collider == null)
            {
                continue;
            }

            hitCount++;
            Vector3 wallPointLocal = _cameraTransform.InverseTransformPoint(hit.point);
            if (i == 0 || wallPointLocal.z < closestZ)
            {
                closestZ = wallPointLocal.z;
            }
        }

        if (hitCount == 0)
            return; // нет валидных пересечений — не трогаем текущую глубину

        // Толщина по направлению взгляда с учетом предсказанного масштаба (устойчиво к поворотам)
        float predictedScale = GetPredictedUniformScale();
        float halfThickness = ComputeHalfThicknessAlongPredicted(_cameraTransform.forward, predictedScale);

        // --- Коррекция минимальной и максимальной дистанции ---
        float minDistance = 0.25f + halfThickness;
        float maxDistance = 50f;
        closestZ = Mathf.Clamp(closestZ, minDistance, maxDistance);

        // --- Вычисляем позицию объекта ---
        Vector3 newLocalPos = transform.localPosition;

        // Гарантия непроникновения в камеру с небольшим зазором
        const float cameraGap = 0.1f;
        newLocalPos.z = Mathf.Max(closestZ - halfThickness, cameraGap);

        transform.localPosition = newLocalPos;
    }

    /// <summary>
    /// Поддерживает постоянный видимый размер объекта: масштаб = дистанция до камеры / исходное отношение.
    /// После смены масштаба позиция компенсируется так, чтобы сохранить текущую экранную (viewport) точку.
    /// </summary>
    private void UpdateScale()
    {
        float newScale = (_cameraTransform.position - transform.position).magnitude / _orgDistanceToScaleRatio;
        if (Mathf.Abs(newScale - transform.localScale.x) < 0.0001f) return;

        transform.localScale = Vector3.one * newScale;
        // При масштабировании сохраняем экранное положение
        if (Camera.main != null)
        {
            Vector3 newPos = Camera.main.ViewportToWorldPoint(new Vector3(_orgViewportPos.x, _orgViewportPos.y,
                (transform.position - _cameraTransform.position).magnitude));
            transform.position = newPos;
        }
    }

    private void MaintainViewportAnchor()
    {
        if (Camera.main == null) return;
        // Текущая дистанция до камеры по прямой
        float dist = (transform.position - _cameraTransform.position).magnitude;
        Vector3 target = Camera.main.ViewportToWorldPoint(new Vector3(_orgViewportPos.x, _orgViewportPos.y, dist));
        transform.position = target;
    }

    // -------- Helper: predicted uniform scale used later in UpdateScale --------
    private float GetPredictedUniformScale()
    {
        float dist = (_cameraTransform.position - transform.position).magnitude;
        return dist / _orgDistanceToScaleRatio;
    }

    // -------- Helper: half-thickness of OBB along worldDir using predicted scale --------
    private float ComputeHalfThicknessAlongPredicted(Vector3 worldDir, float predictedUniformScale)
    {
        worldDir = worldDir.normalized;

        if (_proxyCollider is BoxCollider box)
        {
            Transform t = box.transform;
            Vector3 halfLocal = box.size * 0.5f;
            float k = predictedUniformScale / Mathf.Max(1e-6f, transform.localScale.x);
            Vector3 predictedLossy = Vector3.Scale(t.lossyScale, new Vector3(k, k, k));
            Vector3 halfWorld = Vector3.Scale(halfLocal, predictedLossy);

            float h =
                Mathf.Abs(Vector3.Dot(worldDir, t.right)) * halfWorld.x +
                Mathf.Abs(Vector3.Dot(worldDir, t.up)) * halfWorld.y +
                Mathf.Abs(Vector3.Dot(worldDir, t.forward)) * halfWorld.z;
            return Mathf.Max(h, 0.001f);
        }

        var rend = GetComponentInChildren<Renderer>();
        if (rend != null)
        {
            Transform t = rend.transform;
            Vector3 halfLocal = rend.localBounds.extents;
            float k = predictedUniformScale / Mathf.Max(1e-6f, transform.localScale.x);
            Vector3 predictedLossy = Vector3.Scale(t.lossyScale, new Vector3(k, k, k));
            Vector3 halfWorld = Vector3.Scale(halfLocal, predictedLossy);

            float h =
                Mathf.Abs(Vector3.Dot(worldDir, t.right)) * halfWorld.x +
                Mathf.Abs(Vector3.Dot(worldDir, t.up)) * halfWorld.y +
                Mathf.Abs(Vector3.Dot(worldDir, t.forward)) * halfWorld.z;
            return Mathf.Max(h, 0.001f);
        }

        return 0.1f;
    }

    // -------- Helper: composite local Bounds of all child Renderers in root local space --------
    private Bounds CalculateCompositeLocalBounds(Transform root)
    {
        var renderers = GetComponentsInChildren<Renderer>(true);
        bool has = false;
        Bounds combined = new Bounds(Vector3.zero, Vector3.zero);

        foreach (var r in renderers)
        {
            if (r == null) continue;
            // Преобразуем 8 углов локальных границ рендера в локаль root и инкапсулируем
            Bounds lb = r.localBounds;
            Matrix4x4 toRoot = root.worldToLocalMatrix * r.transform.localToWorldMatrix;
            Vector3[] corners = new Vector3[8];
            Vector3 min = lb.min; Vector3 max = lb.max;
            corners[0] = toRoot.MultiplyPoint3x4(new Vector3(min.x, min.y, min.z));
            corners[1] = toRoot.MultiplyPoint3x4(new Vector3(max.x, min.y, min.z));
            corners[2] = toRoot.MultiplyPoint3x4(new Vector3(min.x, max.y, min.z));
            corners[3] = toRoot.MultiplyPoint3x4(new Vector3(max.x, max.y, min.z));
            corners[4] = toRoot.MultiplyPoint3x4(new Vector3(min.x, min.y, max.z));
            corners[5] = toRoot.MultiplyPoint3x4(new Vector3(max.x, min.y, max.z));
            corners[6] = toRoot.MultiplyPoint3x4(new Vector3(min.x, max.y, max.z));
            corners[7] = toRoot.MultiplyPoint3x4(new Vector3(max.x, max.y, max.z));

            if (!has)
            {
                combined = new Bounds(corners[0], Vector3.zero);
                has = true;
            }
            for (int i = 0; i < 8; i++) combined.Encapsulate(corners[i]);
        }

        return has ? combined : new Bounds(Vector3.zero, Vector3.zero);
    }

    private RaycastHit CastTowardsGridPoint(Vector3 gridPoint, LayerMask layers)
    {
        Vector3 worldPoint = _cameraTransform.TransformPoint(gridPoint);
        Vector3 origin = CameraToWorldOnNearPlane(worldPoint);
        Vector3 direction = worldPoint - origin;
        Physics.Raycast(origin, direction, out RaycastHit hit, 1000f, layers);
        return hit;
    }

    private Vector3 CameraToWorldOnNearPlane(Vector3 worldPoint)
    {
        if (Camera.main == null) return _cameraTransform.position;
        Vector3 vp = Camera.main.WorldToViewportPoint(worldPoint);
        vp.z = 0f; // near plane
        return Camera.main.ViewportToWorldPoint(vp);
    }
}
