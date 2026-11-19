using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A room where the player is safe from enemies, defined by a box (local <see cref="center"/> / <see cref="size"/>,
/// drawn as a green gizmo). While the Player-tagged object is inside:
///   - <see cref="EnemyFollow"/> cannot see or track the player and abandons any chase (it goes off roaming);
///   - <see cref="EnemyProximityStatic"/> fades the radio static out;
///   - this room's ambience loop fades in over <see cref="fadeInTime"/> seconds, and fades back out over
///     <see cref="fadeOutTime"/> seconds after the player leaves. It pauses at silence and resumes where it left off.
/// The box is a plain containment test, not a collider, so it never interferes with physics, raycasts or NavMesh baking.
/// Enemies are also kept out physically: the room's door is solid and the NavMesh is baked with it closed,
/// plus a NavMeshModifierVolume (Not Walkable) over the room removes the NavMesh inside it entirely.
/// </summary>
public class SafeRoom : MonoBehaviour
{
    /// <summary>All active safe rooms.</summary>
    public static readonly List<SafeRoom> All = new List<SafeRoom>();

    [Header("Volume (local space)")]
    public Vector3 center = new Vector3(0f, 1.5f, 0f);
    public Vector3 size = new Vector3(4f, 3f, 4f);

    [Header("Ambience")]
    [Tooltip("Looping music/ambience played (2D) while the player is inside.")]
    public AudioClip ambience;
    [Range(0f, 1f)] public float ambienceVolume = 0.6f;
    [Tooltip("Seconds for the ambience to rise from silence to full volume after entering.")]
    public float fadeInTime = 4f;
    [Tooltip("Seconds for the ambience to die away after leaving.")]
    public float fadeOutTime = 4f;

    Transform player;
    AudioSource source;
    bool playerInside;
    bool started;
    float fade;   // 0..1, linear in time; volume uses fade² so the rise sounds even to the ear

    /// <summary>True while the player is inside this room.</summary>
    public bool PlayerInside => playerInside;

    /// <summary>True while the player is inside any safe room.</summary>
    public static bool PlayerIsSafe
    {
        get
        {
            foreach (var r in All)
                if (r.playerInside)
                    return true;
            return false;
        }
    }

    /// <summary>Whether a world-space point is inside any safe room.</summary>
    public static bool IsSafe(Vector3 worldPoint)
    {
        foreach (var r in All)
            if (r.Contains(worldPoint))
                return true;
        return false;
    }

    public bool Contains(Vector3 worldPoint)
    {
        Vector3 p = transform.InverseTransformPoint(worldPoint) - center;
        Vector3 h = size * 0.5f;
        return Mathf.Abs(p.x) <= h.x && Mathf.Abs(p.y) <= h.y && Mathf.Abs(p.z) <= h.z;
    }

    void OnEnable() => All.Add(this);

    void OnDisable()
    {
        All.Remove(this);
        playerInside = false;
    }

    void Start()
    {
        var p = GameObject.FindGameObjectWithTag("Player");
        if (p != null)
            player = p.transform;

        source = gameObject.AddComponent<AudioSource>();
        source.clip = ambience;
        source.loop = true;
        source.playOnAwake = false;
        source.spatialBlend = 0f;   // 2D: it fills the room rather than coming from a point
        source.volume = 0f;
    }

    void Update()
    {
        playerInside = player != null && Contains(player.position);

        float duration = playerInside ? fadeInTime : fadeOutTime;
        fade = Mathf.MoveTowards(fade, playerInside ? 1f : 0f, Time.deltaTime / Mathf.Max(duration, 0.01f));

        if (source == null || source.clip == null)
            return;

        source.volume = ambienceVolume * fade * fade;
        if (fade > 0f && !source.isPlaying)
        {
            if (started)
                source.UnPause();
            else
            {
                source.Play();
                started = true;
            }
        }
        else if (fade <= 0f && source.isPlaying)
        {
            source.Pause();
        }
    }

    void OnDrawGizmos()
    {
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.08f);
        Gizmos.DrawCube(center, size);
        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.8f);
        Gizmos.DrawWireCube(center, size);
    }
}
