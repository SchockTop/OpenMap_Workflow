using System.IO;
using UnityEngine;

namespace OpenMapViewer
{
    /// <summary>
    /// The viewer's user interface (IMGUI — works with zero scene setup and
    /// zero packages). Two states: a bundle picker when nothing is loaded,
    /// and the control panel when a scene is up: playback transport with
    /// timeline, camera follow, sensor toggle with live boresight readout,
    /// overlay legend with toggles, aircraft position readout in UTM, and a
    /// screenshot button. Scales itself for high-DPI screens.
    /// </summary>
    public class OpenMapHud : MonoBehaviour
    {
        public OpenMapSceneLoader loader;

        private static readonly float[] Speeds = { 0.5f, 1f, 2f, 5f, 10f, 25f };

        private string _pathInput;
        private bool _showLoadPanel;
        private string _screenshotNote;
        private float _screenshotNoteUntil;
        private Texture2D _panelBackground;
        private Texture2D _swatch;
        private GUIStyle _title;
        private GUIStyle _section;
        private GUIStyle _panel;
        private bool _stylesReady;

        private void Update()
        {
            // Keyboard transport: space = play/pause (documented in the HUD).
            if (loader != null && loader.Playback != null && Input.GetKeyDown(KeyCode.Space))
                loader.Playback.playing = !loader.Playback.playing;
        }

