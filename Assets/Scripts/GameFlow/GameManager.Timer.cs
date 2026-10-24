using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

internal sealed partial class GameManager
{
    private void UpdateTimer()
    {
        if (HasMechanic(MazeMechanic.Timed))
        {
            timerValue = Mathf.Max(0f, timerValue - Time.deltaTime);
            UpdateTimerText();
            UpdateTimerUrgency();

            if (timerValue <= 0f)
            {
                RestartAfterTimeout();
            }

            return;
        }

        timerValue += Time.deltaTime;
        UpdateTimerText();
    }

    private void RestartAfterTimeout()
    {
        levelIsRunning = false;
        ResetTimerUrgency();
        ResetCameraRotation();
        ShowLoseScreen();
    }

    private void ShowLoseScreen()
    {
        Time.timeScale = 0f;

        if (gameCanvas != null)
        {
            gameCanvas.SetActive(false);
        }

        if (mainMenuCanvas != null)
        {
            mainMenuCanvas.SetActive(false);
        }

        if (winCanvas != null)
        {
            winCanvas.SetActive(false);
        }

        SetGameplayWorldVisible(false);

        if (loseCanvas == null)
        {
            return;
        }

        StopLoseAnimation();
        ResetLoseElementScales(Vector3.zero);

        if (loseMessageText != null)
        {
            loseMessageText.text = LoseMessages[UnityEngine.Random.Range(0, LoseMessages.Length)];
        }

        loseCanvas.SetActive(true);
        Canvas.ForceUpdateCanvases();

        loseSequence = DOTween.Sequence()
            .SetUpdate(true)
            .AppendInterval(0.12f)
            .Append(CreatePop(loseHourglass, loseHourglassScale, 0.42f))
            .AppendInterval(0.08f)
            .Append(CreatePop(loseTitle, loseTitleScale, 0.32f))
            .AppendInterval(0.05f)
            .Append(CreatePop(loseMessage, loseMessageScale, 0.3f))
            .AppendInterval(0.08f)
            .Append(CreatePop(loseBack, loseBackScale, 0.3f))
            .Join(CreatePop(loseRetry, loseRetryScale, 0.3f));
    }

    private void RetryFromLoseScreen()
    {
        StopLoseAnimation();

        if (loseCanvas != null)
        {
            loseCanvas.SetActive(false);
        }

        if (mainMenuCanvas != null)
        {
            mainMenuCanvas.SetActive(false);
        }

        if (gameCanvas != null)
        {
            gameCanvas.SetActive(true);
        }

        SetGameplayWorldVisible(true);
        Time.timeScale = 1f;
        RestartLevel();
    }

    private void ReturnToMenuFromLoseScreen()
    {
        // Prepare a fresh attempt so Play can resume this level from its start.
        RestartLevel();
        ShowMainMenu();
    }

    private void UpdateTimerText()
    {
        if (gameplayTimerText == null)
        {
            return;
        }

        int totalSeconds = HasMechanic(MazeMechanic.Timed)
            ? Mathf.CeilToInt(timerValue)
            : Mathf.FloorToInt(timerValue);
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;
        gameplayTimerText.text = $"{minutes:00}:{seconds:00}";
    }

    private void CacheGameplayTimerAppearance()
    {
        if (gameplayTimerText == null)
        {
            return;
        }

        gameplayTimerTransform = gameplayTimerText.rectTransform.parent as RectTransform;
        if (gameplayTimerTransform == null)
        {
            gameplayTimerTransform = gameplayTimerText.rectTransform;
        }

        gameplayTimerScale = gameplayTimerTransform.localScale;
        gameplayTimerColor = gameplayTimerText.color;
    }

    private void UpdateTimerUrgency()
    {
        if (gameplayTimerText == null || gameplayTimerTransform == null
            || timerValue > urgentTimerThreshold || urgentTimerThreshold <= 0f)
        {
            ResetTimerUrgency();
            return;
        }

        float urgency = 1f - Mathf.Clamp01(timerValue / urgentTimerThreshold);
        float pulseSpeed = Mathf.Lerp(
            urgentTimerSlowPulseSpeed,
            urgentTimerFastPulseSpeed,
            urgency);
        urgentTimerPulsePhase = Mathf.Repeat(
            urgentTimerPulsePhase + Time.deltaTime * pulseSpeed,
            1f);

        // Start at the normal size, grow toward red, then return to normal.
        float pulseAmount = 0.5f - 0.5f * Mathf.Cos(
            urgentTimerPulsePhase * Mathf.PI * 2f);
        gameplayTimerTransform.localScale = gameplayTimerScale
            * Mathf.Lerp(1f, urgentTimerScale, pulseAmount);
        gameplayTimerText.color = Color.Lerp(
            gameplayTimerColor,
            urgentTimerColor,
            pulseAmount);
    }

    private void ResetTimerUrgency()
    {
        urgentTimerPulsePhase = 0f;

        if (gameplayTimerTransform != null)
        {
            gameplayTimerTransform.localScale = gameplayTimerScale;
        }

        if (gameplayTimerText != null)
        {
            gameplayTimerText.color = gameplayTimerColor;
        }
    }
}
