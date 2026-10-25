using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Moves the player between clicked maze points. A destination is valid when it
/// lies anywhere along the player's current unobstructed row or column.
/// </summary>
[DisallowMultipleComponent]
internal sealed partial class PlayerMovement : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private MazeGenerator mazeGenerator;
    [SerializeField] private Camera inputCamera;
    [SerializeField] private MazeZoomController viewportController;

    [Header("Movement")]
    [SerializeField, Min(0.1f)] private float movementSpeed = 4f;
    [SerializeField, Range(0.1f, 0.5f)] private float selectionRadiusInCells = 0.38f;
    [SerializeField, Range(0.05f, 0.75f)] private float pointPopDepartureDistanceInCells = 0.35f;

    [Header("Movement Line")]
    [SerializeField, Min(0.01f)] private float lineWidth = 0.08f;
    [SerializeField] private Color lineColor = new Color(0.8431f, 0.4941f, 0.3137f, 1f);
    [SerializeField, Range(0, 16)] private int lineCornerVertices = 8;

    private readonly List<int> trailPointIndices = new List<int>(128);
    private readonly List<Vector3> trailPositions = new List<Vector3>(128);
    private LineRenderer movementLine;
    private Material lineMaterial;
    private int currentPointIndex = -1;
    private int movementStartPointIndex = -1;
    private int destinationPointIndex = -1;
    private int activeTrailPositionIndex = -1;
    private int backtrackTargetTrailIndex = -1;
    private int backtrackNextTrailIndex = -1;
    private int hoveredPointIndex = -1;
    private Vector3 destinationPosition;
    private Vector3 movementOriginPosition;
    private Vector3 activeTrailSegmentStart;
    private Vector3 activeTrailSegmentEnd;
    private Vector2Int movementCellDirection;
    private int movementCellCount;
    private int nextPointPopStep;
    private bool isMoving;
    private bool isBacktracking;
    private bool hasReachedEnd;
    private bool subscribedToMaze;
    private bool inputLocked;
    private bool hasEntranceTrail;
    private Vector3 entranceTrailStartPosition;

    private int CurrentPointIndex => currentPointIndex;
    private bool IsMoving => isMoving;
    private bool HasReachedEnd => hasReachedEnd;
    internal bool IsCurrentlyMoving => isMoving;
    internal float MovementSpeed => movementSpeed;
    private int GameplayTrailPositionOffset => hasEntranceTrail ? 1 : 0;

    internal void SetInputLocked(bool isLocked)
    {
        inputLocked = isLocked;
        if (isLocked)
        {
            ClearPointHover();
        }
    }

    internal bool TryGetCurrentCell(out Vector2Int cell)
    {
        if (mazeGenerator == null || currentPointIndex < 0
            || currentPointIndex >= mazeGenerator.NavigationPoints.Count)
        {
            cell = default;
            return false;
        }

        cell = mazeGenerator.GetNavigationPoint(currentPointIndex).Cell;
        return true;
    }

    internal void CopyTrailCells(List<Vector2Int> destination)
    {
        destination.Clear();

        for (int trailIndex = 0; trailIndex < trailPointIndices.Count; trailIndex++)
        {
            int pointIndex = trailPointIndices[trailIndex];
            if (pointIndex >= 0 && pointIndex < mazeGenerator.NavigationPoints.Count)
            {
                destination.Add(mazeGenerator.GetNavigationPoint(pointIndex).Cell);
            }
        }
    }

    internal void CopyShiftProtectedCells(List<Vector2Int> destination)
    {
        CopyTrailCells(destination);
        if (!isMoving || movementStartPointIndex < 0 || movementCellCount <= 0)
        {
            return;
        }

        Vector2Int startCell = mazeGenerator.GetNavigationPoint(
            movementStartPointIndex).Cell;
        for (int step = 1; step <= movementCellCount; step++)
        {
            Vector2Int protectedCell = startCell + movementCellDirection * step;
            if (destination.Count == 0
                || destination[destination.Count - 1] != protectedCell)
            {
                destination.Add(protectedCell);
            }
        }
    }

    private bool TryGetTrailPoint(
        Vector3 worldPosition,
        float maximumWorldDistance,
        out int pointIndex,
        out int trailIndex)
    {
        pointIndex = -1;
        trailIndex = -1;
        float closestDistanceSquared = maximumWorldDistance * maximumWorldDistance;

        // The final item is the player's current point, so only earlier trail
        // points are valid backtracking destinations.
        for (int index = trailPositions.Count - 2; index >= 0; index--)
        {
            Vector2 delta = (Vector2)(trailPositions[index] - worldPosition);
            float distanceSquared = delta.sqrMagnitude;
            if (distanceSquared > closestDistanceSquared
                || (pointIndex >= 0 && distanceSquared >= closestDistanceSquared))
            {
                continue;
            }

            closestDistanceSquared = distanceSquared;
            pointIndex = trailPointIndices[index];
            trailIndex = index;
        }

        return trailIndex >= 0;
    }

    internal event Action LevelCompleted;
    internal event Action<int> PointsTravelled;

    private void Awake()
    {
        ResolveReferences();
        ConfigureVisuals();
    }

    private void OnEnable()
    {
        RemoveLegacyEntranceTrailObject();
        SubscribeToMaze();
    }

    private void Start()
    {
        ResolveReferences();
        SubscribeToMaze();

        if (mazeGenerator != null && mazeGenerator.StartPointIndex >= 0
            && currentPointIndex < 0)
        {
            SpawnAtMazeStart();
        }
    }

    private void Update()
    {
        if (isMoving)
        {
            MoveTowardsDestination();
        }
    }

    private void LateUpdate()
    {
        if (!isMoving)
        {
            UpdatePointHover();
            ReadPointClick();
        }
    }

    private void OnDisable()
    {
        ClearPointHover();
        UnsubscribeFromMaze();
    }

    /// <summary>Places the player on the start point and resets its trail.</summary>
    private void SpawnAtMazeStart()
    {
        if (mazeGenerator == null || mazeGenerator.StartPointIndex < 0)
        {
            return;
        }

        currentPointIndex = mazeGenerator.StartPointIndex;
        movementStartPointIndex = -1;
        destinationPointIndex = -1;
        activeTrailPositionIndex = -1;
        backtrackTargetTrailIndex = -1;
        backtrackNextTrailIndex = -1;
        movementCellCount = 0;
        nextPointPopStep = 0;
        isMoving = false;
        isBacktracking = false;
        hasReachedEnd = false;
        hoveredPointIndex = -1;

        Vector3 startPosition = mazeGenerator.GetNavigationPointWorldPosition(currentPointIndex);
        startPosition.z = transform.position.z;
        transform.position = startPosition;

        trailPointIndices.Clear();
        trailPointIndices.Add(currentPointIndex);
        trailPositions.Clear();
        trailPositions.Add(ToLinePosition(startPosition));
        hasEntranceTrail = false;
        entranceTrailStartPosition = default;
        movementLine.positionCount = 1;
        movementLine.SetPosition(0, trailPositions[0]);
    }

    private void ResolveReferences()
    {
        if (mazeGenerator == null)
        {
            mazeGenerator = FindFirstObjectByType<MazeGenerator>();
        }

        if (inputCamera == null)
        {
            inputCamera = Camera.main;
        }

        if (viewportController == null && inputCamera != null)
        {
            viewportController = inputCamera.GetComponent<MazeZoomController>();
        }
    }

    private void SubscribeToMaze()
    {
        if (!subscribedToMaze && mazeGenerator != null)
        {
            mazeGenerator.MazeGenerated += HandleMazeGenerated;
            subscribedToMaze = true;
        }
    }

    private void UnsubscribeFromMaze()
    {
        if (subscribedToMaze && mazeGenerator != null)
        {
            mazeGenerator.MazeGenerated -= HandleMazeGenerated;
            subscribedToMaze = false;
        }
    }

    private void HandleMazeGenerated(MazeGenerator generatedMaze)
    {
        mazeGenerator = generatedMaze;
        SpawnAtMazeStart();
    }

    private static Vector3 ToLinePosition(Vector3 position)
    {
        position.z = -0.03f;
        return position;
    }

    private void OnValidate()
    {
        movementSpeed = Mathf.Max(0.1f, movementSpeed);
        selectionRadiusInCells = Mathf.Clamp(selectionRadiusInCells, 0.1f, 0.5f);
        pointPopDepartureDistanceInCells = Mathf.Clamp(
            pointPopDepartureDistanceInCells,
            0.05f,
            0.75f);
        lineWidth = Mathf.Max(0.01f, lineWidth);
        lineCornerVertices = Mathf.Clamp(lineCornerVertices, 0, 16);

        if (movementLine != null)
        {
            movementLine.startWidth = lineWidth;
            movementLine.endWidth = lineWidth;
            movementLine.startColor = lineColor;
            movementLine.endColor = lineColor;
            movementLine.numCornerVertices = lineCornerVertices;
        }
    }

    private void OnDestroy()
    {
        if (lineMaterial == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(lineMaterial);
        }
        else
        {
            DestroyImmediate(lineMaterial);
        }
    }
}
