using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

internal sealed partial class GameManager
{
    private void UpdateRotation()
    {
        if (!HasMechanic(MazeMechanic.Rotating) || gameplayCamera == null)
        {
            return;
        }

        bool cameraReachedTarget = Mathf.Abs(Mathf.DeltaAngle(
            gameplayCamera.transform.eulerAngles.z,
            targetCameraAngle)) < 0.1f;
        if (pointsSinceRotation >= GetRotationPointInterval()
            && playerMovement != null
            && !playerMovement.IsCurrentlyMoving
            && cameraReachedTarget
            && mixedEventCooldownPoints <= 0)
        {
            targetCameraAngle += UnityEngine.Random.value < 0.5f ? -90f : 90f;
            pointsSinceRotation = 0;
            if (currentMazeMode == MazeMode.Mixed)
            {
                mixedEventCooldownPoints = 3;
            }
        }

        Vector3 eulerAngles = gameplayCamera.transform.eulerAngles;
        eulerAngles.z = Mathf.MoveTowardsAngle(
            eulerAngles.z,
            targetCameraAngle,
            GetRotationSpeed() * Time.deltaTime);
        gameplayCamera.transform.rotation = Quaternion.Euler(eulerAngles);
        mazeZoomController?.FrameMazeForCurrentRotation();
    }

    private void UpdateShiftingMaze()
    {
        if (!HasMechanic(MazeMechanic.Shifting) || mazeGenerator == null
            || playerMovement == null || shiftWarningIsRunning)
        {
            return;
        }

        if (pointsSinceShift >= GetShiftPointInterval()
            && !playerMovement.IsCurrentlyMoving
            && mixedEventCooldownPoints <= 0)
        {
            if (currentMazeMode == MazeMode.Mixed)
            {
                mixedEventCooldownPoints = 3;
            }
            StartCoroutine(WarnAndShiftMaze());
        }
    }

    private IEnumerator WarnAndShiftMaze()
    {
        shiftWarningIsRunning = true;
        const float warningDuration = 0.7f;
        const float passageOpenDelay = 0.16f;
        Color originalColor = gameplayMazeTypeText != null
            ? gameplayMazeTypeText.color
            : Color.white;

        bool shiftPrepared = false;
        if (playerMovement.TryGetCurrentCell(out Vector2Int warningPlayerCell))
        {
            playerMovement.CopyTrailCells(protectedTrailCells);
            shiftPrepared = mazeGenerator.PrepareWallShift(
                warningPlayerCell,
                protectedTrailCells,
                GetWallExchangeCount());
        }

        if (!shiftPrepared)
        {
            pointsSinceShift = Mathf.Max(0, GetShiftPointInterval() - 1);
            shiftWarningIsRunning = false;
            yield break;
        }

        if (gameplayMazeTypeText != null && currentMazeMode != MazeMode.Mixed)
        {
            gameplayMazeTypeText.text = "Maze Shifting...";
        }

        float elapsed = 0f;
        while (elapsed < warningDuration
            && levelIsRunning
            && HasMechanic(MazeMechanic.Shifting))
        {
            elapsed += Time.deltaTime;
            if (gameplayMazeTypeText != null)
            {
                Color warningColor = Color.Lerp(
                    originalColor,
                    new Color(1f, 0.32f, 0.02f, 1f),
                    Mathf.PingPong(elapsed * 5f, 1f));
                gameplayMazeTypeText.color = warningColor;
            }

            mazeGenerator.SetShiftWarningAlpha(
                Mathf.Lerp(0.3f, 1f, Mathf.PingPong(elapsed * 5f, 1f)));

            yield return null;
        }

        if (levelIsRunning && HasMechanic(MazeMechanic.Shifting)
            && !playerMovement.IsCurrentlyMoving)
        {
            playerMovement.SetInputLocked(true);
            mazeGenerator.OpenPreparedShiftPassage();
            yield return new WaitForSeconds(passageOpenDelay);

            if (levelIsRunning && HasMechanic(MazeMechanic.Shifting)
                && !playerMovement.IsCurrentlyMoving)
            {
                mazeGenerator.CompletePreparedWallShift();
            }
            else
            {
                mazeGenerator.CancelPreparedWallShift();
            }

            playerMovement.SetInputLocked(false);
        }
        else
        {
            mazeGenerator.CancelPreparedWallShift();
        }

        if (gameplayMazeTypeText != null)
        {
            gameplayMazeTypeText.color = originalColor;
            gameplayMazeTypeText.text = GetMazeModeLabel();
        }

        pointsSinceShift = 0;
        shiftWarningIsRunning = false;
    }

