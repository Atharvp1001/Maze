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

    private enum MazeMode
    {
        Normal,
        Timed,
        Rotating,
        Shifting
    }

    private enum MazeModeTestOverride
    {
        Random,
        Normal,
        Timed,
        Rotating,
        Shifting
    }

    [Header("Testing")]
    [Tooltip("Choose a mode to force it for testing. Leave this on Random for normal gameplay.")]
    [SerializeField] private MazeModeTestOverride testMazeMode = MazeModeTestOverride.Random;

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

    private static readonly string[] WinMessages =
    {
        "Awesome",
        "Amazing",
        "Fantastic",
        "Brilliant"
    };

    private MazeGenerator.MazeDifficulty selectedDifficulty;
    private readonly List<Vector2Int> protectedTrailCells = new List<Vector2Int>(128);
    private MazeMode currentMazeMode;
    private MazeMode previousMazeMode;
    private Quaternion defaultCameraRotation;
    private MazeZoomController mazeZoomController;
    private int currentLevel = 1;
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
    private float rotationCountdown;
    private float shiftCountdown;
    private float targetCameraAngle;
    private bool shiftWarningIsRunning;
    private Sequence winSequence;
    private Vector3 winStarScale = Vector3.one;
    private Vector3 winTitleScale = Vector3.one;
    private Vector3 winMessageScale = Vector3.one;
    private Vector3 winTimerScale = Vector3.one;

    private void Awake()
    {
        CacheWinScreenScales();
        if (winCanvas != null)
        {
            winCanvas.transform.localScale = Vector3.one;
            winCanvas.SetActive(false);
        }

        if (gameplayCamera != null)
        {
            defaultCameraRotation = gameplayCamera.transform.rotation;
            mazeZoomController = gameplayCamera.GetComponent<MazeZoomController>();
        }

        if (!TryLoadProgress())
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

        if (playerMovement != null)
        {
            playerMovement.LevelCompleted += HandleLevelCompleted;
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

        if (playerMovement != null)
        {
            playerMovement.LevelCompleted -= HandleLevelCompleted;
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
                savedWallLayout);
            hasActiveLevel = true;
            savedWallLayout = mazeGenerator.CaptureWallLayout();
            SaveProgress();
            BeginLevelRuntime();
        }
    }

    private void ShowMainMenu()
    {
        StopWinAnimation();
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

        SetGameplayWorldVisible(false);
        UpdateLabels();
    }

    private void RestartLevel()
    {
        if (hasActiveLevel && mazeGenerator != null)
        {
            levelIsRunning = false;
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

        float completionTime = currentMazeMode == MazeMode.Timed
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
            .Append(CreateWinPop(winStar, winStarScale, 0.42f))
            .AppendInterval(0.08f)
            .Append(CreateWinPop(winTitle, winTitleScale, 0.32f))
            .AppendInterval(0.05f)
            .Append(CreateWinPop(winMessage, winMessageScale, 0.3f))
            .AppendInterval(0.08f)
            .Append(CreateWinPop(winTimer, winTimerScale, 0.34f))
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

    private static Sequence CreateWinPop(
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
        timerValue = currentMazeMode == MazeMode.Timed
            ? GetTimedMazeLimit()
            : 0f;
        rotationCountdown = GetRotationInterval();
        shiftCountdown = GetShiftInterval();
        targetCameraAngle = GetDefaultCameraAngle();
        levelIsRunning = true;
        UpdateTimerText();
    }

    private void UpdateTimer()
    {
        if (currentMazeMode == MazeMode.Timed)
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
        mazeGenerator.RestartCurrentLevel();
        BeginLevelRuntime();
    }

    private void UpdateTimerText()
    {
        if (gameplayTimerText == null)
        {
            return;
        }

        int totalSeconds = currentMazeMode == MazeMode.Timed
            ? Mathf.CeilToInt(timerValue)
            : Mathf.FloorToInt(timerValue);
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;
        gameplayTimerText.text = $"{minutes:00}:{seconds:00}";
    }

    private void UpdateRotation()
    {
        if (currentMazeMode != MazeMode.Rotating || gameplayCamera == null)
        {
            return;
        }

        rotationCountdown -= Time.deltaTime;
        if (rotationCountdown <= 0f)
        {
            targetCameraAngle += UnityEngine.Random.value < 0.5f ? -90f : 90f;
            rotationCountdown = GetRotationInterval();
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
        if (currentMazeMode != MazeMode.Shifting || mazeGenerator == null
            || playerMovement == null || shiftWarningIsRunning)
        {
            return;
        }

        shiftCountdown -= Time.deltaTime;
        if (shiftCountdown <= 0f && !playerMovement.IsCurrentlyMoving)
        {
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
                1);
        }

        if (!shiftPrepared)
        {
            shiftCountdown = 1f;
            shiftWarningIsRunning = false;
            yield break;
        }

        if (gameplayMazeTypeText != null)
        {
            gameplayMazeTypeText.text = "Maze Shifting...";
        }

        float elapsed = 0f;
        while (elapsed < warningDuration
            && levelIsRunning
            && currentMazeMode == MazeMode.Shifting)
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

        if (levelIsRunning && currentMazeMode == MazeMode.Shifting
            && !playerMovement.IsCurrentlyMoving)
        {
            playerMovement.SetInputLocked(true);
            mazeGenerator.OpenPreparedShiftPassage();
            yield return new WaitForSeconds(passageOpenDelay);

            if (levelIsRunning && currentMazeMode == MazeMode.Shifting
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
            gameplayMazeTypeText.text = "Shifting Maze";
        }

        shiftCountdown = GetShiftInterval();
        shiftWarningIsRunning = false;
    }

    private void SelectRandomMazeMode()
    {
        if (testMazeMode != MazeModeTestOverride.Random)
        {
            currentMazeMode = (MazeMode)(testMazeMode - 1);
            return;
        }

        MazeMode selectedMode;
        int attempts = 0;

        do
        {
            selectedMode = RollWeightedMazeMode();
            attempts++;
        }
        while (consecutiveModeCount >= 2
            && selectedMode == previousMazeMode
            && attempts < 12);

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
    }

    private MazeMode RollWeightedMazeMode()
    {
        float normalWeight;
        float timedWeight;
        float rotatingWeight;
        float shiftingWeight;

        switch (selectedDifficulty)
        {
            case MazeGenerator.MazeDifficulty.Easy:
                normalWeight = 50f;
                timedWeight = 20f;
                rotatingWeight = 15f;
                shiftingWeight = 15f;
                break;
            case MazeGenerator.MazeDifficulty.Medium:
                normalWeight = 35f;
                timedWeight = 25f;
                rotatingWeight = 20f;
                shiftingWeight = 20f;
                break;
            case MazeGenerator.MazeDifficulty.Hard:
                normalWeight = 20f;
                timedWeight = 30f;
                rotatingWeight = 25f;
                shiftingWeight = 25f;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        float levelShift = Mathf.Min((currentLevel - 1) * 1.5f, 15f);
        normalWeight = Mathf.Max(10f, normalWeight - levelShift);
        timedWeight += levelShift * 0.3f;
        rotatingWeight += levelShift * 0.35f;
        shiftingWeight += levelShift * 0.35f;
        float roll = UnityEngine.Random.Range(
            0f,
            normalWeight + timedWeight + rotatingWeight + shiftingWeight);

        if (roll < normalWeight)
        {
            return MazeMode.Normal;
        }

        if (roll < normalWeight + timedWeight)
        {
            return MazeMode.Timed;
        }

        return roll < normalWeight + timedWeight + rotatingWeight
            ? MazeMode.Rotating
            : MazeMode.Shifting;
    }

    private float GetShiftInterval()
    {
        switch (selectedDifficulty)
        {
            case MazeGenerator.MazeDifficulty.Easy:
                return 12f;
            case MazeGenerator.MazeDifficulty.Medium:
                return 9f;
            case MazeGenerator.MazeDifficulty.Hard:
                return 7f;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private float GetTimedMazeLimit()
    {
        float baseLimit;
        float minimumLimit;

        switch (selectedDifficulty)
        {
            case MazeGenerator.MazeDifficulty.Easy:
                baseLimit = 120f;
                minimumLimit = 75f;
                break;
            case MazeGenerator.MazeDifficulty.Medium:
                baseLimit = 90f;
                minimumLimit = 55f;
                break;
            case MazeGenerator.MazeDifficulty.Hard:
                baseLimit = 65f;
                minimumLimit = 40f;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        return Mathf.Max(minimumLimit, baseLimit - (currentLevel - 1) * 2f);
    }

    private float GetRotationInterval()
    {
        float baseInterval;
        float minimumInterval;

        switch (selectedDifficulty)
        {
            case MazeGenerator.MazeDifficulty.Easy:
                baseInterval = 12f;
                minimumInterval = 8f;
                break;
            case MazeGenerator.MazeDifficulty.Medium:
                baseInterval = 9f;
                minimumInterval = 6f;
                break;
            case MazeGenerator.MazeDifficulty.Hard:
                baseInterval = 7f;
                minimumInterval = 4f;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        return Mathf.Max(minimumInterval, baseInterval - (currentLevel - 1) * 0.15f);
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

    private void ResetCameraRotation()
    {
        if (gameplayCamera != null)
        {
            gameplayCamera.transform.rotation = defaultCameraRotation;
        }
    }

    private void PrepareUpcomingLevel()
    {
        selectedDifficulty = GetDifficultyForLevel();
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
            || savedMode < 0 || savedMode > (int)MazeMode.Shifting
            || savedColumns < 2 || savedRows < 2)
        {
            return false;
        }

        currentLevel = Mathf.Max(1, PlayerPrefs.GetInt(SaveLevelKey, 1));
        selectedDifficulty = (MazeGenerator.MazeDifficulty)savedDifficulty;
        currentMazeMode = (MazeMode)savedMode;
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

    private MazeGenerator.MazeDifficulty GetDifficultyForLevel()
    {
        if (currentLevel <= 5)
        {
            return MazeGenerator.MazeDifficulty.Easy;
        }

        int difficultyIndex = (currentLevel - 6) % 3;
        return (MazeGenerator.MazeDifficulty)difficultyIndex;
    }

    private void SelectMazeDimensions()
    {
        int minimumSize;
        int maximumSize;

        if (currentLevel <= 5)
        {
            int growingSize = 5 + currentLevel;
            minimumSize = Mathf.Max(5, growingSize - 1);
            maximumSize = growingSize + 1;
        }
        else
        {
            switch (selectedDifficulty)
            {
                case MazeGenerator.MazeDifficulty.Easy:
                    minimumSize = 9;
                    maximumSize = 12;
                    break;
                case MazeGenerator.MazeDifficulty.Medium:
                    minimumSize = 13;
                    maximumSize = 17;
                    break;
                case MazeGenerator.MazeDifficulty.Hard:
                    minimumSize = 18;
                    maximumSize = 22;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        previousMazeColumns = mazeColumns;
        previousMazeRows = mazeRows;

        float laterLevelBias = currentLevel <= 5
            ? 0f
            : Mathf.Clamp01((currentLevel - 6) / 30f);
        int attempts = 0;

        do
        {
            mazeColumns = RollDimension(minimumSize, maximumSize, laterLevelBias);
            mazeRows = RollDimension(minimumSize, maximumSize, laterLevelBias);
            attempts++;
        }
        while (mazeColumns == previousMazeColumns
            && mazeRows == previousMazeRows
            && attempts < 12);
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
            gameplayMazeTypeText.text = $"{currentMazeMode} Maze";
        }
    }

    private void OnDestroy()
    {
        StopWinAnimation();
        SaveProgress();
        ResetCameraRotation();
        Time.timeScale = 1f;
    }
}
