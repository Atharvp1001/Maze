using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

internal sealed partial class GameManager
{
    private IEnumerator ShowWinThenNextLevelTransition(float completionTime)
    {
        if (playButton != null)
        {
            playButton.interactable = false;
        }

        yield return PlayPlayerExitSequence();
        yield return ShowWinAnimation(completionTime);

        ShowMainMenu();

        yield return FadeMenuLevelText(1f, 0f, 0.3f);

        currentLevel++;
        PrepareUpcomingLevel();

        yield return FadeMenuLevelText(0f, 1f, 0.3f);

        if (playButton != null)
        {
            playButton.interactable = true;
        }

        SetGameplayButtonsInteractable(true);
        isAdvancingLevel = false;
    }

    private IEnumerator ShowWinAnimation(float completionTime)
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

        SetGameplayWorldVisible(false);
        ResetCameraRotation();

        if (winCanvas == null)
        {
            yield break;
        }

        StopWinAnimation();
        ResetWinElementScales(Vector3.zero);

        if (winMessageText != null)
        {
            winMessageText.text = WinMessages[UnityEngine.Random.Range(0, WinMessages.Length)];
        }

        SetWinTimerText(0f);
        winCanvas.SetActive(true);
        Canvas.ForceUpdateCanvases();

        float displayedTime = 0f;
        float countDuration = Mathf.Clamp(completionTime * 0.025f, 0.8f, 1.5f);

        winSequence = DOTween.Sequence()
            .SetUpdate(true)
            .AppendInterval(0.12f)
            .Append(CreatePop(winStar, winStarScale, 0.42f))
            .AppendInterval(0.08f)
            .Append(CreatePop(winTitle, winTitleScale, 0.32f))
            .AppendInterval(0.05f)
            .Append(CreatePop(winMessage, winMessageScale, 0.3f))
            .AppendInterval(0.08f)
            .Append(CreatePop(winTimer, winTimerScale, 0.34f))
            .Append(DOTween.To(
                    () => displayedTime,
                    value =>
                    {
                        displayedTime = value;
                        SetWinTimerText(displayedTime);
                    },
                    completionTime,
                    countDuration)
                .SetEase(Ease.OutCubic))
            .AppendInterval(1.25f);

