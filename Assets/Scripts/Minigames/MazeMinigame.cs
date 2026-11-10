using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Tilt-maze minigame that lives inside the normal scene.
/// Look at the maze + press E: the camera goes top-down, WASD / arrows tilt the
/// board (pitch + roll) and a physics ball rolls through the maze to the goal.
/// Esc exits and gives control back to the player.
///
/// Prefab layout this script expects (built for you in MazeBoard.prefab):
///   MazeBoard (this script, static)
///     Pedestal                      (visual only)
///     Tilt   [kinematic Rigidbody]  (the part that tilts, pivot = board centre)
///       Board (scaled)              (model + wall colliders + StartPoint + GoalPoint)
///     Ball   [dynamic Rigidbody]    (NOT under Tilt, so the parent never moves it)
///     CameraAnchor                  (top-down view position, auto-fitted on begin)
///     Canvas (prompt + status text)
/// </summary>
public enum MazeChoice
{
    Classic = 0,
    Hard = 1
}

/// <summary>
/// One playable maze layout. maze.blend holds several mazes side by side in a single model;
/// a variant says which model parts belong to it, where to centre it on the board, and where
/// the ball starts / finishes. Add a new maze by adding a variant and a MazeChoice entry.
/// </summary>
[System.Serializable]
public class MazeVariant
{
    public string name;

    [Tooltip("Activated only while this maze is selected. Holds its wall/floor colliders, " +
             "start and goal points and markers (child of Board).")]
    public GameObject root;

    [Tooltip("Children of the shared maze model (MazeModel) that make up this maze. " +
             "Everything else in the model is hidden.")]
    public GameObject[] modelParts;

    [Tooltip("Local position given to the maze model so this maze sits on the board centre.")]
    public Vector3 modelOffset;

    [Tooltip("Where the ball is placed (maze units, child of Board).")]
    public Transform startPoint;

    [Tooltip("Centre of the goal hole (maze units, child of Board).")]
    public Transform goalPoint;

    [Tooltip("Ball radius in maze units. Corridors are about 1.0 wide in the hard maze, 1.2 in the classic one.")]
    public float ballRadius = 0.27f;

    [Tooltip("Goal is reached when the ball centre is this close to the goal centre (maze units).")]
    public float goalCaptureRadius = 0.3f;

    [Tooltip("Half the board width in maze units. Used to fit the top-down camera.")]
    public float boardHalfExtent = 7.4f;
}

public class MazeMinigame : MonoBehaviour
{
    [Header("Maze Selection")]
    [Tooltip("Which maze this board plays. Changing it in the Inspector swaps the maze right away.")]
    [SerializeField] private MazeChoice selectedMaze = MazeChoice.Classic;

    [Tooltip("Index matches MazeChoice (0 = Classic, 1 = Hard).")]
    [SerializeField] private MazeVariant[] variants;

    [Tooltip("The shared imported model (maze.blend instance, child of Board).")]
    [SerializeField] private Transform modelRoot;

