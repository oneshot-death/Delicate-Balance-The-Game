using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Full-screen black overlay for fade-to-black transitions. Creates itself the first time it is used
/// (a Screen Space Overlay canvas drawn above everything else), so nothing needs to be placed in a scene.
/// Uses unscaled time so it still works if the game is paused.
/// </summary>
public class ScreenFader : MonoBehaviour
{
    static ScreenFader instance;
    CanvasGroup group;

    static ScreenFader Instance
    {
        get
        {
            if (instance == null)
                instance = Create();
            return instance;
        }
    }

    /// <summary>Current opacity of the black overlay (0 clear, 1 black).</summary>
    public static float Alpha => instance != null ? instance.group.alpha : 0f;

    static ScreenFader Create()
    {
        var go = new GameObject("ScreenFader");
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;   // above every other UI

        var fader = go.AddComponent<ScreenFader>();
        fader.group = go.AddComponent<CanvasGroup>();
        fader.group.alpha = 0f;
        fader.group.blocksRaycasts = false;
        fader.group.interactable = false;

        var imageGo = new GameObject("Black", typeof(RectTransform));
        imageGo.transform.SetParent(go.transform, false);
        var rt = (RectTransform)imageGo.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var image = imageGo.AddComponent<Image>();
        image.color = Color.black;
        image.raycastTarget = false;
        return fader;
    }

    /// <summary>Fades the overlay to <paramref name="targetAlpha"/> over <paramref name="duration"/> seconds (smoothstep).</summary>
    public static IEnumerator Fade(float targetAlpha, float duration)
    {
        var g = Instance.group;
        float start = g.alpha;
        if (duration <= 0f)
        {
            g.alpha = targetAlpha;
            yield break;
        }
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            g.alpha = Mathf.Lerp(start, targetAlpha, Mathf.SmoothStep(0f, 1f, t / duration));
            yield return null;
        }
        g.alpha = targetAlpha;
    }

    void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }
}
