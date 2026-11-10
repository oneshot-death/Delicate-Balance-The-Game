using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Loads a companion scene additively at runtime (e.g. Floor_Environment alongside Floor).
/// If the scene is already open (both scenes opened together in the editor) nothing happens.
/// The scene must be listed in Build Settings.
/// </summary>
public class AdditiveSceneLoader : MonoBehaviour
{
    [SerializeField] private string sceneName = "Floor_Environment";

    private void Awake()
    {
        if (!SceneManager.GetSceneByName(sceneName).isLoaded)
            SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
    }
}
