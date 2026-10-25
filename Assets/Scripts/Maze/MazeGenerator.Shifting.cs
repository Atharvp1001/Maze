using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

internal sealed partial class MazeGenerator
{
    internal bool PrepareWallShift(
        Vector2Int playerCell,
        IReadOnlyList<Vector2Int> protectedTrail,
        int exchangeCount,
        int targetedExchangeCount)
    {
        if (!hasCurrentLevel || walls == null || !IsInside(playerCell))
        {
            return false;
        }

        CancelPreparedWallShift();
        exchangeCount = Mathf.Max(1, exchangeCount);
        targetedExchangeCount = Mathf.Clamp(
            targetedExchangeCount,
            0,
            exchangeCount);

        for (int exchangeIndex = 0; exchangeIndex < exchangeCount; exchangeIndex++)
        {
            CollectShiftCandidates(protectedTrail);
            bool shouldTargetRoute = exchangeIndex < targetedExchangeCount;
            if (shouldTargetRoute)
            {
                CollectTargetCloseEdges(playerCell);
            }

            bool exchangePrepared = shouldTargetRoute
                && TryPrepareWallExchange(playerCell, shiftTargetCloseEdges);

            // If a useful route change is not available, keep the requested
            // amount of maze activity by falling back to a safe random pair.
            if (!exchangePrepared)
            {
                exchangePrepared = TryPrepareWallExchange(
                    playerCell,
                    shiftOpenEdges);
            }

            if (!exchangePrepared)
            {
                break;
            }
        }

        // Selection is validated cumulatively, then restored until the warning ends.
        for (int index = preparedEdgesToOpen.Count - 1; index >= 0; index--)
        {
            SetEdgeOpen(preparedEdgesToClose[index], true);
            SetEdgeOpen(preparedEdgesToOpen[index], false);
        }

        if (preparedEdgesToOpen.Count == 0)
        {
            return false;
        }

        hasPreparedShift = true;
        preparedPassageIsOpen = false;
        ShowPreparedShiftWarnings();
        return true;
    }

    internal bool ValidatePreparedWallShift(
        Vector2Int playerCell,
        IReadOnlyList<Vector2Int> protectedTrail)
    {
        if (!hasPreparedShift || preparedPassageIsOpen
            || !IsInside(playerCell))
        {
            return false;
        }

        List<LoopCandidate> candidateOpenEdges = new List<LoopCandidate>(
            preparedEdgesToOpen);
        List<LoopCandidate> candidateCloseEdges = new List<LoopCandidate>(
            preparedEdgesToClose);
        preparedEdgesToOpen.Clear();
        preparedEdgesToClose.Clear();

        for (int index = 0; index < candidateOpenEdges.Count; index++)
        {
            LoopCandidate edgeToOpen = candidateOpenEdges[index];
            LoopCandidate edgeToClose = candidateCloseEdges[index];
            if (IsProtectedTrailEdge(edgeToClose, protectedTrail))
            {
                continue;
            }

            SetEdgeOpen(edgeToOpen, true);
            SetEdgeOpen(edgeToClose, false);
            if (!IsEntireMazeReachable(playerCell))
            {
                SetEdgeOpen(edgeToClose, true);
                SetEdgeOpen(edgeToOpen, false);
                continue;
            }

            preparedEdgesToOpen.Add(edgeToOpen);
            preparedEdgesToClose.Add(edgeToClose);
        }

        // Validation is cumulative. Restore the live layout until the warning
        // finishes, just as preparation does.
        for (int index = preparedEdgesToOpen.Count - 1; index >= 0; index--)
        {
            SetEdgeOpen(preparedEdgesToClose[index], true);
            SetEdgeOpen(preparedEdgesToOpen[index], false);
        }

        if (preparedEdgesToOpen.Count == 0)
        {
            ClearPreparedShift();
            return false;
        }

        ShowPreparedShiftWarnings();
        return true;
    }

    internal void SetShiftWarningAlpha(float alpha)
    {
        // Opening an edge removes its existing wall, so preview it in red.
        foreach (LineRenderer warningLine in shiftOpenWarningLines)
        {
            SetWarningLineAlpha(warningLine, new Color(1f, 0.12f, 0.08f), alpha);
        }

        // Closing an edge creates a new wall, so preview it in green.
        foreach (LineRenderer warningLine in shiftCloseWarningLines)
        {
            SetWarningLineAlpha(warningLine, new Color(0.12f, 0.8f, 0.3f), alpha);
        }
    }

