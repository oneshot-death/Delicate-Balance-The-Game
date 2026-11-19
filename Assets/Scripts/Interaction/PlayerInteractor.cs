using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Something the player can use by looking at it and pressing E (see <see cref="PlayerInteractor"/>).</summary>
public interface IInteractable
{
    void Interact(PlayerInteractor interactor);
}

/// <summary>
/// Lives on the player's camera. On E it raycasts from the centre of the view and calls
/// <see cref="IInteractable.Interact"/> on whatever it hits (the component can be on the hit collider or any parent).
/// Also gives interactables a safe way to freeze, teleport and unfreeze the player.
///
/// Minigames (darts, maze) do their own E raycast against their own colliders; they have no
/// <see cref="IInteractable"/>, so this ignores them, and while a minigame runs PlayerMovement is disabled,
/// which also turns this off.
/// <see cref="Door"/> adds this to the player automatically at runtime, so no scene setup is needed.
/// </summary>
[RequireComponent(typeof(Camera))]
public class PlayerInteractor : MonoBehaviour
{
    [Tooltip("How far (m) from the camera the player can reach.")]
    public float reach = 2.5f;

    [Tooltip("Layers the interaction ray can hit.")]
    public LayerMask mask = Physics.DefaultRaycastLayers;

    Camera cam;
    PlayerMovement movement;
    FPSCameraLook look;
    CharacterController controller;
    bool movementWasEnabled, lookWasEnabled, suspended;

    /// <summary>The player root (the object that moves).</summary>
    public Transform Player => movement != null ? movement.transform : transform.root;

    /// <summary>Makes sure the Player-tagged object's camera has an interactor.</summary>
    public static void EnsureOnPlayer()
    {
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player == null || player.GetComponentInChildren<PlayerInteractor>() != null)
            return;
        var c = player.GetComponentInChildren<Camera>();
        if (c != null)
            c.gameObject.AddComponent<PlayerInteractor>();
    }

    void Awake()
    {
        cam = GetComponent<Camera>();
        movement = GetComponentInParent<PlayerMovement>();
        look = GetComponent<FPSCameraLook>();
        if (look == null)
            look = GetComponentInParent<FPSCameraLook>();
        controller = movement != null ? movement.GetComponent<CharacterController>() : GetComponentInParent<CharacterController>();
    }

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null || !keyboard.eKey.wasPressedThisFrame || suspended)
            return;
        if (movement != null && !movement.enabled)
            return;   // inside a minigame (or another interaction)

        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (!Physics.Raycast(ray, out var hit, reach, mask, QueryTriggerInteraction.Ignore))
            return;
        var target = hit.collider.GetComponentInParent<IInteractable>();
        target?.Interact(this);
    }

    /// <summary>Freezes walking and mouse look, remembering whether they were on.</summary>
    public void SuspendControls()
    {
        if (suspended)
            return;
        suspended = true;
        movementWasEnabled = movement != null && movement.enabled;
        lookWasEnabled = look != null && look.enabled;
        if (movement != null) movement.enabled = false;
        if (look != null) look.enabled = false;
    }

    /// <summary>Restores walking and mouse look to how they were before <see cref="SuspendControls"/>.</summary>
    public void ResumeControls()
    {
        if (!suspended)
            return;
        suspended = false;
        if (movement != null) movement.enabled = movementWasEnabled;
        if (look != null) look.enabled = lookWasEnabled;
    }

    /// <summary>
    /// Moves the player so their feet are at <paramref name="feet"/>'s position, facing its forward (yaw only;
    /// the camera keeps its pitch). The CharacterController is switched off for the move, otherwise it
    /// overwrites the new position with its own on the next Move.
    /// </summary>
    public void TeleportFeetTo(Transform feet)
    {
        Transform player = Player;
        bool ccWasEnabled = controller != null && controller.enabled;
        if (controller != null) controller.enabled = false;

        // Pivot height above the feet: for a capsule, (height / 2 - center.y), plus a small lift so it settles.
        float pivotLift = controller != null ? controller.height * 0.5f - controller.center.y + 0.05f : 0f;
        player.position = feet.position + Vector3.up * pivotLift;

        Vector3 fwd = Vector3.ProjectOnPlane(feet.forward, Vector3.up);
        if (fwd.sqrMagnitude > 0.0001f)
            player.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);

        Physics.SyncTransforms();
        if (controller != null) controller.enabled = ccWasEnabled;
    }
}
