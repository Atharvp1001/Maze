using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

internal sealed partial class PlayerMovement
{
    private void MoveTowardsDestination()
    {
        transform.position = Vector3.MoveTowards(
            transform.position,
            destinationPosition,
            movementSpeed * Time.deltaTime);
        UpdateActiveTrailPosition(transform.position);
        PopDepartedNavigationPoints();

        if ((transform.position - destinationPosition).sqrMagnitude > 0.000001f)
        {
            return;
        }

        transform.position = destinationPosition;
        PointsTravelled?.Invoke(movementCellCount);

        if (isBacktracking)
        {
            currentPointIndex = trailPointIndices[backtrackNextTrailIndex];
            RemoveLastTrailPoint();

            if (backtrackNextTrailIndex > backtrackTargetTrailIndex)
            {
                backtrackNextTrailIndex--;
                BeginNextBacktrackSegment();
                return;
            }

            if (currentPointIndex != destinationPointIndex)
            {
                // The clicked point lies beyond the part of the old trail that
                // was retraced. The old segment is gone, so this remainder is
                // a clean new forward segment from the junction to the target.
                isBacktracking = false;
                backtrackTargetTrailIndex = -1;
                backtrackNextTrailIndex = -1;
                BeginForwardSegment();
                return;
            }
        }
        else
        {
            CompleteForwardMovement();
            currentPointIndex = destinationPointIndex;
        }

        hasReachedEnd = currentPointIndex == mazeGenerator.EndPointIndex;

        if (hasReachedEnd)
        {
            LevelCompleted?.Invoke();
        }

        movementStartPointIndex = -1;
        destinationPointIndex = -1;
        activeTrailPositionIndex = -1;
        backtrackTargetTrailIndex = -1;
        backtrackNextTrailIndex = -1;
        movementCellCount = 0;
        nextPointPopStep = 0;
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

        int trailPositionOffset = GameplayTrailPositionOffset;
        movementLine.positionCount = trailPositionOffset + trailPositions.Count;
        for (int positionIndex = firstNewPosition;
             positionIndex < trailPositions.Count;
             positionIndex++)
        {
            movementLine.SetPosition(
                trailPositionOffset + positionIndex,
                trailPositions[positionIndex]);
        }
    }

    private void BeginNextBacktrackSegment()
    {
        int nextPointIndex = trailPointIndices[backtrackNextTrailIndex];
        ConfigureMovementSegment(currentPointIndex, nextPointIndex);
        activeTrailPositionIndex = backtrackNextTrailIndex + 1;
        activeTrailSegmentStart = destinationPosition;
        activeTrailSegmentEnd = transform.position;
        UpdateActiveTrailPosition(transform.position);
    }

    private void BeginForwardSegment()
    {
        ConfigureMovementSegment(currentPointIndex, destinationPointIndex);
        activeTrailPositionIndex = trailPositions.Count;
        activeTrailSegmentStart = transform.position;
        activeTrailSegmentEnd = destinationPosition;
        UpdateActiveTrailPosition(transform.position);
    }

    private void ConfigureMovementSegment(int startPointIndex, int endPointIndex)
    {
        movementStartPointIndex = startPointIndex;
        destinationPosition = mazeGenerator.GetNavigationPointWorldPosition(endPointIndex);
        destinationPosition.z = transform.position.z;
        movementOriginPosition = transform.position;

        MazeNavigationPoint startPoint = mazeGenerator.GetNavigationPoint(startPointIndex);
        MazeNavigationPoint endPoint = mazeGenerator.GetNavigationPoint(endPointIndex);
        Vector2Int cellDelta = endPoint.Cell - startPoint.Cell;
        movementCellDirection = new Vector2Int(
            cellDelta.x == 0 ? 0 : (cellDelta.x > 0 ? 1 : -1),
            cellDelta.y == 0 ? 0 : (cellDelta.y > 0 ? 1 : -1));
        movementCellCount = Mathf.Abs(cellDelta.x) + Mathf.Abs(cellDelta.y);
        nextPointPopStep = 0;
    }

    private void RemoveLastTrailPoint()
    {
        int lastTrailIndex = trailPositions.Count - 1;
        if (lastTrailIndex <= backtrackTargetTrailIndex)
        {
            return;
        }

        trailPointIndices.RemoveAt(lastTrailIndex);
        trailPositions.RemoveAt(lastTrailIndex);
        movementLine.positionCount = GameplayTrailPositionOffset
            + trailPositions.Count;
    }

    private int FindBacktrackJunction(int destinationIndex)
    {
        if (trailPointIndices.Count < 2)
        {
            return -1;
        }

        Vector2Int currentCell = mazeGenerator.GetNavigationPoint(currentPointIndex).Cell;
        Vector2Int destinationCell = mazeGenerator.GetNavigationPoint(destinationIndex).Cell;
        Vector2Int cellDelta = destinationCell - currentCell;
        Vector2Int direction = new Vector2Int(
            cellDelta.x == 0 ? 0 : (cellDelta.x > 0 ? 1 : -1),
            cellDelta.y == 0 ? 0 : (cellDelta.y > 0 ? 1 : -1));
        Vector2Int expectedPreviousCell = currentCell + direction;
        int junctionTrailIndex = -1;

        // Only retract the continuous tail that overlaps this new move. For
        // 2 -> 3 followed by a click on 1, this finds point 2, removes 3 -> 2,
        // and lets a new 2 -> 1 segment be drawn without a 180-degree join.
        for (int trailIndex = trailPointIndices.Count - 2; trailIndex >= 0; trailIndex--)
        {
            MazeNavigationPoint trailPoint = mazeGenerator.GetNavigationPoint(
                trailPointIndices[trailIndex]);
            if (trailPoint.Cell != expectedPreviousCell)
            {
                break;
            }

            junctionTrailIndex = trailIndex;
            if (trailPointIndices[trailIndex] == destinationIndex)
            {
                break;
            }

            expectedPreviousCell += direction;
        }

        return junctionTrailIndex;
    }

    private int GetPointIndexAtCell(Vector2Int targetCell)
    {
        // A generated maze has one lightweight navigation point per grid cell.
        // Straight moves only visit a small number of these points at a time.
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

    private void PopDepartedNavigationPoints()
    {
        // The destination stays at its normal size while the player rests on it.
        // It becomes step zero, and pops, only when the following move leaves it.
        if (movementCellCount <= 0 || nextPointPopStep >= movementCellCount)
        {
            return;
        }

        Vector3 movement = destinationPosition - movementOriginPosition;
        float movementLengthSquared = movement.sqrMagnitude;
        float progress = movementLengthSquared <= 0.000001f
            ? 1f
            : Mathf.Clamp01(Vector3.Dot(
                transform.position - movementOriginPosition,
                movement) / movementLengthSquared);
        float departedCellDistance = progress * movementCellCount
            - pointPopDepartureDistanceInCells;
        int lastDepartedStep = Mathf.Min(
            movementCellCount - 1,
            Mathf.FloorToInt(departedCellDistance + 0.0001f));
        MazeNavigationPoint startPoint = mazeGenerator.GetNavigationPoint(movementStartPointIndex);

        while (nextPointPopStep <= lastDepartedStep)
        {
            Vector2Int reachedCell = startPoint.Cell
                + movementCellDirection * nextPointPopStep;
            int pointIndex = GetPointIndexAtCell(reachedCell);
            if (pointIndex >= 0)
            {
                mazeGenerator.PopNavigationPoint(pointIndex);
            }

            nextPointPopStep++;
        }
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
}
