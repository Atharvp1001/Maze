using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Generates a 2D maze and a lightweight point-to-point navigation
/// graph. The complete maze and all visible points use only two render meshes.
/// No physics colliders are required.
/// </summary>
internal sealed partial class MazeGenerator : MonoBehaviour
{
    internal enum MazeDifficulty
    {
        Easy,
        Medium,
        Hard
    }

    [Header("Maze Size")]
    [SerializeField, Min(2)] private int columns = 12;
    [SerializeField, Min(2)] private int rows = 8;
    [SerializeField, Min(0.1f)] private float cellSize = 1f;
    [SerializeField, Min(0.02f)] private float wallThickness = 0.14f;

    [Header("Randomization")]
    [Tooltip("Turn this off and enter a seed to reproduce the same maze.")]
    [SerializeField] private bool useRandomSeed = true;
    [SerializeField] private int seed = 12345;
    [SerializeField] private TMP_Text difficultyText;

    [Header("Appearance")]
    [SerializeField] private Color floorColor = new Color(0.9333f, 0.9059f, 0.8627f, 1f);
    [SerializeField] private Color wallColor = new Color(0.1451f, 0.149f, 0.1412f, 1f);
    [SerializeField] private bool createEntranceAndExit = true;
    [SerializeField] private Transform endpointVisual;

    [Header("Navigation Points")]
    [SerializeField] private bool showNavigationPoints = true;
    [SerializeField, Range(0.05f, 0.75f)] private float navigationPointSize = 0.15f;
    [SerializeField] private Color navigationPointColor = new Color(0.8431f, 0.4941f, 0.3137f, 1f);
    [SerializeField, Min(1f)] private float navigationPointHoverScale = 1.5f;
    [SerializeField, Min(0.02f)] private float navigationPointHoverDuration = 0.1f;
    [SerializeField, Min(1f)] private float navigationPointPopScale = 2f;
    [SerializeField, Min(0.02f)] private float navigationPointPopDuration = 0.24f;

    [Header("Camera")]
    [SerializeField] private bool fitMainCamera = true;
    [SerializeField, Min(0f)] private float cameraPadding = 1f;
    [Tooltip("Positive values frame the maze lower on the screen.")]
    [SerializeField] private float mazeVerticalScreenOffset = 0.8f;

    private const string GeneratedRootName = "Generated Maze";
    private const string GeometryObjectName = "Maze Geometry";
    private const string PointObjectName = "Navigation Point Display";
    private const string ShiftOpenWarningName = "Shift Opening Warning";
    private const string ShiftCloseWarningName = "Shift Closing Warning";
    private const int PointCircleSegments = 20;
    private const int WallCapSegments = 20;
    private const float EasyExtraPassageRatio = 0.06f;
    private const float MediumExtraPassageRatio = 0.03f;
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
    private int[,] pathDistances;
    private Vector2Int[,] pathParents;
    private Vector2Int[,] growthDirections;
    private bool[,] solutionPathCells;
    private Vector2Int[] activeCells;
    private int[] activeDepths;
    private readonly Neighbour[] neighbourBuffer = new Neighbour[4];

    private Transform generatedRoot;
    private MeshFilter geometryFilter;
    private MeshRenderer geometryRenderer;
    private MeshFilter pointFilter;
    private MeshRenderer pointRenderer;
    private LineRenderer shiftOpenWarning;
    private LineRenderer shiftCloseWarning;
    private Mesh geometryMesh;
    private Mesh pointMesh;
    private Material runtimeMaterial;
    private Tween[] pointPopTweens = Array.Empty<Tween>();
    private float[] pointScales = Array.Empty<float>();

