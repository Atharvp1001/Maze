using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

internal sealed partial class MazeGenerator
{
    private void AnalyzeMazeComplexity()
    {
        solutionPath.Clear();
        Array.Clear(solutionPathCells, 0, solutionPathCells.Length);

        for (int x = 0; x < columns; x++)
        {
            for (int y = 0; y < rows; y++)
            {
                pathDistances[x, y] = -1;
                pathParents[x, y] = new Vector2Int(-1, -1);
            }
        }

        Vector2Int start = Vector2Int.zero;
        Vector2Int end = new Vector2Int(columns - 1, rows - 1);
        int queueStart = 0;
        int queueEnd = 1;
        activeCells[0] = start;
        pathDistances[0, 0] = 0;

        while (queueStart < queueEnd)
        {
            Vector2Int cell = activeCells[queueStart++];
            foreach (Vector2Int direction in CardinalDirections)
            {
                Vector2Int neighbour = cell + direction;
                if (!IsInside(neighbour)
                    || pathDistances[neighbour.x, neighbour.y] >= 0
                    || !CanMove(cell, direction))
                {
                    continue;
                }

                pathDistances[neighbour.x, neighbour.y]
                    = pathDistances[cell.x, cell.y] + 1;
                pathParents[neighbour.x, neighbour.y] = cell;
                activeCells[queueEnd++] = neighbour;
            }
        }

        if (pathDistances[end.x, end.y] < 0)
        {
            SolutionLength = 0;
            SolutionTurnCount = 0;
            SolutionJunctionCount = 0;
            MisleadingBranchDepth = 0;
            LongestMisleadingBranch = 0;
            ShortDeadEndCount = 0;
            ComplexityScore = 0f;
            return;
        }

        Vector2Int pathCell = end;
        while (pathCell.x >= 0 && pathCell.y >= 0)
        {
            solutionPath.Add(pathCell);
            solutionPathCells[pathCell.x, pathCell.y] = true;
            if (pathCell == start)
            {
                break;
            }

            pathCell = pathParents[pathCell.x, pathCell.y];
        }

        solutionPath.Reverse();
        SolutionLength = Mathf.Max(0, solutionPath.Count - 1);
        SolutionTurnCount = 0;
        SolutionJunctionCount = 0;

        Vector2Int previousDirection = Vector2Int.zero;
        for (int index = 0; index < solutionPath.Count; index++)
        {
            Vector2Int cell = solutionPath[index];
            if (CountOpenNeighbours(cell) >= 3)
            {
                SolutionJunctionCount++;
            }

            if (index == 0)
            {
                continue;
            }

            Vector2Int direction = solutionPath[index] - solutionPath[index - 1];
            if (index > 1 && direction != previousDirection)
            {
                SolutionTurnCount++;
            }

            previousDirection = direction;
        }

        int relevantBranchCells = 0;
        int relevantDeadEnds = 0;
        int branchComponents = 0;
        MisleadingBranchDepth = 0;
        LongestMisleadingBranch = 0;
        ShortDeadEndCount = 0;
        Array.Clear(visited, 0, visited.Length);

        foreach (Vector2Int solutionCell in solutionPath)
        {
            foreach (Vector2Int direction in CardinalDirections)
            {
                Vector2Int neighbour = solutionCell + direction;
                if (!IsInside(neighbour)
                    || solutionPathCells[neighbour.x, neighbour.y]
                    || visited[neighbour.x, neighbour.y]
                    || !CanMove(solutionCell, direction))
                {
                    continue;
                }

                branchComponents++;
                int branchQueueStart = 0;
                int branchQueueEnd = 1;
                activeCells[0] = neighbour;
                activeDepths[0] = 1;
                visited[neighbour.x, neighbour.y] = true;
                int componentMaximumDepth = 0;

                while (branchQueueStart < branchQueueEnd)
                {
                    Vector2Int branchCell = activeCells[branchQueueStart];
                    int branchDepth = activeDepths[branchQueueStart++];
                    relevantBranchCells++;
                    componentMaximumDepth = Mathf.Max(
                        componentMaximumDepth,
                        branchDepth);
                    if (CountOpenNeighbours(branchCell) <= 1)
                    {
                        relevantDeadEnds++;
                        if (branchDepth <= 3)
                        {
                            ShortDeadEndCount++;
                        }
                    }

                    foreach (Vector2Int branchDirection in CardinalDirections)
                    {
                        Vector2Int next = branchCell + branchDirection;
                        if (!IsInside(next)
                            || solutionPathCells[next.x, next.y]
                            || visited[next.x, next.y]
                            || !CanMove(branchCell, branchDirection))
                        {
                            continue;
                        }

                        visited[next.x, next.y] = true;
                        activeCells[branchQueueEnd] = next;
                        activeDepths[branchQueueEnd++] = branchDepth + 1;
                    }
                }

                MisleadingBranchDepth += componentMaximumDepth;
                LongestMisleadingBranch = Mathf.Max(
                    LongestMisleadingBranch,
                    componentMaximumDepth);
            }
        }

        int cellCount = columns * rows;
        int directDistance = columns + rows - 2;
        float pathScore = Mathf.Clamp01((SolutionLength - directDistance)
            / (float)Mathf.Max(1, cellCount - directDistance));
        float turnScore = Mathf.Clamp01(SolutionTurnCount
            / (float)Mathf.Max(1, SolutionLength) * 2f);
        float junctionScore = Mathf.Clamp01(SolutionJunctionCount
            / (float)Mathf.Max(1, solutionPath.Count) * 4f);
        float branchDepthScore = Mathf.Clamp01(
            (MisleadingBranchDepth + LongestMisleadingBranch * 1.5f)
            / Mathf.Max(1f, solutionPath.Count * 0.8f));
        float branchVarietyScore = Mathf.Clamp01(
            (branchComponents * 2f + relevantDeadEnds + relevantBranchCells * 0.04f)
            / Mathf.Max(1f, solutionPath.Count));
        float shortDeadEndPenalty = Mathf.Clamp01(
            ShortDeadEndCount / (float)Mathf.Max(1, branchComponents));

        ComplexityScore = Mathf.Clamp01(
            pathScore * 0.35f
            + turnScore * 0.2f
            + junctionScore * 0.15f
            + branchDepthScore * 0.2f
            + branchVarietyScore * 0.1f
            - shortDeadEndPenalty * 0.1f);
    }

    private int CountOpenNeighbours(Vector2Int cell)
    {
        int count = 0;
        foreach (Vector2Int direction in CardinalDirections)
        {
            Vector2Int neighbour = cell + direction;
            if (IsInside(neighbour) && CanMove(cell, direction))
            {
                count++;
            }
        }

        return count;
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
}
