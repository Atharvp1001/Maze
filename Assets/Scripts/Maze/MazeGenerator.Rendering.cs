using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

internal sealed partial class MazeGenerator
{
    private void BuildMazeMesh()
    {
        geometryVertices.Clear();
        geometryTriangles.Clear();
        geometryColors.Clear();

        float mazeWidth = columns * cellSize;
        float mazeHeight = rows * cellSize;
        float left = -mazeWidth * 0.5f;
        float bottom = -mazeHeight * 0.5f;

        AddQuad(
            geometryVertices,
            geometryTriangles,
            geometryColors,
            Vector2.zero,
            new Vector2(mazeWidth, mazeHeight),
            0.1f,
            floorColor);

        for (int x = 0; x < columns; x++)
        {
            for (int y = 0; y < rows; y++)
            {
                float centreX = left + (x + 0.5f) * cellSize;
                float centreY = bottom + (y + 0.5f) * cellSize;

                if (HasWall(x, y, Wall.North)
                    && !(createEntranceAndExit && x == columns - 1 && y == rows - 1))
                {
                    AddWall(
                        new Vector2(centreX, centreY + cellSize * 0.5f),
                        new Vector2(cellSize + wallThickness, wallThickness));
                }

                if (HasWall(x, y, Wall.East))
                {
                    AddWall(
                        new Vector2(centreX + cellSize * 0.5f, centreY),
                        new Vector2(wallThickness, cellSize + wallThickness));
                }

                if (y == 0 && HasWall(x, y, Wall.South)
                    && !(createEntranceAndExit && x == 0))
                {
                    AddWall(
                        new Vector2(centreX, centreY - cellSize * 0.5f),
                        new Vector2(cellSize + wallThickness, wallThickness));
                }

                if (x == 0 && HasWall(x, y, Wall.West))
                {
                    AddWall(
                        new Vector2(centreX - cellSize * 0.5f, centreY),
                        new Vector2(wallThickness, cellSize + wallThickness));
                }
            }
        }

        ApplyMesh(geometryMesh, geometryVertices, geometryTriangles, geometryColors);
        geometryRenderer.enabled = true;
    }

    private void AddWall(Vector2 centre, Vector2 size)
    {
        bool isHorizontal = size.x >= size.y;
        float diameter = isHorizontal ? size.y : size.x;
        float coreLength = Mathf.Max((isHorizontal ? size.x : size.y) - diameter, 0f);
        Vector2 coreSize = isHorizontal
            ? new Vector2(coreLength, diameter)
            : new Vector2(diameter, coreLength);
        Vector2 capOffset = isHorizontal
            ? new Vector2(coreLength * 0.5f, 0f)
            : new Vector2(0f, coreLength * 0.5f);

        AddQuad(
            geometryVertices,
            geometryTriangles,
            geometryColors,
            centre,
            coreSize,
            0f,
            wallColor);
        AddSolidCircle(
            geometryVertices,
            geometryTriangles,
            geometryColors,
            centre - capOffset,
            diameter * 0.5f,
            0f,
            wallColor,
            WallCapSegments);
        AddSolidCircle(
            geometryVertices,
            geometryTriangles,
            geometryColors,
            centre + capOffset,
            diameter * 0.5f,
            0f,
            wallColor,
            WallCapSegments);
    }

    private void BuildPointMesh()
    {
        KillPointPopTweens();
        pointVertices.Clear();
        pointTriangles.Clear();
        pointColors.Clear();

        pointPopTweens = new Tween[navigationPoints.Count];
        pointScales = new float[navigationPoints.Count];
        for (int pointIndex = 0; pointIndex < pointScales.Length; pointIndex++)
        {
            pointScales[pointIndex] = 1f;
        }

        if (!showNavigationPoints)
        {
            pointMesh.Clear();
            pointRenderer.enabled = false;
            return;
        }

        for (int pointIndex = 0; pointIndex < navigationPoints.Count; pointIndex++)
        {
            Vector2Int cell = navigationPoints[pointIndex].Cell;
            AddSolidPoint(GetCellLocalPosition(cell.x, cell.y));
        }

        ApplyMesh(pointMesh, pointVertices, pointTriangles, pointColors);
        pointRenderer.enabled = pointVertices.Count > 0;
    }

