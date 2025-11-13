using System.Collections.Generic;
using UnityEngine;

public partial class ForcedPerspectiveFromPickup
{
    private Vector3[] GetBoundingBoxPoints()
    {
        var rend = GetComponentInChildren<Renderer>();
        if (rend == null) return new Vector3[0];

        Vector3 size = rend.localBounds.size;
        Vector3 x = new Vector3(size.x, 0, 0);
        Vector3 y = new Vector3(0, size.y, 0);
        Vector3 z = new Vector3(0, 0, size.z);
        Vector3 min = rend.localBounds.min;
        Vector3[] bbPoints =
        {
            min,
            min + x,
            min + y,
            min + x + y,
            min + z,
            min + z + x,
            min + z + y,
            min + z + x + y
        };
        return bbPoints;
    }

    /// <summary>
    /// Готовит «силуэтную» выборку точек.
    /// 1) Находит рамки прямоугольника проекции объекта в локале камеры (GetRectConfines).
    /// 2) Строит прямоугольную или полярную сетку и фильтрует точки, лучи через которые пересекают сам объект — это и есть силуэт.
    /// </summary>
    private void SetupShapedGrid(Vector3[] bbPoints)
    {
        _left = _right = _top = _bottom = Vector3.zero;
        GetRectConfines(bbPoints);

        if (usePolarGrid)
        {
            var points = SetupPolarEllipseGrid();
            GetShapedGrid(points);
        }
        else
        {
            Vector3[,] grid = SetupGrid();
            GetShapedGrid(grid);
        }
    }

    private void GetRectConfines(Vector3[] bbPoints)
    {
        var rend = GetComponentInChildren<Renderer>();
        if (rend == null) return;

        Vector3 closestPointWorld = rend.bounds.ClosestPoint(_cameraTransform.position);
        float closestZ = _cameraTransform.InverseTransformPoint(closestPointWorld).z;
        if (closestZ <= 0) throw new System.Exception("HeldObject inside the player!");

        for (int i = 0; i < bbPoints.Length; i++)
        {
            Vector3 bbPointWorld = transform.TransformPoint(bbPoints[i]);
            Vector3 cameraPoint = _cameraTransform.InverseTransformPoint(bbPointWorld);
            cameraPoint.z = closestZ;

            if (i == 0) { _left = _right = _top = _bottom = cameraPoint; }

            if (cameraPoint.x < _left.x) _left = cameraPoint;
            if (cameraPoint.x > _right.x) _right = cameraPoint;
            if (cameraPoint.y > _top.y) _top = cameraPoint;
            if (cameraPoint.y < _bottom.y) _bottom = cameraPoint;
        }
    }

    private Vector3[,] SetupGrid()
    {
        float rectHrLength = _right.x - _left.x;
        float rectVertLength = _top.y - _bottom.y;
        Vector3 hrStep = new Vector2(rectHrLength / Mathf.Max(1, (NUMBER_OF_GRID_COLUMNS - 1)), 0);
        Vector3 vertStep = new Vector2(0, rectVertLength / Mathf.Max(1, (NUMBER_OF_GRID_ROWS - 1)));

        Vector3[,] grid = new Vector3[NUMBER_OF_GRID_ROWS, NUMBER_OF_GRID_COLUMNS];
        grid[0, 0] = new Vector3(_left.x, _bottom.y, _left.z);

        for (int i = 0; i < grid.GetLength(0); i++)
        {
            for (int w = 0; w < grid.GetLength(1); w++)
            {
                if (i == 0 & w == 0) continue;
                else if (w == 0)
                {
                    grid[i, w] = grid[i - 1, 0] + vertStep;
                }
                else grid[i, w] = grid[i, w - 1] + hrStep;
            }
        }
        return grid;
    }

    /// <summary>
    /// Генерирует полярную сетку точек внутри эллипса, аппроксимирующего проекцию объекта.
    /// Полуоси эллипса берутся по ширине/высоте прямоугольника проекции; точки распределяются по кольцам и секторам.
    /// Параметр edgeBias > 1 сгущает кольца к контуру, что улучшает детекцию контакта.
    /// </summary>
    private List<Vector3> SetupPolarEllipseGrid()
    {
        float halfW = Mathf.Max(1e-5f, (_right.x - _left.x) * 0.5f);
        float halfH = Mathf.Max(1e-5f, (_top.y - _bottom.y) * 0.5f);
        Vector3 center = new Vector3((_left.x + _right.x) * 0.5f,
                                     (_bottom.y + _top.y) * 0.5f,
                                     _left.z);

        var list = new List<Vector3>(polarRings * polarSectors + 1) { center };

        for (int r = 1; r <= polarRings; r++)
        {
            float t = (float)r / polarRings;
            float rho = Mathf.Pow(t, 1f / Mathf.Max(0.0001f, edgeBias));
            rho = Mathf.Clamp(rho, 0f, 1f); // защита

            for (int s = 0; s < polarSectors; s++)
            {
                float theta = (Mathf.PI * 2f) * s / polarSectors;
                float x = center.x + Mathf.Cos(theta) * rho * halfW;
                float y = center.y + Mathf.Sin(theta) * rho * halfH;

                // Ограничиваем в границах объекта
                x = Mathf.Clamp(x, _left.x, _right.x);
                y = Mathf.Clamp(y, _bottom.y, _top.y);

                list.Add(new Vector3(x, y, Mathf.Max(center.z, 0.01f)));
            }
        }

        return list;
    }

    private void GetShapedGrid(Vector3[,] grid)
    {
        _shapedGrid.Clear();
        // Вместо слоёв используем собственные коллайдеры: луч должен пересечь этот объект
        foreach (Vector3 point in grid)
        {
            Vector3 worldPoint = _cameraTransform.TransformPoint(point);
            Vector3 origin = CameraToWorldOnNearPlane(worldPoint);
            Vector3 dir = worldPoint - origin;
            Ray r = new Ray(origin, dir.normalized);
            float maxDist = Mathf.Infinity; // allow long ray to always reach own collider

            bool silhouette = false;
            foreach (var c in _selfColliders)
            {
                if (c != null && c.Raycast(r, out var hit, maxDist)) { silhouette = true; break; }
            }
            if (silhouette) _shapedGrid.Add(point);
        }
    }

    private void GetShapedGrid(List<Vector3> points)
    {
        _shapedGrid.Clear();
        foreach (Vector3 point in points)
        {
            Vector3 worldPoint = _cameraTransform.TransformPoint(point);
            Vector3 origin = CameraToWorldOnNearPlane(worldPoint);
            Vector3 dir = worldPoint - origin;
            Ray r = new Ray(origin, dir.normalized);
            float maxDist = Mathf.Infinity; // allow long ray to always reach own collider

            bool silhouette = false;
            foreach (var c in _selfColliders)
            {
                if (c != null && c.Raycast(r, out var hit, maxDist)) { silhouette = true; break; }
            }
            if (silhouette) _shapedGrid.Add(point);
        }
    }
}
