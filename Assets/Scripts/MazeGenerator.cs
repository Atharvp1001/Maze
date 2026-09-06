using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Generates a perfect 2D maze and a lightweight point-to-point navigation
/// graph. The complete maze and all visible points use only two render meshes.
/// No physics colliders are required.
/// </summary>
public sealed class MazeGenerator : MonoBehaviour
{
    [Header("Maze Size")]
    [SerializeField, Min(2)] private int columns = 12;
    [SerializeField, Min(2)] private int rows = 8;
    [SerializeField, Min(0.1f)] private float cellSize = 1f;
    [SerializeField, Min(0.02f)] private float wallThickness = 0.14f;

    [Header("Randomization")]
    [Tooltip("Turn this off and enter a seed to reproduce the same maze.")]
    [SerializeField] private bool useRandomSeed = true;
    [SerializeField] private int seed = 12345;

    [Header("Appearance")]
    [SerializeField] private Color floorColor = new Color(0.9333f, 0.9059f, 0.8627f, 1f);
    [SerializeField] private Color wallColor = new Color(0.1451f, 0.149f, 0.1412f, 1f);
    [SerializeField] private bool createEntranceAndExit = true;

    [Header("Navigation Points")]
    [SerializeField] private bool showNavigationPoints = true;
    [SerializeField, Range(0.05f, 0.75f)] private float navigationPointSize = 0.15f;
    [SerializeField] private Color navigationPointColor = new Color(0.8431f, 0.4941f, 0.3137f, 1f);

    [Header("Camera")]
    [SerializeField] private bool fitMainCamera = true;
    [SerializeField, Min(0f)] private float cameraPadding = 1f;
    [Tooltip("Positive values frame the maze lower on the screen.")]
    [SerializeField] private float mazeVerticalScreenOffset = 0.8f;

    private const string GeneratedRootName = "Generated Maze";
    private const string GeometryObjectName = "Maze Geometry";
    private const string PointObjectName = "Navigation Point Display";
    private const int PointCircleSegments = 20;
    private const int WallCapSegments = 20;

    private static readonly Vector2Int[] CardinalDirections =
    {
        Vector2Int.up,
        Vector2Int.right,
        Vector2Int.down,
        Vector2Int.left
    };

    private Wall[,] walls;
    private bool[,] visited;
    private int[,] pointIndexByCell;
    private Vector2Int[] pathStack;
    private readonly Neighbour[] neighbourBuffer = new Neighbour[4];

    private Transform generatedRoot;
    private MeshFilter geometryFilter;
    private MeshRenderer geometryRenderer;
    private MeshFilter pointFilter;
    private MeshRenderer pointRenderer;
    private Mesh geometryMesh;
    private Mesh pointMesh;
    private Material runtimeMaterial;

    private readonly List<Vector3> geometryVertices = new List<Vector3>(1024);
    private readonly List<int> geometryTriangles = new List<int>(1536);
    private readonly List<Color32> geometryColors = new List<Color32>(1024);
    private readonly List<Vector3> pointVertices = new List<Vector3>(512);
    private readonly List<int> pointTriangles = new List<int>(768);
    private readonly List<Color32> pointColors = new List<Color32>(512);
    private readonly List<MazeNavigationPoint> navigationPoints = new List<MazeNavigationPoint>(128);

    public int Columns => columns;
    public int Rows => rows;
    public float CellSize => cellSize;
    public int LastGeneratedSeed { get; private set; }
    public int StartPointIndex { get; private set; } = -1;
    public int EndPointIndex { get; private set; } = -1;
    public Vector3 StartWorldPosition => GetCellWorldPosition(0, 0);
    public Vector3 ExitWorldPosition => GetCellWorldPosition(columns - 1, rows - 1);
    public IReadOnlyList<MazeNavigationPoint> NavigationPoints => navigationPoints;

    /// <summary>Raised after geometry and navigation data are ready.</summary>
    public event Action<MazeGenerator> MazeGenerated;

    [Flags]
    private enum Wall
    {
        None = 0,
        North = 1,
        East = 2,
        South = 4,
        West = 8,
        All = North | East | South | West
    }

    private readonly struct Neighbour
    {
        public readonly Vector2Int Position;
        public readonly Wall Direction;
        public readonly Wall OppositeDirection;

        public Neighbour(Vector2Int position, Wall direction, Wall oppositeDirection)
        {
            Position = position;
            Direction = direction;
            OppositeDirection = oppositeDirection;
        }
    }

    private void Start()
    {
        GenerateMaze();
    }

