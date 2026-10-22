using System;
using System.Globalization;
using System.IO;
using KSP.UI.Screens;
using UnityEngine;

namespace AeronauticAfterburner
{
    // The player's choices, saved to PluginData/settings.cfg; verbose.txt next to Plugins turns on diagnostic logs.
    public static class AfterburnerSettings
    {
        public const string Version = "1.0.0";
        public const float MinLength = 0.5f, MaxLength = 2f;

        static bool loaded, on = true, blue;
        static float lengthScale = 1f;
        static string root;

        // The mod's GameData folder (the parent of Plugins).
        public static string Root
        {
            get
            {
                if (root == null) root = Path.GetDirectoryName(Path.GetDirectoryName(typeof(AfterburnerSettings).Assembly.Location));
                return root;
            }
        }

        static readonly bool verbose = File.Exists(Path.Combine(Root, "verbose.txt"));
        public static bool Verbose { get { return verbose; } }

        public static bool On { get { Ensure(); return on; } set { Ensure(); if (on != value) { on = value; Save(); } } }

        // A blue flame instead of the orange one.
        public static bool Blue { get { Ensure(); return blue; } set { Ensure(); if (blue != value) { blue = value; Save(); } } }

        // The flame's length against the default (1 = 3 to 14 exit diameters).
        public static float LengthScale
        {
            get { Ensure(); return lengthScale; }
            set { Ensure(); value = Mathf.Clamp(value, MinLength, MaxLength); if (lengthScale != value) { lengthScale = value; Dirty = true; ChangedAt = Time.unscaledTime; } }
        }

        // The length slider writes often while it is dragged; the window saves once it settles.
        public static bool Dirty;
        public static float ChangedAt;

        public static void Defaults() { Ensure(); on = true; blue = false; lengthScale = 1f; Save(); }

        static string FilePath { get { return Path.Combine(Path.Combine(Root, "PluginData"), "settings.cfg"); } }

        static void Ensure()
        {
            if (loaded) return;
            loaded = true;
            try
            {
                ConfigNode node = File.Exists(FilePath) ? ConfigNode.Load(FilePath) : null;
                if (node == null) return;
                bool b; float f;
                if (bool.TryParse(node.GetValue("enabled"), out b)) on = b;
                if (bool.TryParse(node.GetValue("blue"), out b)) blue = b;
                if (float.TryParse(node.GetValue("length"), NumberStyles.Float, CultureInfo.InvariantCulture, out f)) lengthScale = Mathf.Clamp(f, MinLength, MaxLength);
            }
            catch (Exception e) { Debug.Log("[AFTERBURNER] settings: could not read, using defaults: " + e.Message); }
        }

