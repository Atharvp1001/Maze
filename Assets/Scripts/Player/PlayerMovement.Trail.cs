using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

internal sealed partial class PlayerMovement
{
    internal void BeginEntranceTransitionTrail(Vector3 offscreenPosition)
    {
        if (movementLine == null || trailPositions.Count == 0)
        {
            return;
        }

        hasEntranceTrail = true;
        entranceTrailStartPosition = ToLinePosition(offscreenPosition);
        movementLine.positionCount = 2;
        movementLine.SetPosition(0, entranceTrailStartPosition);
        movementLine.SetPosition(1, entranceTrailStartPosition);
    }

    internal void UpdateEntranceTransitionTrail(Vector3 playerPosition)
    {
        if (movementLine == null || !hasEntranceTrail
            || movementLine.positionCount < 2)
        {
            return;
        }

        movementLine.SetPosition(1, ToLinePosition(playerPosition));
    }

    internal void CompleteEntranceTransitionTrail()
    {
        if (movementLine == null || !hasEntranceTrail
            || trailPositions.Count == 0)
        {
            return;
        }

        movementLine.positionCount = GameplayTrailPositionOffset
            + trailPositions.Count;
        movementLine.SetPosition(0, entranceTrailStartPosition);
        for (int positionIndex = 0;
             positionIndex < trailPositions.Count;
             positionIndex++)
        {
            movementLine.SetPosition(
                GameplayTrailPositionOffset + positionIndex,
                trailPositions[positionIndex]);
        }
    }

    internal void BeginExitTransitionTrail()
    {
        if (movementLine == null || trailPositions.Count == 0)
        {
            return;
        }

        movementLine.positionCount = GameplayTrailPositionOffset
            + trailPositions.Count;
    }

    internal void UpdateExitTransitionTrail(Vector3 playerPosition)
    {
        if (movementLine == null || trailPositions.Count == 0)
        {
            return;
        }

        Vector3 linePosition = ToLinePosition(playerPosition);
        Vector3 trailEnd = trailPositions[trailPositions.Count - 1];
        float minimumSegmentLength = Mathf.Max(lineWidth, 0.001f);
        if ((linePosition - trailEnd).sqrMagnitude
            <= minimumSegmentLength * minimumSegmentLength)
        {
            movementLine.positionCount = GameplayTrailPositionOffset
                + trailPositions.Count;
            return;
        }

        int exitPositionIndex = GameplayTrailPositionOffset
            + trailPositions.Count;
        movementLine.positionCount = exitPositionIndex + 1;
        movementLine.SetPosition(exitPositionIndex, linePosition);
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
            movementLine.positionCount = GameplayTrailPositionOffset
                + activeTrailPositionIndex;
            return;
        }

        int activeLinePositionIndex = GameplayTrailPositionOffset
            + activeTrailPositionIndex;
        movementLine.positionCount = activeLinePositionIndex + 1;
        movementLine.SetPosition(activeLinePositionIndex, activePosition);
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
        movementLine.numCornerVertices = lineCornerVertices;
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

    private void RemoveLegacyEntranceTrailObject()
    {
        Transform legacyEntranceTrail = transform.Find("Entrance Trail");
        if (legacyEntranceTrail == null)
        {
            return;
        }

        legacyEntranceTrail.gameObject.SetActive(false);
        if (Application.isPlaying)
        {
            Destroy(legacyEntranceTrail.gameObject);
        }
        else
        {
            DestroyImmediate(legacyEntranceTrail.gameObject);
        }
    }
}