    private readonly List<Vector3> geometryVertices = new List<Vector3>(1024);
    private readonly List<int> geometryTriangles = new List<int>(1536);
    private readonly List<Color32> geometryColors = new List<Color32>(1024);
    private readonly List<Vector3> pointVertices = new List<Vector3>(512);
    private readonly List<int> pointTriangles = new List<int>(768);
    private readonly List<Color32> pointColors = new List<Color32>(512);
    private readonly List<MazeNavigationPoint> navigationPoints = new List<MazeNavigationPoint>(128);
    private readonly List<LoopCandidate> loopCandidates = new List<LoopCandidate>(256);
    private readonly List<LoopCandidate> shiftOpenEdges = new List<LoopCandidate>(256);
    private readonly List<LoopCandidate> shiftClosedEdges = new List<LoopCandidate>(256);
    private readonly List<LoopCandidate> shiftRouteEdges = new List<LoopCandidate>(256);
    private readonly List<LoopCandidate> shiftTargetCloseEdges = new List<LoopCandidate>(64);
    private readonly List<LoopCandidate> preparedEdgesToOpen = new List<LoopCandidate>(8);
    private readonly List<LoopCandidate> preparedEdgesToClose = new List<LoopCandidate>(8);
    private readonly List<LineRenderer> shiftOpenWarningLines = new List<LineRenderer>(8);
    private readonly List<LineRenderer> shiftCloseWarningLines = new List<LineRenderer>(8);
    private readonly List<Vector2Int> solutionPath = new List<Vector2Int>(256);

    private int Columns => columns;
    private int Rows => rows;
    internal float CellSize => cellSize;
    private int currentLevelSeed;
    private MazeDifficulty currentDifficulty;
    private bool useAdvancedGeneration;
    private float baseComplexityMultiplier = 1f;
    private bool hasCurrentLevel;
    private bool hasPreparedShift;
    private bool preparedPassageIsOpen;
    internal int StartPointIndex { get; private set; } = -1;
    internal int EndPointIndex { get; private set; } = -1;
    internal int SolutionLength { get; private set; }
    internal int SolutionTurnCount { get; private set; }
    internal int SolutionJunctionCount { get; private set; }
    internal int MisleadingBranchDepth { get; private set; }
    internal int LongestMisleadingBranch { get; private set; }
    internal int ShortDeadEndCount { get; private set; }
    internal float ComplexityScore { get; private set; }
    internal int CurrentLevelSeed => currentLevelSeed;
    private Vector3 StartWorldPosition => GetCellWorldPosition(0, 0);
    private Vector3 ExitWorldPosition => GetCellWorldPosition(columns - 1, rows - 1);
    internal IReadOnlyList<MazeNavigationPoint> NavigationPoints => navigationPoints;

    internal void SetDimensions(int newColumns, int newRows)
    {
        columns = Mathf.Max(2, newColumns);
        rows = Mathf.Max(2, newRows);
    }

    /// <summary>
    /// Selects a batch of local wall exchanges without changing the maze yet.
    /// Open trail edges are excluded so the route already drawn by the player
    /// is never closed by a shift.
    /// </summary>
    internal event Action<MazeGenerator> MazeGenerated;
    internal event Action<MazeGenerator> WallLayoutChanged;

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
        internal readonly Vector2Int Position;
        internal readonly Wall Direction;
        internal readonly Wall OppositeDirection;

