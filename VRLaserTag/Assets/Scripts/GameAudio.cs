using UnityEngine;

// Central place for all game sound effects.
// One instance lives in ArenaScene. Other scripts call the static
// helpers, so they do not need their own AudioSource references.
public class GameAudio : MonoBehaviour
{
    public static GameAudio Instance { get; private set; }

    [Header("Weapon")]
    public AudioClip laserShot;
    public AudioClip hitConfirm;
    public AudioClip reload;
    public AudioClip emptyClick;

    [Header("Player")]
    public AudioClip damageTaken;
    public AudioClip eliminated;
    public AudioClip respawn;
    public AudioClip denied;

    [Header("Match")]
    public AudioClip countdownBeep;
    public AudioClip matchStart;
    public AudioClip matchEnd;

    [Header("Settings")]
    [Range(0f, 1f)] public float volume = 0.8f;
    public float spatialMaxDistance = 25f;

    // Non-spatial source for sounds that belong to the local player
    // (UI beeps, own damage, own hit confirmation).
    private AudioSource source2D;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        source2D = gameObject.AddComponent<AudioSource>();
        source2D.playOnAwake = false;
        source2D.spatialBlend = 0f;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    // Plays a sound for the local listener only (not positioned in the world).
    public static void Play2D(AudioClip clip, float volumeScale = 1f)
    {
        if (Instance == null || clip == null)
            return;

        Instance.source2D.PlayOneShot(clip, Instance.volume * volumeScale);
    }

    // Plays a 3D sound at a world position, e.g. another player's shot.
    public static void PlayAt(AudioClip clip, Vector3 position, float volumeScale = 1f)
    {
        if (Instance == null || clip == null)
            return;

        GameObject temp = new GameObject("OneShotAudio");
        temp.transform.position = position;

        AudioSource source = temp.AddComponent<AudioSource>();
        source.clip = clip;
        source.volume = Instance.volume * volumeScale;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 1f;
        source.maxDistance = Instance.spatialMaxDistance;
        source.Play();

        Destroy(temp, clip.length + 0.1f);
    }
}
