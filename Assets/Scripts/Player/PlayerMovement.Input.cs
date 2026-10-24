using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

internal sealed partial class PlayerMovement
{
    private void ReadPointClick()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasReleasedThisFrame
            || mazeGenerator == null || inputCamera == null || currentPointIndex < 0
            || hasReachedEnd || inputLocked)
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
        ClearPointHover();
        movementStartPointIndex = currentPointIndex;
        destinationPointIndex = pointIndex;
        backtrackTargetTrailIndex = FindBacktrackJunction(pointIndex);
        isBacktracking = backtrackTargetTrailIndex >= 0;
        isMoving = true;

        if (isBacktracking)
        {
            // Retrace the recorded route point by point. Collapsing the whole
            // return path into one segment can create diagonal shortcuts and
            // unstable LineRenderer joins when the trail contains corners.
            backtrackNextTrailIndex = trailPointIndices.Count - 2;
            BeginNextBacktrackSegment();
        }
        else
        {
            BeginForwardSegment();
        }
    }

    private void UpdatePointHover()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || mazeGenerator == null || inputCamera == null
            || currentPointIndex < 0 || hasReachedEnd || inputLocked)
        {
            ClearPointHover();
            return;
        }

        Vector2 pointerPosition = mouse.position.ReadValue();
        if (viewportController != null
            && !viewportController.IsPointerInsideViewport(pointerPosition))
        {
            ClearPointHover();
            return;
        }

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            ClearPointHover();
            return;
        }

        float cameraDistance = Mathf.Abs(inputCamera.transform.position.z - transform.position.z);
        Vector3 worldPosition = inputCamera.ScreenToWorldPoint(
            new Vector3(pointerPosition.x, pointerPosition.y, cameraDistance));
        float selectionRadius = mazeGenerator.CellSize * selectionRadiusInCells;

        int nextHoveredPoint = mazeGenerator.TryGetReachablePoint(
            worldPosition,
            currentPointIndex,
            selectionRadius,
            out int pointIndex)
            ? pointIndex
            : -1;

        if (nextHoveredPoint == hoveredPointIndex)
        {
            return;
        }

        ClearPointHover();
        hoveredPointIndex = nextHoveredPoint;
        if (hoveredPointIndex >= 0)
        {
            mazeGenerator.SetNavigationPointHovered(hoveredPointIndex, true);
        }
    }

    private void ClearPointHover()
    {
        if (hoveredPointIndex < 0)
        {
            return;
        }

        if (mazeGenerator != null)
        {
            mazeGenerator.SetNavigationPointHovered(hoveredPointIndex, false);
        }

        hoveredPointIndex = -1;
    }
}
