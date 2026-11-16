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

    [Header("Audio Sources")]
    [Tooltip("Optional. A 2D AudioSource is created automatically when this is empty.")]
    [SerializeField] private AudioSource sfxSource;
    [SerializeField] private AudioSource timerSource;
    [SerializeField] private AudioSource resultSource;

    [Header("Maze Sounds")]
    [SerializeField] private AudioClip buttonClickClip;
    [SerializeField] private AudioClip clockTickingClip;
    [SerializeField] private AudioClip levelCompleteClip;
    [SerializeField] private AudioClip timeUpClip;
    [SerializeField] private AudioClip popupClip;
    [SerializeField] private AudioClip navigationTapClip;
    [SerializeField, Range(0f, 1f)] private float navigationTapVolume = 0.3f;

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

        sfxSource = ConfigureSource(sfxSource, false);
        timerSource = ConfigureSource(timerSource, true);
        resultSource = ConfigureSource(resultSource, false);
        ApplySfxVolume();
    }

    private AudioSource ConfigureSource(AudioSource source, bool loop)
    {
        if (source == null)
        {
            source = gameObject.AddComponent<AudioSource>();
        }

        source.playOnAwake = false;
        source.loop = loop;
        source.spatialBlend = 0f;
        return source;
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
    internal void PlayMove() => Play(navigationTapClip, navigationTapVolume);
    internal void PlayLevelComplete() => PlayResult(levelCompleteClip);
    internal void PlayTimeUp() => PlayResult(timeUpClip);
    internal void PlayPopup() => Play(popupClip);

    private void PlayResult(AudioClip clip)
    {
        SetClockTicking(false);

        if (!sfxEnabled || clip == null || resultSource == null)
        {
            return;
        }

        resultSource.Stop();
        resultSource.PlayOneShot(clip);
    }

    internal void SetClockTicking(bool active)
    {
        if (timerSource == null)
        {
            return;
        }

        if (!isActiveAndEnabled || !active || !sfxEnabled || clockTickingClip == null)
        {
            timerSource.Stop();
            return;
        }

        if (!timerSource.isPlaying)
        {
            timerSource.clip = clockTickingClip;
            timerSource.Play();
        }
    }

    internal void SetMuteAll(bool forceMute)
    {
        if (forceMute == isForceMuted)
        {
            return;
        }

        isForceMuted = forceMute;
        AudioListener.volume = forceMute ? 0f : 1f;
    }

    private void Play(AudioClip clip, float volumeScale = 1f)
    {
        if (!sfxEnabled || clip == null || sfxSource == null)
        {
            return;
        }

        sfxSource.PlayOneShot(clip, volumeScale);
    }

    private void ApplySfxVolume()
    {
        float volume = sfxEnabled ? sfxVolume : 0f;
        if (sfxSource != null)
        {
            sfxSource.volume = volume;
        }

        if (timerSource != null)
        {
            timerSource.volume = volume;
        }

        if (resultSource != null)
        {
            resultSource.volume = volume;
        }

        if (!sfxEnabled)
        {
            SetClockTicking(false);
        }
    }

    private void OnDisable()
    {
        SetClockTicking(false);
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
