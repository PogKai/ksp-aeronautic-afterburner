using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Builds the plume shader into one AssetBundle per platform (afterburner-windows.bundle, -macos,
// -linux), each with every graphics API KSP can run on that platform, into ../Shaders. Run by build.ps1.
public static class BuildBundles
{
    struct Target
    {
        public BuildTarget Build;
        public string Suffix;
        public GraphicsDeviceType[] Apis;
    }

    static readonly Target[] Targets =
    {
        new Target { Build = BuildTarget.StandaloneWindows64, Suffix = "windows", Apis = new[] { GraphicsDeviceType.Direct3D11, GraphicsDeviceType.OpenGLCore } },
        new Target { Build = BuildTarget.StandaloneOSX, Suffix = "macos", Apis = new[] { GraphicsDeviceType.Metal, GraphicsDeviceType.OpenGLCore } },
        new Target { Build = BuildTarget.StandaloneLinux64, Suffix = "linux", Apis = new[] { GraphicsDeviceType.OpenGLCore, GraphicsDeviceType.Vulkan } },
    };

    public static void BuildAll()
    {
        string[] shaders = { "Assets/Shaders/Afterburner.shader" };
        foreach (string s in shaders) if (!File.Exists(s)) throw new IOException("missing " + s);

        string output = Path.GetFullPath("Bundles");
        string shipped = Path.GetFullPath(Path.Combine("..", "Shaders"));
        Directory.CreateDirectory(shipped);

        foreach (Target target in Targets)
        {
            PlayerSettings.SetUseDefaultGraphicsAPIs(target.Build, false);
            PlayerSettings.SetGraphicsAPIs(target.Build, target.Apis);

            string dir = Path.Combine(output, target.Suffix);
            Directory.CreateDirectory(dir);
            var builds = new[] { new AssetBundleBuild { assetBundleName = "afterburner-" + target.Suffix + ".bundle", assetNames = shaders } };
            AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(dir, builds,
                BuildAssetBundleOptions.ForceRebuildAssetBundle | BuildAssetBundleOptions.DeterministicAssetBundle, target.Build);
            if (manifest == null) throw new IOException("bundle build failed for " + target.Build);

            string built = Path.Combine(dir, builds[0].assetBundleName);
            File.Copy(built, Path.Combine(shipped, builds[0].assetBundleName), true);
            Debug.Log("[BuildBundles] " + target.Suffix + ": " + new FileInfo(built).Length + " bytes, " + shaders.Length + " shader(s)");
        }
    }
}
