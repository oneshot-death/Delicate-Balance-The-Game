using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Night-time lighting preset. Lives in the environment scene (Floor_Environment), which is loaded
/// additively, so Unity would otherwise use the gameplay scene's (active scene's) Lighting settings.
/// This component pushes the night ambient, skybox, fog and moonlight onto RenderSettings when it is
/// enabled, in play mode and in the editor, so the lighting stays owned by the environment layer.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class NightLighting : MonoBehaviour
{
    [Header("Moon")]
    [Tooltip("Directional light used as the moon (the scene's sun).")]
    [SerializeField] Light moon;
    [SerializeField] Color moonColor = new Color(0.55f, 0.65f, 0.9f);
    [SerializeField] float moonIntensity = 0.08f;

    [Header("Sky")]
    [SerializeField] Material nightSkybox;

    [Header("Ambient (trilight)")]
    [SerializeField] Color ambientSky = new Color(0.025f, 0.03f, 0.05f);
    [SerializeField] Color ambientEquator = new Color(0.015f, 0.017f, 0.025f);
    [SerializeField] Color ambientGround = new Color(0.006f, 0.006f, 0.008f);
    [SerializeField, Range(0f, 1f)] float reflectionIntensity = 0.15f;

    [Header("Fog")]
    [SerializeField] bool fog = true;
    [SerializeField] Color fogColor = new Color(0.01f, 0.012f, 0.02f);
    [SerializeField] float fogDensity = 0.03f;

    void OnEnable() => Apply();

#if UNITY_EDITOR
    void OnValidate()
    {
        if (isActiveAndEnabled) UnityEditor.EditorApplication.delayCall += () => { if (this) Apply(); };
    }
#endif

    [ContextMenu("Apply")]
    public void Apply()
    {
        if (moon != null)
        {
            moon.color = moonColor;
            moon.intensity = moonIntensity;
            moon.useColorTemperature = false;
            RenderSettings.sun = moon;
        }

        if (nightSkybox != null) RenderSettings.skybox = nightSkybox;

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = ambientSky;
        RenderSettings.ambientEquatorColor = ambientEquator;
        RenderSettings.ambientGroundColor = ambientGround;
        RenderSettings.reflectionIntensity = reflectionIntensity;

        RenderSettings.fog = fog;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = fogColor;
        RenderSettings.fogDensity = fogDensity;

        DynamicGI.UpdateEnvironment();
    }
}
