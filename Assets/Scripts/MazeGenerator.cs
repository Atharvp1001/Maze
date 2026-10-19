using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Generates a perfect 2D maze and a lightweight point-to-point navigation
/// graph. The complete maze and all visible points use only two render meshes.
/// No physics colliders are required.
/// </summary>
internal sealed class MazeGenerator : MonoBehaviour
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
    private const float MediumExtraPassageRatio = 0.025f;
    private const float HardExtraPassageRatio = 0.05f;
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
    private Vector2Int[] activeCells;
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

    private int Columns => columns;
    private int Rows => rows;
    internal float CellSize => cellSize;
    private int currentLevelSeed;
    private MazeDifficulty currentDifficulty;
    private bool hasCurrentLevel;
    private bool hasPreparedShift;
    private bool preparedPassageIsOpen;
    private LoopCandidate preparedEdgeToOpen;
    private LoopCandidate preparedEdgeToClose;
    internal int StartPointIndex { get; private set; } = -1;
    internal int EndPointIndex { get; private set; } = -1;
    private Vector3 StartWorldPosition => GetCellWorldPosition(0, 0);
    private Vector3 ExitWorldPosition => GetCellWorldPosition(columns - 1, rows - 1);
    internal IReadOnlyList<MazeNavigationPoint> NavigationPoints => navigationPoints;

    internal void SetDimensions(int newColumns, int newRows)
    {
        columns = Mathf.Max(2, newColumns);
        rows = Mathf.Max(2, newRows);
    }

    /// <summary>
    /// Selects one local wall exchange without changing the maze yet.
    /// The player's trail and nearby cells are excluded.
    /// </summary>
    internal bool PrepareWallShift(
        Vector2Int playerCell,
        IReadOnlyList<Vector2Int> protectedTrail,
        int safeRadius)
    {
        if (!hasCurrentLevel || walls == null || !IsInside(playerCell))
        {
            return false;
        }

        CancelPreparedWallShift();
        safeRadius = Mathf.Max(1, safeRadius);
        CollectShiftCandidates(playerCell, protectedTrail, safeRadius);
        if (shiftOpenEdges.Count == 0 || shiftClosedEdges.Count == 0)
        {
            return false;
        }

        int maximumAttempts = Mathf.Min(
            512,
            shiftOpenEdges.Count * shiftClosedEdges.Count);

        for (int attempt = 0; attempt < maximumAttempts; attempt++)
        {
            LoopCandidate edgeToOpen = shiftClosedEdges[
                UnityEngine.Random.Range(0, shiftClosedEdges.Count)];
            LoopCandidate edgeToClose = shiftOpenEdges[
                UnityEngine.Random.Range(0, shiftOpenEdges.Count)];

            if (!EdgesShareCell(edgeToOpen, edgeToClose))
            {
                continue;
            }

            SetEdgeOpen(edgeToOpen, true);
            SetEdgeOpen(edgeToClose, false);
            bool remainsConnected = IsEntireMazeReachable(playerCell);
            SetEdgeOpen(edgeToClose, true);
            SetEdgeOpen(edgeToOpen, false);

            if (!remainsConnected)
            {
                continue;
            }

            preparedEdgeToOpen = edgeToOpen;
            preparedEdgeToClose = edgeToClose;
            hasPreparedShift = true;
            preparedPassageIsOpen = false;
            ShowPreparedShiftWarnings();
            return true;
        }

        return false;
    }

    internal void SetShiftWarningAlpha(float alpha)
    {
        // Opening an edge removes its existing wall, so preview it in red.
        SetWarningLineAlpha(shiftOpenWarning, new Color(1f, 0.12f, 0.08f), alpha);

        // Closing an edge creates a new wall, so preview it in green.
        SetWarningLineAlpha(shiftCloseWarning, new Color(0.12f, 0.8f, 0.3f), alpha);
    }

    internal void OpenPreparedShiftPassage()
    {
        if (!hasPreparedShift || preparedPassageIsOpen)
        {
            return;
        }

        SetEdgeOpen(preparedEdgeToOpen, true);
        preparedPassageIsOpen = true;
        BuildMazeMesh();
        BuildNavigationData();
        if (shiftOpenWarning != null)
        {
            shiftOpenWarning.enabled = false;
        }
    }

    internal void CompletePreparedWallShift()
    {
        if (!hasPreparedShift)
        {
            return;
        }

        if (!preparedPassageIsOpen)
        {
            SetEdgeOpen(preparedEdgeToOpen, true);
        }

        SetEdgeOpen(preparedEdgeToClose, false);
        BuildMazeMesh();
        BuildNavigationData();
        ClearPreparedShift();
        WallLayoutChanged?.Invoke(this);
    }

    internal void CancelPreparedWallShift()
    {
        if (hasPreparedShift && preparedPassageIsOpen)
        {
            SetEdgeOpen(preparedEdgeToOpen, false);
            BuildMazeMesh();
            BuildNavigationData();
        }

        ClearPreparedShift();
    }

    /// <summary>Raised after geometry and navigation data are ready.</summary>
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
        hasCurrentLevel = true;
        GenerateCurrentLevel();
    }

    /// <summary>Builds a fresh level using the difficulty selected by the player.</summary>
    internal void GenerateNewLevel(MazeDifficulty difficulty)
    {
        currentLevelSeed = useRandomSeed ? CreateRandomSeed() : seed;
        currentDifficulty = difficulty;
        hasCurrentLevel = true;
        GenerateCurrentLevel();
    }

    /// <summary>Builds a level from persistent settings and an optional shifted layout.</summary>
    internal void GenerateSavedLevel(
        MazeDifficulty difficulty,
        int levelSeed,
        string savedWallLayout)
    {
        currentLevelSeed = levelSeed;
        currentDifficulty = difficulty;
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

        System.Random random = new System.Random(currentLevelSeed);
        CarvePassages(random);
        AddDifficultyLoops(random);
        RestoreWallLayout(savedWallLayout);
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

    private void EnsureGenerationBuffers()
    {
        if (walls == null || walls.GetLength(0) != columns || walls.GetLength(1) != rows)
        {
            walls = new Wall[columns, rows];
            visited = new bool[columns, rows];
            pointIndexByCell = new int[columns, rows];
            activeCells = new Vector2Int[columns * rows];
        }
    }

    private void ResetMazeWalls()
    {
        for (int x = 0; x < columns; x++)
        {
            for (int y = 0; y < rows; y++)
            {
                walls[x, y] = Wall.All;
                visited[x, y] = false;
            }
        }
    }

    private void CarvePassages(System.Random random)
    {
        ResetMazeWalls();

        int stackCount = 1;
        activeCells[0] = Vector2Int.zero;
        visited[0, 0] = true;

        while (stackCount > 0)
        {
            Vector2Int current = activeCells[stackCount - 1];
            int choiceCount = GetUnvisitedNeighbours(current);

            if (choiceCount == 0)
            {
                stackCount--;
                continue;
            }

            Neighbour next = neighbourBuffer[random.Next(choiceCount)];
            walls[current.x, current.y] &= ~next.Direction;
            walls[next.Position.x, next.Position.y] &= ~next.OppositeDirection;
            visited[next.Position.x, next.Position.y] = true;
            activeCells[stackCount++] = next.Position;
        }
    }

    private void AddDifficultyLoops(System.Random random)
    {
        float extraPassageRatio;
        switch (currentDifficulty)
        {
            case MazeDifficulty.Easy:
                return;
            case MazeDifficulty.Medium:
                extraPassageRatio = MediumExtraPassageRatio;
                break;
            case MazeDifficulty.Hard:
                extraPassageRatio = HardExtraPassageRatio;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        CollectLoopCandidates();
        int passageCount = Mathf.Min(
            loopCandidates.Count,
            Mathf.Max(1, Mathf.RoundToInt(columns * rows * extraPassageRatio)));

        for (int index = 0; index < passageCount; index++)
        {
            int randomIndex = random.Next(index, loopCandidates.Count);
            LoopCandidate candidate = loopCandidates[randomIndex];
            loopCandidates[randomIndex] = loopCandidates[index];
            loopCandidates[index] = candidate;

            walls[candidate.Cell.x, candidate.Cell.y] &= ~candidate.Direction;
            walls[candidate.NeighbourCell.x, candidate.NeighbourCell.y]
                &= ~candidate.OppositeDirection;
        }
    }

    private void CollectLoopCandidates()
    {
        loopCandidates.Clear();

        for (int x = 0; x < columns; x++)
        {
            for (int y = 0; y < rows; y++)
            {
                Vector2Int cell = new Vector2Int(x, y);

                if (x + 1 < columns && HasWall(x, y, Wall.East))
                {
                    loopCandidates.Add(new LoopCandidate(
                        cell,
                        cell + Vector2Int.right,
                        Wall.East,
                        Wall.West));
                }

                if (y + 1 < rows && HasWall(x, y, Wall.North))
                {
                    loopCandidates.Add(new LoopCandidate(
                        cell,
                        cell + Vector2Int.up,
                        Wall.North,
                        Wall.South));
                }
            }
        }
    }

    private void CollectShiftCandidates(
        Vector2Int playerCell,
        IReadOnlyList<Vector2Int> protectedTrail,
        int safeRadius)
    {
        shiftOpenEdges.Clear();
        shiftClosedEdges.Clear();

        for (int x = 0; x < columns; x++)
        {
            for (int y = 0; y < rows; y++)
            {
                Vector2Int cell = new Vector2Int(x, y);

                if (x + 1 < columns)
                {
                    AddShiftCandidate(
                        new LoopCandidate(
                            cell,
                            cell + Vector2Int.right,
                            Wall.East,
                            Wall.West),
                        playerCell,
                        protectedTrail,
                        safeRadius);
                }

                if (y + 1 < rows)
                {
                    AddShiftCandidate(
                        new LoopCandidate(
                            cell,
                            cell + Vector2Int.up,
                            Wall.North,
                            Wall.South),
                        playerCell,
                        protectedTrail,
                        safeRadius);
                }
            }
        }
    }

    private void AddShiftCandidate(
        LoopCandidate candidate,
        Vector2Int playerCell,
        IReadOnlyList<Vector2Int> protectedTrail,
        int safeRadius)
    {
        if (ManhattanDistance(candidate.Cell, playerCell) <= safeRadius
            || ManhattanDistance(candidate.NeighbourCell, playerCell) <= safeRadius)
        {
            return;
        }

        bool hasWall = HasWall(
            candidate.Cell.x,
            candidate.Cell.y,
            candidate.Direction);

        if (hasWall)
        {
            shiftClosedEdges.Add(candidate);
        }
        else if (!IsProtectedTrailEdge(candidate, protectedTrail))
        {
            shiftOpenEdges.Add(candidate);
        }
    }

    private static bool IsProtectedTrailEdge(
        LoopCandidate candidate,
        IReadOnlyList<Vector2Int> protectedTrail)
    {
        if (protectedTrail == null)
        {
            return false;
        }

        for (int index = 1; index < protectedTrail.Count; index++)
        {
            Vector2Int first = protectedTrail[index - 1];
            Vector2Int second = protectedTrail[index];
            if ((first == candidate.Cell && second == candidate.NeighbourCell)
                || (second == candidate.Cell && first == candidate.NeighbourCell))
            {
                return true;
            }
        }

        return false;
    }

    private static bool EdgesShareCell(LoopCandidate first, LoopCandidate second)
    {
        return first.Cell == second.Cell
            || first.Cell == second.NeighbourCell
            || first.NeighbourCell == second.Cell
            || first.NeighbourCell == second.NeighbourCell;
    }

    private void SetEdgeOpen(LoopCandidate candidate, bool isOpen)
    {
        if (isOpen)
        {
            walls[candidate.Cell.x, candidate.Cell.y] &= ~candidate.Direction;
            walls[candidate.NeighbourCell.x, candidate.NeighbourCell.y]
                &= ~candidate.OppositeDirection;
            return;
        }

        walls[candidate.Cell.x, candidate.Cell.y] |= candidate.Direction;
        walls[candidate.NeighbourCell.x, candidate.NeighbourCell.y]
            |= candidate.OppositeDirection;
    }

    private bool IsEntireMazeReachable(Vector2Int startCell)
    {
        Array.Clear(visited, 0, visited.Length);
        int queueStart = 0;
        int queueEnd = 1;
        int visitedCount = 1;
        activeCells[0] = startCell;
        visited[startCell.x, startCell.y] = true;

        while (queueStart < queueEnd)
        {
            Vector2Int cell = activeCells[queueStart++];

            foreach (Vector2Int direction in CardinalDirections)
            {
                Vector2Int neighbour = cell + direction;
                if (!IsInside(neighbour)
                    || visited[neighbour.x, neighbour.y]
                    || !CanMove(cell, direction))
                {
                    continue;
                }

                visited[neighbour.x, neighbour.y] = true;
                activeCells[queueEnd++] = neighbour;
                visitedCount++;
            }
        }

        return visitedCount == columns * rows
            && visited[columns - 1, rows - 1];
    }

    private static int ManhattanDistance(Vector2Int first, Vector2Int second)
    {
        return Mathf.Abs(first.x - second.x) + Mathf.Abs(first.y - second.y);
    }

    private void ShowPreparedShiftWarnings()
    {
        ConfigureWarningLine(shiftOpenWarning, preparedEdgeToOpen);
        ConfigureWarningLine(shiftCloseWarning, preparedEdgeToClose);
        SetShiftWarningAlpha(1f);
    }

    private void ConfigureWarningLine(LineRenderer line, LoopCandidate edge)
    {
        if (line == null)
        {
            return;
        }

        Vector3 firstCell = GetCellLocalPosition(edge.Cell.x, edge.Cell.y);
        Vector3 secondCell = GetCellLocalPosition(
            edge.NeighbourCell.x,
            edge.NeighbourCell.y);
        Vector3 centre = (firstCell + secondCell) * 0.5f;
        Vector3 tangent = edge.Direction == Wall.East || edge.Direction == Wall.West
            ? Vector3.up
            : Vector3.right;
        float halfLength = cellSize * 0.42f;
        centre.z = -0.08f;
        line.positionCount = 2;
        line.SetPosition(0, centre - tangent * halfLength);
        line.SetPosition(1, centre + tangent * halfLength);
        line.enabled = true;
    }

    private static void SetWarningLineAlpha(
        LineRenderer line,
        Color baseColor,
        float alpha)
    {
        if (line == null)
        {
            return;
        }

        Color color = new Color(
            baseColor.r,
            baseColor.g,
            baseColor.b,
            Mathf.Clamp01(alpha));
        line.startColor = color;
        line.endColor = color;
    }

    private void ClearPreparedShift()
    {
        hasPreparedShift = false;
        preparedPassageIsOpen = false;

        if (shiftOpenWarning != null)
        {
            shiftOpenWarning.enabled = false;
        }

        if (shiftCloseWarning != null)
        {
            shiftCloseWarning.enabled = false;
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
                || child == shiftOpenTransform || child == shiftCloseTransform)
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
