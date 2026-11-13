using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Predator AI: chases the player while it can see them, searches when it loses them.
///
/// Chase:  the player is within <see cref="sightRange"/> and (optionally) in line of sight.
///         Runs along the NavMesh shortest path at <see cref="speed"/> (slower than the player,
///         so the player can outrun it) with the Run animation.
/// Search: entered when the player gets beyond <see cref="sightRange"/> or stays hidden behind
///         walls for longer than <see cref="loseSightGrace"/>. Walks (Search animation, which scans
///         the head left/right) in three phases:
///           1. Investigate: walk to where the player was last seen, pushed a little along the
///              direction they were running (a guess at where they went).
///           2. Sweep: visit random points around that spot within <see cref="searchRadius"/>.
///           3. Roam: after <see cref="searchDuration"/> seconds, give up on that spot and wander
///              more widely around wherever it currently is.
///         Seeing the player again at any point switches straight back to Chase.
///
/// If there is no NavMesh under the enemy (e.g. SampleScene) it falls back to moving straight at the target.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class EnemyFollow : MonoBehaviour
{
    public enum State { Chase, Search }

    /// <summary>All active enemies (used by the player's proximity static to find the nearest one).</summary>
    public static readonly List<EnemyFollow> All = new List<EnemyFollow>();

    [Tooltip("What to chase. If empty, the object tagged \"Player\" is used.")]
    public Transform target;

    [Header("Speeds")]
    [Tooltip("Chase (run) speed in m/s. Keep below PlayerMovement.speed (5) so the player can outrun it.")]
    public float speed = 4.2f;

    [Tooltip("Search (walk) speed in m/s.")]
    public float searchSpeed = 1.3f;

    [Header("Senses")]
    [Tooltip("MAXIMUM SIGHT / CHASE DISTANCE in metres. Within this the enemy can spot and chase the player; " +
             "once the player is farther than this, it loses them and starts searching.")]
    public float sightRange = 18f;

    [Tooltip("If on, walls block sight: the enemy only spots the player with a clear line of sight, " +
             "and loses them after they stay hidden for Lose Sight Grace seconds.")]
    public bool requireLineOfSight = true;

    [Tooltip("Seconds the player may stay hidden behind cover (while still in range) before the enemy gives up the chase.")]
    public float loseSightGrace = 2f;

    [Tooltip("Height of the enemy's eyes above its feet, for line-of-sight checks.")]
    public float eyeHeight = 1.65f;

    [Header("Search")]
    [Tooltip("Radius around the last known position that is swept while searching.")]
    public float searchRadius = 8f;

    [Tooltip("Seconds spent sweeping around the last known position before roaming more widely.")]
    public float searchDuration = 20f;

    [Tooltip("How far ahead (in seconds of the player's last velocity) to guess where they went.")]
    public float predictAhead = 1.5f;

    [SerializeField] private State state = State.Chase;

    [Header("Animation")]
    [Tooltip("Animator on the model child. Found automatically if empty.")]
    public Animator animator;

    [Tooltip("Movement speed at which the run clip plays at normal speed (feet match the ground).")]
    public float runClipSpeed = 4.5f;

    [Tooltip("Movement speed at which the search clip plays at normal speed.")]
    public float searchClipSpeed = 1.3f;

    [Header("Audio")]
    [Tooltip("Looping radio static played to the player, louder the closer the nearest enemy is. " +
             "Added to the player's camera automatically at runtime.")]
    public AudioClip proximityStatic;

    static readonly int IsRunningHash = Animator.StringToHash("IsRunning");
    static readonly int AnimSpeedHash = Animator.StringToHash("AnimSpeed");

    NavMeshAgent agent;
    float repathTimer;
    float hiddenTimer;
    float searchTimer;
    Vector3 lastKnownPosition;
    Vector3 lastKnownVelocity;
    Vector3 lastSeenVelocity;
    Vector3 previousTargetPosition;
    bool investigating;

    public State CurrentState => state;
    public Vector3 LastKnownPosition => lastKnownPosition;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
    }

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    void Start()
    {
        if (target == null)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
                target = player.transform;
        }

        // Snap onto the NavMesh if we were placed slightly off it.
        if (!agent.isOnNavMesh && NavMesh.SamplePosition(transform.position, out var hit, 2f, NavMesh.AllAreas))
            agent.Warp(hit.position);

        lastKnownPosition = transform.position;
        if (target != null)
        {
            previousTargetPosition = target.position;
            EnemyProximityStatic.EnsureOn(target, proximityStatic);
        }

        // Start by searching unless the player is already in view.
        SetState(CanSeeTarget() ? State.Chase : State.Search);
    }

    public void SetState(State newState)
    {
        state = newState;
        agent.speed = state == State.Chase ? speed : searchSpeed;
        repathTimer = 0f;
        hiddenTimer = 0f;
        searchTimer = 0f;
        investigating = state == State.Search;
        if (agent.isOnNavMesh)
            agent.ResetPath();
    }

    void Update()
    {
        TrackTargetVelocity();

        if (agent.isOnNavMesh)
        {
            if (state == State.Chase)
                UpdateChase();
            else
                UpdateSearch();
        }
        else if (state == State.Chase && target != null)
        {
            // No NavMesh: straight-line fallback so the prefab still works in test scenes.
            Vector3 to = target.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to), 10f * Time.deltaTime);
            transform.position = Vector3.MoveTowards(transform.position,
                new Vector3(target.position.x, transform.position.y, target.position.z), speed * Time.deltaTime);
        }

        UpdateAnimator();
    }

    // ------------------------------------------------------------------ senses

    void TrackTargetVelocity()
    {
        if (target == null || Time.deltaTime <= 0f)
            return;
        Vector3 v = (target.position - previousTargetPosition) / Time.deltaTime;
        v.y = 0f;
        v = Vector3.ClampMagnitude(v, 10f);   // ignore teleports / respawns
        lastKnownVelocity = Vector3.Lerp(lastKnownVelocity, v, 10f * Time.deltaTime);
        previousTargetPosition = target.position;
    }

    float DistanceToTarget()
    {
        Vector3 d = target.position - transform.position;
        d.y = 0f;
        return d.magnitude;
    }

    bool InRange() => target != null && DistanceToTarget() <= sightRange;

    bool HasLineOfSight()
    {
        if (!requireLineOfSight)
            return true;
        Vector3 eye = transform.position + Vector3.up * eyeHeight;
        Vector3 aim = target.position + Vector3.up * 0.5f;   // player pivot is the capsule centre
        // Linecast ignores colliders that contain the start point, so the enemy's own capsule doesn't block.
        if (Physics.Linecast(eye, aim, out var hit, ~0, QueryTriggerInteraction.Ignore))
            return hit.transform == target || hit.transform.IsChildOf(target);
        return true;
    }

    bool CanSeeTarget() => InRange() && HasLineOfSight();

    // ------------------------------------------------------------------ chase

    void UpdateChase()
    {
        if (target == null)
            return;

        if (!InRange())
        {
            LoseTarget();
            return;
        }

        if (HasLineOfSight())
        {
            hiddenTimer = 0f;
            lastKnownPosition = target.position;
        }
        else
        {
            // Still in range but behind cover: keep heading for the last known position for a moment.
            hiddenTimer += Time.deltaTime;
            if (hiddenTimer > loseSightGrace)
            {
                LoseTarget();
                return;
            }
        }

        // Re-path a few times a second rather than every frame. While the player is in range the
        // enemy keeps tracking their real position (it "heard" them), which feels fair for a short grace.
        repathTimer -= Time.deltaTime;
        if (repathTimer <= 0f)
        {
            repathTimer = 0.2f;
            agent.SetDestination(target.position);
        }
    }

    void LoseTarget()
    {
        // lastKnownPosition was updated every frame the player was actually visible, so the search
        // starts from the last place it saw them (not wherever they are now), heading the way they
        // were last seen moving.
        lastSeenVelocity = lastKnownVelocity;
        SetState(State.Search);
    }

    // ------------------------------------------------------------------ search

    void UpdateSearch()
    {
        if (CanSeeTarget())
        {
            SetState(State.Chase);
            return;
        }

        searchTimer += Time.deltaTime;

        if (agent.pathPending)
            return;

        bool arrived = !agent.hasPath || agent.remainingDistance <= agent.stoppingDistance + 0.3f;
        if (!arrived)
            return;

        Vector3 next;
        if (investigating)
        {
            // Phase 1: head for where the player was last seen, nudged along their last heading.
            investigating = false;
            next = lastKnownPosition + lastSeenVelocity * predictAhead;
        }
        else if (searchTimer < searchDuration)
        {
            // Phase 2: sweep around the last known position.
            next = lastKnownPosition + RandomFlat(searchRadius);
        }
        else
        {
            // Phase 3: lost the trail, roam more widely from wherever we are.
            next = transform.position + RandomFlat(searchRadius * 2f);
        }

        if (NavMesh.SamplePosition(next, out var hit, searchRadius, NavMesh.AllAreas))
            agent.SetDestination(hit.position);
    }

    static Vector3 RandomFlat(float radius)
    {
        Vector2 r = Random.insideUnitCircle * radius;
        return new Vector3(r.x, 0f, r.y);
    }

    // ------------------------------------------------------------------ animation

    void UpdateAnimator()
    {
        if (animator == null)
            return;

        bool running = state == State.Chase;
        animator.SetBool(IsRunningHash, running);

        // Scale clip playback with real movement speed so the feet don't slide;
        // keep a floor so the enemy never freezes mid-stride when blocked.
        float moveSpeed = agent.isOnNavMesh ? agent.velocity.magnitude : (running ? speed : 0f);
        float reference = running ? runClipSpeed : searchClipSpeed;
        animator.SetFloat(AnimSpeedHash, Mathf.Clamp(moveSpeed / Mathf.Max(reference, 0.01f), 0.6f, 1.5f));
    }

    void OnDrawGizmosSelected()
    {
        // Sight range (yellow) and, while searching, the area being swept (cyan).
        Gizmos.color = Color.yellow;
        DrawCircle(transform.position, sightRange);
        if (Application.isPlaying && state == State.Search)
        {
            Gizmos.color = Color.cyan;
            DrawCircle(lastKnownPosition, searchRadius);
            Gizmos.DrawSphere(lastKnownPosition + Vector3.up * 0.1f, 0.25f);
        }
    }

    static void DrawCircle(Vector3 c, float r)
    {
        Vector3 prev = c + new Vector3(r, 0.05f, 0f);
        for (int i = 1; i <= 48; i++)
        {
            float a = i / 48f * Mathf.PI * 2f;
            Vector3 p = c + new Vector3(Mathf.Cos(a) * r, 0.05f, Mathf.Sin(a) * r);
            Gizmos.DrawLine(prev, p);
            prev = p;
        }
    }
}
