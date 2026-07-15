using UnityEngine;

namespace OpenMapViewer
{
    /// <summary>
    /// The on-screen control panel (IMGUI, so it needs zero scene setup):
    /// playback transport, timeline scrubbing, speed, camera follow, sensor
    /// and overlay toggles, plus the georeference of the scene origin.
    /// </summary>
    public class OpenMapHud : MonoBehaviour
    {
        public OpenMapSceneLoader loader;
        public FlightPlayback playback;
        public SensorVisualizer sensorVisualizer;
        public OrbitCamera orbitCamera;

        private static readonly float[] Speeds = { 0.5f, 1f, 2f, 5f, 10f, 25f };

        private void OnGUI()
        {
            if (loader == null || !loader.Loaded) return;

            GUILayout.BeginArea(new Rect(10, 10, 340, Screen.height - 20));
            GUILayout.BeginVertical(GUI.skin.box);

            var anchor = loader.Manifest.anchor;
            GUILayout.Label("OpenMap Scene  (1 unit = 1 m)");
            GUILayout.Label(string.Format("Origin: {0:F6}, {1:F6}  (EPSG:{2})",
                anchor.latitude, anchor.longitude, anchor.epsg));

            if (playback != null && playback.HasData)
            {
                GUILayout.Space(6);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(playback.playing ? "Pause" : "Play", GUILayout.Width(60)))
                    playback.playing = !playback.playing;
                GUILayout.Label(string.Format("{0:F1} / {1:F1} s",
                    playback.TimeSeconds, playback.Duration));
                GUILayout.EndHorizontal();

                float scrubbed = GUILayout.HorizontalSlider(
                    playback.TimeSeconds, 0f, playback.Duration);
                if (Mathf.Abs(scrubbed - playback.TimeSeconds) > 0.0001f)
                {
                    playback.playing = false;
                    playback.TimeSeconds = scrubbed;
                }

                GUILayout.BeginHorizontal();
                GUILayout.Label("Speed", GUILayout.Width(45));
                foreach (float s in Speeds)
                {
                    bool active = Mathf.Approximately(playback.speed, s);
                    if (GUILayout.Toggle(active, s + "x", GUI.skin.button) && !active)
                        playback.speed = s;
                }
                GUILayout.EndHorizontal();

                playback.loop = GUILayout.Toggle(playback.loop, " Loop");
                if (orbitCamera != null)
                    orbitCamera.follow = GUILayout.Toggle(orbitCamera.follow, " Follow aircraft");
            }

            if (sensorVisualizer != null && sensorVisualizer.sensor != null)
            {
                GUILayout.Space(6);
                sensorVisualizer.show = GUILayout.Toggle(sensorVisualizer.show,
                    " Sensor: " + sensorVisualizer.sensor.name +
                    " (" + sensorVisualizer.sensor.type + ")");
            }

            if (loader.OverlayCount > 0)
            {
                GUILayout.Space(6);
                GUILayout.Label("Ground overlays");
                for (int i = 0; i < loader.OverlayCount; i++)
                {
                    bool visible = loader.IsOverlayVisible(i);
                    bool toggled = GUILayout.Toggle(visible, " " + loader.OverlayName(i));
                    if (toggled != visible)
                        loader.SetOverlayVisible(i, toggled);
                }
            }

            GUILayout.Space(6);
            GUILayout.Label("RMB orbit · MMB pan · scroll zoom · F frame");

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }
    }
}
