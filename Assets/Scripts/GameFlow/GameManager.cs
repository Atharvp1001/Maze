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
internal sealed partial class GameManager : MonoBehaviour
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

    [Header("Level Transitions")]
    [SerializeField, Min(0.1f)] private float playerEntranceSpeed = 7.5f;
    [SerializeField, Min(0.1f)] private float playerTransitionSpeed = 7.5f;
    [SerializeField, Min(0f)] private float entranceStartDelay = 0.2f;
    [Tooltip("Extra distance below the screen for modes that rotate, measured as a fraction of the viewport.")]
    [SerializeField, Min(0f)] private float rotatingEntranceExtraViewportDistance = 0.45f;
    [SerializeField, Min(0f)] private float endpointFadeDuration = 0.4f;
    [SerializeField, Min(0f)] private float offscreenViewportMargin = 0.08f;

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

    [Header("Timer Urgency")]
    [SerializeField, Min(1f)] private float urgentTimerScale = 1.18f;
    [SerializeField, Min(0f)] private float urgentTimerThreshold = 30f;
    [SerializeField, Min(0.1f)] private float urgentTimerSlowPulseSpeed = 0.9f;
    [SerializeField, Min(0.1f)] private float urgentTimerFastPulseSpeed = 1.8f;
    [SerializeField] private Color urgentTimerColor = new Color(0.9f, 0.08f, 0.04f, 1f);

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
    private RectTransform gameplayTimerTransform;
    private Vector3 gameplayTimerScale = Vector3.one;
    private Color gameplayTimerColor = Color.white;
    private float urgentTimerPulsePhase;
    private SpriteRenderer endpointRenderer;
    private Color endpointColor = Color.white;

    private void Awake()
    {
        CacheWinScreenScales();
        CacheLoseScreenScales();
        CacheGameplayTimerAppearance();
        CacheEndpointAppearance();
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
        ResetTimerUrgency();

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
        ResetTimerUrgency();
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
        ResetTimerUrgency();
        isAdvancingLevel = true;
        hasActiveLevel = false;
        SetGameplayButtonsInteractable(false);
        StartCoroutine(ShowWinThenNextLevelTransition(completionTime));
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