    [Header("Interaction")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private float interactDistance = 6f;
    [SerializeField] private LayerMask interactionMask = ~0;

    [Header("Maze Parts")]
    [Tooltip("Kinematic Rigidbody that tilts. Its pivot should be the board centre.")]
    [SerializeField] private Rigidbody tiltBody;

    [Tooltip("The (scaled) board. Used to convert to maze units for the goal / fall checks.")]
    [SerializeField] private Transform board;

    [SerializeField] private Rigidbody ball;
    [SerializeField] private Transform cameraAnchor;

    // Legacy serialized references from the single-maze version; the active variant's points are used now.
    [SerializeField, HideInInspector] private Transform startPoint;
    [SerializeField, HideInInspector] private Transform goalPoint;

    [Header("Player Controls")]
    [Tooltip("Movement + mouse-look scripts. Left empty, PlayerMovement and FPSCameraLook are found automatically.")]
    [SerializeField] private MonoBehaviour[] playerBehavioursToDisable;

    [Header("UI")]
    [SerializeField] private TMP_Text promptText;
    [SerializeField] private TMP_Text statusText;

    [Header("Tilt")]
    [SerializeField, Range(1f, 20f)] private float maxTiltDegrees = 7f;
    [Tooltip("Seconds the board takes to follow the keys. Lower = snappier.")]
    [SerializeField, Min(0.01f)] private float tiltSmoothTime = 0.15f;

    [Tooltip("Hard cap on how fast the board may rotate (deg/s). A board that snaps too " +
             "quickly acts like a trampoline and flings the ball over the walls.")]
    [SerializeField, Min(1f)] private float maxTiltSpeedDegPerSec = 30f;

    [Header("Ball")]
    [Tooltip("If the ball drops this far below the floor (maze units) it is put back on the start.")]
    [SerializeField] private float fallResetDepth = 1.5f;

    [Header("Top-down Camera")]
    [SerializeField] private float viewMargin = 1.12f;

    private bool isPlaying;
    private bool isWon;
    private bool timerRunning;
    private float elapsed;
    private float bestTime = -1f;

    private Vector2 tiltInput;     // smoothed, -1..1 on each screen axis
    private Vector2 tiltVelocity;
    private Quaternion restRotation;

    private Vector3 savedCameraPosition;
    private Quaternion savedCameraRotation;
    private CursorLockMode savedLock;
    private bool savedCursorVisible;

    private bool[] previousEnabledStates;
    private Renderer[] hiddenRenderers;
    private bool[] hiddenRendererStates;

    private void Awake()
    {
        if (playerCamera == null)
            playerCamera = Camera.main;

        if (tiltBody != null)
            restRotation = tiltBody.rotation;

        // Unity caps a Rigidbody's spin at 7 rad/s by default. For a small ball that
        // limits rolling speed to ~0.4 m/s, so raise it.
        if (ball != null)
            ball.maxAngularVelocity = 100f;

        ApplySelection();
    }

    private MazeVariant Active
    {
        get
        {
            if (variants == null || variants.Length == 0) return null;
            return variants[Mathf.Clamp((int)selectedMaze, 0, variants.Length - 1)];
        }
    }

    /// <summary>
    /// Shows the selected maze (model parts, colliders, markers), hides the others and puts the
    /// ball on its start. Runs on Awake and whenever the choice changes in the Inspector.
    /// </summary>
    [ContextMenu("Apply Maze Selection")]
    public void ApplySelection()
    {
        MazeVariant active = Active;
        if (active == null) return;

        foreach (MazeVariant v in variants)
            if (v.root != null) v.root.SetActive(v == active);

        if (modelRoot != null)
        {
            foreach (Transform child in modelRoot)
                child.gameObject.SetActive(false);

            if (active.modelParts != null)
                foreach (GameObject part in active.modelParts)
                    if (part != null) part.SetActive(true);

            modelRoot.localPosition = active.modelOffset;
        }

        startPoint = active.startPoint;
        goalPoint = active.goalPoint;

        if (ball != null && board != null)
        {
            // Sphere primitive has diameter 1, so scale = 2r in world units.
            ball.transform.localScale = Vector3.one * (active.ballRadius * 2f * board.lossyScale.x);
            if (startPoint != null && !isPlaying)
                ResetBall(keepKinematic: true);
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Preview the chosen maze in the editor. Deferred because SetActive isn't allowed inside OnValidate.
        if (Application.isPlaying) return;
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this == null || !gameObject.scene.IsValid()) return;
            ApplySelection();
            UnityEditor.EditorUtility.SetDirty(this);
        };
    }
#endif

    private void Start()
    {
        if (promptText != null) promptText.gameObject.SetActive(false);
        if (statusText != null) statusText.gameObject.SetActive(false);

        ResetBall(keepKinematic: true);
    }

    private void Update()
    {
        if (!isPlaying)
        {
            bool looking = IsLookingAtMaze();

            if (promptText != null)
            {
                promptText.gameObject.SetActive(looking);
                if (looking) promptText.text = "Press E to play the maze";
            }

            if (looking && Keyboard.current != null &&
                Keyboard.current.eKey.wasPressedThisFrame)
            {
                BeginMinigame();
            }
            return;
        }

        UpdateMinigame();
    }

    private void FixedUpdate()
    {
        if (!isPlaying || tiltBody == null)
            return;

        // Tilt so the ball is pulled toward the direction the player presses.
        // d is that direction in world space (screen-right / screen-up of the top-down view).
        Vector3 d = transform.right * tiltInput.x + transform.forward * tiltInput.y;
        if (d.sqrMagnitude > 1f) d.Normalize();

        // A ball rolls toward the horizontal lean of the board's normal, so tilt the
        // normal toward d: rotate about (up x d).
        Vector3 axis = Vector3.Cross(transform.up, d);
        float angle = d.magnitude * maxTiltDegrees;

        Quaternion target = angle > 0.001f
            ? Quaternion.AngleAxis(angle, axis.normalized) * restRotation
            : restRotation;

        tiltBody.MoveRotation(target);
    }

