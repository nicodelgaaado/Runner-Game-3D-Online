using UnityEngine;

namespace RunnerGame.Online
{
    [DisallowMultipleComponent]
    public sealed class RunnerAudioFeedback : MonoBehaviour
    {
        private const float MovementMinDistance = 1f;
        private const float MovementMaxDistance = 40f;
        private const float SfxMinDistance = 1f;
        private const float SfxMaxDistance = 55f;

        private AudioSource movementSource;
        private AudioSource sfxSource;
        private bool initialized;
        private int observedHazardHitNonce;
        private int lastWinRoundStartTick = int.MinValue;
        private int lastWinLevelIndex = -1;

        private void Awake()
        {
            EnsureSources();
        }

        private void OnDisable()
        {
            StopAll();
        }

        public void UpdateFeedback(RaceRoundState roundState, bool moving, bool falling, bool climbing, bool respawning, bool winner, int hazardHitNonce)
        {
            EnsureSources();

            bool shouldPlayMovement = roundState.Phase == RaceRoundPhase.Racing
                && moving
                && !falling
                && !climbing
                && !respawning;

            SetMovementLoopActive(shouldPlayMovement);

            if (!initialized)
            {
                observedHazardHitNonce = hazardHitNonce;
                if (winner)
                {
                    lastWinRoundStartTick = roundState.RoundStartTick;
                    lastWinLevelIndex = roundState.LevelIndex;
                }

                initialized = true;
                return;
            }

            if (hazardHitNonce != observedHazardHitNonce)
            {
                observedHazardHitNonce = hazardHitNonce;
                PlayOneShot(OnlineAudioDirector.Instance != null ? OnlineAudioDirector.Instance.DeathClip : null);
            }

            bool shouldPlayWin = winner
                && (roundState.Phase == RaceRoundPhase.RoundResult || roundState.Phase == RaceRoundPhase.MatchComplete)
                && (roundState.RoundStartTick != lastWinRoundStartTick || roundState.LevelIndex != lastWinLevelIndex);

            if (shouldPlayWin)
            {
                lastWinRoundStartTick = roundState.RoundStartTick;
                lastWinLevelIndex = roundState.LevelIndex;
                PlayOneShot(OnlineAudioDirector.Instance != null ? OnlineAudioDirector.Instance.WinClip : null);
            }
        }

        public void StopAll()
        {
            if (movementSource != null)
            {
                movementSource.Stop();
                movementSource.clip = null;
            }

            if (sfxSource != null)
            {
                sfxSource.Stop();
                sfxSource.clip = null;
            }

            initialized = false;
        }

        private void EnsureSources()
        {
            movementSource ??= CreateSpatialSource(loop: true, MovementMinDistance, MovementMaxDistance);
            sfxSource ??= CreateSpatialSource(loop: false, SfxMinDistance, SfxMaxDistance);
        }

        private AudioSource CreateSpatialSource(bool loop, float minDistance, float maxDistance)
        {
            AudioSource source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = minDistance;
            source.maxDistance = maxDistance;
            source.dopplerLevel = 0f;
            source.volume = 1f;
            return source;
        }

        private void SetMovementLoopActive(bool shouldPlay)
        {
            if (!shouldPlay)
            {
                if (movementSource.isPlaying)
                {
                    movementSource.Stop();
                }

                movementSource.clip = null;
                return;
            }

            AudioClip walkLoopClip = OnlineAudioDirector.Instance != null ? OnlineAudioDirector.Instance.WalkLoopClip : null;
            if (walkLoopClip == null)
            {
                if (movementSource.isPlaying)
                {
                    movementSource.Stop();
                }

                movementSource.clip = null;
                return;
            }

            if (movementSource.clip != walkLoopClip)
            {
                movementSource.clip = walkLoopClip;
            }

            if (!movementSource.isPlaying)
            {
                movementSource.Play();
            }
        }

        private void PlayOneShot(AudioClip clip)
        {
            if (clip == null)
            {
                return;
            }

            sfxSource.PlayOneShot(clip, 1f);
        }
    }
}