        internal Neighbour(Vector2Int position, Wall direction, Wall oppositeDirection)
        {
            Position = position;
            Direction = direction;
            OppositeDirection = oppositeDirection;
        }
    }

    private readonly struct LoopCandidate
    {
        internal readonly Vector2Int Cell;
        internal readonly Vector2Int NeighbourCell;
        internal readonly Wall Direction;
        internal readonly Wall OppositeDirection;

        internal LoopCandidate(
            Vector2Int cell,
            Vector2Int neighbourCell,
            Wall direction,
            Wall oppositeDirection)
        {
            Cell = cell;
            NeighbourCell = neighbourCell;
            Direction = direction;
            OppositeDirection = oppositeDirection;
        }
    }

    private void Awake()
    {
        if (endpointVisual != null)
        {
            endpointVisual.gameObject.SetActive(false);
        }
    }

    /// <summary>Chooses a fresh difficulty and seed, then builds a new maze.</summary>
    [ContextMenu("Generate New Level")]
    public void GenerateNewLevel()
    {
        currentLevelSeed = useRandomSeed ? CreateRandomSeed() : seed;
        currentDifficulty = (MazeDifficulty)new System.Random(currentLevelSeed).Next(3);
        useAdvancedGeneration = false;
        baseComplexityMultiplier = 1f;
        hasCurrentLevel = true;
        GenerateCurrentLevel();
    }

    /// <summary>Builds a fresh level using the difficulty selected by the player.</summary>
    internal void GenerateNewLevel(MazeDifficulty difficulty)
    {
        currentLevelSeed = useRandomSeed ? CreateRandomSeed() : seed;
        currentDifficulty = difficulty;
        useAdvancedGeneration = false;
        baseComplexityMultiplier = 1f;
        hasCurrentLevel = true;
        GenerateCurrentLevel();
    }

    /// <summary>Builds a level from persistent settings and an optional shifted layout.</summary>
    internal void GenerateSavedLevel(
        MazeDifficulty difficulty,
        int levelSeed,
        string savedWallLayout,
        bool shouldUseAdvancedGeneration,
        float requestedBaseComplexityMultiplier)
    {
        currentLevelSeed = levelSeed;
        currentDifficulty = difficulty;
        useAdvancedGeneration = shouldUseAdvancedGeneration;
        baseComplexityMultiplier = Mathf.Clamp(
            requestedBaseComplexityMultiplier,
            0.5f,
            1f);
        hasCurrentLevel = true;
        GenerateCurrentLevel(savedWallLayout);
    }

    internal static int CreateLevelSeed()
    {
        return CreateRandomSeed();
    }

    internal string CaptureWallLayout()
    {
        if (walls == null
            || walls.GetLength(0) != columns
            || walls.GetLength(1) != rows)
        {
            return string.Empty;
        }

        byte[] wallData = new byte[columns * rows];
        int dataIndex = 0;
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                wallData[dataIndex++] = (byte)walls[x, y];
            }
        }

        return Convert.ToBase64String(wallData);
    }

    /// <summary>Rebuilds the current maze without changing its difficulty or seed.</summary>
    [ContextMenu("Restart Current Level")]
    public void RestartCurrentLevel()
    {
        if (!hasCurrentLevel)
        {
            GenerateNewLevel();
            return;
        }

        GenerateCurrentLevel();
    }

    private void GenerateCurrentLevel(string savedWallLayout = null)
    {
        ClampSettings();
        EnsureRenderObjects();
        ClearMaze();
        EnsureGenerationBuffers();
        UpdateDifficultyText();

        if (!string.IsNullOrEmpty(savedWallLayout))
        {
            CarvePassages(new System.Random(currentLevelSeed));
            RestoreWallLayout(savedWallLayout);
            ConnectEndpointFromFarthestNeighbour();
            AnalyzeMazeComplexity();
        }
        else if (useAdvancedGeneration)
        {
            System.Random random = new System.Random(currentLevelSeed);
            CarveGrowingTree(random);
            AnalyzeMazeComplexity();
            StrengthenSolutionJunctions(random);
            AnalyzeMazeComplexity();
        }
        else
        {
            System.Random random = new System.Random(currentLevelSeed);
            CarvePassages(random);
            AddDifficultyLoops(random);
            AnalyzeMazeComplexity();
        }

        BuildMazeMesh();
        BuildNavigationData();
        PositionEndpointVisual();
        BuildPointMesh();

        if (fitMainCamera)
        {
            FitCamera();
        }

        MazeGenerated?.Invoke(this);
    }

    private void RestoreWallLayout(string savedWallLayout)
    {
        if (string.IsNullOrEmpty(savedWallLayout))
        {
            return;
        }

        try
        {
            byte[] wallData = Convert.FromBase64String(savedWallLayout);
            if (wallData.Length != columns * rows)
            {
                return;
            }

            int dataIndex = 0;
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    walls[x, y] = (Wall)wallData[dataIndex++];
                }
            }
        }
        catch (FormatException)
        {
            Debug.LogWarning("Saved maze wall data was invalid; regenerated from its seed.", this);
        }
    }

    private static int CreateRandomSeed()
    {
        return unchecked(Environment.TickCount * 397 ^ Guid.NewGuid().GetHashCode());
    }

    private void UpdateDifficultyText()
    {
        if (difficultyText != null)
        {
            difficultyText.text = currentDifficulty.ToString();
        }
    }

    /// <summary>Returns a navigation point by index without creating an object.</summary>
    internal MazeNavigationPoint GetNavigationPoint(int pointIndex)
    {
        if (pointIndex < 0 || pointIndex >= navigationPoints.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(pointIndex));
        }

        return navigationPoints[pointIndex];
    }

    /// <summary>Returns the world-space centre of a navigation point.</summary>
    internal Vector3 GetNavigationPointWorldPosition(int pointIndex)
    {
        MazeNavigationPoint point = GetNavigationPoint(pointIndex);
        return GetCellWorldPosition(point.Cell.x, point.Cell.y);
    }

    /// <summary>Briefly enlarges one visible navigation point, then restores it.</summary>
    internal void PopNavigationPoint(int pointIndex)
    {
        if (!showNavigationPoints || pointMesh == null
            || pointIndex < 0 || pointIndex >= pointScales.Length)
        {
            return;
        }

        Tween activeTween = pointPopTweens[pointIndex];
        if (activeTween != null && activeTween.IsActive())
        {
            activeTween.Kill();
        }

        SetNavigationPointScale(pointIndex, 1f);

        float growDuration = navigationPointPopDuration * 0.4f;
        float settleDuration = navigationPointPopDuration - growDuration;
        Sequence sequence = DOTween.Sequence();
        pointPopTweens[pointIndex] = sequence;

        sequence.Append(DOTween.To(
                () => pointScales[pointIndex],
                scale => SetNavigationPointScale(pointIndex, scale),
                navigationPointPopScale,
                growDuration)
            .SetEase(Ease.OutCubic));
        sequence.Append(DOTween.To(
                () => pointScales[pointIndex],
                scale => SetNavigationPointScale(pointIndex, scale),
                1f,
                settleDuration)
            .SetEase(Ease.InOutSine));
        sequence.SetTarget(this);
        sequence.OnKill(() =>
        {
            if (pointIndex < pointPopTweens.Length
                && ReferenceEquals(pointPopTweens[pointIndex], sequence))
            {
                pointPopTweens[pointIndex] = null;
            }
        });
    }

    /// <summary>Smoothly enlarges or restores one selectable navigation point.</summary>
    internal void SetNavigationPointHovered(int pointIndex, bool isHovered)
    {
        if (!showNavigationPoints || pointMesh == null
            || pointIndex < 0 || pointIndex >= pointScales.Length)
        {
            return;
        }

        Tween activeTween = pointPopTweens[pointIndex];
        if (activeTween != null && activeTween.IsActive())
        {
            activeTween.Kill();
        }

        float targetScale = isHovered ? navigationPointHoverScale : 1f;
        Tween hoverTween = DOTween.To(
                () => pointScales[pointIndex],
                scale => SetNavigationPointScale(pointIndex, scale),
                targetScale,
                navigationPointHoverDuration)
            .SetEase(Ease.OutQuad)
            .SetTarget(this);
        pointPopTweens[pointIndex] = hoverTween;

        hoverTween.OnKill(() =>
        {
            if (pointIndex < pointPopTweens.Length
                && ReferenceEquals(pointPopTweens[pointIndex], hoverTween))
            {
                pointPopTweens[pointIndex] = null;
            }
        });
    }

    /// <summary>Returns the world-space centre of a maze cell.</summary>
    private Vector3 GetCellWorldPosition(int column, int row)
    {
        ValidateCell(column, row);
        return transform.TransformPoint(GetCellLocalPosition(column, row));
    }

    /// <summary>Returns an axis-aligned world-space bound around the maze floor.</summary>
    internal Bounds GetWorldBounds()
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
    internal bool TryGetReachablePoint(
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
    private bool CanTravelStraight(int fromPointIndex, int destinationPointIndex)
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
    private void ClearMaze()
    {
        CancelPreparedWallShift();
        KillPointPopTweens();
        navigationPoints.Clear();
        StartPointIndex = -1;
        EndPointIndex = -1;

        if (endpointVisual != null)
        {
            endpointVisual.gameObject.SetActive(false);
        }

        geometryVertices.Clear();
        geometryTriangles.Clear();
        geometryColors.Clear();
        pointVertices.Clear();
        pointTriangles.Clear();
        pointColors.Clear();
        pointPopTweens = Array.Empty<Tween>();
        pointScales = Array.Empty<float>();

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

    private void PositionEndpointVisual()
    {
        if (endpointVisual == null || EndPointIndex < 0)
        {
            return;
        }

        Vector3 endpointPosition = GetNavigationPointWorldPosition(EndPointIndex);
        endpointPosition.z = endpointVisual.position.z;
        endpointVisual.position = endpointPosition;
        endpointVisual.gameObject.SetActive(true);
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
        navigationPointHoverScale = Mathf.Max(1f, navigationPointHoverScale);
        navigationPointHoverDuration = Mathf.Max(0.02f, navigationPointHoverDuration);
        navigationPointPopScale = Mathf.Max(1f, navigationPointPopScale);
        navigationPointPopDuration = Mathf.Max(0.02f, navigationPointPopDuration);
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
        KillPointPopTweens();
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