        yield return winSequence.WaitForCompletion();
        winSequence = null;
    }

    private static Sequence CreatePop(
        RectTransform target,
        Vector3 originalScale,
        float duration)
    {
        Sequence pop = DOTween.Sequence();
        if (target == null)
        {
            return pop;
        }

        pop.Append(target.DOScale(originalScale * 1.18f, duration * 0.65f)
            .SetEase(Ease.OutBack));
        pop.Append(target.DOScale(originalScale, duration * 0.35f)
            .SetEase(Ease.OutQuad));
        return pop;
    }

    private void CacheWinScreenScales()
    {
        if (winStar != null)
        {
            winStarScale = winStar.localScale;
        }

        if (winTitle != null)
        {
            winTitleScale = winTitle.localScale;
        }

        if (winMessage != null)
        {
            winMessageScale = winMessage.localScale;
        }

        if (winTimer != null)
        {
            winTimerScale = winTimer.localScale;
        }
    }

    private void ResetWinElementScales(Vector3? overrideScale = null)
    {
        if (winStar != null)
        {
            winStar.localScale = overrideScale ?? winStarScale;
        }

        if (winTitle != null)
        {
            winTitle.localScale = overrideScale ?? winTitleScale;
        }

        if (winMessage != null)
        {
            winMessage.localScale = overrideScale ?? winMessageScale;
        }

        if (winTimer != null)
        {
            winTimer.localScale = overrideScale ?? winTimerScale;
        }
    }

    private void StopWinAnimation()
    {
        if (winSequence != null)
        {
            winSequence.Kill();
            winSequence = null;
        }

        ResetWinElementScales();
    }

    private void CacheLoseScreenScales()
    {
        if (loseHourglass != null)
        {
            loseHourglassScale = loseHourglass.localScale;
        }

        if (loseTitle != null)
        {
            loseTitleScale = loseTitle.localScale;
        }

        if (loseMessage != null)
        {
            loseMessageScale = loseMessage.localScale;
        }

        if (loseBack != null)
        {
            loseBackScale = loseBack.localScale;
        }

        if (loseRetry != null)
        {
            loseRetryScale = loseRetry.localScale;
        }
    }

    private void ResetLoseElementScales(Vector3? overrideScale = null)
    {
        if (loseHourglass != null)
        {
            loseHourglass.localScale = overrideScale ?? loseHourglassScale;
        }

        if (loseTitle != null)
        {
            loseTitle.localScale = overrideScale ?? loseTitleScale;
        }

        if (loseMessage != null)
        {
            loseMessage.localScale = overrideScale ?? loseMessageScale;
        }

        if (loseBack != null)
        {
            loseBack.localScale = overrideScale ?? loseBackScale;
        }

        if (loseRetry != null)
        {
            loseRetry.localScale = overrideScale ?? loseRetryScale;
        }
    }

    private void StopLoseAnimation()
    {
        if (loseSequence != null)
        {
            loseSequence.Kill();
            loseSequence = null;
        }

        ResetLoseElementScales();
    }

    private void SetWinTimerText(float elapsedSeconds)
    {
        if (winTimerText == null)
        {
            return;
        }

        int totalSeconds = Mathf.Max(0, Mathf.FloorToInt(elapsedSeconds));
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;
        winTimerText.text = $"{minutes:00}:{seconds:00}";
    }

    private IEnumerator FadeMenuLevelText(float startAlpha, float endAlpha, float duration)
    {
        if (menuLevelText == null)
        {
            yield return new WaitForSecondsRealtime(duration);
            yield break;
        }

        Color color = menuLevelText.color;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            color.a = Mathf.Lerp(startAlpha, endAlpha, elapsed / duration);
            menuLevelText.color = color;
            yield return null;
        }

        color.a = endAlpha;
        menuLevelText.color = color;
    }

    private void BeginLevelRuntime()
    {
        ResetTimerUrgency();
        ResetEndpointAppearance();
        SetGameplayButtonsInteractable(true);
        timerValue = HasMechanic(MazeMechanic.Timed)
            ? GetTimedMazeLimit()
            : 0f;
        pointsSinceRotation = 0;
        pointsSinceShift = 0;
        mixedEventCooldownPoints = 0;
        targetCameraAngle = GetDefaultCameraAngle();
        levelIsRunning = false;
        UpdateTimerText();
        StartCoroutine(PlayPlayerEntranceSequence());
    }

    private IEnumerator PlayPlayerEntranceSequence()
    {
        if (playerMovement == null || mazeGenerator == null
            || gameplayCamera == null || mazeGenerator.StartPointIndex < 0)
        {
            levelIsRunning = true;
            yield break;
        }

        playerMovement.SetInputLocked(true);
        Vector3 startPosition = mazeGenerator.GetNavigationPointWorldPosition(
            mazeGenerator.StartPointIndex);
        startPosition.z = playerMovement.transform.position.z;
        Vector3 entranceOutwardDirection = mazeGenerator.transform.TransformDirection(
            Vector3.down).normalized;
        float extraEntranceDistance = HasMechanic(MazeMechanic.Rotating)
            ? rotatingEntranceExtraViewportDistance
            : 0f;
        Vector3 offscreenPosition = GetOffscreenPosition(
            startPosition,
            entranceOutwardDirection,
            extraEntranceDistance);
        playerMovement.transform.position = offscreenPosition;
        playerMovement.BeginEntranceTransitionTrail(offscreenPosition);

        if (entranceStartDelay > 0f)
        {
            yield return new WaitForSeconds(entranceStartDelay);
        }

        yield return MovePlayerTo(
            startPosition,
            playerEntranceSpeed,
            playerMovement.UpdateEntranceTransitionTrail);

        playerMovement.transform.position = startPosition;
        playerMovement.CompleteEntranceTransitionTrail();
        playerMovement.SetInputLocked(false);
        levelIsRunning = true;
    }

    private IEnumerator PlayPlayerExitSequence()
    {
        if (playerMovement == null || mazeGenerator == null
            || gameplayCamera == null || mazeGenerator.EndPointIndex < 0)
        {
            yield break;
        }

        playerMovement.SetInputLocked(true);

        // Let PlayerMovement finish its endpoint bookkeeping before the
        // transition starts changing its transform directly.
        yield return null;
        yield return FadeEndpoint();

        Vector3 exitPosition = mazeGenerator.GetNavigationPointWorldPosition(
            mazeGenerator.EndPointIndex);
        exitPosition.z = playerMovement.transform.position.z;
        playerMovement.transform.position = exitPosition;

        Vector3 exitOutwardDirection = mazeGenerator.transform.TransformDirection(
            Vector3.up).normalized;
        Vector3 offscreenPosition = GetOffscreenPosition(
            exitPosition,
            exitOutwardDirection);
        playerMovement.BeginExitTransitionTrail();
        yield return MovePlayerTo(
            offscreenPosition,
            playerTransitionSpeed,
            playerMovement.UpdateExitTransitionTrail);
        playerMovement.transform.position = offscreenPosition;
    }

    private IEnumerator MovePlayerTo(
        Vector3 destination,
        float movementSpeed,
        Action<Vector3> updateTrail)
    {
        Transform playerTransform = playerMovement.transform;
        float speed = Mathf.Max(0.1f, movementSpeed);

        while ((playerTransform.position - destination).sqrMagnitude > 0.000001f)
        {
            playerTransform.position = Vector3.MoveTowards(
                playerTransform.position,
                destination,
                speed * Time.deltaTime);
            updateTrail?.Invoke(playerTransform.position);
            yield return null;
        }

        updateTrail?.Invoke(destination);
    }

    private Vector3 GetOffscreenPosition(
        Vector3 origin,
        Vector3 worldDirection,
        float extraViewportDistance = 0f)
    {
        Vector3 originViewport = gameplayCamera.WorldToViewportPoint(origin);
        Vector3 directionViewportPoint = gameplayCamera.WorldToViewportPoint(
            origin + worldDirection);
        Vector2 viewportDirection = new Vector2(
            directionViewportPoint.x - originViewport.x,
            directionViewportPoint.y - originViewport.y).normalized;

        if (viewportDirection.sqrMagnitude <= 0.000001f)
        {
            return origin + worldDirection * gameplayCamera.orthographicSize * 3f;
        }

        float margin = Mathf.Max(0f, offscreenViewportMargin);
        float distanceToEdge = float.PositiveInfinity;
        if (viewportDirection.x > 0.0001f)
        {
            distanceToEdge = Mathf.Min(
                distanceToEdge,
                (1f + margin - originViewport.x) / viewportDirection.x);
        }
        else if (viewportDirection.x < -0.0001f)
        {
            distanceToEdge = Mathf.Min(
                distanceToEdge,
                (-margin - originViewport.x) / viewportDirection.x);
        }

        if (viewportDirection.y > 0.0001f)
        {
            distanceToEdge = Mathf.Min(
                distanceToEdge,
                (1f + margin - originViewport.y) / viewportDirection.y);
        }
        else if (viewportDirection.y < -0.0001f)
        {
            distanceToEdge = Mathf.Min(
                distanceToEdge,
                (-margin - originViewport.y) / viewportDirection.y);
        }

        if (float.IsInfinity(distanceToEdge) || distanceToEdge < 0f)
        {
            return origin + worldDirection * gameplayCamera.orthographicSize * 3f;
        }

        distanceToEdge += Mathf.Max(0f, extraViewportDistance);

        Vector3 targetViewport = originViewport;
        targetViewport.x += viewportDirection.x * distanceToEdge;
        targetViewport.y += viewportDirection.y * distanceToEdge;
        Vector3 targetWorld = gameplayCamera.ViewportToWorldPoint(targetViewport);
        targetWorld.z = origin.z;
        return targetWorld;
    }

    private IEnumerator FadeEndpoint()
    {
        if (endpointRenderer == null)
        {
            yield break;
        }

        float duration = Mathf.Max(0f, endpointFadeDuration);
        if (duration <= 0f)
        {
            Color transparentColor = endpointColor;
            transparentColor.a = 0f;
            endpointRenderer.color = transparentColor;
            yield break;
        }

        Color startColor = endpointRenderer.color;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            Color fadedColor = startColor;
            fadedColor.a = Mathf.Lerp(startColor.a, 0f, elapsed / duration);
            endpointRenderer.color = fadedColor;
            yield return null;
        }

        Color finalColor = startColor;
        finalColor.a = 0f;
        endpointRenderer.color = finalColor;
    }

    private void CacheEndpointAppearance()
    {
        if (endpointWorld == null)
        {
            return;
        }

        endpointRenderer = endpointWorld.GetComponentInChildren<SpriteRenderer>(true);
        if (endpointRenderer != null)
        {
            endpointColor = endpointRenderer.color;
        }
    }

    private void ResetEndpointAppearance()
    {
        if (endpointRenderer != null)
        {
            endpointRenderer.color = endpointColor;
        }
    }

    private void SetGameplayButtonsInteractable(bool isInteractable)
    {
        if (backButton != null)
        {
            backButton.interactable = isInteractable;
        }

        if (restartButton != null)
        {
            restartButton.interactable = isInteractable;
        }
    }
}
