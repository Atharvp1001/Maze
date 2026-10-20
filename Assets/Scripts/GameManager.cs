using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Manages the main menu, maze modes, automatic difficulty, and level progression.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]
internal sealed class GameManager : MonoBehaviour
{
    private const int SaveVersion = 1;
    private const string SaveVersionKey = "Maze.SaveVersion";
    private const string SaveLevelKey = "Maze.Level";
    private const string SaveDifficultyKey = "Maze.Difficulty";
    private const string SaveModeKey = "Maze.Mode";
    private const string SaveColumnsKey = "Maze.Columns";
    private const string SaveRowsKey = "Maze.Rows";
    private const string SaveSeedKey = "Maze.Seed";
    private const string SaveWallsKey = "Maze.Walls";
    private const string SavePreviousDifficultyKey = "Maze.PreviousDifficulty";
    private const string SaveDifficultyStreakKey = "Maze.DifficultyStreak";
    private const string SavePreviousModeKey = "Maze.PreviousMode";
    private const string SaveModeStreakKey = "Maze.ModeStreak";
    private const string SaveMixedMechanicsKey = "Maze.MixedMechanics";

    private enum MazeMode
    {
        Normal,
        Timed,
        Rotating,
        Shifting,
        Mixed
    }

    private enum MazeModeTestOverride
    {
        Random,
        Normal,
        Timed,
        Rotating,
        Shifting,
        Mixed
    }

    [Flags]
    private enum MazeMechanic
    {
        None = 0,
        Timed = 1,
        Rotating = 2,
        Shifting = 4
    }

    [Header("Testing")]
    [Tooltip("Choose a mode to force it for testing. Leave this on Random for normal gameplay.")]
    [SerializeField] private MazeModeTestOverride testMazeMode = MazeModeTestOverride.Random;
    [Tooltip("Clear all saved progression when Play mode starts. Uncheck this after the reset.")]
    [SerializeField] private bool resetSavedProgress;
    [Tooltip("Start directly at this level for testing. Use 0 to load progress normally.")]
    [SerializeField, Min(0)] private int startAtLevel;

    [Header("Gameplay")]
    [SerializeField] private MazeGenerator mazeGenerator;
    [SerializeField] private PlayerMovement playerMovement;
    [SerializeField] private GameObject mazeWorld;
    [SerializeField] private GameObject playerWorld;
    [SerializeField] private GameObject endpointWorld;

    [Header("Canvases")]
    [SerializeField] private GameObject mainMenuCanvas;
    [SerializeField] private GameObject gameCanvas;
    [SerializeField] private GameObject winCanvas;
    [SerializeField] private GameObject loseCanvas;

    [Header("Menu")]
    [SerializeField] private Button playButton;
    [SerializeField] private TMP_Text menuDifficultyText;
    [SerializeField] private TMP_Text menuLevelText;

    [Header("In-Game UI")]
    [SerializeField] private Button backButton;
    [SerializeField] private Button restartButton;
    [SerializeField] private TMP_Text gameplayLevelText;
    [SerializeField] private TMP_Text gameplayMazeTypeText;
    [SerializeField] private TMP_Text gameplayTimerText;
    [SerializeField] private TMP_Text gameplayDifficultyText;
    [SerializeField] private Camera gameplayCamera;

    [Header("Win UI")]
    [SerializeField] private RectTransform winStar;
    [SerializeField] private RectTransform winTitle;
    [SerializeField] private RectTransform winMessage;
    [SerializeField] private RectTransform winTimer;
    [SerializeField] private TMP_Text winMessageText;
    [SerializeField] private TMP_Text winTimerText;

    [Header("Lose UI")]
    [SerializeField] private RectTransform loseHourglass;
    [SerializeField] private RectTransform loseTitle;
    [SerializeField] private RectTransform loseMessage;
    [SerializeField] private RectTransform loseBack;
    [SerializeField] private RectTransform loseRetry;
    [SerializeField] private TMP_Text loseMessageText;
    [SerializeField] private Button loseBackButton;
    [SerializeField] private Button loseRetryButton;

    private static readonly string[] WinMessages =
    {
        "Awesome",
        "Amazing",
        "Fantastic",
        "Brilliant"
    };

    private static readonly string[] LoseMessages =
    {
        "Nice Try!",
        "So Close!",
        "Almost There!",
        "Keep Going!"
    };

