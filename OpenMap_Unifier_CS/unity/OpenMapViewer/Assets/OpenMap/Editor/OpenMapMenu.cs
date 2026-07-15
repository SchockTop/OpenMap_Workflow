using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenMapViewer
{
    public static class OpenMapMenu
    {
        [MenuItem("OpenMap/Create Scene Loader...")]
        public static void CreateSceneLoader()
        {
            string dir = EditorUtility.OpenFolderPanel(
                "Select an OpenMap bundle folder (contains manifest.json)", "", "");
            if (string.IsNullOrEmpty(dir)) return;

            var go = new GameObject("OpenMap Scene");
            var loader = go.AddComponent<OpenMapSceneLoader>();
            loader.bundlePath = dir;
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("OpenMap: loader created. Press Play to load '" + dir + "'.");
        }
    }
}
