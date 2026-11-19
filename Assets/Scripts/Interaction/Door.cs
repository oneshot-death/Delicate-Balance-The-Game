using System.Collections;
using UnityEngine;

/// <summary>
/// A door the player can use (look at it, press E; see <see cref="PlayerInteractor"/>).
///
/// Locked (or no room set up): plays <see cref="lockedClip"/> (a handle rattle) and nothing else.
/// Unlocked with a <see cref="room"/>: plays <see cref="unlockedClip"/>, fades to black, moves the player to
/// <see cref="roomSide"/> (or back to <see cref="hallSide"/> if they are already inside the room), then fades back in.
/// The door itself never opens; it is a solid collider, so the only way through is this transition.
///
/// The sound comes from a 3D AudioSource at the door's centre, created at runtime.
/// </summary>
public class Door : MonoBehaviour, IInteractable
{
    [Tooltip("Locked doors only rattle. Unlock it and set Room / Room Side / Hall Side to make it a passage.")]
    public bool locked = true;

    [Header("Sounds")]
    public AudioClip lockedClip;
    public AudioClip unlockedClip;
    [Range(0f, 1f)] public float volume = 1f;

    [Header("Passage (unlocked doors)")]
    [Tooltip("The safe room behind this door.")]
    public SafeRoom room;
    [Tooltip("Where the player's feet land inside the room; its forward is the direction they face.")]
    public Transform roomSide;
    [Tooltip("Where the player's feet land in the hallway; its forward is the direction they face.")]
    public Transform hallSide;

    [Header("Transition (seconds)")]
    public float fadeOutTime = 0.6f;
    public float blackHoldTime = 0.4f;
    public float fadeInTime = 0.8f;

    static bool transitioning;

    /// <summary>True while any door is mid fade/teleport.</summary>
    public static bool IsTransitioning => transitioning;

    AudioSource source;

    bool IsPassage => !locked && room != null && roomSide != null && hallSide != null;

    void Start()
    {
        PlayerInteractor.EnsureOnPlayer();
    }

    public void Interact(PlayerInteractor interactor)
    {
        if (transitioning)
            return;

        if (!IsPassage)
        {
            // Don't restart the rattle on every press; let it finish first.
            if (!Source.isPlaying)
                Play(lockedClip);
            return;
        }

        StartCoroutine(PassThrough(interactor));
    }

    IEnumerator PassThrough(PlayerInteractor interactor)
    {
        transitioning = true;
        interactor.SuspendControls();
        Play(unlockedClip);

        bool entering = !room.Contains(interactor.Player.position);

        yield return ScreenFader.Fade(1f, fadeOutTime);
        interactor.TeleportFeetTo(entering ? roomSide : hallSide);
        yield return new WaitForSecondsRealtime(blackHoldTime);
        yield return ScreenFader.Fade(0f, fadeInTime);

        interactor.ResumeControls();
        transitioning = false;
    }

    AudioSource Source
    {
        get
        {
            if (source == null)
            {
                // Put the sound at the middle of the door (the pivot is at floor level).
                var bounds = new Bounds(transform.position + Vector3.up, Vector3.zero);
                bool any = false;
                foreach (var r in GetComponentsInChildren<Renderer>())
                {
                    if (!any) { bounds = r.bounds; any = true; }
                    else bounds.Encapsulate(r.bounds);
                }
                var go = new GameObject("DoorAudio");
                go.transform.SetParent(transform, false);
                go.transform.position = bounds.center;
                source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 1f;
                source.rolloffMode = AudioRolloffMode.Logarithmic;
                source.minDistance = 1.5f;
                source.maxDistance = 20f;
                source.dopplerLevel = 0f;
            }
            return source;
        }
    }

    void Play(AudioClip clip)
    {
        if (clip == null)
            return;
        Source.clip = clip;
        Source.volume = volume;
        Source.Play();
    }

    void OnDrawGizmosSelected()
    {
        if (roomSide != null) DrawSpot(roomSide, Color.green);
        if (hallSide != null) DrawSpot(hallSide, Color.yellow);
    }

    static void DrawSpot(Transform t, Color c)
    {
        Gizmos.color = c;
        Gizmos.DrawWireSphere(t.position + Vector3.up * 0.05f, 0.3f);
        Gizmos.DrawLine(t.position + Vector3.up * 0.05f, t.position + Vector3.up * 0.05f + t.forward * 0.6f);
    }
}
