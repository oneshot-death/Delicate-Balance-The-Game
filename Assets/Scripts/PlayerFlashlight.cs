using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Hand-held flashlight for the player. Put it on the player camera: it creates (or reuses)
/// a child spot light that follows the view with a little lag, starts switched on, and
/// toggles with F while the player is in control (ignored during minigames, i.e. while
/// PlayerMovement is disabled).
/// </summary>
[DisallowMultipleComponent]
public class PlayerFlashlight : MonoBehaviour
{
    [Header("State")]
    [SerializeField] bool startOn = true;
    [SerializeField] Key toggleKey = Key.F;

    [Header("Beam")]
    [SerializeField] Color color = new Color(1f, 0.95f, 0.85f);
    [SerializeField] float intensity = 10f;
    [SerializeField] float range = 30f;
    [SerializeField, Range(1f, 179f)] float outerAngle = 55f;
    [SerializeField, Range(1f, 179f)] float innerAngle = 12f;
    [SerializeField] LightShadows shadows = LightShadows.Soft;

    [Header("Hold")]
    [Tooltip("Where the light sits relative to the camera (slightly right and down, like a hand).")]
    [SerializeField] Vector3 holdOffset = new Vector3(0.18f, -0.12f, 0.1f);
    [Tooltip("How quickly the beam catches up with the view direction. 0 = rigidly attached.")]
    [SerializeField] float followSharpness = 18f;

    [Header("Minigames")]
    [Tooltip("Intensity multiplier while a minigame has taken the camera (boards are viewed up close).")]
    [SerializeField] float minigameIntensityScale = 0.15f;

    [Header("Audio (optional)")]
    [SerializeField] AudioClip toggleClip;
    [SerializeField, Range(0f, 1f)] float toggleVolume = 0.6f;

    Light beam;
    Transform pivot;
    PlayerMovement movement;
    AudioSource audioSource;
    bool isOn;

    public bool IsOn => isOn;
    public Light Beam => beam;

    void Awake()
    {
        movement = GetComponentInParent<PlayerMovement>();
        EnsureLight();
        SetOn(startOn, false);
    }

    void OnValidate()
    {
        if (beam != null) ApplySettings();
    }

    void EnsureLight()
    {
        if (beam == null) beam = GetComponentInChildren<Light>(true);
        if (beam == null)
        {
            var go = new GameObject("Flashlight");
            go.transform.SetParent(transform, false);
            beam = go.AddComponent<Light>();
        }
        pivot = beam.transform;
        pivot.localPosition = holdOffset;
        pivot.localRotation = Quaternion.identity;
        ApplySettings();
    }

    void ApplySettings()
    {
        beam.type = LightType.Spot;
        beam.color = color;
        beam.range = range;
        beam.spotAngle = outerAngle;
        beam.innerSpotAngle = Mathf.Min(innerAngle, outerAngle);
        beam.shadows = shadows;
        beam.intensity = CurrentIntensity();
    }

    float CurrentIntensity()
    {
        bool inMinigame = movement != null && !movement.enabled;
        return intensity * (inMinigame ? minigameIntensityScale : 1f);
    }

    public void SetOn(bool on) => SetOn(on, true);

    void SetOn(bool on, bool playSound)
    {
        isOn = on;
        if (beam != null) beam.enabled = on;
        if (playSound && toggleClip != null)
        {
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 0f;
            }
            audioSource.PlayOneShot(toggleClip, toggleVolume);
        }
    }

    void Update()
    {
        bool inControl = movement == null || movement.enabled;
        var kb = Keyboard.current;
        if (inControl && kb != null && kb[toggleKey].wasPressedThisFrame)
            SetOn(!isOn);

        if (beam != null) beam.intensity = CurrentIntensity();
    }

    void LateUpdate()
    {
        if (pivot == null) return;
        // The pivot is a child of the camera; keep its position fixed in camera space but let its
        // world rotation trail the camera slightly so fast turns sweep the beam.
        pivot.localPosition = holdOffset;
        if (followSharpness <= 0f || !(movement == null || movement.enabled))
        {
            pivot.rotation = transform.rotation;
            return;
        }
        float t = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
        pivot.rotation = Quaternion.Slerp(pivot.rotation, transform.rotation, t);
    }
}
