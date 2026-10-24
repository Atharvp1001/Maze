using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

internal sealed partial class MazeGenerator
{
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
}