    private MazeGenerator.MazeDifficulty selectedDifficulty;
    private MazeGenerator.MazeDifficulty previousDifficulty;
    private readonly List<Vector2Int> protectedTrailCells = new List<Vector2Int>(128);
    private MazeMode currentMazeMode;
    private MazeMode previousMazeMode;
    private MazeMechanic activeMechanics;
    private Quaternion defaultCameraRotation;
    private MazeZoomController mazeZoomController;
    private int currentLevel = 1;
    private int consecutiveDifficultyCount;
    private int consecutiveModeCount;
    private int mazeColumns;
    private int mazeRows;
    private int previousMazeColumns;
    private int previousMazeRows;
    private int mazeSeed;
    private string savedWallLayout = string.Empty;
    private bool hasActiveLevel;
    private bool isAdvancingLevel;
    private bool levelIsRunning;
    private float timerValue;
    private int pointsSinceRotation;
    private int pointsSinceShift;
    private int mixedEventCooldownPoints;
    private float targetCameraAngle;
    private bool shiftWarningIsRunning;
    private Sequence winSequence;
    private Sequence loseSequence;
    private Vector3 winStarScale = Vector3.one;
    private Vector3 winTitleScale = Vector3.one;
    private Vector3 winMessageScale = Vector3.one;
    private Vector3 winTimerScale = Vector3.one;
    private Vector3 loseHourglassScale = Vector3.one;
    private Vector3 loseTitleScale = Vector3.one;
    private Vector3 loseMessageScale = Vector3.one;
    private Vector3 loseBackScale = Vector3.one;
    private Vector3 loseRetryScale = Vector3.one;

    private void Awake()
    {
        CacheWinScreenScales();
        CacheLoseScreenScales();
        if (winCanvas != null)
        {
            winCanvas.transform.localScale = Vector3.one;
            winCanvas.SetActive(false);
        }

        if (loseCanvas != null)
        {
            loseCanvas.transform.localScale = Vector3.one;
            loseCanvas.SetActive(false);
        }

        if (gameplayCamera != null)
        {
            defaultCameraRotation = gameplayCamera.transform.rotation;
            mazeZoomController = gameplayCamera.GetComponent<MazeZoomController>();
        }

        if (resetSavedProgress)
        {
            ClearSavedProgress();
            resetSavedProgress = false;
        }

        if (startAtLevel > 0)
        {
            ClearSavedProgress();
            currentLevel = Mathf.Max(1, startAtLevel);
            PrepareUpcomingLevel();
        }
        else if (!TryLoadProgress())
        {
            PrepareUpcomingLevel();
        }
        else
        {
            ApplyTestModeOverride();
            UpdateLabels();
        }

        SaveProgress();
        ShowMainMenu();
    }

    private void Update()
    {
        if (!levelIsRunning || isAdvancingLevel)
        {
            return;
        }

        UpdateTimer();
        if (!levelIsRunning)
        {
            return;
        }

        UpdateRotation();
        UpdateShiftingMaze();
    }

    private void OnEnable()
    {
        if (playButton != null)
        {
            playButton.onClick.AddListener(Play);
        }

        if (backButton != null)
        {
            backButton.onClick.AddListener(ShowMainMenu);
        }

        if (restartButton != null)
        {
            restartButton.onClick.AddListener(RestartLevel);
        }

        if (loseBackButton != null)
        {
            loseBackButton.onClick.AddListener(ReturnToMenuFromLoseScreen);
        }

        if (loseRetryButton != null)
        {
            loseRetryButton.onClick.AddListener(RetryFromLoseScreen);
        }

        if (playerMovement != null)
        {
            playerMovement.LevelCompleted += HandleLevelCompleted;
            playerMovement.PointsTravelled += HandlePointsTravelled;
        }

        if (mazeGenerator != null)
        {
            mazeGenerator.WallLayoutChanged += HandleWallLayoutChanged;
        }
    }

    private void OnDisable()
    {
        if (playButton != null)
        {
            playButton.onClick.RemoveListener(Play);
        }

        if (backButton != null)
        {
            backButton.onClick.RemoveListener(ShowMainMenu);
        }

        if (restartButton != null)
        {
            restartButton.onClick.RemoveListener(RestartLevel);
        }

        if (loseBackButton != null)
        {
            loseBackButton.onClick.RemoveListener(ReturnToMenuFromLoseScreen);
        }

        if (loseRetryButton != null)
        {
            loseRetryButton.onClick.RemoveListener(RetryFromLoseScreen);
        }

        if (playerMovement != null)
        {
            playerMovement.LevelCompleted -= HandleLevelCompleted;
            playerMovement.PointsTravelled -= HandlePointsTravelled;
        }

        if (mazeGenerator != null)
        {
            mazeGenerator.WallLayoutChanged -= HandleWallLayoutChanged;
        }
    }

