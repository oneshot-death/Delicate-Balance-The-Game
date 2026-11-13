using UnityEngine;

/// <summary>
/// Looping radio static heard by the player, louder the closer the nearest enemy is.
/// Lives on the player's camera (the AudioListener) as a 2D sound. <see cref="EnemyFollow"/> adds it
/// automatically at runtime if the player doesn't already have one, so no scene setup is needed;
/// add it to the Player prefab by hand if you want to tune the values in the Inspector.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class EnemyProximityStatic : MonoBehaviour
{
    [Tooltip("Looping static clip (Assets/Sound Effects/Radio/Radio Static Danger).")]
    public AudioClip clip;

    [Tooltip("At this distance (m) or closer the static is at full volume.")]
    public float fullVolumeDistance = 3f;

    [Tooltip("Beyond this distance (m) the static is silent.")]
    public float silentDistance = 25f;

    [Range(0f, 1f)] public float maxVolume = 0.9f;

    [Tooltip("Shape of the fade: 1 = linear, higher = stays quiet longer and ramps up sharply when close.")]
    public float curvePower = 2f;

    [Tooltip("How quickly the volume follows distance changes (higher = snappier).")]
    public float smoothing = 4f;

    AudioSource source;
    Transform listener;

    /// <summary>Makes sure the player has the static, on its camera if it has one.</summary>
    public static void EnsureOn(Transform player, AudioClip clip)
    {
        if (player == null || player.GetComponentInChildren<EnemyProximityStatic>() != null)
            return;
        var cam = player.GetComponentInChildren<Camera>();
        var host = cam != null ? cam.gameObject : player.gameObject;
        var s = host.AddComponent<EnemyProximityStatic>();
        s.clip = clip;
        s.Begin();
    }

    void Awake() => source = GetComponent<AudioSource>();

    void Start() => Begin();

    void Begin()
    {
        if (source == null)
            source = GetComponent<AudioSource>();
        listener = transform;
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 0f;   // 2D: it's the player's own radio
        if (clip == null)
        {
            Debug.LogWarning("EnemyProximityStatic: no clip assigned.", this);
            return;
        }
        if (source.clip == clip && source.isPlaying)
            return;
        source.clip = clip;
        source.volume = 0f;
        source.Play();
        source.time = Random.Range(0f, clip.length);   // don't always start on the same crackle
    }

    void Update()
    {
        if (source == null || source.clip == null)
            return;

        float nearest = float.MaxValue;
        foreach (var e in EnemyFollow.All)
            nearest = Mathf.Min(nearest, Vector3.Distance(listener.position, e.transform.position));

        float t = Mathf.InverseLerp(silentDistance, fullVolumeDistance, nearest);   // 0 far, 1 close
        float targetVolume = maxVolume * Mathf.Pow(t, curvePower);
        source.volume = Mathf.Lerp(source.volume, targetVolume, 1f - Mathf.Exp(-smoothing * Time.deltaTime));
    }
}
