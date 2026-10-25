
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;


public class DartboardMinigame : MonoBehaviour
{
    [Header("Interaction")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private Collider dartboardCollider;
    [SerializeField] private float interactDistance = 3f;
    [SerializeField] private LayerMask interactionMask = ~0;

    [Header("Dartboard Geometry")]
    [Tooltip("Origin at the bullseye. +Z points out toward the player, +Y up.")]
    [SerializeField] private Transform boardFace;

    [Tooltip("The fixed camera position and rotation for playing darts.")]
    [SerializeField] private Transform cameraAnchor;

    [Header("Player Controls")]
    [Tooltip("Add movement and mouse-look scripts here. Do NOT add this script.")]
    [SerializeField] private MonoBehaviour[] playerBehavioursToDisable;

    [Header("UI")]
    [SerializeField] private TMP_Text scoreText;
    [SerializeField] private TMP_Text statusText;

    [Header("Horizontal Sway")]
    [Tooltip("Maximum camera translation from the anchor, in metres.")]
    [SerializeField] private float swayAmplitudeMeters = 0.12f;

    [Tooltip("Number of complete sway cycles per second.")]
    [SerializeField] private float swayCyclesPerSecond = 0.55f;

    [SerializeField] private float swayPhaseRadians = 0f;

    [Header("Hidden Throw Bias")]
    [Tooltip("Positive values shift the dart's impact to the right.")]
    [SerializeField] private float shotOffsetMeters = 0.035f;

    [Header("Dartboard Scoring")]
    [Tooltip("Physical radius of the playable dartboard, in metres.")]
    [SerializeField] private float boardRadiusMeters = 0.225f;

    [SerializeField, Range(0f, 1f)]
    private float innerBullRadiusRatio = 0.037f;

    [SerializeField, Range(0f, 1f)]
    private float outerBullRadiusRatio = 0.094f;

    [SerializeField, Range(0f, 1f)]
    private float tripleRingInnerRatio = 0.585f;

    [SerializeField, Range(0f, 1f)]
    private float tripleRingOuterRatio = 0.632f;

    [SerializeField, Range(0f, 1f)]
    private float doubleRingInnerRatio = 0.95f;

    [Header("Optional Dart Visual")]
    [Tooltip("Optional prefab. Its origin should be at its tip, with +Z pointing toward the tip.")]
    [SerializeField] private GameObject dartPrefab;

    [SerializeField] private float dartSurfaceOffset = 0.002f;
    [SerializeField] private float maxShotDistance = 20f;

    private bool isPlaying;
    private bool isHoldingThrow;
    private float holdStartTime;

    private Vector3 savedCameraPosition;
    private Quaternion savedCameraRotation;

    private bool[] previousEnabledStates;

    private int totalScore;
    private int dartsThrown;
    private string lastShotSummary = "—";

    private readonly List<GameObject> spawnedDarts =
        new List<GameObject>();

    // Standard clockwise order, starting at the top of a dartboard.
    private static readonly int[] SectorNumbers =
    {
        20, 1, 18, 4, 13, 6, 10, 15, 2, 17,
        3, 19, 7, 16, 8, 11, 14, 9, 12, 5
    };

    private void Awake()
    {
        if (playerCamera == null)
            playerCamera = Camera.main;

        if (dartboardCollider == null)
            dartboardCollider = GetComponent<Collider>();

        if (boardFace == null)
            boardFace = transform;
    }

    private void Start()
    {
        if (scoreText != null)
            scoreText.gameObject.SetActive(false);

        if (statusText != null)
            statusText.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (!isPlaying)
        {
            bool lookingAtBoard = IsLookingAtBoard();

            if (statusText != null)
            {
                statusText.gameObject.SetActive(lookingAtBoard);

                if (lookingAtBoard)
                    statusText.text = "Press E to play darts";
            }

            if (lookingAtBoard &&
                Keyboard.current != null &&
                Keyboard.current.eKey.wasPressedThisFrame)
            {
                BeginMinigame();
            }

            return;
        }

        UpdateMinigame();
    }

    private bool IsLookingAtBoard()
    {
        if (playerCamera == null || dartboardCollider == null)
            return false;

        Ray ray = playerCamera.ViewportPointToRay(
            new Vector3(0.5f, 0.5f, 0f)
        );

        // The first object hit must be the dartboard.
        if (Physics.Raycast(
            ray,
            out RaycastHit hit,
            interactDistance,
            interactionMask,
            QueryTriggerInteraction.Ignore))
        {
            return hit.collider == dartboardCollider;
        }

        return false;
    }

    private void BeginMinigame()
    {
        if (playerCamera == null ||
            dartboardCollider == null ||
            boardFace == null ||
            cameraAnchor == null)
        {
            Debug.LogError(
                "DartboardMinigame: Assign the camera, collider, " +
                "BoardFace, and Camera Anchor in the Inspector."
            );
            return;
        }

        savedCameraPosition = playerCamera.transform.position;
        savedCameraRotation = playerCamera.transform.rotation;

        // Remember previous enabled states before disabling controls.
        int count = playerBehavioursToDisable == null
            ? 0
            : playerBehavioursToDisable.Length;

        previousEnabledStates = new bool[count];

        for (int i = 0; i < count; i++)
        {
            MonoBehaviour component = playerBehavioursToDisable[i];

            if (component == null)
                continue;

            previousEnabledStates[i] = component.enabled;
            component.enabled = false;
        }

        // Remove visual darts from the previous session.
        foreach (GameObject dart in spawnedDarts)
        {
            if (dart != null)
                Destroy(dart);
        }

        spawnedDarts.Clear();

        totalScore = 0;
        dartsThrown = 0;
        lastShotSummary = "—";
        isHoldingThrow = false;
        isPlaying = true;

        if (scoreText != null)
        {
            scoreText.gameObject.SetActive(true);
            RefreshScoreUI();
        }

        if (statusText != null)
        {
            statusText.gameObject.SetActive(true);
            statusText.text =
                "Hold LMB, then release to throw.\n" +
                "Press Esc to exit.";
        }

        Debug.Log(
        $"Dart anchor world position: {cameraAnchor.position}\n" +
        $"Dart anchor world rotation: {cameraAnchor.eulerAngles}\n" +
        $"Camera: {playerCamera.name}"
        );

        playerCamera.transform.SetPositionAndRotation(
            cameraAnchor.position,
            cameraAnchor.rotation
        );
    }

    private void UpdateMinigame()
    {
        if (Keyboard.current != null &&
            Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            EndMinigame();
            return;
        }

        Mouse mouse = Mouse.current;

        bool pressed = mouse != null &&
                       mouse.leftButton.wasPressedThisFrame;

        bool released = mouse != null &&
                        mouse.leftButton.wasReleasedThisFrame;

        if (pressed && !isHoldingThrow)
        {
            isHoldingThrow = true;
            holdStartTime = Time.time;
        }

        if (isHoldingThrow)
        {
            float phase =
                (Time.time - holdStartTime) *
                swayCyclesPerSecond * 2f * Mathf.PI +
                swayPhaseRadians;

            // Horizontal movement: camera rotation remains fixed.
            float swayOffset =
                Mathf.Sin(phase) * swayAmplitudeMeters;

            Vector3 cameraPosition =
                cameraAnchor.position +
                cameraAnchor.right * swayOffset;

            playerCamera.transform.SetPositionAndRotation(
                cameraPosition,
                cameraAnchor.rotation
            );
        }
        else
        {
            playerCamera.transform.SetPositionAndRotation(
                cameraAnchor.position,
                cameraAnchor.rotation
            );
        }

        // The throw uses the camera position on the release frame.
        if (released && isHoldingThrow)
        {
            ThrowDart();

            isHoldingThrow = false;

            playerCamera.transform.SetPositionAndRotation(
                cameraAnchor.position,
                cameraAnchor.rotation
            );
        }
    }

    private void ThrowDart()
    {
        Ray ray = playerCamera.ViewportPointToRay(
            new Vector3(0.5f, 0.5f, 0f)
        );

        int points = 0;
        string shotLabel = "Miss";
        bool onBoard = false;

        if (dartboardCollider.Raycast(
            ray,
            out RaycastHit hit,
            maxShotDistance))
        {
            // Introduce a consistent horizontal bias to the impact.
            // The player must compensate for this through timing.
            Vector3 impactPoint =
                hit.point + boardFace.right * shotOffsetMeters;

            points = CalculateScore(
                impactPoint,
                out shotLabel,
                out onBoard
            );

            if (onBoard && dartPrefab != null)
            {
                Vector3 spawnPosition =
                    impactPoint +
                    boardFace.forward * dartSurfaceOffset;

                Quaternion dartRotation = Quaternion.LookRotation(
                    -boardFace.forward,
                    boardFace.up
                );

                GameObject dart = Instantiate(
                    dartPrefab,
                    spawnPosition,
                    dartRotation
                );

                spawnedDarts.Add(dart);
            }
        }

        totalScore += points;
        dartsThrown++;

        lastShotSummary = $"{shotLabel} (+{points})";

        RefreshScoreUI();

        if (statusText != null)
        {
            statusText.text =
                $"{shotLabel}: +{points} points\n" +
                "Hold LMB and release to throw. Esc to exit.";
        }
    }

    private int CalculateScore(
        Vector3 impactPoint,
        out string label,
        out bool onBoard)
    {
        Vector3 delta = impactPoint - boardFace.position;

        float x = Vector3.Dot(delta, boardFace.right);
        float y = Vector3.Dot(delta, boardFace.up);

        float radius = Mathf.Sqrt(x * x + y * y);

        onBoard = radius <= boardRadiusMeters;

        if (!onBoard)
        {
            label = "Miss";
            return 0;
        }

        if (radius <= boardRadiusMeters * innerBullRadiusRatio)
        {
            label = "Bullseye";
            return 50;
        }

        if (radius <= boardRadiusMeters * outerBullRadiusRatio)
        {
            label = "Outer bull";
            return 25;
        }

        // Determine which numbered segment was hit.
        // The angle is measured clockwise from the top.
        float angle = Mathf.Atan2(x, y) * Mathf.Rad2Deg;

        if (angle < 0f)
            angle += 360f;

        int sectorIndex =
            Mathf.FloorToInt((angle + 9f) / 18f) % 20;

        int segment = SectorNumbers[sectorIndex];

        if (radius >= boardRadiusMeters * doubleRingInnerRatio)
        {
            label = $"Double {segment}";
            return segment * 2;
        }

        if (radius >= boardRadiusMeters * tripleRingInnerRatio &&
            radius <= boardRadiusMeters * tripleRingOuterRatio)
        {
            label = $"Triple {segment}";
            return segment * 3;
        }

        label = $"Single {segment}";
        return segment;
    }

    private void RefreshScoreUI()
    {
        if (scoreText == null)
            return;

        scoreText.text =
            $"SCORE: {totalScore}\n" +
            $"THROWS: {dartsThrown}   LAST: {lastShotSummary}";
    }

    private void EndMinigame()
    {
        if (!isPlaying)
            return;

        isPlaying = false;
        isHoldingThrow = false;

        playerCamera.transform.SetPositionAndRotation(
            savedCameraPosition,
            savedCameraRotation
        );

        // Restore exactly the previously enabled/disabled states.
        if (playerBehavioursToDisable != null &&
            previousEnabledStates != null)
        {
            for (int i = 0;
                 i < playerBehavioursToDisable.Length;
                 i++)
            {
                MonoBehaviour component =
                    playerBehavioursToDisable[i];

                if (component != null)
                    component.enabled = previousEnabledStates[i];
            }
        }

        if (scoreText != null)
            scoreText.gameObject.SetActive(false);

        if (statusText != null)
            statusText.gameObject.SetActive(false);
    }
}
