using System;
using UnityEngine;

public enum MazeNavigationPointType
{
    Grid,
    Start,
    End
}

/// <summary>A lightweight navigation node. It does not create a GameObject.</summary>
[Serializable]
public struct MazeNavigationPoint
{
    [SerializeField] private Vector2Int cell;
    [SerializeField] private MazeNavigationPointType pointType;
    [SerializeField] private int northConnection;
    [SerializeField] private int eastConnection;
    [SerializeField] private int southConnection;
    [SerializeField] private int westConnection;

    public Vector2Int Cell => cell;
    public MazeNavigationPointType PointType => pointType;

    internal MazeNavigationPoint(Vector2Int mazeCell, MazeNavigationPointType type)
    {
        cell = mazeCell;
        pointType = type;
        northConnection = -1;
        eastConnection = -1;
        southConnection = -1;
        westConnection = -1;
    }

    /// <summary>Returns one of this node's four possible connected node indices.</summary>
    public int GetConnection(int slot)
    {
        switch (slot)
        {
            case 0:
                return northConnection;
            case 1:
                return eastConnection;
            case 2:
                return southConnection;
            case 3:
                return westConnection;
            default:
                throw new ArgumentOutOfRangeException(nameof(slot));
        }
    }

    public bool IsConnectedTo(int pointIndex)
    {
        return pointIndex >= 0
            && (northConnection == pointIndex
                || eastConnection == pointIndex
                || southConnection == pointIndex
                || westConnection == pointIndex);
    }

    internal void SetConnection(Vector2Int direction, int pointIndex)
    {
        if (direction == Vector2Int.up)
        {
            northConnection = pointIndex;
        }
        else if (direction == Vector2Int.right)
        {
            eastConnection = pointIndex;
        }
        else if (direction == Vector2Int.down)
        {
            southConnection = pointIndex;
        }
        else if (direction == Vector2Int.left)
        {
            westConnection = pointIndex;
        }
    }
}