    /// <summary>Builds a new random or seeded maze using the existing meshes.</summary>
    [ContextMenu("Generate Maze")]
    public void GenerateMaze()
    {
        ClampSettings();
        EnsureRenderObjects();
        ClearMaze();
        EnsureGenerationBuffers();

        LastGeneratedSeed = useRandomSeed
            ? unchecked(Environment.TickCount * 397 ^ Guid.NewGuid().GetHashCode())
            : seed;

        CarvePassages(new System.Random(LastGeneratedSeed));
        BuildMazeMesh();
        BuildNavigationData();
        BuildPointMesh();

        if (fitMainCamera)
        {
            FitCamera();
        }

        MazeGenerated?.Invoke(this);
    }

    /// <summary>Returns a navigation point by index without creating an object.</summary>
    public MazeNavigationPoint GetNavigationPoint(int pointIndex)
    {
        if (pointIndex < 0 || pointIndex >= navigationPoints.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(pointIndex));
        }

        return navigationPoints[pointIndex];
    }

    /// <summary>Returns the world-space centre of a navigation point.</summary>
    public Vector3 GetNavigationPointWorldPosition(int pointIndex)
    {
        MazeNavigationPoint point = GetNavigationPoint(pointIndex);
        return GetCellWorldPosition(point.Cell.x, point.Cell.y);
    }

    /// <summary>Returns the world-space centre of a maze cell.</summary>
    public Vector3 GetCellWorldPosition(int column, int row)
    {
        ValidateCell(column, row);
        return transform.TransformPoint(GetCellLocalPosition(column, row));
    }

    /// <summary>Returns an axis-aligned world-space bound around the maze floor.</summary>
    public Bounds GetWorldBounds()
    {
        float halfWidth = columns * cellSize * 0.5f;
        float halfHeight = rows * cellSize * 0.5f;
        Bounds bounds = new Bounds(
            transform.TransformPoint(new Vector3(-halfWidth, -halfHeight, 0f)),
            Vector3.zero);

        bounds.Encapsulate(transform.TransformPoint(new Vector3(-halfWidth, halfHeight, 0f)));
        bounds.Encapsulate(transform.TransformPoint(new Vector3(halfWidth, -halfHeight, 0f)));
        bounds.Encapsulate(transform.TransformPoint(new Vector3(halfWidth, halfHeight, 0f)));
        return bounds;
    }

    /// <summary>
    /// Finds the closest point that can be reached in one straight, unobstructed
    /// move. Intermediate grid points in a long corridor may be skipped.
    /// </summary>
    public bool TryGetReachablePoint(
        Vector3 worldPosition,
        int fromPointIndex,
        float maximumWorldDistance,
        out int destinationPointIndex)
    {
        destinationPointIndex = -1;
        if (fromPointIndex < 0 || fromPointIndex >= navigationPoints.Count
            || maximumWorldDistance < 0f)
        {
            return false;
        }

        float closestDistanceSquared = maximumWorldDistance * maximumWorldDistance;

        for (int candidateIndex = 0; candidateIndex < navigationPoints.Count; candidateIndex++)
        {
            if (candidateIndex == fromPointIndex
                || !CanTravelStraight(fromPointIndex, candidateIndex))
            {
                continue;
            }

            Vector3 candidatePosition = GetNavigationPointWorldPosition(candidateIndex);
            float distanceSquared = (candidatePosition - worldPosition).sqrMagnitude;
            if (distanceSquared <= closestDistanceSquared)
            {
                closestDistanceSquared = distanceSquared;
                destinationPointIndex = candidateIndex;
            }
        }

        return destinationPointIndex >= 0;
    }

    /// <summary>
    /// Returns true when two points share a row or column and every grid edge
    /// between them is open.
    /// </summary>
    public bool CanTravelStraight(int fromPointIndex, int destinationPointIndex)
    {
        if (fromPointIndex < 0 || fromPointIndex >= navigationPoints.Count
            || destinationPointIndex < 0 || destinationPointIndex >= navigationPoints.Count
            || fromPointIndex == destinationPointIndex)
        {
            return false;
        }

        Vector2Int fromCell = navigationPoints[fromPointIndex].Cell;
        Vector2Int destinationCell = navigationPoints[destinationPointIndex].Cell;
        Vector2Int direction;

        if (fromCell.x == destinationCell.x)
        {
            direction = destinationCell.y > fromCell.y ? Vector2Int.up : Vector2Int.down;
        }
        else if (fromCell.y == destinationCell.y)
        {
            direction = destinationCell.x > fromCell.x ? Vector2Int.right : Vector2Int.left;
        }
        else
        {
            return false;
        }

        Vector2Int cell = fromCell;
        while (cell != destinationCell)
        {
            if (!CanMove(cell, direction))
            {
                return false;
            }

            cell += direction;
        }

        return true;
    }

    /// <summary>Clears generated data and meshes while retaining reusable objects.</summary>
    [ContextMenu("Clear Maze")]
    public void ClearMaze()
    {
        navigationPoints.Clear();
        StartPointIndex = -1;
        EndPointIndex = -1;

        geometryVertices.Clear();
        geometryTriangles.Clear();
        geometryColors.Clear();
        pointVertices.Clear();
        pointTriangles.Clear();
        pointColors.Clear();

        if (geometryMesh != null)
        {
            geometryMesh.Clear();
        }

        if (pointMesh != null)
        {
            pointMesh.Clear();
        }

        if (pointRenderer != null)
        {
            pointRenderer.enabled = false;
        }
    }

    private void EnsureGenerationBuffers()
    {
        if (walls == null || walls.GetLength(0) != columns || walls.GetLength(1) != rows)
        {
            walls = new Wall[columns, rows];
            visited = new bool[columns, rows];
            pointIndexByCell = new int[columns, rows];
            pathStack = new Vector2Int[columns * rows];
        }
    }

    private void CarvePassages(System.Random random)
    {
        for (int x = 0; x < columns; x++)
        {
            for (int y = 0; y < rows; y++)
            {
                walls[x, y] = Wall.All;
                visited[x, y] = false;
            }
        }

        int stackIndex = 0;
        pathStack[0] = Vector2Int.zero;
        visited[0, 0] = true;

        while (stackIndex >= 0)
        {
            Vector2Int current = pathStack[stackIndex];
            int choiceCount = GetUnvisitedNeighbours(current);

            if (choiceCount == 0)
            {
                stackIndex--;
                continue;
            }

            Neighbour next = neighbourBuffer[random.Next(choiceCount)];
            walls[current.x, current.y] &= ~next.Direction;
            walls[next.Position.x, next.Position.y] &= ~next.OppositeDirection;
            visited[next.Position.x, next.Position.y] = true;
            pathStack[++stackIndex] = next.Position;
        }
    }

    private int GetUnvisitedNeighbours(Vector2Int cell)
    {
        int count = 0;
        TryAddNeighbour(cell + Vector2Int.up, Wall.North, Wall.South, ref count);
        TryAddNeighbour(cell + Vector2Int.right, Wall.East, Wall.West, ref count);
        TryAddNeighbour(cell + Vector2Int.down, Wall.South, Wall.North, ref count);
        TryAddNeighbour(cell + Vector2Int.left, Wall.West, Wall.East, ref count);
        return count;
    }

    private void TryAddNeighbour(
        Vector2Int position,
        Wall direction,
        Wall oppositeDirection,
        ref int count)
    {
        if (IsInside(position) && !visited[position.x, position.y])
        {
            neighbourBuffer[count++] = new Neighbour(position, direction, oppositeDirection);
        }
    }

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

    private void BuildNavigationData()
    {
        navigationPoints.Clear();

        for (int x = 0; x < columns; x++)
        {
            for (int y = 0; y < rows; y++)
            {
                pointIndexByCell[x, y] = -1;
            }
        }

        for (int x = 0; x < columns; x++)
        {
            for (int y = 0; y < rows; y++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                MazeNavigationPointType pointType = GetPointType(cell);

                int pointIndex = navigationPoints.Count;
                pointIndexByCell[x, y] = pointIndex;
                navigationPoints.Add(new MazeNavigationPoint(cell, pointType));

                if (pointType == MazeNavigationPointType.Start)
                {
                    StartPointIndex = pointIndex;
                }
                else if (pointType == MazeNavigationPointType.End)
                {
                    EndPointIndex = pointIndex;
                }
            }
        }

        for (int pointIndex = 0; pointIndex < navigationPoints.Count; pointIndex++)
        {
            MazeNavigationPoint point = navigationPoints[pointIndex];

            for (int directionIndex = 0; directionIndex < CardinalDirections.Length; directionIndex++)
            {
                Vector2Int direction = CardinalDirections[directionIndex];
                int connectedIndex = GetConnectedNeighbourPoint(point.Cell, direction);
                point.SetConnection(direction, connectedIndex);
            }

            navigationPoints[pointIndex] = point;
        }
    }

    private MazeNavigationPointType GetPointType(Vector2Int cell)
    {
        if (cell == Vector2Int.zero)
        {
            return MazeNavigationPointType.Start;
        }

        if (cell.x == columns - 1 && cell.y == rows - 1)
        {
            return MazeNavigationPointType.End;
        }

        return MazeNavigationPointType.Grid;
    }

    private int GetConnectedNeighbourPoint(Vector2Int cell, Vector2Int direction)
    {
        if (!CanMove(cell, direction))
        {
            return -1;
        }

        Vector2Int neighbourCell = cell + direction;
        return IsInside(neighbourCell)
            ? pointIndexByCell[neighbourCell.x, neighbourCell.y]
            : -1;
    }

    private void BuildPointMesh()
    {
        pointVertices.Clear();
        pointTriangles.Clear();
        pointColors.Clear();

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
        RemoveLegacyChildren(geometryTransform, pointTransform);

        geometryFilter = GetOrAddComponent<MeshFilter>(geometryTransform.gameObject);
        geometryRenderer = GetOrAddComponent<MeshRenderer>(geometryTransform.gameObject);
        pointFilter = GetOrAddComponent<MeshFilter>(pointTransform.gameObject);
        pointRenderer = GetOrAddComponent<MeshRenderer>(pointTransform.gameObject);

        geometryMesh = GetOrCreateMesh(geometryFilter, "Runtime Maze Geometry");
        pointMesh = GetOrCreateMesh(pointFilter, "Runtime Maze Points");
        EnsureRuntimeMaterial();

        ConfigureRenderer(geometryRenderer, 0);
        ConfigureRenderer(pointRenderer, 2);
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

    private void RemoveLegacyChildren(Transform geometryTransform, Transform pointTransform)
    {
        for (int childIndex = generatedRoot.childCount - 1; childIndex >= 0; childIndex--)
        {
            Transform child = generatedRoot.GetChild(childIndex);
            if (child == geometryTransform || child == pointTransform)
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

    private bool CanMove(Vector2Int cell, Vector2Int direction)
    {
        if (direction == Vector2Int.up)
        {
            return !HasWall(cell.x, cell.y, Wall.North);
        }

        if (direction == Vector2Int.right)
        {
            return !HasWall(cell.x, cell.y, Wall.East);
        }

        if (direction == Vector2Int.down)
        {
            return !HasWall(cell.x, cell.y, Wall.South);
        }

        return !HasWall(cell.x, cell.y, Wall.West);
    }

    private bool HasWall(int x, int y, Wall wall)
    {
        return (walls[x, y] & wall) != 0;
    }

    private bool IsInside(Vector2Int cell)
    {
        return cell.x >= 0 && cell.x < columns && cell.y >= 0 && cell.y < rows;
    }

    private Vector3 GetCellLocalPosition(int column, int row)
    {
        float mazeWidth = columns * cellSize;
        float mazeHeight = rows * cellSize;
        return new Vector3(
            -mazeWidth * 0.5f + (column + 0.5f) * cellSize,
            -mazeHeight * 0.5f + (row + 0.5f) * cellSize,
            0f);
    }

    private void ValidateCell(int column, int row)
    {
        if (column < 0 || column >= columns)
        {
            throw new ArgumentOutOfRangeException(nameof(column));
        }

        if (row < 0 || row >= rows)
        {
            throw new ArgumentOutOfRangeException(nameof(row));
        }
    }

    private void FitCamera()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null || !mainCamera.orthographic)
        {
            return;
        }

        Vector3 mazeCentre = transform.position;
        Vector3 cameraPosition = mainCamera.transform.position;
        mainCamera.transform.position = new Vector3(
            mazeCentre.x,
            mazeCentre.y + mazeVerticalScreenOffset,
            cameraPosition.z);

        float verticalHalfSize = rows * cellSize * 0.5f;
        float horizontalHalfSize = columns * cellSize * 0.5f / Mathf.Max(mainCamera.aspect, 0.01f);
        mainCamera.orthographicSize = Mathf.Max(verticalHalfSize, horizontalHalfSize) + cameraPadding;
    }

    private void ClampSettings()
    {
        columns = Mathf.Max(2, columns);
        rows = Mathf.Max(2, rows);
        cellSize = Mathf.Max(0.1f, cellSize);
        wallThickness = Mathf.Clamp(wallThickness, 0.02f, cellSize * 0.5f);
        navigationPointSize = Mathf.Clamp(navigationPointSize, 0.05f, 0.75f);
        cameraPadding = Mathf.Max(0f, cameraPadding);
    }

    private void OnValidate()
    {
        ClampSettings();

        bool buffersMatchInspector = walls != null
            && walls.GetLength(0) == columns
            && walls.GetLength(1) == rows;

        if (Application.isPlaying && buffersMatchInspector && geometryMesh != null)
        {
            BuildMazeMesh();
            if (navigationPoints.Count > 0)
            {
                BuildPointMesh();
            }
        }
    }

    private void OnDestroy()
    {
        DestroyRuntimeObject(geometryMesh);
        DestroyRuntimeObject(pointMesh);
        DestroyRuntimeObject(runtimeMaterial);
    }

    private void DestroyRuntimeObject(UnityEngine.Object runtimeObject)
    {
        if (runtimeObject == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(runtimeObject);
        }
        else
        {
            DestroyImmediate(runtimeObject);
        }
    }
}
