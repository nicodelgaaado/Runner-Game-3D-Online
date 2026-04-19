using UnityEngine;

namespace RunnerGame.Online
{
    [CreateAssetMenu(menuName = "Runner Game Online/Online Audio Profile", fileName = "OnlineAudioProfile")]
    public sealed class OnlineAudioProfile : ScriptableObject
    {
        [Header("Music")]
        [SerializeField] private AudioClip menuMusic = null;
        [SerializeField] private AudioClip gameplayMusic = null;
        [Header("UI")]
        [SerializeField] private AudioClip uiSelect = null;
        [Header("Runner")]
        [SerializeField] private AudioClip walkLoop = null;
        [SerializeField] private AudioClip death = null;
        [SerializeField] private AudioClip win = null;

        public AudioClip MenuMusic => menuMusic;
        public AudioClip GameplayMusic => gameplayMusic;
        public AudioClip UiSelect => uiSelect;
        public AudioClip WalkLoop => walkLoop;
        public AudioClip Death => death;
        public AudioClip Win => win;
    }
}