    internal void OpenPreparedShiftPassage()
    {
        if (!hasPreparedShift || preparedPassageIsOpen)
        {
            return;
        }

        foreach (LoopCandidate edgeToOpen in preparedEdgesToOpen)
        {
            SetEdgeOpen(edgeToOpen, true);
        }

        preparedPassageIsOpen = true;
        BuildMazeMesh();
        BuildNavigationData();
        foreach (LineRenderer warningLine in shiftOpenWarningLines)
        {
            warningLine.enabled = false;
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
            foreach (LoopCandidate edgeToOpen in preparedEdgesToOpen)
            {
                SetEdgeOpen(edgeToOpen, true);
            }
        }

        foreach (LoopCandidate edgeToClose in preparedEdgesToClose)
        {
            SetEdgeOpen(edgeToClose, false);
        }

        BuildMazeMesh();
        BuildNavigationData();
        ClearPreparedShift();
        WallLayoutChanged?.Invoke(this);
    }

    internal void CancelPreparedWallShift()
    {
        if (hasPreparedShift && preparedPassageIsOpen)
        {
            foreach (LoopCandidate edgeToOpen in preparedEdgesToOpen)
            {
                SetEdgeOpen(edgeToOpen, false);
            }

            BuildMazeMesh();
            BuildNavigationData();
        }

        ClearPreparedShift();
    }

    /// <summary>Raised after geometry and navigation data are ready.</summary>

