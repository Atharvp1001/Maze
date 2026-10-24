using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

internal sealed partial class GameManager
{
    private void SetGameplayWorldVisible(bool isVisible)
    {
        if (mazeWorld != null)
        {
            mazeWorld.SetActive(isVisible);
        }

        if (playerWorld != null)
        {
            playerWorld.SetActive(isVisible);
        }

        if (endpointWorld != null)
        {
            endpointWorld.SetActive(isVisible && hasActiveLevel);
        }
    }

    private void UpdateLabels()
    {
        string levelLabel = $"Level {currentLevel}";
        string difficultyLabel = selectedDifficulty.ToString();
        bool shouldShowDifficulty = currentLevel > 5;

        if (menuDifficultyText != null)
        {
            menuDifficultyText.text = difficultyLabel;
            menuDifficultyText.gameObject.SetActive(shouldShowDifficulty);
        }

        if (menuLevelText != null)
        {
            menuLevelText.text = levelLabel;
        }

        if (gameplayLevelText != null)
        {
            gameplayLevelText.text = levelLabel;
        }

        if (gameplayDifficultyText != null)
        {
            gameplayDifficultyText.text = difficultyLabel;
            gameplayDifficultyText.gameObject.SetActive(shouldShowDifficulty);
        }

        if (gameplayMazeTypeText != null)
        {
            gameplayMazeTypeText.text = GetMazeModeLabel();
        }
    }
}
