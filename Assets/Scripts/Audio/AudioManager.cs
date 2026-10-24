using UnityEngine;

/// <summary>
/// Centralizes the small set of sound effects currently used by the maze.
/// Add new clips only when a matching gameplay event exists.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)]
internal sealed class AudioManager : MonoBehaviour
{
    private const string SfxEnabledKey = "audio_sfx_enabled";
    private const string SfxVolumeKey = "audio_sfx_volume";

    internal static AudioManager Instance { get; private set; }

    [Header("Audio Source")]
    [Tooltip("Optional. A 2D AudioSource is created automatically when this is empty.")]
    [SerializeField] private AudioSource sfxSource;

    [Header("Maze Sounds")]
    [SerializeField] private AudioClip buttonClickClip;
    [SerializeField] private AudioClip moveClip;
    [SerializeField] private AudioClip mazeChangeClip;
    [SerializeField] private AudioClip levelCompleteClip;
    [SerializeField] private AudioClip timeUpClip;

    private bool sfxEnabled = true;
    private float sfxVolume = 1f;
    private bool isForceMuted;

    internal bool SfxEnabled => sfxEnabled;
    internal float SfxVolume => sfxVolume;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("More than one AudioManager exists. Disabling the duplicate.", this);
            enabled = false;
            return;
        }

        Instance = this;
        sfxEnabled = PlayerPrefs.GetInt(SfxEnabledKey, 1) == 1;
        sfxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(SfxVolumeKey, 1f));

        if (sfxSource == null)
        {
            sfxSource = gameObject.AddComponent<AudioSource>();
        }

        sfxSource.playOnAwake = false;
        sfxSource.loop = false;
        sfxSource.spatialBlend = 0f;
        ApplySfxVolume();
    }

    internal void SetSfxEnabled(bool isEnabled)
    {
        sfxEnabled = isEnabled;
        PlayerPrefs.SetInt(SfxEnabledKey, isEnabled ? 1 : 0);
        PlayerPrefs.Save();
        ApplySfxVolume();
    }

    internal void SetSfxVolume(float volume)
    {
        sfxVolume = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat(SfxVolumeKey, sfxVolume);
        PlayerPrefs.Save();
        ApplySfxVolume();
    }

    internal void PlayButton() => Play(buttonClickClip);
    internal void PlayMove() => Play(moveClip);
    internal void PlayMazeChange() => Play(mazeChangeClip);
    internal void PlayLevelComplete() => Play(levelCompleteClip);
    internal void PlayTimeUp() => Play(timeUpClip);

    internal void SetMuteAll(bool forceMute)
    {
        if (forceMute == isForceMuted)
        {
            return;
        }

        isForceMuted = forceMute;
        AudioListener.volume = forceMute ? 0f : 1f;
    }

    private void Play(AudioClip clip)
    {
        if (!sfxEnabled || clip == null || sfxSource == null)
        {
            return;
        }

        sfxSource.PlayOneShot(clip);
    }

    private void ApplySfxVolume()
    {
        if (sfxSource != null)
        {
            sfxSource.volume = sfxEnabled ? sfxVolume : 0f;
        }
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        SetMuteAll(!hasFocus);
    }

    private void OnApplicationPause(bool isPaused)
    {
        SetMuteAll(isPaused);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
}