    private void HandlePointsTravelled(int pointCount)
    {
        if (!levelIsRunning || pointCount <= 0)
        {
            return;
        }

        mixedEventCooldownPoints = Mathf.Max(0, mixedEventCooldownPoints - pointCount);

        if (HasMechanic(MazeMechanic.Rotating))
        {
            pointsSinceRotation += pointCount;
        }

        if (HasMechanic(MazeMechanic.Shifting))
        {
            pointsSinceShift += pointCount;
        }
    }

    private void SelectRandomMazeMode()
    {
        if (testMazeMode != MazeModeTestOverride.Random)
        {
            SetSelectedMazeMode((MazeMode)(testMazeMode - 1));
            return;
        }

        // Introduce all five modes once in a predictable tutorial sequence.
        if (currentLevel <= 5)
        {
            SetSelectedMazeMode((MazeMode)(currentLevel - 1));
            return;
        }

        float totalWeight = 0f;
        for (int modeIndex = 0; modeIndex <= (int)MazeMode.Mixed; modeIndex++)
        {
            MazeMode mode = (MazeMode)modeIndex;
            if (IsModeAllowed(mode))
            {
                totalWeight += GetModeWeight(mode);
            }
        }

        float roll = UnityEngine.Random.Range(0f, totalWeight);
        MazeMode selectedMode = MazeMode.Normal;
        for (int modeIndex = 0; modeIndex <= (int)MazeMode.Mixed; modeIndex++)
        {
            MazeMode mode = (MazeMode)modeIndex;
            if (!IsModeAllowed(mode))
            {
                continue;
            }

            roll -= GetModeWeight(mode);
            if (roll <= 0f)
            {
                selectedMode = mode;
                break;
            }
        }

        SetSelectedMazeMode(selectedMode);
    }

    private void SetSelectedMazeMode(MazeMode selectedMode)
    {
        if (selectedMode == previousMazeMode)
        {
            consecutiveModeCount++;
        }
        else
        {
            previousMazeMode = selectedMode;
            consecutiveModeCount = 1;
        }

        currentMazeMode = selectedMode;
        SelectActiveMechanicsForMode();
    }

    private bool IsModeAllowed(MazeMode mode)
    {
        if (mode != previousMazeMode)
        {
            return true;
        }

        return mode == MazeMode.Mixed
            ? consecutiveModeCount < 1
            : consecutiveModeCount < 2;
    }

    private static float GetModeWeight(MazeMode mode)
    {
        return mode == MazeMode.Mixed ? 10f : 22.5f;
    }

