using UnityEngine;

namespace OpenMapViewer
{
    /// <summary>
    /// Makes the project work with literally zero setup: pressing Play in any
    /// scene spawns the viewer if none exists, and the HUD takes it from
    /// there (bundle picker, demo scene). If you embed the OpenMap scripts in
    /// your own project and want manual control instead, delete this file and
    /// add the OpenMapSceneLoader component yourself.
    /// </summary>
    public static class OpenMapBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoStart()
        {
            if (Object.FindObjectOfType<OpenMapSceneLoader>() != null) return;
            var go = new GameObject("OpenMap Scene");
            go.AddComponent<OpenMapSceneLoader>();
        }
    }
}