    private void SetNavigationPointScale(int pointIndex, float scale)
    {
        if (pointMesh == null || pointIndex < 0 || pointIndex >= pointScales.Length)
        {
            return;
        }

        pointScales[pointIndex] = scale;
        Vector2Int cell = navigationPoints[pointIndex].Cell;
        Vector3 centre = GetCellLocalPosition(cell.x, cell.y);
        float radius = cellSize * navigationPointSize * 0.5f * scale;
        int firstPerimeterVertex = pointIndex * (PointCircleSegments + 1) + 1;

        for (int segment = 0; segment < PointCircleSegments; segment++)
        {
            float angle = segment * Mathf.PI * 2f / PointCircleSegments;
            pointVertices[firstPerimeterVertex + segment] = new Vector3(
                centre.x + Mathf.Cos(angle) * radius,
                centre.y + Mathf.Sin(angle) * radius,
                -0.05f);
        }

        pointMesh.SetVertices(pointVertices);
        pointMesh.RecalculateBounds();
    }

    private void KillPointPopTweens()
    {
        for (int pointIndex = 0; pointIndex < pointPopTweens.Length; pointIndex++)
        {
            Tween tween = pointPopTweens[pointIndex];
            if (tween != null && tween.IsActive())
            {
                tween.Kill();
            }
        }
    }

    private void AddSolidPoint(Vector3 centre)
    {
        AddSolidCircle(
            pointVertices,
            pointTriangles,
            pointColors,
            centre,
            cellSize * navigationPointSize * 0.5f,
            -0.05f,
            navigationPointColor,
            PointCircleSegments);
    }

    private static void AddSolidCircle(
        List<Vector3> vertices,
        List<int> triangles,
        List<Color32> colors,
        Vector2 centre,
        float radius,
        float z,
        Color32 color,
        int segmentCount)
    {
        int centreVertex = vertices.Count;
        vertices.Add(new Vector3(centre.x, centre.y, z));
        colors.Add(color);

        for (int segment = 0; segment < segmentCount; segment++)
        {
            float angle = segment * Mathf.PI * 2f / segmentCount;
            vertices.Add(new Vector3(
                centre.x + Mathf.Cos(angle) * radius,
                centre.y + Mathf.Sin(angle) * radius,
                z));
            colors.Add(color);
        }

        for (int segment = 0; segment < segmentCount; segment++)
        {
            triangles.Add(centreVertex);
            triangles.Add(centreVertex + 1 + segment);
            triangles.Add(centreVertex + 1 + (segment + 1) % segmentCount);
        }
    }

    private static void AddQuad(
        List<Vector3> vertices,
        List<int> triangles,
        List<Color32> colors,
        Vector2 centre,
        Vector2 size,
        float z,
        Color32 color)
    {
        int firstVertex = vertices.Count;
        Vector2 halfSize = size * 0.5f;

        vertices.Add(new Vector3(centre.x - halfSize.x, centre.y - halfSize.y, z));
        vertices.Add(new Vector3(centre.x - halfSize.x, centre.y + halfSize.y, z));
        vertices.Add(new Vector3(centre.x + halfSize.x, centre.y + halfSize.y, z));
        vertices.Add(new Vector3(centre.x + halfSize.x, centre.y - halfSize.y, z));

        for (int i = 0; i < 4; i++)
        {
            colors.Add(color);
        }

        triangles.Add(firstVertex);
        triangles.Add(firstVertex + 1);
        triangles.Add(firstVertex + 2);
        triangles.Add(firstVertex);
        triangles.Add(firstVertex + 2);
        triangles.Add(firstVertex + 3);
    }