    private void CollectShiftCandidates(
        IReadOnlyList<Vector2Int> protectedTrail)
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
                        protectedTrail);
                }

                if (y + 1 < rows)
                {
                    AddShiftCandidate(
                        new LoopCandidate(
                            cell,
                            cell + Vector2Int.up,
                            Wall.North,
                            Wall.South),
                        protectedTrail);
                }
            }
        }
    }

    private bool TryPrepareWallExchange(
        Vector2Int playerCell,
        IReadOnlyList<LoopCandidate> closeCandidates)
    {
        if (shiftClosedEdges.Count == 0 || closeCandidates.Count == 0)
        {
            return false;
        }

        int maximumAttempts = Mathf.Min(
            512,
            shiftClosedEdges.Count * closeCandidates.Count);
        for (int attempt = 0; attempt < maximumAttempts; attempt++)
        {
            LoopCandidate edgeToOpen = shiftClosedEdges[
                UnityEngine.Random.Range(0, shiftClosedEdges.Count)];
            LoopCandidate edgeToClose = closeCandidates[
                UnityEngine.Random.Range(0, closeCandidates.Count)];

            if (!EdgesShareCell(edgeToOpen, edgeToClose)
                || IsAlreadyPrepared(edgeToOpen)
                || IsAlreadyPrepared(edgeToClose))
            {
                continue;
            }

            SetEdgeOpen(edgeToOpen, true);
            SetEdgeOpen(edgeToClose, false);
            if (!IsEntireMazeReachable(playerCell))
            {
                SetEdgeOpen(edgeToClose, true);
                SetEdgeOpen(edgeToOpen, false);
                continue;
            }

            preparedEdgesToOpen.Add(edgeToOpen);
            preparedEdgesToClose.Add(edgeToClose);
            return true;
        }

        return false;
    }

    private void CollectTargetCloseEdges(Vector2Int playerCell)
    {
        shiftRouteEdges.Clear();
        shiftTargetCloseEdges.Clear();
        Vector2Int exitCell = new Vector2Int(columns - 1, rows - 1);
        if (playerCell == exitCell)
        {
            return;
        }

        Array.Clear(visited, 0, visited.Length);
        int queueStart = 0;
        int queueEnd = 1;
        activeCells[0] = playerCell;
        visited[playerCell.x, playerCell.y] = true;
        pathParents[playerCell.x, playerCell.y] = new Vector2Int(-1, -1);

        while (queueStart < queueEnd && !visited[exitCell.x, exitCell.y])
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
                pathParents[neighbour.x, neighbour.y] = cell;
                activeCells[queueEnd++] = neighbour;
            }
        }

        if (!visited[exitCell.x, exitCell.y])
        {
            return;
        }

        Vector2Int routeCell = exitCell;
        while (routeCell != playerCell)
        {
            Vector2Int parentCell = pathParents[routeCell.x, routeCell.y];
            shiftRouteEdges.Add(CreateEdgeCandidate(parentCell, routeCell));
            routeCell = parentCell;
        }

        foreach (LoopCandidate openEdge in shiftOpenEdges)
        {
            foreach (LoopCandidate routeEdge in shiftRouteEdges)
            {
                if (EdgesMatch(openEdge, routeEdge))
                {
                    shiftTargetCloseEdges.Add(openEdge);
                    break;
                }
            }
        }
    }

    private static LoopCandidate CreateEdgeCandidate(
        Vector2Int firstCell,
        Vector2Int secondCell)
    {
        Vector2Int direction = secondCell - firstCell;
        if (direction == Vector2Int.up)
        {
            return new LoopCandidate(firstCell, secondCell, Wall.North, Wall.South);
        }

        if (direction == Vector2Int.right)
        {
            return new LoopCandidate(firstCell, secondCell, Wall.East, Wall.West);
        }

        if (direction == Vector2Int.down)
        {
            return new LoopCandidate(firstCell, secondCell, Wall.South, Wall.North);
        }

        return new LoopCandidate(firstCell, secondCell, Wall.West, Wall.East);
    }

    private void AddShiftCandidate(
        LoopCandidate candidate,
        IReadOnlyList<Vector2Int> protectedTrail)
    {
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

    private bool IsAlreadyPrepared(LoopCandidate candidate)
    {
        foreach (LoopCandidate preparedEdge in preparedEdgesToOpen)
        {
            if (EdgesMatch(candidate, preparedEdge))
            {
                return true;
            }
        }

        foreach (LoopCandidate preparedEdge in preparedEdgesToClose)
        {
            if (EdgesMatch(candidate, preparedEdge))
            {
                return true;
            }
        }

        return false;
    }

    private static bool EdgesMatch(LoopCandidate first, LoopCandidate second)
    {
        return (first.Cell == second.Cell
                && first.NeighbourCell == second.NeighbourCell)
            || (first.Cell == second.NeighbourCell
                && first.NeighbourCell == second.Cell);
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

    private void ShowPreparedShiftWarnings()
    {
        EnsureShiftWarningLineCount(preparedEdgesToOpen.Count);

        for (int index = 0; index < shiftOpenWarningLines.Count; index++)
        {
            bool shouldShow = index < preparedEdgesToOpen.Count;
            shiftOpenWarningLines[index].enabled = shouldShow;
            shiftCloseWarningLines[index].enabled = shouldShow;
            if (!shouldShow)
            {
                continue;
            }

            ConfigureWarningLine(shiftOpenWarningLines[index], preparedEdgesToOpen[index]);
            ConfigureWarningLine(shiftCloseWarningLines[index], preparedEdgesToClose[index]);
        }

        SetShiftWarningAlpha(1f);
    }

    private void EnsureShiftWarningLineCount(int requiredCount)
    {
        for (int index = shiftOpenWarningLines.Count; index < requiredCount; index++)
        {
            Transform openTransform = GetOrCreateChild($"{ShiftOpenWarningName} {index + 1}");
            Transform closeTransform = GetOrCreateChild($"{ShiftCloseWarningName} {index + 1}");
            LineRenderer openLine = GetOrAddComponent<LineRenderer>(openTransform.gameObject);
            LineRenderer closeLine = GetOrAddComponent<LineRenderer>(closeTransform.gameObject);
            ConfigureShiftWarningLine(openLine);
            ConfigureShiftWarningLine(closeLine);
            shiftOpenWarningLines.Add(openLine);
            shiftCloseWarningLines.Add(closeLine);
        }
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
        preparedEdgesToOpen.Clear();
        preparedEdgesToClose.Clear();

        foreach (LineRenderer warningLine in shiftOpenWarningLines)
        {
            if (warningLine != null)
            {
                warningLine.enabled = false;
            }
        }

        foreach (LineRenderer warningLine in shiftCloseWarningLines)
        {
            if (warningLine != null)
            {
                warningLine.enabled = false;
            }
        }
    }
}