        public static void Save()
        {
            Dirty = false;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                var node = new ConfigNode("AeronauticAfterburnerSettings");
                node.AddValue("enabled", on.ToString());
                node.AddValue("blue", blue.ToString());
                node.AddValue("length", lengthScale.ToString("F2", CultureInfo.InvariantCulture));
                node.Save(FilePath);
            }
            catch (Exception e) { Debug.Log("[AFTERBURNER] settings: could not save: " + e.Message); }
        }
    }

    // The flight toolbar button opens a small window: plumes on or off, the flame's colour and its length.
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class AfterburnerWindow : MonoBehaviour
    {
        const int WindowId = 7149281;
        const float SaveDelay = 1f;   // s after the last slider change

        ApplicationLauncherButton button;
        static Texture2D icon;
        Rect rect = new Rect(120f, 120f, 260f, 10f);
        bool open, uiHidden;

        void Start()
        {
            GameEvents.onGUIApplicationLauncherReady.Add(AddButton);
            GameEvents.onGUIApplicationLauncherDestroyed.Add(RemoveButton);
            GameEvents.onHideUI.Add(HideUI);
            GameEvents.onShowUI.Add(ShowUI);
            if (ApplicationLauncher.Ready) AddButton();
        }

        void OnDestroy()
        {
            GameEvents.onGUIApplicationLauncherReady.Remove(AddButton);
            GameEvents.onGUIApplicationLauncherDestroyed.Remove(RemoveButton);
            GameEvents.onHideUI.Remove(HideUI);
            GameEvents.onShowUI.Remove(ShowUI);
            RemoveButton();
            if (AfterburnerSettings.Dirty) AfterburnerSettings.Save();
        }

        void HideUI() { uiHidden = true; }
        void ShowUI() { uiHidden = false; }

        void AddButton()
        {
            if (button != null || ApplicationLauncher.Instance == null) return;
            if (icon == null) icon = MakeIcon();
            button = ApplicationLauncher.Instance.AddModApplication(
                () => open = true, Close, null, null, null, null, ApplicationLauncher.AppScenes.FLIGHT, icon);
        }

        void RemoveButton()
        {
            if (button != null && ApplicationLauncher.Instance != null) ApplicationLauncher.Instance.RemoveModApplication(button);
            button = null;
        }

        void Close()
        {
            open = false;
            if (AfterburnerSettings.Dirty) AfterburnerSettings.Save();
        }

        void Update()
        {
            // One write after a slider drag, not hundreds.
            if (AfterburnerSettings.Dirty && Time.unscaledTime - AfterburnerSettings.ChangedAt > SaveDelay) AfterburnerSettings.Save();
        }

        void OnGUI()
        {
            if (!open || uiHidden) return;
            GUI.skin = HighLogic.Skin;
            rect = GUILayout.Window(WindowId, rect, Draw, "Afterburner " + AfterburnerSettings.Version, GUILayout.Width(260f));
            rect.x = Mathf.Clamp(rect.x, 0f, Screen.width - 60f);
            rect.y = Mathf.Clamp(rect.y, 0f, Screen.height - 40f);
        }

        void Draw(int id)
        {
            GUILayout.Space(4f);
            AfterburnerSettings.On = GUILayout.Toggle(AfterburnerSettings.On, " <b>Afterburner plumes</b>", new GUIStyle(GUI.skin.toggle) { richText = true });

            bool wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && AfterburnerSettings.On;
            GUILayout.Space(6f);
            GUILayout.Label("Flame colour");
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(!AfterburnerSettings.Blue, " Orange", GUI.skin.button)) AfterburnerSettings.Blue = false;
            if (GUILayout.Toggle(AfterburnerSettings.Blue, " Blue", GUI.skin.button)) AfterburnerSettings.Blue = true;
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Length", GUILayout.Width(60f));
            float percent = GUILayout.HorizontalSlider(AfterburnerSettings.LengthScale * 100f, AfterburnerSettings.MinLength * 100f, AfterburnerSettings.MaxLength * 100f);
            GUILayout.Label(Mathf.RoundToInt(percent) + "%", GUILayout.Width(44f));
            GUILayout.EndHorizontal();
            AfterburnerSettings.LengthScale = Mathf.Round(percent) / 100f;
            GUI.enabled = wasEnabled;

            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Defaults")) AfterburnerSettings.Defaults();
            if (GUILayout.Button("Close")) { if (button != null) button.SetFalse(true); else Close(); }
            GUILayout.EndHorizontal();
            GUI.DragWindow();
        }

        // A flame cone pointing right: white core, orange edge, fading aft.
        static Texture2D MakeIcon()
        {
            const int size = 38;
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float along = (size - 1 - x) / (float)(size - 1);
                    float half = 0.42f * (1 - along) + 0.04f;
                    float off = Mathf.Abs(y - (size - 1) / 2f) / (size - 1);
                    float inside = Mathf.Clamp01((half - off) * 30f);
                    float core = Mathf.Clamp01((half * 0.45f - off) * 30f);
                    Color c = Color.Lerp(new Color(1f, 0.55f, 0.15f), new Color(1f, 0.95f, 0.8f), core);
                    c.a = inside * Mathf.Clamp01(1.2f - along);
                    tex.SetPixel(x, y, c);
                }
            tex.Apply();
            return tex;
        }
    }
}