    private static void ApplyMesh(
        Mesh mesh,
        List<Vector3> vertices,
        List<int> triangles,
        List<Color32> colors)
    {
        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0, false);
        mesh.RecalculateBounds();
    }

    private void EnsureRenderObjects()
    {
        generatedRoot = transform.Find(GeneratedRootName);
        if (generatedRoot == null)
        {
            generatedRoot = new GameObject(GeneratedRootName).transform;
            generatedRoot.SetParent(transform, false);
        }

        Transform geometryTransform = GetOrCreateChild(GeometryObjectName);
        Transform pointTransform = GetOrCreateChild(PointObjectName);
        Transform shiftOpenTransform = GetOrCreateChild(ShiftOpenWarningName);
        Transform shiftCloseTransform = GetOrCreateChild(ShiftCloseWarningName);
        RemoveLegacyChildren(
            geometryTransform,
            pointTransform,
            shiftOpenTransform,
            shiftCloseTransform);

        geometryFilter = GetOrAddComponent<MeshFilter>(geometryTransform.gameObject);
        geometryRenderer = GetOrAddComponent<MeshRenderer>(geometryTransform.gameObject);
        pointFilter = GetOrAddComponent<MeshFilter>(pointTransform.gameObject);
        pointRenderer = GetOrAddComponent<MeshRenderer>(pointTransform.gameObject);
        shiftOpenWarning = GetOrAddComponent<LineRenderer>(shiftOpenTransform.gameObject);
        shiftCloseWarning = GetOrAddComponent<LineRenderer>(shiftCloseTransform.gameObject);
        shiftOpenWarningLines.Clear();
        shiftCloseWarningLines.Clear();
        shiftOpenWarningLines.Add(shiftOpenWarning);
        shiftCloseWarningLines.Add(shiftCloseWarning);

        geometryMesh = GetOrCreateMesh(geometryFilter, "Runtime Maze Geometry");
        pointMesh = GetOrCreateMesh(pointFilter, "Runtime Maze Points");
        EnsureRuntimeMaterial();

        ConfigureRenderer(geometryRenderer, 0);
        ConfigureRenderer(pointRenderer, 2);
        ConfigureShiftWarningLine(shiftOpenWarning);
        ConfigureShiftWarningLine(shiftCloseWarning);
    }

    private Transform GetOrCreateChild(string childName)
    {
        Transform child = generatedRoot.Find(childName);
        if (child == null)
        {
            child = new GameObject(childName).transform;
            child.SetParent(generatedRoot, false);
        }

        child.gameObject.layer = gameObject.layer;
        return child;
    }

    private void RemoveLegacyChildren(
        Transform geometryTransform,
        Transform pointTransform,
        Transform shiftOpenTransform,
        Transform shiftCloseTransform)
    {
        for (int childIndex = generatedRoot.childCount - 1; childIndex >= 0; childIndex--)
        {
            Transform child = generatedRoot.GetChild(childIndex);
            if (child == geometryTransform || child == pointTransform
                || child == shiftOpenTransform || child == shiftCloseTransform
                || child.name.StartsWith(ShiftOpenWarningName, StringComparison.Ordinal)
                || child.name.StartsWith(ShiftCloseWarningName, StringComparison.Ordinal))
            {
                continue;
            }

            if (Application.isPlaying)
            {
                Destroy(child.gameObject);
            }
            else
            {
                DestroyImmediate(child.gameObject);
            }
        }
    }

    private static T GetOrAddComponent<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : target.AddComponent<T>();
    }

    private void ConfigureShiftWarningLine(LineRenderer line)
    {
        line.useWorldSpace = false;
        line.loop = false;
        line.sharedMaterial = runtimeMaterial;
        line.startWidth = wallThickness * 1.8f;
        line.endWidth = wallThickness * 1.8f;
        line.numCapVertices = 8;
        line.numCornerVertices = 4;
        line.alignment = LineAlignment.TransformZ;
        line.sortingOrder = 4;
        line.positionCount = 0;
        line.enabled = false;
    }

    private static Mesh GetOrCreateMesh(MeshFilter filter, string meshName)
    {
        if (filter.sharedMesh != null)
        {
            return filter.sharedMesh;
        }

        Mesh mesh = new Mesh
        {
            name = meshName,
            indexFormat = IndexFormat.UInt32,
            hideFlags = HideFlags.HideAndDontSave
        };
        mesh.MarkDynamic();
        filter.sharedMesh = mesh;
        return mesh;
    }

    private void EnsureRuntimeMaterial()
    {
        if (runtimeMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Debug.LogError("The Sprites/Default shader is required to render the maze.", this);
                return;
            }

            runtimeMaterial = new Material(shader)
            {
                name = "Runtime Maze Material",
                mainTexture = Texture2D.whiteTexture,
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        geometryRenderer.sharedMaterial = runtimeMaterial;
        pointRenderer.sharedMaterial = runtimeMaterial;
    }

    private static void ConfigureRenderer(MeshRenderer renderer, int sortingOrder)
    {
        renderer.sortingOrder = sortingOrder;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }
}