    // ---------------------------------------------------------------- interaction

    private bool IsLookingAtMaze()
    {
        if (playerCamera == null) return false;

        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance,
                            interactionMask, QueryTriggerInteraction.Ignore))
        {
            // Any collider that belongs to this maze counts (walls, floor, ball).
            return hit.collider.GetComponentInParent<MazeMinigame>() == this;
        }
        return false;
    }

    private void BeginMinigame()
    {
        if (playerCamera == null || tiltBody == null || ball == null ||
            startPoint == null || goalPoint == null || cameraAnchor == null)
        {
            Debug.LogError("MazeMinigame: assign camera, tilt body, ball, start, goal and camera anchor.");
            return;
        }

        savedCameraPosition = playerCamera.transform.position;
        savedCameraRotation = playerCamera.transform.rotation;
        savedLock = Cursor.lockState;
        savedCursorVisible = Cursor.visible;

        DisablePlayerBehaviours();
        HidePlayerRenderers();

        isPlaying = true;
        isWon = false;
        timerRunning = false;
        elapsed = 0f;
        tiltInput = Vector2.zero;
        tiltVelocity = Vector2.zero;

        tiltBody.rotation = restRotation;
        ResetBall(keepKinematic: false);

        PlaceCamera();

        if (promptText != null) promptText.gameObject.SetActive(false);
        if (statusText != null) statusText.gameObject.SetActive(true);
        RefreshStatus();
    }

    private void UpdateMinigame()
    {
        Keyboard kb = Keyboard.current;

        if (kb != null && kb.escapeKey.wasPressedThisFrame)
        {
            EndMinigame();
            return;
        }

        if (kb != null && kb.rKey.wasPressedThisFrame)
        {
            ResetRun();
        }

        // Input is read in screen space of the top-down camera: W = ball goes up the screen.
        Vector2 raw = Vector2.zero;
        if (!isWon && kb != null)
        {
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed)    raw.y += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed)  raw.y -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) raw.x += 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)  raw.x -= 1f;
        }

        if (raw != Vector2.zero) timerRunning = true;

        tiltInput = Vector2.SmoothDamp(
            tiltInput, raw, ref tiltVelocity, tiltSmoothTime,
            maxTiltSpeedDegPerSec / maxTiltDegrees);

        if (timerRunning && !isWon)
            elapsed += Time.deltaTime;

        if (!isWon)
        {
            CheckGoal();
            CheckFall();
        }

        RefreshStatus();
    }

    // ---------------------------------------------------------------- gameplay

    private void CheckGoal()
    {
        Vector3 local = board.InverseTransformPoint(ball.position);
        Vector3 goal = board.InverseTransformPoint(goalPoint.position);

        float dx = local.x - goal.x;
        float dz = local.z - goal.z;

        float capture = Active.goalCaptureRadius;
        if (dx * dx + dz * dz <= capture * capture)
            StartCoroutine(WinRoutine());
    }

    private void CheckFall()
    {
        Vector3 local = board.InverseTransformPoint(ball.position);
        float limit = Active.boardHalfExtent + 1f;

        if (local.y < -fallResetDepth ||
            Mathf.Abs(local.x) > limit || Mathf.Abs(local.z) > limit)
        {
            ResetBall(keepKinematic: false);
        }
    }

    private IEnumerator WinRoutine()
    {
        isWon = true;
        timerRunning = false;

        if (bestTime < 0f || elapsed < bestTime) bestTime = elapsed;

        // Let the ball drop into the hole: freeze physics, glide it to the goal centre.
        ball.linearVelocity = Vector3.zero;
        ball.angularVelocity = Vector3.zero;
        ball.isKinematic = true;

        Vector3 from = ball.position;
        Vector3 to = goalPoint.position - tiltBody.transform.up * (ball.transform.lossyScale.x * 0.35f);

        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.35f)
        {
            ball.transform.position = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }
        ball.transform.position = to;
    }

    private void ResetRun()
    {
        isWon = false;
        timerRunning = false;
        elapsed = 0f;
        StopAllCoroutines();
        ResetBall(keepKinematic: false);
    }

    private void ResetBall(bool keepKinematic)
    {
        if (ball == null || startPoint == null) return;

        if (!ball.isKinematic)
        {
            ball.linearVelocity = Vector3.zero;
            ball.angularVelocity = Vector3.zero;
        }

        ball.isKinematic = true;
        ball.position = startPoint.position;
        ball.rotation = Quaternion.identity;
        ball.transform.SetPositionAndRotation(startPoint.position, Quaternion.identity);

        ball.isKinematic = keepKinematic;
        if (!keepKinematic) ball.WakeUp();
    }

    private void PlaceCamera()
    {
        // Fit the whole board in view whatever the aspect ratio / FOV.
        float half = Active.boardHalfExtent * board.lossyScale.x * viewMargin;
        float tanHalfV = Mathf.Tan(playerCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float distance = Mathf.Max(half / tanHalfV, half / (tanHalfV * playerCamera.aspect));

        // Straight down over the board centre; screen-up is the maze's local +Z.
        Vector3 centre = tiltBody.position;
        Vector3 pos = centre + transform.up * distance;
        Quaternion rot = Quaternion.LookRotation(-transform.up, transform.forward);

        cameraAnchor.SetPositionAndRotation(pos, rot);
        playerCamera.transform.SetPositionAndRotation(pos, rot);
    }

    private void RefreshStatus()
    {
        if (statusText == null) return;

        string time = $"{elapsed:0.0}s";
        string best = bestTime >= 0f ? $"   BEST: {bestTime:0.0}s" : "";

        statusText.text = isWon
            ? $"MAZE COMPLETE!  Time: {time}{best}\nR: play again   Esc: exit"
            : $"TIME: {time}{best}\nWASD / Arrows: tilt   R: reset ball   Esc: exit";
    }

    // ---------------------------------------------------------------- exit

    private void EndMinigame()
    {
        if (!isPlaying) return;

        isPlaying = false;
        isWon = false;
        StopAllCoroutines();

        tiltInput = Vector2.zero;
        tiltVelocity = Vector2.zero;
        tiltBody.rotation = restRotation;
        ResetBall(keepKinematic: true);

        playerCamera.transform.SetPositionAndRotation(savedCameraPosition, savedCameraRotation);

        if (statusText != null) statusText.gameObject.SetActive(false);

        // The player scripts are re-enabled one frame later so the Esc press that ended
        // the minigame isn't also seen by FPSCameraLook (which would unlock the cursor).
        StartCoroutine(RestorePlayerNextFrame());
    }

    private IEnumerator RestorePlayerNextFrame()
    {
        yield return null;

        if (playerBehavioursToDisable != null && previousEnabledStates != null)
        {
            for (int i = 0; i < playerBehavioursToDisable.Length; i++)
            {
                if (playerBehavioursToDisable[i] != null)
                    playerBehavioursToDisable[i].enabled = previousEnabledStates[i];
            }
        }

        if (hiddenRenderers != null)
        {
            for (int i = 0; i < hiddenRenderers.Length; i++)
                if (hiddenRenderers[i] != null)
                    hiddenRenderers[i].enabled = hiddenRendererStates[i];
        }

        Cursor.lockState = savedLock;
        Cursor.visible = savedCursorVisible;
    }

    // ---------------------------------------------------------------- player helpers

    private void DisablePlayerBehaviours()
    {
        if (playerBehavioursToDisable == null || playerBehavioursToDisable.Length == 0)
        {
            // Drop-in convenience: find the player's scripts automatically.
            var movement = FindAnyObjectByType<PlayerMovement>();
            var look = FindAnyObjectByType<FPSCameraLook>();
            playerBehavioursToDisable = new MonoBehaviour[] { movement, look };
        }

        previousEnabledStates = new bool[playerBehavioursToDisable.Length];

        for (int i = 0; i < playerBehavioursToDisable.Length; i++)
        {
            MonoBehaviour c = playerBehavioursToDisable[i];
            if (c == null) continue;
            previousEnabledStates[i] = c.enabled;
            c.enabled = false;
        }
    }

    // The player capsule would otherwise show up in the top-down view.
    private void HidePlayerRenderers()
    {
        var cc = playerCamera.GetComponentInParent<CharacterController>();
        Transform root = cc != null ? cc.transform : playerCamera.transform.root;

        hiddenRenderers = root.GetComponentsInChildren<Renderer>();
        hiddenRendererStates = new bool[hiddenRenderers.Length];

        for (int i = 0; i < hiddenRenderers.Length; i++)
        {
            hiddenRendererStates[i] = hiddenRenderers[i].enabled;
            hiddenRenderers[i].enabled = false;
        }
    }
}
