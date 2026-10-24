using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

internal sealed partial class MazeGenerator
{
    private void EnsureGenerationBuffers()
    {
        if (walls == null || walls.GetLength(0) != columns || walls.GetLength(1) != rows)
        {
            walls = new Wall[columns, rows];
            visited = new bool[columns, rows];
            pointIndexByCell = new int[columns, rows];
            pathDistances = new int[columns, rows];
            pathParents = new Vector2Int[columns, rows];
            growthDirections = new Vector2Int[columns, rows];
            solutionPathCells = new bool[columns, rows];
            activeCells = new Vector2Int[columns * rows];
            activeDepths = new int[columns * rows];
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

    /// <summary>
    /// Builds one difficulty-shaped maze. Random frontier selection creates
    /// meaningful junctions, while committed growth runs make false routes
    /// travel far enough to be believable instead of ending immediately.
    /// </summary>
    private void CarveGrowingTree(System.Random random)
    {
        ResetMazeWalls();
        Array.Clear(growthDirections, 0, growthDirections.Length);

        int activeCount = 1;
        int committedCellIndex = -1;
        int committedStepsRemaining = 0;
        activeCells[0] = Vector2Int.zero;
        visited[0, 0] = true;

        while (activeCount > 0)
        {
            int activeIndex = committedStepsRemaining > 0
                && committedCellIndex >= 0
                && committedCellIndex < activeCount
                    ? committedCellIndex
                    : ChooseGrowthCellIndex(random, activeCount);
            Vector2Int current = activeCells[activeIndex];
            int choiceCount = GetUnvisitedGrowthNeighbours(current);

            if (choiceCount == 0)
            {
                RemoveActiveCell(activeIndex, ref activeCount);
                committedCellIndex = -1;
                committedStepsRemaining = 0;
                continue;
            }

            Neighbour next = ChooseGrowthNeighbour(current, choiceCount, random);
            walls[current.x, current.y] &= ~next.Direction;
            walls[next.Position.x, next.Position.y] &= ~next.OppositeDirection;
            visited[next.Position.x, next.Position.y] = true;
            growthDirections[next.Position.x, next.Position.y]
                = next.Position - current;

            activeCells[activeCount] = next.Position;
            committedCellIndex = activeCount;
            activeCount++;

            if (committedStepsRemaining > 0)
            {
                committedStepsRemaining--;
            }
            else
            {
                committedStepsRemaining = GetCommittedGrowthLength(random) - 1;
            }
        }

        ConnectEndpointFromFarthestNeighbour();
    }

    private int GetUnvisitedGrowthNeighbours(Vector2Int cell)
    {
        int count = GetUnvisitedNeighbours(cell);
        Vector2Int endpoint = new Vector2Int(columns - 1, rows - 1);

        for (int index = count - 1; index >= 0; index--)
        {
            if (neighbourBuffer[index].Position != endpoint)
            {
                continue;
            }

            for (int moveIndex = index; moveIndex < count - 1; moveIndex++)
            {
                neighbourBuffer[moveIndex] = neighbourBuffer[moveIndex + 1];
            }

            count--;
        }

        return count;
    }

    private void ConnectEndpointFromFarthestNeighbour()
    {
        Vector2Int endpoint = new Vector2Int(columns - 1, rows - 1);
        CalculateDistancesFromStart();

        // Fresh advanced mazes intentionally leave the endpoint untouched so it
        // can be attached to the longest available route. This guard also lets
        // the method safely validate restored layouts without adding a loop to
        // an endpoint that is already reachable.
        if (pathDistances[endpoint.x, endpoint.y] >= 0)
        {
            return;
        }

        Vector2Int selectedNeighbour = new Vector2Int(-1, -1);
        int selectedDistance = -1;
        foreach (Vector2Int direction in CardinalDirections)
        {
            Vector2Int neighbour = endpoint + direction;
            if (!IsInside(neighbour)
                || pathDistances[neighbour.x, neighbour.y] <= selectedDistance)
            {
                continue;
            }

            selectedNeighbour = neighbour;
            selectedDistance = pathDistances[neighbour.x, neighbour.y];
        }

        if (selectedNeighbour.x < 0
            || !TryGetWallPair(
                selectedNeighbour,
                endpoint,
                out Wall directionToEndpoint,
                out Wall directionFromEndpoint))
        {
            return;
        }

        walls[selectedNeighbour.x, selectedNeighbour.y] &= ~directionToEndpoint;
        walls[endpoint.x, endpoint.y] &= ~directionFromEndpoint;
        visited[endpoint.x, endpoint.y] = true;
    }

    private void CalculateDistancesFromStart()
    {
        for (int x = 0; x < columns; x++)
        {
            for (int y = 0; y < rows; y++)
            {
                pathDistances[x, y] = -1;
            }
        }

        int queueStart = 0;
        int queueEnd = 1;
        activeCells[0] = Vector2Int.zero;
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
                activeCells[queueEnd++] = neighbour;
            }
        }
    }

