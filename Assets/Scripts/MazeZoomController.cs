using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Frames, zooms, and pans the maze inside the screen region below a UI divider.
/// A UI panel hides world geometry above the divider without affecting the HUD.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class MazeZoomController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private MazeGenerator mazeGenerator;
    [SerializeField] private RectTransform dividerLine;
    [SerializeField] private RectTransform topOcclusionPanel;

    [Header("Framing and Zoom")]
    [SerializeField, Min(0f)] private float framingPadding = 0.5f;
    [SerializeField, Min(0.1f)] private float minimumSize = 3.5f;
    [SerializeField, Min(0.1f)] private float maximumSize = 10f;
    [Tooltip("Percentage of the current zoom changed by each wheel event.")]
    [SerializeField, Range(0.01f, 0.5f)] private float zoomStepPerScroll = 0.12f;
    [SerializeField, Min(0f)] private float zoomSmoothTime = 0.08f;

    [Header("Panning")]
    [SerializeField] private bool enablePanning = true;
    [SerializeField, Min(0f)] private float dragThresholdPixels = 8f;
    [SerializeField, Min(0.01f)] private float panSpeed = 1f;
    [SerializeField] private bool constrainToMaze = true;
    [SerializeField, Min(0f)] private float panBoundsPadding = 0.35f;

    [Header("Fallback Input Area")]
    [Tooltip("Used only if no divider line is assigned.")]
    [SerializeField, Range(0.05f, 1f)] private float fallbackViewportTop = 0.78f;

    private readonly Vector3[] dividerCorners = new Vector3[4];
    private Camera controlledCamera;
    private float targetSize;
    private float zoomVelocity;
    private Vector2 pointerPressPosition;
    private Vector2 previousPointerPosition;
    private bool isZooming;
    private bool isTrackingPointer;
    private bool subscribedToMaze;

    public bool IsPanning { get; private set; }
    public bool DidPanOnCurrentRelease { get; private set; }

    private void Awake()
    {
        controlledCamera = GetComponent<Camera>();
        ResolveMazeReference();

        if (!controlledCamera.orthographic)
        {
            Debug.LogWarning("MazeZoomController requires an orthographic camera.", this);
            enabled = false;
        }
    }

    private void OnEnable()
    {
        SubscribeToMaze();
    }

    private void Start()
    {
        ResolveMazeReference();
        SubscribeToMaze();
        SynchronizeOcclusionPanel();

        if (mazeGenerator != null && mazeGenerator.StartPointIndex >= 0)
        {
            FrameMaze();
        }
    }

    private void Update()
    {
        DidPanOnCurrentRelease = false;
        SynchronizeOcclusionPanel();

        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            ApplySmoothZoom();
            return;
        }

        Vector2 pointerPosition = mouse.position.ReadValue();
        HandlePanning(mouse, pointerPosition);
        HandleZoom(mouse, pointerPosition);
        ApplySmoothZoom();
    }

    private void OnDisable()
    {
        UnsubscribeFromMaze();
        isTrackingPointer = false;
        IsPanning = false;
    }

    /// <summary>Returns true when a screen point is below the divider line.</summary>
    public bool IsPointerInsideViewport(Vector2 screenPosition)
    {
        return screenPosition.y <= GetViewportTopScreenY();
    }

    /// <summary>Frames the complete maze inside the visible lower viewport.</summary>
    [ContextMenu("Frame Maze")]
    public void FrameMaze()
    {
        if (mazeGenerator == null || controlledCamera == null)
        {
            return;
        }

        StopZoom();
        Bounds mazeBounds = mazeGenerator.GetWorldBounds();
        float viewportTop = GetViewportTopNormalized();
        float requiredVerticalSize = (mazeBounds.extents.y + framingPadding)
            / Mathf.Max(viewportTop, 0.05f);
        float requiredHorizontalSize = (mazeBounds.extents.x + framingPadding)
            / Mathf.Max(controlledCamera.aspect, 0.05f);

        controlledCamera.orthographicSize = Mathf.Clamp(
            Mathf.Max(requiredVerticalSize, requiredHorizontalSize),
            minimumSize,
            maximumSize);

        Vector3 cameraPosition = controlledCamera.transform.position;
        cameraPosition.x = mazeBounds.center.x;
        cameraPosition.y = mazeBounds.center.y
            + (1f - viewportTop) * controlledCamera.orthographicSize;
        controlledCamera.transform.position = cameraPosition;
        ClampCameraToMaze();
    }

    /// <summary>Stops an active zoom without changing the current camera size.</summary>
    public void StopZoom()
    {
        isZooming = false;
        zoomVelocity = 0f;
    }

    private void HandleZoom(Mouse mouse, Vector2 pointerPosition)
    {
        if (!IsPointerInsideViewport(pointerPosition))
        {
            return;
        }

        float scrollAmount = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scrollAmount) <= 0.01f)
        {
            return;
        }

        if (!isZooming)
        {
            targetSize = controlledCamera.orthographicSize;
        }

        float zoomMultiplier = scrollAmount > 0f
            ? 1f - zoomStepPerScroll
            : 1f / (1f - zoomStepPerScroll);
        targetSize = Mathf.Clamp(targetSize * zoomMultiplier, minimumSize, maximumSize);
        isZooming = true;
    }

    private void HandlePanning(Mouse mouse, Vector2 pointerPosition)
    {
        if (!enablePanning)
        {
            return;
        }

        if (mouse.leftButton.wasPressedThisFrame)
        {
            bool pointerOverUi = EventSystem.current != null
                && EventSystem.current.IsPointerOverGameObject();
            isTrackingPointer = IsPointerInsideViewport(pointerPosition) && !pointerOverUi;
            pointerPressPosition = pointerPosition;
            previousPointerPosition = pointerPosition;
            IsPanning = false;
        }

        if (isTrackingPointer && mouse.leftButton.isPressed)
        {
            if (!IsPanning
                && (pointerPosition - pointerPressPosition).sqrMagnitude
                    >= dragThresholdPixels * dragThresholdPixels)
            {
                IsPanning = true;
                StopZoom();
            }

            if (IsPanning)
            {
                PanCamera(pointerPosition - previousPointerPosition);
            }

            previousPointerPosition = pointerPosition;
        }

        if (mouse.leftButton.wasReleasedThisFrame)
        {
            DidPanOnCurrentRelease = isTrackingPointer && IsPanning;
            isTrackingPointer = false;
            IsPanning = false;
        }
    }

    private void PanCamera(Vector2 screenDelta)
    {
        float worldUnitsPerPixel = controlledCamera.orthographicSize * 2f
            / Mathf.Max(Screen.height, 1);
        Vector3 cameraPosition = controlledCamera.transform.position;
        cameraPosition.x -= screenDelta.x * worldUnitsPerPixel * panSpeed;
        cameraPosition.y -= screenDelta.y * worldUnitsPerPixel * panSpeed;
        controlledCamera.transform.position = cameraPosition;
        ClampCameraToMaze();
    }

    private void ApplySmoothZoom()
    {
        if (!isZooming)
        {
            return;
        }

        float previousSize = controlledCamera.orthographicSize;
        float nextSize = Mathf.SmoothDamp(
            previousSize,
            targetSize,
            ref zoomVelocity,
            zoomSmoothTime,
            Mathf.Infinity,
            Time.unscaledDeltaTime);

        // Keep the centre of the visible lower viewport fixed while zooming.
        float viewportTop = GetViewportTopNormalized();
        Vector3 cameraPosition = controlledCamera.transform.position;
        cameraPosition.y += (1f - viewportTop) * (nextSize - previousSize);
        controlledCamera.orthographicSize = nextSize;
        controlledCamera.transform.position = cameraPosition;
        ClampCameraToMaze();

        if (Mathf.Abs(nextSize - targetSize) <= 0.001f)
        {
            controlledCamera.orthographicSize = targetSize;
            StopZoom();
            ClampCameraToMaze();
        }
    }

    private void ClampCameraToMaze()
    {
        if (!constrainToMaze || mazeGenerator == null)
        {
            return;
        }

        Bounds mazeBounds = mazeGenerator.GetWorldBounds();
        float size = controlledCamera.orthographicSize;
        float viewportTop = GetViewportTopNormalized();
        float halfVisibleWidth = size * controlledCamera.aspect;
        Vector3 cameraPosition = controlledCamera.transform.position;

        float minimumX = mazeBounds.min.x - panBoundsPadding + halfVisibleWidth;
        float maximumX = mazeBounds.max.x + panBoundsPadding - halfVisibleWidth;
        cameraPosition.x = minimumX <= maximumX
            ? Mathf.Clamp(cameraPosition.x, minimumX, maximumX)
            : mazeBounds.center.x;

        float visibleTopOffset = (viewportTop * 2f - 1f) * size;
        float minimumY = mazeBounds.min.y - panBoundsPadding + size;
        float maximumY = mazeBounds.max.y + panBoundsPadding - visibleTopOffset;
        cameraPosition.y = minimumY <= maximumY
            ? Mathf.Clamp(cameraPosition.y, minimumY, maximumY)
            : mazeBounds.center.y + (1f - viewportTop) * size;

        controlledCamera.transform.position = cameraPosition;
    }

    private float GetViewportTopNormalized()
    {
        return Mathf.Clamp(GetViewportTopScreenY() / Mathf.Max(Screen.height, 1), 0.05f, 1f);
    }

    private float GetViewportTopScreenY()
    {
        if (dividerLine == null)
        {
            return Screen.height * fallbackViewportTop;
        }

        dividerLine.GetWorldCorners(dividerCorners);
        Camera canvasCamera = GetCanvasCamera(dividerLine);
        return RectTransformUtility.WorldToScreenPoint(canvasCamera, dividerCorners[0]).y;
    }

    private void SynchronizeOcclusionPanel()
    {
        if (topOcclusionPanel == null || dividerLine == null)
        {
            return;
        }

        Canvas canvas = dividerLine.GetComponentInParent<Canvas>();
        RectTransform canvasRect = canvas != null ? canvas.transform as RectTransform : null;
        if (canvasRect == null)
        {
            return;
        }

        dividerLine.GetWorldCorners(dividerCorners);
        Camera canvasCamera = GetCanvasCamera(dividerLine);
        Vector2 boundaryScreenPoint = RectTransformUtility.WorldToScreenPoint(
            canvasCamera,
            dividerCorners[0]);

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect,
                boundaryScreenPoint,
                canvasCamera,
                out Vector2 boundaryLocalPoint))
        {
            return;
        }

        topOcclusionPanel.anchorMin = new Vector2(0f, 1f);
        topOcclusionPanel.anchorMax = Vector2.one;
        topOcclusionPanel.pivot = new Vector2(0.5f, 1f);
        topOcclusionPanel.anchoredPosition = Vector2.zero;
        topOcclusionPanel.sizeDelta = new Vector2(
            0f,
            Mathf.Max(0f, canvasRect.rect.yMax - boundaryLocalPoint.y));
        topOcclusionPanel.SetAsFirstSibling();
    }

    private static Camera GetCanvasCamera(RectTransform uiElement)
    {
        Canvas canvas = uiElement.GetComponentInParent<Canvas>();
        return canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera
            : null;
    }

    private void ResolveMazeReference()
    {
        if (mazeGenerator == null)
        {
            mazeGenerator = FindFirstObjectByType<MazeGenerator>();
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
        FrameMaze();
    }

    private void OnValidate()
    {
        framingPadding = Mathf.Max(0f, framingPadding);
        minimumSize = Mathf.Max(0.1f, minimumSize);
        maximumSize = Mathf.Max(minimumSize, maximumSize);
        zoomStepPerScroll = Mathf.Clamp(zoomStepPerScroll, 0.01f, 0.5f);
        zoomSmoothTime = Mathf.Max(0f, zoomSmoothTime);
        dragThresholdPixels = Mathf.Max(0f, dragThresholdPixels);
        panSpeed = Mathf.Max(0.01f, panSpeed);
        panBoundsPadding = Mathf.Max(0f, panBoundsPadding);
        fallbackViewportTop = Mathf.Clamp(fallbackViewportTop, 0.05f, 1f);
    }
}