    private void Play()
    {
        if (mazeGenerator == null)
        {
            return;
        }

        if (mainMenuCanvas != null)
        {
            mainMenuCanvas.SetActive(false);
        }

        if (winCanvas != null)
        {
            winCanvas.SetActive(false);
        }

        if (loseCanvas != null)
        {
            loseCanvas.SetActive(false);
        }

        if (gameCanvas != null)
        {
            gameCanvas.SetActive(true);
        }

        SetGameplayWorldVisible(true);
        Time.timeScale = 1f;
        Canvas.ForceUpdateCanvases();

        if (!hasActiveLevel)
        {
            ResetCameraRotation();
            UpdateLabels();
            mazeGenerator.SetDimensions(mazeColumns, mazeRows);
            mazeGenerator.GenerateSavedLevel(
                selectedDifficulty,
                mazeSeed,
                savedWallLayout,
                currentLevel > 5,
                GetBaseMazeComplexityMultiplier());
            mazeSeed = mazeGenerator.CurrentLevelSeed;
            hasActiveLevel = true;
            savedWallLayout = mazeGenerator.CaptureWallLayout();
            SaveProgress();
            BeginLevelRuntime();
        }
    }

    private void ShowMainMenu()
    {
        StopWinAnimation();
        StopLoseAnimation();
        Time.timeScale = 0f;

        if (gameCanvas != null)
        {
            gameCanvas.SetActive(false);
        }

        if (mainMenuCanvas != null)
        {
            mainMenuCanvas.SetActive(true);
        }

        if (winCanvas != null)
        {
            winCanvas.SetActive(false);
        }

        if (loseCanvas != null)
        {
            loseCanvas.SetActive(false);
        }

        SetGameplayWorldVisible(false);
        UpdateLabels();
    }

    private void RestartLevel()
    {
        if (hasActiveLevel && mazeGenerator != null)
        {
            StopAllCoroutines();
            levelIsRunning = false;
            shiftWarningIsRunning = false;
            mazeGenerator.CancelPreparedWallShift();
            if (playerMovement != null)
            {
                playerMovement.SetInputLocked(false);
            }

            ResetCameraRotation();
            mazeGenerator.RestartCurrentLevel();
            savedWallLayout = mazeGenerator.CaptureWallLayout();
            SaveProgress();
            BeginLevelRuntime();
        }
    }

    private void HandleLevelCompleted()
    {
        if (!hasActiveLevel || isAdvancingLevel)
        {
            return;
        }

        float completionTime = HasMechanic(MazeMechanic.Timed)
            ? Mathf.Max(0f, GetTimedMazeLimit() - timerValue)
            : Mathf.Max(0f, timerValue);

        levelIsRunning = false;
        isAdvancingLevel = true;
        hasActiveLevel = false;
        ResetCameraRotation();
        StartCoroutine(ShowWinThenNextLevelTransition(completionTime));
    }

    private IEnumerator ShowWinThenNextLevelTransition(float completionTime)
    {
        if (playButton != null)
        {
            playButton.interactable = false;
        }

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
        timerValue = HasMechanic(MazeMechanic.Timed)
            ? GetTimedMazeLimit()
            : 0f;
        pointsSinceRotation = 0;
        pointsSinceShift = 0;
        mixedEventCooldownPoints = 0;
        targetCameraAngle = GetDefaultCameraAngle();
        levelIsRunning = true;
        UpdateTimerText();
    }

    private void UpdateTimer()
    {
        if (HasMechanic(MazeMechanic.Timed))
        {
            timerValue = Mathf.Max(0f, timerValue - Time.deltaTime);
            UpdateTimerText();

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
        if (currentLevel <= 5 || mazeGenerator == null)
        {
            return 120f;
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
                secondsPerStep = 0.8f;
                secondsPerTurn = 0.55f;
                secondsPerJunction = 1.5f;
                wrongRouteRatio = 0.35f;
                secondsPerWrongStep = 0.4f;
                safetyBuffer = 15f;
                minimumLimit = 60f;
                maximumLimit = 240f;
                break;
            case MazeGenerator.MazeDifficulty.Medium:
                secondsPerStep = 0.65f;
                secondsPerTurn = 0.5f;
                secondsPerJunction = 1.3f;
                wrongRouteRatio = 0.45f;
                secondsPerWrongStep = 0.35f;
                safetyBuffer = 13f;
                minimumLimit = 50f;
                maximumLimit = 210f;
                break;
            case MazeGenerator.MazeDifficulty.Hard:
                secondsPerStep = 0.55f;
                secondsPerTurn = 0.45f;
                secondsPerJunction = 1.1f;
                wrongRouteRatio = 0.55f;
                secondsPerWrongStep = 0.3f;
                safetyBuffer = 12f;
                minimumLimit = 45f;
                maximumLimit = 180f;
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

    private void OnDestroy()
    {
        StopWinAnimation();
        StopLoseAnimation();
        SaveProgress();
        ResetCameraRotation();
        Time.timeScale = 1f;
    }
}