    private void SelectActiveMechanicsForMode()
    {
        switch (currentMazeMode)
        {
            case MazeMode.Normal:
                activeMechanics = MazeMechanic.None;
                break;
            case MazeMode.Timed:
                activeMechanics = MazeMechanic.Timed;
                break;
            case MazeMode.Rotating:
                activeMechanics = MazeMechanic.Rotating;
                break;
            case MazeMode.Shifting:
                activeMechanics = MazeMechanic.Shifting;
                break;
            case MazeMode.Mixed:
                if (selectedDifficulty == MazeGenerator.MazeDifficulty.Hard
                    && UnityEngine.Random.value < 0.2f)
                {
                    activeMechanics = MazeMechanic.Timed
                        | MazeMechanic.Rotating
                        | MazeMechanic.Shifting;
                    break;
                }

                int combination = UnityEngine.Random.Range(0, 3);
                activeMechanics = combination == 0
                    ? MazeMechanic.Timed | MazeMechanic.Rotating
                    : combination == 1
                        ? MazeMechanic.Timed | MazeMechanic.Shifting
                        : MazeMechanic.Rotating | MazeMechanic.Shifting;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private bool HasMechanic(MazeMechanic mechanic)
    {
        return (activeMechanics & mechanic) != 0;
    }

    private string GetMazeModeLabel()
    {
        return $"{currentMazeMode} Maze";
    }

    private int GetShiftPointInterval()
    {
        if (currentMazeMode == MazeMode.Mixed)
        {
            switch (selectedDifficulty)
            {
                case MazeGenerator.MazeDifficulty.Easy:
                    return 14;
                case MazeGenerator.MazeDifficulty.Medium:
                    return 11;
                case MazeGenerator.MazeDifficulty.Hard:
                    return 9;
            }
        }

        switch (selectedDifficulty)
        {
            case MazeGenerator.MazeDifficulty.Easy:
                return 10;
            case MazeGenerator.MazeDifficulty.Medium:
                return 7;
            case MazeGenerator.MazeDifficulty.Hard:
                return 5;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private int GetWallExchangeCount()
    {
        if (currentMazeMode == MazeMode.Mixed)
        {
            switch (selectedDifficulty)
            {
                case MazeGenerator.MazeDifficulty.Easy:
                    return 2;
                case MazeGenerator.MazeDifficulty.Medium:
                    return 3;
                case MazeGenerator.MazeDifficulty.Hard:
                    return 4;
            }
        }

        switch (selectedDifficulty)
        {
            case MazeGenerator.MazeDifficulty.Easy:
                return 3;
            case MazeGenerator.MazeDifficulty.Medium:
                return UnityEngine.Random.Range(4, 6);
            case MazeGenerator.MazeDifficulty.Hard:
                return 6;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private float GetTimedMazeLimit()
    {
        if (mazeGenerator == null)
        {
            return 45f;
        }

        int solutionLength = Mathf.Max(1, mazeGenerator.SolutionLength);
        float movementSpeed = playerMovement != null
            ? Mathf.Max(0.1f, playerMovement.MovementSpeed)
            : 4f;
        float travelTime = solutionLength * mazeGenerator.CellSize / movementSpeed;
        float secondsPerStep;
        float secondsPerTurn;
        float secondsPerJunction;
        float wrongRouteRatio;
        float secondsPerWrongStep;
        float safetyBuffer;
        float minimumLimit;
        float maximumLimit;

        switch (selectedDifficulty)
        {
            case MazeGenerator.MazeDifficulty.Easy:
                secondsPerStep = 0.55f;
                secondsPerTurn = 0.35f;
                secondsPerJunction = 0.9f;
                wrongRouteRatio = 0.25f;
                secondsPerWrongStep = 0.25f;
                safetyBuffer = 8f;
                minimumLimit = 35f;
                maximumLimit = 150f;
                break;
            case MazeGenerator.MazeDifficulty.Medium:
                secondsPerStep = 0.45f;
                secondsPerTurn = 0.3f;
                secondsPerJunction = 0.75f;
                wrongRouteRatio = 0.3f;
                secondsPerWrongStep = 0.22f;
                safetyBuffer = 7f;
                minimumLimit = 32f;
                maximumLimit = 135f;
                break;
            case MazeGenerator.MazeDifficulty.Hard:
                secondsPerStep = 0.38f;
                secondsPerTurn = 0.25f;
                secondsPerJunction = 0.65f;
                wrongRouteRatio = 0.35f;
                secondsPerWrongStep = 0.2f;
                safetyBuffer = 6f;
                minimumLimit = 30f;
                maximumLimit = 120f;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        float reasonableWrongSteps = Mathf.Min(
            mazeGenerator.MisleadingBranchDepth,
            solutionLength * wrongRouteRatio
                + mazeGenerator.LongestMisleadingBranch * 0.5f);
        float limit = travelTime
            + solutionLength * secondsPerStep
            + mazeGenerator.SolutionTurnCount * secondsPerTurn
            + mazeGenerator.SolutionJunctionCount * secondsPerJunction
            + reasonableWrongSteps * secondsPerWrongStep
            + safetyBuffer;

        if (currentMazeMode == MazeMode.Mixed)
        {
            bool hasAllMechanics = activeMechanics
                == (MazeMechanic.Timed | MazeMechanic.Rotating | MazeMechanic.Shifting);
            limit *= hasAllMechanics ? 1.35f : 1.2f;
        }

        return Mathf.Clamp(limit, minimumLimit, maximumLimit);
    }

    private int GetRotationPointInterval()
    {
        if (currentMazeMode == MazeMode.Mixed)
        {
            switch (selectedDifficulty)
            {
                case MazeGenerator.MazeDifficulty.Easy:
                    return 12;
                case MazeGenerator.MazeDifficulty.Medium:
                    return 9;
                case MazeGenerator.MazeDifficulty.Hard:
                    return 7;
            }
        }

        switch (selectedDifficulty)
        {
            case MazeGenerator.MazeDifficulty.Easy:
                return 8;
            case MazeGenerator.MazeDifficulty.Medium:
                return 6;
            case MazeGenerator.MazeDifficulty.Hard:
                return 4;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private float GetRotationSpeed()
    {
        switch (selectedDifficulty)
        {
            case MazeGenerator.MazeDifficulty.Easy:
                return 90f;
            case MazeGenerator.MazeDifficulty.Medium:
                return 120f;
            case MazeGenerator.MazeDifficulty.Hard:
                return 150f;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private float GetDefaultCameraAngle()
    {
        return defaultCameraRotation.eulerAngles.z;
    }

    private float GetBaseMazeComplexityMultiplier()
    {
        switch (currentMazeMode)
        {
            case MazeMode.Rotating:
            case MazeMode.Shifting:
                return 0.9f;
            case MazeMode.Mixed:
                return 0.75f;
            default:
                return 1f;
        }
    }

    private void ResetCameraRotation()
    {
        if (gameplayCamera != null)
        {
            gameplayCamera.transform.rotation = defaultCameraRotation;
        }
    }
}
