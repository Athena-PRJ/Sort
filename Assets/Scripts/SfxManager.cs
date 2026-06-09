using UnityEngine;

namespace Sort
{
    /// <summary>The distinct one-shot sound effects the game can play. One AudioClip per id, wired in the Inspector.</summary>
    public enum SfxId
    {
        ColumnClick,    // a piece dropped onto a column (a valid tap)
        Reject,         // tapped a column that can't accept the move (frozen / Only-Stack mismatch)
        ColumnComplete, // a column got sorted & locked
        Win,            // level cleared
        Rewind,         // Rewind booster used
        Switch,         // Switch booster used
        Magnet,         // Magnet booster used
        TieBreak,       // a Tie binding broke
        Unfreeze        // a frozen / Lock-Color column unfroze
    }

    /// <summary>
    /// Central one-shot SFX player. Put ONE of these in the gameplay scene (an empty GameObject with this
    /// component) and drag an AudioClip into each slot in the Inspector — leave any slot null to play
    /// nothing for that event. Gameplay code fires sounds with the static <see cref="Play"/> so callers
    /// don't need a reference: e.g. <c>SfxManager.Play(SfxId.ColumnClick)</c>.
    ///
    /// Uses a single AudioSource + PlayOneShot, so overlapping effects mix correctly. The on/off state
    /// persists in PlayerPrefs via <see cref="Enabled"/> (default ON) — bind a Settings button to it the
    /// same way as Haptics. If no SfxManager exists in the scene, all Play calls are harmless no-ops.
    /// </summary>
    [DisallowMultipleComponent]
    public class SfxManager : MonoBehaviour
    {
        public static SfxManager Instance { get; private set; }

        [Tooltip("AudioSource used for PlayOneShot. Auto-added if left null. Set its output to your SFX " +
                 "AudioMixer group if you use one.")]
        [SerializeField] private AudioSource source;

        [Header("Clips (drag a .wav per event; null = silent for that event)")]
        [SerializeField] private AudioClip columnClick;
        [SerializeField] private AudioClip reject;
        [SerializeField] private AudioClip columnComplete;
        [SerializeField] private AudioClip win;
        [SerializeField] private AudioClip rewind;
        [SerializeField] private AudioClip switchUse;
        [SerializeField] private AudioClip magnet;
        [SerializeField] private AudioClip tieBreak;
        [SerializeField] private AudioClip unfreeze;

        [Header("Mix")]
        [Tooltip("Master multiplier applied to every SFX PlayOneShot.")]
        [Range(0f, 1f)] [SerializeField] private float volume = 1f;

        const string PrefKey = "Sort_SfxEnabled";
        static bool? cachedEnabled;

        /// <summary>Whether SFX play. Persisted in PlayerPrefs (default ON). Bind a Settings toggle/button to this.</summary>
        public static bool Enabled
        {
            get
            {
                if (cachedEnabled == null) cachedEnabled = PlayerPrefs.GetInt(PrefKey, 1) != 0;
                return cachedEnabled.Value;
            }
            set
            {
                cachedEnabled = value;
                PlayerPrefs.SetInt(PrefKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        void Awake()
        {
            // Last one loaded wins (a scene reload spawns a fresh manager); that's fine since all SFX are in-game.
            Instance = this;
            if (source == null) source = GetComponent<AudioSource>();
            if (source == null) source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Plays the clip for <paramref name="id"/> if a manager exists, SFX are enabled, and a clip is wired.</summary>
        public static void Play(SfxId id) => Instance?.PlayInternal(id);

        void PlayInternal(SfxId id)
        {
            if (!Enabled || source == null) return;
            var clip = Resolve(id);
            if (clip != null) source.PlayOneShot(clip, volume);
        }

        AudioClip Resolve(SfxId id)
        {
            switch (id)
            {
                case SfxId.ColumnClick:    return columnClick;
                case SfxId.Reject:         return reject;
                case SfxId.ColumnComplete: return columnComplete;
                case SfxId.Win:            return win;
                case SfxId.Rewind:         return rewind;
                case SfxId.Switch:         return switchUse;
                case SfxId.Magnet:         return magnet;
                case SfxId.TieBreak:       return tieBreak;
                case SfxId.Unfreeze:       return unfreeze;
                default:                   return null;
            }
        }
    }
}