    private int ChooseGrowthCellIndex(System.Random random, int activeCount)
    {
        float newestChance;
        switch (currentDifficulty)
        {
            case MazeDifficulty.Easy:
                newestChance = 0.7f;
                break;
            case MazeDifficulty.Medium:
                newestChance = 0.4f;
                break;
            case MazeDifficulty.Hard:
                newestChance = 0.2f;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        // Mechanically difficult modes deliberately use a slightly calmer base.
        newestChance = Mathf.Lerp(0.75f, newestChance, baseComplexityMultiplier);
        return random.NextDouble() < newestChance
            ? activeCount - 1
            : random.Next(activeCount);
    }

    private Neighbour ChooseGrowthNeighbour(
        Vector2Int current,
        int choiceCount,
        System.Random random)
    {
        Vector2Int previousDirection = growthDirections[current.x, current.y];
        float straightChance;
        switch (currentDifficulty)
        {
            case MazeDifficulty.Easy:
                straightChance = 0.55f;
                break;
            case MazeDifficulty.Medium:
                straightChance = 0.38f;
                break;
            case MazeDifficulty.Hard:
                straightChance = 0.22f;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        if (previousDirection != Vector2Int.zero
            && random.NextDouble() < straightChance)
        {
            for (int index = 0; index < choiceCount; index++)
            {
                if (neighbourBuffer[index].Position - current == previousDirection)
                {
                    return neighbourBuffer[index];
                }
            }
        }

        return neighbourBuffer[random.Next(choiceCount)];
    }

    private int GetCommittedGrowthLength(System.Random random)
    {
        int minimum;
        int maximumExclusive;
        switch (currentDifficulty)
        {
            case MazeDifficulty.Easy:
                minimum = 2;
                maximumExclusive = 5;
                break;
            case MazeDifficulty.Medium:
                minimum = 4;
                maximumExclusive = 8;
                break;
            case MazeDifficulty.Hard:
                minimum = 6;
                maximumExclusive = 11;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        int shapedLength = random.Next(minimum, maximumExclusive);
        return Mathf.Max(2, Mathf.RoundToInt(
            Mathf.Lerp(2f, shapedLength, baseComplexityMultiplier)));
    }

    private void RemoveActiveCell(int index, ref int activeCount)
    {
        for (int moveIndex = index; moveIndex < activeCount - 1; moveIndex++)
        {
            activeCells[moveIndex] = activeCells[moveIndex + 1];
        }

        activeCount--;
    }

    /// <summary>
    /// Moves existing deep subtrees onto underused points of the correct route.
    /// Each move opens one wall and closes one wall, so the maze remains a tree
    /// with exactly one solution instead of gaining shortcut loops.
    /// </summary>
    private void StrengthenSolutionJunctions(System.Random random)
    {
        if (solutionPath.Count < 5)
        {
            return;
        }

        float junctionDensity;
        int minimumBranchDepth;
        switch (currentDifficulty)
        {
            case MazeDifficulty.Easy:
                junctionDensity = 0.08f;
                minimumBranchDepth = 4;
                break;
            case MazeDifficulty.Medium:
                junctionDensity = 0.11f;
                minimumBranchDepth = 6;
                break;
            case MazeDifficulty.Hard:
                junctionDensity = 0.14f;
                minimumBranchDepth = 8;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        int desiredJunctions = Mathf.Clamp(
            Mathf.RoundToInt(
                SolutionLength * junctionDensity * baseComplexityMultiplier),
            2,
            10);
        minimumBranchDepth = Mathf.Max(3, Mathf.RoundToInt(
            minimumBranchDepth * baseComplexityMultiplier));

        int maximumAdjustments = Mathf.Max(
            0,
            desiredJunctions - SolutionJunctionCount);
        for (int adjustment = 0; adjustment < maximumAdjustments; adjustment++)
        {
            bool movedDeepBranch = false;
            int maximumAttempts = solutionPath.Count * 8;

            for (int attempt = 0; attempt < maximumAttempts; attempt++)
            {
                int pathIndex = random.Next(1, solutionPath.Count - 1);
                Vector2Int solutionCell = solutionPath[pathIndex];
                if (CountOpenNeighbours(solutionCell) >= 3)
                {
                    continue;
                }

                int firstDirection = random.Next(CardinalDirections.Length);
                for (int directionOffset = 0;
                     directionOffset < CardinalDirections.Length;
                     directionOffset++)
                {
                    Vector2Int branchCell = solutionCell
                        + CardinalDirections[(firstDirection + directionOffset)
                            % CardinalDirections.Length];
                    if (!IsInside(branchCell)
                        || solutionPathCells[branchCell.x, branchCell.y]
                        || CanMove(solutionCell, branchCell - solutionCell))
                    {
                        continue;
                    }

                    Vector2Int oldParent = pathParents[branchCell.x, branchCell.y];
                    if (!IsInside(oldParent)
                        || solutionPathCells[oldParent.x, oldParent.y]
                        || MeasureDescendantDepth(branchCell) < minimumBranchDepth
                        || !TryGetWallPair(
                            solutionCell,
                            branchCell,
                            out Wall newDirection,
                            out Wall newOppositeDirection)
                        || !TryGetWallPair(
                            branchCell,
                            oldParent,
                            out Wall oldDirection,
                            out Wall oldOppositeDirection))
                    {
                        continue;
                    }

                    walls[solutionCell.x, solutionCell.y] &= ~newDirection;
                    walls[branchCell.x, branchCell.y] &= ~newOppositeDirection;
                    walls[branchCell.x, branchCell.y] |= oldDirection;
                    walls[oldParent.x, oldParent.y] |= oldOppositeDirection;
                    movedDeepBranch = true;
                    break;
                }

                if (movedDeepBranch)
                {
                    break;
                }
            }

            if (!movedDeepBranch)
            {
                break;
            }

            AnalyzeMazeComplexity();
        }
    }

    private int MeasureDescendantDepth(Vector2Int root)
    {
        int queueStart = 0;
        int queueEnd = 1;
        int maximumDepth = 1;
        activeCells[0] = root;
        activeDepths[0] = 1;

        while (queueStart < queueEnd)
        {
            Vector2Int cell = activeCells[queueStart];
            int depth = activeDepths[queueStart++];
            maximumDepth = Mathf.Max(maximumDepth, depth);

            foreach (Vector2Int direction in CardinalDirections)
            {
                Vector2Int child = cell + direction;
                if (!IsInside(child)
                    || pathParents[child.x, child.y] != cell)
                {
                    continue;
                }

                activeCells[queueEnd] = child;
                activeDepths[queueEnd++] = depth + 1;
            }
        }

        return maximumDepth;
    }

    private static bool TryGetWallPair(
        Vector2Int from,
        Vector2Int to,
        out Wall direction,
        out Wall oppositeDirection)
    {
        Vector2Int delta = to - from;
        if (delta == Vector2Int.up)
        {
            direction = Wall.North;
            oppositeDirection = Wall.South;
            return true;
        }

        if (delta == Vector2Int.right)
        {
            direction = Wall.East;
            oppositeDirection = Wall.West;
            return true;
        }

        if (delta == Vector2Int.down)
        {
            direction = Wall.South;
            oppositeDirection = Wall.North;
            return true;
        }

        if (delta == Vector2Int.left)
        {
            direction = Wall.West;
            oppositeDirection = Wall.East;
            return true;
        }

        direction = Wall.None;
        oppositeDirection = Wall.None;
        return false;
    }

    private void AddDifficultyLoops(System.Random random)
    {
        float extraPassageRatio;
        switch (currentDifficulty)
        {
            case MazeDifficulty.Easy:
                if (!useAdvancedGeneration)
                {
                    return;
                }
                extraPassageRatio = EasyExtraPassageRatio;
                break;
            case MazeDifficulty.Medium:
                extraPassageRatio = MediumExtraPassageRatio;
                break;
            case MazeDifficulty.Hard:
                return;
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
}
