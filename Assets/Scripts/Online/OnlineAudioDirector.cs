using UnityEngine;

namespace RunnerGame.Online
{
    [DisallowMultipleComponent]
    public sealed class OnlineAudioDirector : MonoBehaviour
    {
        private const string ProfileResourcePath = "OnlineAudioProfile";
        private const float DefaultMusicVolume = 1f;
        private const float DefaultUiVolume = 1f;

        private enum MusicMode : byte
        {
            None = 0,
            Menu = 1,
            Gameplay = 2
        }

        private static OnlineAudioDirector instance;

        [SerializeField] private OnlineAudioProfile profile;

        private AudioSource menuMusicSource;
        private AudioSource gameplayMusicSource;
        private AudioSource uiSource;
        private MusicMode activeMusicMode = MusicMode.None;
        private bool gameplayMusicPaused;
        private bool warnedMissingProfile;

        public static OnlineAudioDirector Instance => instance;
        public AudioClip WalkLoopClip => profile != null ? profile.WalkLoop : null;
        public AudioClip DeathClip => profile != null ? profile.Death : null;
        public AudioClip WinClip => profile != null ? profile.Win : null;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(this);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
            LoadProfileIfNeeded();
            EnsureSources();
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        public void PlayMenuLoop()
        {
            if (!TryEnsureReady())
            {
                return;
            }

            gameplayMusicPaused = false;
            StopSource(gameplayMusicSource);

            if (activeMusicMode == MusicMode.Menu && menuMusicSource.isPlaying)
            {
                return;
            }

            PlayLoop(menuMusicSource, profile.MenuMusic, DefaultMusicVolume);
            activeMusicMode = MusicMode.Menu;
        }

        public void PlayGameplayLoop()
        {
            if (!TryEnsureReady())
            {
                return;
            }

            StopSource(menuMusicSource);

            if (activeMusicMode == MusicMode.Gameplay)
            {
                if (gameplayMusicPaused)
                {
                    return;
                }

                if (gameplayMusicSource.isPlaying)
                {
                    return;
                }
            }

            gameplayMusicPaused = false;
            PlayLoop(gameplayMusicSource, profile.GameplayMusic, DefaultMusicVolume);
            activeMusicMode = MusicMode.Gameplay;
        }

        public void SetGameplayMusicPaused(bool paused)
        {
            if (!TryEnsureReady() || activeMusicMode != MusicMode.Gameplay)
            {
                return;
            }

            if (gameplayMusicPaused == paused)
            {
                return;
            }

            gameplayMusicPaused = paused;
            if (paused)
            {
                if (gameplayMusicSource.isPlaying)
                {
                    gameplayMusicSource.Pause();
                }
            }
            else if (gameplayMusicSource.clip != null)
            {
                gameplayMusicSource.UnPause();
                if (!gameplayMusicSource.isPlaying)
                {
                    gameplayMusicSource.Play();
                }
            }
        }

        public void PlayUiSelect()
        {
            if (!TryEnsureReady() || profile.UiSelect == null)
            {
                return;
            }

            uiSource.PlayOneShot(profile.UiSelect, DefaultUiVolume);
        }

        private bool TryEnsureReady()
        {
            LoadProfileIfNeeded();
            EnsureSources();

            if (profile != null)
            {
                return true;
            }

            if (!warnedMissingProfile)
            {
                Debug.LogWarning("[OnlineAudioDirector] Missing OnlineAudioProfile asset. Audio playback is disabled until the profile is available.");
                warnedMissingProfile = true;
            }

            return false;
        }

        private void LoadProfileIfNeeded()
        {
            if (profile != null)
            {
                return;
            }

            profile = Resources.Load<OnlineAudioProfile>(ProfileResourcePath);
        }

        private void EnsureSources()
        {
            menuMusicSource ??= CreateMusicSource("OnlineMenuMusic");
            gameplayMusicSource ??= CreateMusicSource("OnlineGameplayMusic");
            uiSource ??= CreateUiSource("OnlineUiSfx");
        }

        private AudioSource CreateMusicSource(string sourceName)
        {
            AudioSource source = gameObject.AddComponent<AudioSource>();
            source.name = sourceName;
            source.playOnAwake = false;
            source.loop = true;
            source.volume = DefaultMusicVolume;
            source.spatialBlend = 0f;
            source.ignoreListenerPause = false;
            return source;
        }

        private AudioSource CreateUiSource(string sourceName)
        {
            AudioSource source = gameObject.AddComponent<AudioSource>();
            source.name = sourceName;
            source.playOnAwake = false;
            source.loop = false;
            source.volume = DefaultUiVolume;
            source.spatialBlend = 0f;
            source.ignoreListenerPause = false;
            return source;
        }

        private static void StopSource(AudioSource source)
        {
            if (source == null)
            {
                return;
            }

            source.Stop();
            source.clip = null;
        }

        private static void PlayLoop(AudioSource source, AudioClip clip, float volume)
        {
            if (source == null || clip == null)
            {
                return;
            }

            source.clip = clip;
            source.volume = volume;
            source.loop = true;
            source.Play();
        }
    }
}
