using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

internal sealed partial class GameManager
{
    private void PrepareUpcomingLevel()
    {
        SelectRandomDifficulty();
        SelectRandomMazeMode();
        SelectMazeDimensions();
        mazeSeed = MazeGenerator.CreateLevelSeed();
        savedWallLayout = string.Empty;
        UpdateLabels();
        SaveProgress();
    }

    private void HandleWallLayoutChanged(MazeGenerator changedMaze)
    {
        if (changedMaze == mazeGenerator && hasActiveLevel)
        {
            savedWallLayout = changedMaze.CaptureWallLayout();
            SaveProgress();
        }
    }

    private bool TryLoadProgress()
    {
        if (PlayerPrefs.GetInt(SaveVersionKey, 0) != SaveVersion)
        {
            return false;
        }

        int savedDifficulty = PlayerPrefs.GetInt(SaveDifficultyKey, -1);
        int savedMode = PlayerPrefs.GetInt(SaveModeKey, -1);
        int savedColumns = PlayerPrefs.GetInt(SaveColumnsKey, 0);
        int savedRows = PlayerPrefs.GetInt(SaveRowsKey, 0);
        if (savedDifficulty < 0 || savedDifficulty > (int)MazeGenerator.MazeDifficulty.Hard
            || savedMode < 0 || savedMode > (int)MazeMode.Mixed
            || savedColumns < 2 || savedRows < 2)
        {
            return false;
        }

        currentLevel = Mathf.Max(1, PlayerPrefs.GetInt(SaveLevelKey, 1));
        selectedDifficulty = (MazeGenerator.MazeDifficulty)savedDifficulty;
        currentMazeMode = (MazeMode)savedMode;
        int savedMechanics = PlayerPrefs.GetInt(SaveMixedMechanicsKey, -1);
        MazeMechanic savedMechanicMask = (MazeMechanic)savedMechanics;
        bool hasValidMixedCombination = savedMechanicMask == (MazeMechanic.Timed
                | MazeMechanic.Rotating)
            || savedMechanicMask == (MazeMechanic.Timed | MazeMechanic.Shifting)
            || savedMechanicMask == (MazeMechanic.Rotating | MazeMechanic.Shifting)
            || savedMechanicMask == (MazeMechanic.Timed
                | MazeMechanic.Rotating
                | MazeMechanic.Shifting);
        if (currentMazeMode == MazeMode.Mixed
            && hasValidMixedCombination)
        {
            activeMechanics = savedMechanicMask;
        }
        else
        {
            SelectActiveMechanicsForMode();
        }
        int savedPreviousDifficulty = PlayerPrefs.GetInt(
            SavePreviousDifficultyKey,
            savedDifficulty);
        previousDifficulty = savedPreviousDifficulty >= 0
            && savedPreviousDifficulty <= (int)MazeGenerator.MazeDifficulty.Hard
                ? (MazeGenerator.MazeDifficulty)savedPreviousDifficulty
                : selectedDifficulty;
        consecutiveDifficultyCount = currentLevel <= 5
            ? 0
            : Mathf.Clamp(PlayerPrefs.GetInt(SaveDifficultyStreakKey, 1), 1, 2);

        int savedPreviousMode = PlayerPrefs.GetInt(SavePreviousModeKey, savedMode);
        previousMazeMode = savedPreviousMode >= 0
            && savedPreviousMode <= (int)MazeMode.Mixed
                ? (MazeMode)savedPreviousMode
                : currentMazeMode;
        consecutiveModeCount = Mathf.Clamp(
            PlayerPrefs.GetInt(SaveModeStreakKey, 1),
            1,
            2);
        mazeColumns = savedColumns;
        mazeRows = savedRows;
        mazeSeed = PlayerPrefs.GetInt(SaveSeedKey, MazeGenerator.CreateLevelSeed());
        savedWallLayout = PlayerPrefs.GetString(SaveWallsKey, string.Empty);
        return true;
    }

    private void ApplyTestModeOverride()
    {
        if (testMazeMode != MazeModeTestOverride.Random)
        {
            currentMazeMode = (MazeMode)(testMazeMode - 1);
            SelectActiveMechanicsForMode();
        }
    }

    private void SaveProgress()
    {
        if (mazeColumns < 2 || mazeRows < 2)
        {
            return;
        }

        PlayerPrefs.SetInt(SaveVersionKey, SaveVersion);
        PlayerPrefs.SetInt(SaveLevelKey, currentLevel);
        PlayerPrefs.SetInt(SaveDifficultyKey, (int)selectedDifficulty);
        PlayerPrefs.SetInt(SaveModeKey, (int)currentMazeMode);
        PlayerPrefs.SetInt(SaveColumnsKey, mazeColumns);
        PlayerPrefs.SetInt(SaveRowsKey, mazeRows);
        PlayerPrefs.SetInt(SaveSeedKey, mazeSeed);
        PlayerPrefs.SetString(SaveWallsKey, savedWallLayout ?? string.Empty);
        PlayerPrefs.SetInt(SavePreviousDifficultyKey, (int)previousDifficulty);
        PlayerPrefs.SetInt(SaveDifficultyStreakKey, consecutiveDifficultyCount);
        PlayerPrefs.SetInt(SavePreviousModeKey, (int)previousMazeMode);
        PlayerPrefs.SetInt(SaveModeStreakKey, consecutiveModeCount);
        PlayerPrefs.SetInt(SaveMixedMechanicsKey, (int)activeMechanics);
        PlayerPrefs.Save();
    }

