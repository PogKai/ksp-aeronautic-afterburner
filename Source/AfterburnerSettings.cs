using System;
using System.IO;
using KSP.UI.Screens;
using UnityEngine;

namespace AeronauticAfterburner
{
    // On/off, saved to PluginData/settings.cfg; verbose.txt next to Plugins turns on diagnostic logs.
    public static class AfterburnerSettings
    {
        public const string Version = "1.0.0";

        static bool loaded, on = true;
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

        public static bool On
        {
            get { Ensure(); return on; }
            set { Ensure(); if (on != value) { on = value; Save(); } }
        }

        static string FilePath { get { return Path.Combine(Path.Combine(Root, "PluginData"), "settings.cfg"); } }

        static void Ensure()
        {
            if (loaded) return;
            loaded = true;
            try
            {
                ConfigNode node = File.Exists(FilePath) ? ConfigNode.Load(FilePath) : null;
                bool v;
                if (node != null && bool.TryParse(node.GetValue("enabled"), out v)) on = v;
            }
            catch (Exception e) { Debug.Log("[AFTERBURNER] settings: could not read, using defaults: " + e.Message); }
        }

        static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                var node = new ConfigNode("AeronauticAfterburnerSettings");
                node.AddValue("enabled", on.ToString());
                node.Save(FilePath);
            }
            catch (Exception e) { Debug.Log("[AFTERBURNER] settings: could not save: " + e.Message); }
        }
    }

    // A flight toolbar button that turns the plumes on (lit) and off.
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class AfterburnerToolbar : MonoBehaviour
    {
        ApplicationLauncherButton button;
        static Texture2D icon;

        void Start()
        {
            GameEvents.onGUIApplicationLauncherReady.Add(AddButton);
            GameEvents.onGUIApplicationLauncherDestroyed.Add(RemoveButton);
            if (ApplicationLauncher.Ready) AddButton();
        }

        void OnDestroy()
        {
            GameEvents.onGUIApplicationLauncherReady.Remove(AddButton);
            GameEvents.onGUIApplicationLauncherDestroyed.Remove(RemoveButton);
            RemoveButton();
        }

        void AddButton()
        {
            if (button != null || ApplicationLauncher.Instance == null) return;
            if (icon == null) icon = MakeIcon();
            button = ApplicationLauncher.Instance.AddModApplication(
                () => AfterburnerSettings.On = true, () => AfterburnerSettings.On = false,
                null, null, null, null, ApplicationLauncher.AppScenes.FLIGHT, icon);
            if (AfterburnerSettings.On) button.SetTrue(false);
        }

        void RemoveButton()
        {
            if (button != null && ApplicationLauncher.Instance != null) ApplicationLauncher.Instance.RemoveModApplication(button);
            button = null;
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
