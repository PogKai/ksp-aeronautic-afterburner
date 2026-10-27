using System;
using System.IO;
using UnityEngine;

namespace AeronauticAfterburner
{
    // Platform shader bundles load once from Shaders; null means unavailable or unsupported.
    public static class PlumeShaders
    {
        public const string PlumeName = "AeronauticAfterburner/Plume";

        static bool loaded;
        static Shader plume;
        public static Shader Plume { get { Load(); return plume; } }

        public static void Load()
        {
            if (loaded) return;
            loaded = true;
            string platform = Application.platform == RuntimePlatform.OSXPlayer ? "macos"
                            : Application.platform == RuntimePlatform.LinuxPlayer ? "linux" : "windows";
            try
            {
                string file = Path.Combine(Path.Combine(AfterburnerSettings.Root, "Shaders"), "afterburner-" + platform + ".bundle");
                if (!File.Exists(file)) { Debug.Log("[AFTERBURNER] no shader bundle at " + file); return; }
                AssetBundle bundle = AssetBundle.LoadFromFile(file);
                if (bundle == null) { Debug.Log("[AFTERBURNER] could not load " + file); return; }
                foreach (Shader shader in bundle.LoadAllAssets<Shader>())
                {
                    Debug.Log("[AFTERBURNER] shader " + shader.name + (shader.isSupported ? " loaded" : " NOT supported on " + SystemInfo.graphicsDeviceType));
                    if (shader.name == PlumeName && shader.isSupported) plume = shader;
                }
                bundle.Unload(false);
            }
            catch (Exception e) { Debug.Log("[AFTERBURNER] shader bundle failed: " + e.Message); }
        }
    }

    // Loads the bundle once at the main menu, so a failure shows in the log before any flight.
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class PlumeShaderLoader : MonoBehaviour
    {
        void Start() { PlumeShaders.Load(); }
    }
}