    private static void ClearSavedProgress()
    {
        PlayerPrefs.DeleteKey(SaveVersionKey);
        PlayerPrefs.DeleteKey(SaveLevelKey);
        PlayerPrefs.DeleteKey(SaveDifficultyKey);
        PlayerPrefs.DeleteKey(SaveModeKey);
        PlayerPrefs.DeleteKey(SaveColumnsKey);
        PlayerPrefs.DeleteKey(SaveRowsKey);
        PlayerPrefs.DeleteKey(SaveSeedKey);
        PlayerPrefs.DeleteKey(SaveWallsKey);
        PlayerPrefs.DeleteKey(SavePreviousDifficultyKey);
        PlayerPrefs.DeleteKey(SaveDifficultyStreakKey);
        PlayerPrefs.DeleteKey(SavePreviousModeKey);
        PlayerPrefs.DeleteKey(SaveModeStreakKey);
        PlayerPrefs.DeleteKey(SaveMixedMechanicsKey);
        PlayerPrefs.Save();
    }

    private void OnApplicationPause(bool isPaused)
    {
        if (isPaused)
        {
            SaveProgress();
        }
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
        {
            SaveProgress();
        }
    }

    private void SelectRandomDifficulty()
    {
        if (currentLevel <= 5)
        {
            selectedDifficulty = MazeGenerator.MazeDifficulty.Easy;
            return;
        }

        MazeGenerator.MazeDifficulty rolledDifficulty;
        int attempts = 0;
        do
        {
            float roll = UnityEngine.Random.Range(0f, 100f);
            rolledDifficulty = roll < 30f
                ? MazeGenerator.MazeDifficulty.Easy
                : roll < 70f
                    ? MazeGenerator.MazeDifficulty.Medium
                    : MazeGenerator.MazeDifficulty.Hard;
            attempts++;
        }
        while (consecutiveDifficultyCount >= 2
            && rolledDifficulty == previousDifficulty
            && attempts < 12);

        if (consecutiveDifficultyCount >= 2
            && rolledDifficulty == previousDifficulty)
        {
            int offset = UnityEngine.Random.Range(1, 3);
            rolledDifficulty = (MazeGenerator.MazeDifficulty)(
                ((int)previousDifficulty + offset) % 3);
        }

        if (rolledDifficulty == previousDifficulty)
        {
            consecutiveDifficultyCount++;
        }
        else
        {
            previousDifficulty = rolledDifficulty;
            consecutiveDifficultyCount = 1;
        }

        selectedDifficulty = rolledDifficulty;
    }

    private void SelectMazeDimensions()
    {
        int minimumSize;
        int maximumSize;
        float upperSizeBias;

        if (currentLevel <= 5)
        {
            int growingSize = 5 + currentLevel;
            minimumSize = Mathf.Max(5, growingSize - 1);
            maximumSize = growingSize + 1;
            upperSizeBias = 0f;
        }
        else
        {
            int levelSize = GetLevelBasedMazeSize(currentLevel);
            minimumSize = Mathf.Max(9, levelSize - 1);
            maximumSize = Mathf.Min(27, levelSize + 1);
            upperSizeBias = 0.5f;
        }

        previousMazeColumns = mazeColumns;
        previousMazeRows = mazeRows;

        int attempts = 0;

        do
        {
            mazeColumns = RollDimension(minimumSize, maximumSize, upperSizeBias);
            mazeRows = RollDimension(minimumSize, maximumSize, upperSizeBias);
            attempts++;
        }
        while (mazeColumns == previousMazeColumns
            && mazeRows == previousMazeRows
            && attempts < 12);
    }

    private static int GetLevelBasedMazeSize(int level)
    {
        if (level <= 30)
        {
            return Mathf.RoundToInt(Mathf.Lerp(
                10f,
                19f,
                Mathf.InverseLerp(6f, 30f, level)));
        }

        if (level <= 60)
        {
            return Mathf.RoundToInt(Mathf.Lerp(
                19f,
                23f,
                Mathf.InverseLerp(30f, 60f, level)));
        }

        if (level <= 90)
        {
            return Mathf.RoundToInt(Mathf.Lerp(
                23f,
                26f,
                Mathf.InverseLerp(60f, 90f, level)));
        }

        return 26;
    }

    private static int RollDimension(int minimumSize, int maximumSize, float upperSizeBias)
    {
        float randomValue = UnityEngine.Random.value;
        float biasedValue = Mathf.Lerp(
            randomValue,
            Mathf.Sqrt(randomValue),
            upperSizeBias);
        return Mathf.RoundToInt(Mathf.Lerp(minimumSize, maximumSize, biasedValue));
    }
}