        private void OnGUI()
        {
            if (loader == null) return;
            EnsureStyles();

            // Design in a 1600x900 reference space; scale to the real screen.
            float scale = Mathf.Max(1f, Screen.height / 900f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float width = Screen.width / scale;
            float height = Screen.height / scale;

            if (!loader.Loaded || _showLoadPanel)
                DrawLoadPanel(width, height);
            else
                DrawControlPanel(height);
        }

        // ---- bundle picker ---------------------------------------------------

        private void DrawLoadPanel(float width, float height)
        {
            if (_pathInput == null)
            {
                _pathInput = loader.Loaded ? loader.LoadedPath : OpenMapSceneLoader.RememberedPath;
                if (string.IsNullOrEmpty(_pathInput) &&
                    OpenMapSceneLoader.IsBundle(OpenMapSceneLoader.DefaultStreamingBundle))
                    _pathInput = OpenMapSceneLoader.DefaultStreamingBundle;
            }

            float w = 560f;
            var rect = new Rect((width - w) / 2f, height * 0.28f, w, 0f);
            GUILayout.BeginArea(new Rect(rect.x, rect.y, w, 320f));
            GUILayout.BeginVertical(_panel);

            GUILayout.Label("OpenMap Viewer", _title);
            GUILayout.Label("Open a scene bundle exported by the OpenMapUnifier framework\n" +
                            "(a folder containing manifest.json — the \"unityScene\" output of `openmap scene`).");
            GUILayout.Space(8);

            GUILayout.BeginHorizontal();
            _pathInput = GUILayout.TextField(_pathInput ?? "", GUILayout.Height(26f));
#if UNITY_EDITOR
            if (GUILayout.Button("Browse…", GUILayout.Width(80f), GUILayout.Height(26f)))
            {
                string picked = UnityEditor.EditorUtility.OpenFolderPanel(
                    "Select an OpenMap bundle folder", _pathInput ?? "", "");
                if (!string.IsNullOrEmpty(picked)) _pathInput = picked;
            }
#endif
            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            GUILayout.BeginHorizontal();
            GUI.enabled = !string.IsNullOrEmpty(_pathInput);
            if (GUILayout.Button("Load scene", GUILayout.Height(30f)))
            {
                if (loader.LoadBundle(_pathInput.Trim().Trim('"')))
                    _showLoadPanel = false;
            }
            GUI.enabled = true;
            if (loader.Loaded && GUILayout.Button("Cancel", GUILayout.Width(90f), GUILayout.Height(30f)))
                _showLoadPanel = false;
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(loader.LastError))
            {
                GUILayout.Space(6);
                var error = new GUIStyle(GUI.skin.label);
                error.normal.textColor = new Color(1f, 0.5f, 0.45f);
                error.wordWrap = true;
                GUILayout.Label(loader.LastError, error);
            }

            GUILayout.Space(6);
            GUILayout.Label("Tip: a bundle copied to Assets/StreamingAssets/OpenMapBundle loads automatically.",
                SmallLabel());

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        // ---- control panel -----------------------------------------------------

        private void DrawControlPanel(float height)
        {
            GUILayout.BeginArea(new Rect(12f, 12f, 370f, height - 24f));
            GUILayout.BeginVertical(_panel);

            var anchor = loader.Manifest.anchor;
            GUILayout.Label("OpenMap Viewer", _title);
            GUILayout.Label(string.Format(
                "Origin {0:F6}°, {1:F6}°   EPSG:{2}   1 unit = 1 m",
                anchor.latitude, anchor.longitude, anchor.epsg), SmallLabel());

            var playback = loader.Playback;
            if (playback != null && playback.HasData)
            {
                Section("Flight playback");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(playback.playing ? "Pause" : "Play",
                        GUILayout.Width(90f), GUILayout.Height(26f)))
                    playback.playing = !playback.playing;
                GUILayout.Label(string.Format("  {0:F1} / {1:F1} s",
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
                GUILayout.Label("Speed", GUILayout.Width(45f));
                foreach (float s in Speeds)
                {
                    bool active = Mathf.Approximately(playback.speed, s);
                    if (GUILayout.Toggle(active, s + "x", GUI.skin.button) && !active)
                        playback.speed = s;
                }
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                playback.loop = GUILayout.Toggle(playback.loop, " Loop");
                if (loader.Orbit != null)
                    loader.Orbit.follow = GUILayout.Toggle(loader.Orbit.follow, " Follow aircraft");
                GUILayout.EndHorizontal();

                if (playback.aircraft != null)
                {
                    Section("Aircraft");
                    Vector3 p = playback.aircraft.position;
                    GUILayout.Label(string.Format(
                        "UTM  {0:F1} E   {1:F1} N   alt {2:F1} m",
                        anchor.utmEasting + p.x, anchor.utmNorthing + p.z, p.y), SmallLabel());
                    Vector3 e = playback.aircraft.rotation.eulerAngles;
                    GUILayout.Label(string.Format(
                        "yaw {0:F1}°   pitch {1:F1}°   roll {2:F1}°",
                        e.y, -Normalize180(e.x), -Normalize180(e.z)), SmallLabel());
                }
            }

            var sensor = loader.Sensor;
            if (sensor != null && sensor.sensor != null)
            {
                Section("Sensor");
                sensor.show = GUILayout.Toggle(sensor.show,
                    " " + sensor.sensor.name + "  (" + sensor.sensor.type + ")");
                if (sensor.show && sensor.HasHit)
                {
                    Vector3 hit = sensor.HitPoint;
                    GUILayout.Label(string.Format(
                        "boresight ground:  {0:F1} E   {1:F1} N   {2:F1} m",
                        anchor.utmEasting + hit.x, anchor.utmNorthing + hit.z, hit.y), SmallLabel());
                }
                else if (sensor.show)
                {
                    GUILayout.Label("boresight ground:  above horizon / off map", SmallLabel());
                }
            }

            if (loader.OverlayCount > 0)
            {
                Section("Ground overlays");
                for (int i = 0; i < loader.OverlayCount; i++)
                {
                    GUILayout.BeginHorizontal();
                    bool visible = loader.IsOverlayVisible(i);
                    bool toggled = GUILayout.Toggle(visible, "", GUILayout.Width(20f));
                    var swatchRect = GUILayoutUtility.GetRect(14f, 14f, GUILayout.Width(14f));
                    swatchRect.y += 2f;
                    var previous = GUI.color;
                    GUI.color = loader.OverlayColor(i);
                    GUI.DrawTexture(swatchRect, Swatch());
                    GUI.color = previous;
                    GUILayout.Label(" " + loader.OverlayName(i));
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                    if (toggled != visible)
                        loader.SetOverlayVisible(i, toggled);
                }
            }

            Section("");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Load bundle…", GUILayout.Height(24f)))
            {
                _pathInput = null;
                _showLoadPanel = true;
            }
            if (GUILayout.Button("Screenshot", GUILayout.Width(100f), GUILayout.Height(24f)))
                TakeScreenshot();
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(_screenshotNote) && Time.unscaledTime < _screenshotNoteUntil)
                GUILayout.Label(_screenshotNote, SmallLabel());

            GUILayout.FlexibleSpace();
            GUILayout.Label("RMB orbit · MMB pan · scroll zoom · F frame · Space play/pause",
                SmallLabel());

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        private void TakeScreenshot()
        {
            string dir = Path.Combine(Application.persistentDataPath, "OpenMapScreenshots");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir,
                "openmap_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
            ScreenCapture.CaptureScreenshot(file);
            _screenshotNote = "Saved: " + file;
            _screenshotNoteUntil = Time.unscaledTime + 5f;
        }

        private static float Normalize180(float degrees)
        {
            degrees %= 360f;
            if (degrees > 180f) degrees -= 360f;
            if (degrees < -180f) degrees += 360f;
            return degrees;
        }

        // ---- styling ----------------------------------------------------------

        private void Section(string label)
        {
            GUILayout.Space(8f);
            if (label.Length > 0) GUILayout.Label(label, _section);
        }

        private GUIStyle SmallLabel()
        {
            var style = new GUIStyle(GUI.skin.label);
            style.fontSize = 11;
            style.normal.textColor = new Color(0.85f, 0.85f, 0.85f);
            style.wordWrap = true;
            return style;
        }

        private Texture2D Swatch()
        {
            if (_swatch == null)
            {
                _swatch = new Texture2D(1, 1);
                _swatch.SetPixel(0, 0, Color.white);
                _swatch.Apply();
            }
            return _swatch;
        }

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;

            _panelBackground = new Texture2D(1, 1);
            _panelBackground.SetPixel(0, 0, new Color(0.08f, 0.1f, 0.12f, 0.88f));
            _panelBackground.Apply();

            _panel = new GUIStyle(GUI.skin.box);
            _panel.normal.background = _panelBackground;
            _panel.padding = new RectOffset(12, 12, 10, 10);

            _title = new GUIStyle(GUI.skin.label);
            _title.fontSize = 17;
            _title.fontStyle = FontStyle.Bold;
            _title.normal.textColor = Color.white;

            _section = new GUIStyle(GUI.skin.label);
            _section.fontSize = 12;
            _section.fontStyle = FontStyle.Bold;
            _section.normal.textColor = new Color(0.55f, 0.8f, 1f);
        }
    }
}
