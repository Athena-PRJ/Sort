using UnityEngine;

namespace Sort
{
    /// <summary>
    /// Looping background music. Put ONE of these in a scene (an empty GameObject + this component) and
    /// drag a music clip into <see cref="track"/>. It survives scene reloads so the music DOESN'T restart
    /// every time you replay / advance a level:
    ///   • First MusicManager to load starts its track and (by default) becomes a DontDestroyOnLoad singleton.
    ///   • Reloading the same scene spawns a duplicate whose Awake sees an existing manager playing the SAME
    ///     track → it destroys itself, so the music keeps playing uninterrupted.
    ///   • Entering a scene whose MusicManager has a DIFFERENT track (e.g. MainMenu vs gameplay) → the new
    ///     one takes over and the old one is replaced (a simple track switch).
    ///
    /// On/off persists in PlayerPrefs <see cref="Enabled"/> (key Sort_MusicEnabled, default ON) — bind a
    /// Settings button to it just like SFX / Haptics.
    /// </summary>
    [DisallowMultipleComponent]
    public class MusicManager : MonoBehaviour
    {
        public static MusicManager Instance { get; private set; }

        [Tooltip("The looping music clip for this scene. Leave null to play nothing.")]
        [SerializeField] private AudioClip track;

        [Tooltip("Music volume (0-1).")]
        [Range(0f, 1f)] [SerializeField] private float volume = 0.5f;

        [Tooltip("Keep this manager (and its music) alive across scene loads, so the track doesn't restart " +
                 "on every level reload / transition. Turn OFF for a per-scene, restart-each-load behaviour.")]
        [SerializeField] private bool persistAcrossScenes = true;

        AudioSource source;

        const string PrefKey = "Sort_MusicEnabled";
        static bool? cachedEnabled;

        /// <summary>Whether music plays. Persisted in PlayerPrefs (default ON). Setting it pauses/resumes live.</summary>
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
                if (Instance != null) Instance.ApplyEnabled();
            }
        }

        void Awake()
        {
            // De-dupe: if a manager already exists, the existing one wins when it's the SAME track
            // (keeps music seamless across a scene reload); otherwise let this one take over.
            if (Instance != null && Instance != this)
            {
                if (Instance.track == track) { Destroy(gameObject); return; }
                Destroy(Instance.gameObject);
            }

            Instance = this;
            if (persistAcrossScenes) DontDestroyOnLoad(gameObject);

            source = GetComponent<AudioSource>();
            if (source == null) source = gameObject.AddComponent<AudioSource>();
            source.clip = track;
            source.loop = true;
            source.volume = volume;
            source.playOnAwake = false;

            ApplyEnabled();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void ApplyEnabled()
        {
            if (source == null) return;
            if (Enabled && track != null)
            {
                if (!source.isPlaying) source.Play();
            }
            else
            {
                source.Pause();
            }
        }
    }
}
