using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Moves the player between clicked maze points. A destination is valid when it
/// lies anywhere along the player's current unobstructed row or column.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerMovement : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private MazeGenerator mazeGenerator;
    [SerializeField] private Camera inputCamera;
    [SerializeField] private MazeZoomController viewportController;

    [Header("Movement")]
    [SerializeField, Min(0.1f)] private float movementSpeed = 4f;
    [SerializeField, Range(0.1f, 0.5f)] private float selectionRadiusInCells = 0.38f;

    [Header("Movement Line")]
    [SerializeField, Min(0.01f)] private float lineWidth = 0.08f;
    [SerializeField] private Color lineColor = new Color(0.8431f, 0.4941f, 0.3137f, 1f);

    private readonly List<int> trailPointIndices = new List<int>(128);
    private readonly List<Vector3> trailPositions = new List<Vector3>(128);
    private LineRenderer movementLine;
    private Material lineMaterial;
    private int currentPointIndex = -1;
    private int movementStartPointIndex = -1;
    private int destinationPointIndex = -1;
    private int activeTrailPositionIndex = -1;
    private int backtrackTargetTrailIndex = -1;
    private Vector3 destinationPosition;
    private Vector3 activeTrailSegmentStart;
    private Vector3 activeTrailSegmentEnd;
    private bool isMoving;
    private bool isBacktracking;
    private bool hasReachedEnd;
    private bool subscribedToMaze;

    public int CurrentPointIndex => currentPointIndex;
    public bool IsMoving => isMoving;
    public bool HasReachedEnd => hasReachedEnd;

    private void Awake()
    {
        ResolveReferences();
        ConfigureVisuals();
    }

    private void OnEnable()
    {
        SubscribeToMaze();
    }

    private void Start()
    {
        ResolveReferences();
        SubscribeToMaze();

        if (mazeGenerator != null && mazeGenerator.StartPointIndex >= 0)
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
            ReadPointClick();
        }
    }

    private void OnDisable()
    {
        UnsubscribeFromMaze();
    }

    /// <summary>Places the player on the start point and resets its trail.</summary>
    public void SpawnAtMazeStart()
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
        isMoving = false;
        isBacktracking = false;
        hasReachedEnd = false;

        Vector3 startPosition = mazeGenerator.GetNavigationPointWorldPosition(currentPointIndex);
        startPosition.z = transform.position.z;
        transform.position = startPosition;

        trailPointIndices.Clear();
        trailPointIndices.Add(currentPointIndex);
        trailPositions.Clear();
        trailPositions.Add(ToLinePosition(startPosition));
        movementLine.positionCount = 1;
        movementLine.SetPosition(0, trailPositions[0]);
    }

    private void ReadPointClick()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasReleasedThisFrame
            || mazeGenerator == null || inputCamera == null || currentPointIndex < 0
            || hasReachedEnd)
        {
            return;
        }

        Vector2 pointerPosition = mouse.position.ReadValue();
        if (viewportController != null
            && (viewportController.DidPanOnCurrentRelease
                || !viewportController.IsPointerInsideViewport(pointerPosition)))
        {
            return;
        }

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            return;
        }

        float cameraDistance = Mathf.Abs(inputCamera.transform.position.z - transform.position.z);
        Vector3 worldPosition = inputCamera.ScreenToWorldPoint(
            new Vector3(pointerPosition.x, pointerPosition.y, cameraDistance));

        float selectionRadius = mazeGenerator.CellSize * selectionRadiusInCells;
        if (mazeGenerator.TryGetReachablePoint(
                worldPosition,
                currentPointIndex,
                selectionRadius,
                out int clickedPointIndex))
        {
            BeginMovement(clickedPointIndex);
        }
    }

    private void BeginMovement(int pointIndex)
    {
        movementStartPointIndex = currentPointIndex;
        destinationPointIndex = pointIndex;
        destinationPosition = mazeGenerator.GetNavigationPointWorldPosition(pointIndex);
        destinationPosition.z = transform.position.z;
        backtrackTargetTrailIndex = FindTrailPointIndex(pointIndex);
        isBacktracking = backtrackTargetTrailIndex >= 0;
        isMoving = true;

        if (isBacktracking)
        {
            // Collapse the retraced section to one live segment. Its endpoint
            // then moves towards the earlier route point, erasing only that part.
            activeTrailPositionIndex = backtrackTargetTrailIndex + 1;
            activeTrailSegmentStart = destinationPosition;
            activeTrailSegmentEnd = transform.position;
        }
        else
        {
            activeTrailPositionIndex = trailPositions.Count;
            activeTrailSegmentStart = transform.position;
            activeTrailSegmentEnd = destinationPosition;
        }

        UpdateActiveTrailPosition(transform.position);
    }

    private void MoveTowardsDestination()
    {
        transform.position = Vector3.MoveTowards(
            transform.position,
            destinationPosition,
            movementSpeed * Time.deltaTime);
        UpdateActiveTrailPosition(transform.position);

        if ((transform.position - destinationPosition).sqrMagnitude > 0.000001f)
        {
            return;
        }

        transform.position = destinationPosition;

        if (isBacktracking)
        {
            CompleteBacktracking();
        }
        else
        {
            CompleteForwardMovement();
        }

        currentPointIndex = destinationPointIndex;
        hasReachedEnd = currentPointIndex == mazeGenerator.EndPointIndex;
        movementStartPointIndex = -1;
        destinationPointIndex = -1;
        activeTrailPositionIndex = -1;
        backtrackTargetTrailIndex = -1;
        isMoving = false;
        isBacktracking = false;
    }

    private void CompleteForwardMovement()
    {
        MazeNavigationPoint startPoint = mazeGenerator.GetNavigationPoint(movementStartPointIndex);
        MazeNavigationPoint endPoint = mazeGenerator.GetNavigationPoint(destinationPointIndex);
        Vector2Int cellDelta = endPoint.Cell - startPoint.Cell;
        Vector2Int direction = new Vector2Int(
            cellDelta.x == 0 ? 0 : (cellDelta.x > 0 ? 1 : -1),
            cellDelta.y == 0 ? 0 : (cellDelta.y > 0 ? 1 : -1));

        int firstNewPosition = trailPositions.Count;
        Vector2Int cell = startPoint.Cell;
        while (cell != endPoint.Cell)
        {
            cell += direction;
            int pointIndex = GetPointIndexAtCell(cell);
            if (pointIndex < 0)
            {
                Debug.LogError($"No navigation point exists at maze cell {cell}.", this);
                break;
            }

            trailPointIndices.Add(pointIndex);
            trailPositions.Add(ToLinePosition(
                mazeGenerator.GetNavigationPointWorldPosition(pointIndex)));
        }

        movementLine.positionCount = trailPositions.Count;
        for (int positionIndex = firstNewPosition;
             positionIndex < trailPositions.Count;
             positionIndex++)
        {
            movementLine.SetPosition(positionIndex, trailPositions[positionIndex]);
        }
    }

    private void CompleteBacktracking()
    {
        int removeStartIndex = backtrackTargetTrailIndex + 1;
        int removeCount = trailPositions.Count - removeStartIndex;
        if (removeCount > 0)
        {
            trailPointIndices.RemoveRange(removeStartIndex, removeCount);
            trailPositions.RemoveRange(removeStartIndex, removeCount);
        }

        movementLine.positionCount = trailPositions.Count;
    }

    private int FindTrailPointIndex(int pointIndex)
    {
        // Search backwards because only the most recent occurrence belongs to
        // the currently active route if the player has revisited a cell.
        for (int trailIndex = trailPointIndices.Count - 2; trailIndex >= 0; trailIndex--)
        {
            if (trailPointIndices[trailIndex] == pointIndex)
            {
                return trailIndex;
            }
        }

        return -1;
    }

    private int GetPointIndexAtCell(Vector2Int targetCell)
    {
        // A generated maze has one lightweight navigation point per grid cell.
        // Looking through this small array happens only when a move completes.
        IReadOnlyList<MazeNavigationPoint> points = mazeGenerator.NavigationPoints;
        for (int pointIndex = 0; pointIndex < points.Count; pointIndex++)
        {
            if (points[pointIndex].Cell == targetCell)
            {
                return pointIndex;
            }
        }

        return -1;
    }

    private Vector3 GetStableActiveTrailPosition(Vector3 playerPosition)
    {
        Vector3 segment = activeTrailSegmentEnd - activeTrailSegmentStart;
        float segmentLengthSquared = segment.sqrMagnitude;
        if (segmentLengthSquared <= 0.000001f)
        {
            return ToLinePosition(activeTrailSegmentStart);
        }

        // Project onto the exact point-to-point axis. This prevents tiny
        // off-axis transform changes from making a straight trail wobble.
        float progress = Vector3.Dot(
            playerPosition - activeTrailSegmentStart,
            segment) / segmentLengthSquared;
        Vector3 stablePosition = activeTrailSegmentStart
            + segment * Mathf.Clamp01(progress);
        return ToLinePosition(stablePosition);
    }

    private void UpdateActiveTrailPosition(Vector3 playerPosition)
    {
        Vector3 activePosition = GetStableActiveTrailPosition(playerPosition);
        Vector3 previousPosition = trailPositions[activeTrailPositionIndex - 1];
        float minimumSegmentLength = Mathf.Max(lineWidth, 0.001f);

        // A live endpoint can briefly overlap the preceding point when a move
        // starts or when backtracking finishes. LineRenderer cannot form a
        // stable corner from that zero-length segment, so its mesh spikes.
        // Keep the segment hidden while it is short enough to sit under the
        // player, then add it once its direction is well-defined.
        if ((activePosition - previousPosition).sqrMagnitude
            <= minimumSegmentLength * minimumSegmentLength)
        {
            movementLine.positionCount = activeTrailPositionIndex;
            return;
        }

        movementLine.positionCount = activeTrailPositionIndex + 1;
        movementLine.SetPosition(activeTrailPositionIndex, activePosition);
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

    private void ConfigureVisuals()
    {
        movementLine = GetComponent<LineRenderer>();
        if (movementLine == null)
        {
            movementLine = gameObject.AddComponent<LineRenderer>();
        }

        Shader lineShader = Shader.Find("Sprites/Default");
        if (lineShader != null)
        {
            lineMaterial = new Material(lineShader)
            {
                name = "Runtime Player Path Material",
                mainTexture = Texture2D.whiteTexture,
                hideFlags = HideFlags.HideAndDontSave
            };
            movementLine.sharedMaterial = lineMaterial;
        }

        movementLine.useWorldSpace = true;
        movementLine.loop = false;
        movementLine.startWidth = lineWidth;
        movementLine.endWidth = lineWidth;
        movementLine.startColor = lineColor;
        movementLine.endColor = lineColor;
        movementLine.numCapVertices = 8;
        movementLine.numCornerVertices = 0;
        movementLine.alignment = LineAlignment.TransformZ;
        movementLine.textureMode = LineTextureMode.Stretch;
        movementLine.sortingOrder = 1;
        movementLine.positionCount = 0;

        SpriteRenderer playerRenderer = GetComponent<SpriteRenderer>();
        if (playerRenderer != null)
        {
            movementLine.sortingLayerID = playerRenderer.sortingLayerID;
            playerRenderer.sortingOrder = 3;
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
        lineWidth = Mathf.Max(0.01f, lineWidth);

        if (movementLine != null)
        {
            movementLine.startWidth = lineWidth;
            movementLine.endWidth = lineWidth;
            movementLine.startColor = lineColor;
            movementLine.endColor = lineColor;
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
