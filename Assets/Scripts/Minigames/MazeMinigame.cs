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
public class MazeMinigame : MonoBehaviour
{
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
    [Tooltip("Where the ball is placed (child of Board).")]
    [SerializeField] private Transform startPoint;
    [Tooltip("Centre of the goal hole (child of Board).")]
    [SerializeField] private Transform goalPoint;
    [SerializeField] private Transform cameraAnchor;

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

    [Header("Ball / Goal (in maze units, i.e. Board local space)")]
    [SerializeField] private float goalCaptureRadius = 0.3f;
    [Tooltip("If the ball drops this far below the floor (maze units) it is put back on the start.")]
    [SerializeField] private float fallResetDepth = 1.5f;

    [Header("Top-down Camera")]
    [Tooltip("Half the board width in maze units (7.37 for maze.blend).")]
    [SerializeField] private float boardHalfExtent = 7.4f;
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

        if (board == null && startPoint != null)
            board = startPoint.parent;

        if (tiltBody != null)
            restRotation = tiltBody.rotation;

        // Unity caps a Rigidbody's spin at 7 rad/s by default. For a small ball that
        // limits rolling speed to ~0.4 m/s, so raise it.
        if (ball != null)
            ball.maxAngularVelocity = 100f;
    }

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

        if (dx * dx + dz * dz <= goalCaptureRadius * goalCaptureRadius)
            StartCoroutine(WinRoutine());
    }

    private void CheckFall()
    {
        Vector3 local = board.InverseTransformPoint(ball.position);
        float limit = boardHalfExtent + 1f;

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
        float half = boardHalfExtent * board.lossyScale.x * viewMargin;
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
