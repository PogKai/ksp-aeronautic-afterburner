using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

using AeronauticAfterburner;

// The afterburner plume outside KSP, in KSP's engine (Unity 2019.4.18f1), through the mod's own
// shader and AfterburnerLook: one panel per variant, stacked, on a night and a day background.
//   -variants "name:key=value,key=value;name2:..." (keys: any public field of AfterburnerParams or
//   AfterburnerProfile, any case; plus nozzle, throttle, speed, altitude, burner 0 to 1, surge 0 to 1, time s, view 1 = close behind the nozzle, scale = nozzle open 0 to 1) -out <folder> [-width w] [-height h]
// Run by plume.ps1.
public static class AfterburnerHarness
{
    static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

    public static void Render()
    {
        var args = new Dictionary<string, string>();
        string[] all = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < all.Length; i++)
            if (all[i].StartsWith("-") && all[i].Length > 1) args[all[i].Substring(1).ToLowerInvariant()] = all[i + 1];
        string outDir = args.ContainsKey("out") ? args["out"] : Path.GetFullPath("Bundles/plume");
        int width = args.ContainsKey("width") ? int.Parse(args["width"], Ci) : 800, height = args.ContainsKey("height") ? int.Parse(args["height"], Ci) : 260;
        string variants = args.ContainsKey("variants") ? args["variants"] : "default:";
        Directory.CreateDirectory(outDir);

        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/Afterburner.shader");
        if (shader == null || !shader.isSupported) throw new Exception("Afterburner.shader missing or unsupported");
        var draw = new AfterburnerDraw(shader);
        var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        var camGo = new GameObject("PlumeCamera");
        Camera cam = camGo.AddComponent<Camera>();
        cam.targetTexture = rt;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.depthTextureMode = DepthTextureMode.Depth;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 2000f;
        cam.fieldOfView = 24f;
        // Night: upstream's docs stage; day: KSP's sea and sky as the sandbox shows them.
        var backgrounds = new[] { new KeyValuePair<string, Color>("night", new Color(0.015f, 0.015f, 0.02f)), new KeyValuePair<string, Color>("day", new Color(0.12f, 0.27f, 0.5f)) };
        string[] list = variants.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        try
        {
            foreach (KeyValuePair<string, Color> background in backgrounds)
            {
                cam.backgroundColor = background.Value;
                var sheet = new Texture2D(width, height * list.Length, TextureFormat.RGB24, false);
                for (int row = 0; row < list.Length; row++)
                {
                    string name = list[row].Contains(":") ? list[row].Substring(0, list[row].IndexOf(':')) : list[row];
                    string overrides = list[row].Contains(":") ? list[row].Substring(list[row].IndexOf(':') + 1) : "";
                    double nozzle = 0.35, throttle = 1.1, speed = 150, altitude = 1000, burner = -1, surge = 0, clock = 3.7, view = 0, scale = 1, aspect = 1;
                    var profile = AfterburnerLook.Profile();
                    AfterburnerParams p = null;
                    var pairs = new List<KeyValuePair<string, double>>();
                    foreach (string kv in overrides.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string[] two = kv.Split('=');
                        pairs.Add(new KeyValuePair<string, double>(two[0].Trim().ToLowerInvariant(), double.Parse(two[1], Ci)));
                    }
                    foreach (var kv in pairs)
                    {
                        if (kv.Key == "nozzle") nozzle = kv.Value;
                        else if (kv.Key == "throttle") throttle = kv.Value;
                        else if (kv.Key == "speed") speed = kv.Value;
                        else if (kv.Key == "altitude") altitude = kv.Value;
                        else if (kv.Key == "burner") burner = kv.Value;
                        else if (kv.Key == "surge") surge = kv.Value;
                        else if (kv.Key == "time") clock = kv.Value;
                        else if (kv.Key == "view") view = kv.Value;
                        else if (kv.Key == "scale") scale = kv.Value;
                        else if (kv.Key == "aspect") aspect = kv.Value;
                    }
                    p = AfterburnerLook.Params(nozzle);
                    foreach (var kv in pairs)
                    {
                        if (kv.Key == "nozzle" || kv.Key == "throttle" || kv.Key == "speed" || kv.Key == "altitude" || kv.Key == "burner" || kv.Key == "surge" || kv.Key == "time" || kv.Key == "view" || kv.Key == "scale" || kv.Key == "aspect") continue;
                        if (!Set(p, kv.Key, kv.Value) && !Set(profile, kv.Key, kv.Value)) throw new Exception("no field " + kv.Key);
                    }
                    profile.AltitudeM = altitude;
                    profile.AirspeedMPerS = Math.Min(speed, AfterburnerLook.SpeedLimitMPerS);
                    JetState s = AfterburnerJet.State(p, throttle, profile, scale, burner);

                    // The plume runs along +x from the nozzle at the origin; seen from the side and a little behind.
                    float reach = (float)s.ReachM;
                    Vector3 target = new Vector3(reach * 0.42f, 0, 0);
                    cam.fieldOfView = 24f;
                    if (view > 0.5)
                    {
                        // Close behind the nozzle, to the side, looking at it: the plume comes toward the camera.
                        cam.fieldOfView = 55f;
                        cam.transform.position = new Vector3(reach * 0.45f, reach * 0.10f, -reach * 0.3f);
                        cam.transform.LookAt(new Vector3(0, 0, 0));
                    }
                    else
                    {
                        cam.transform.position = target + new Vector3(reach * 0.35f, reach * 0.08f, -reach * 1.25f);
                        cam.transform.LookAt(target);
                    }
                    draw.WriteProfile(profile, (float)clock);
                    draw.Draw(cam, 0, Vector3.zero, Quaternion.identity, p, s, 1.3f, (float)surge, new Vector3(1, (float)aspect, (float)(1 / aspect)));
                    cam.Render();
                    RenderTexture.active = rt;
                    var panel = new Texture2D(width, height, TextureFormat.RGB24, false);
                    panel.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    panel.Apply();
                    RenderTexture.active = null;
                    sheet.SetPixels(0, (list.Length - 1 - row) * height, width, height, panel.GetPixels());
                    UnityEngine.Object.DestroyImmediate(panel);
                    Debug.Log(string.Format(Ci, "[PlumeHarness] {0} {1}: radius {2:F2} m (x{3:F2}), reach {4:F1} m ({5:F1} D), core {6:F1} D, spacing {7:F2} D, shock length {8:F1} D, T {9:F0} K, burner {10:F2}",
                        background.Key, name, s.RadiusM, s.RadiusM / nozzle, s.ReachM, s.ReachM / (2 * nozzle), s.CoreLengthM / (2 * nozzle), s.ShockSpacingM / (2 * nozzle), s.ShockLengthM / (2 * nozzle), s.TemperatureK, s.Burner));
                }
                sheet.Apply();
                File.WriteAllBytes(Path.Combine(outDir, "plume-" + background.Key + ".png"), sheet.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(sheet);
            }
            Debug.Log("[PlumeHarness] wrote " + outDir);
        }
        finally
        {
            draw.Destroy(true);
            UnityEngine.Object.DestroyImmediate(camGo);
            rt.Release();
        }
    }

    static bool Set(object target, string name, double value)
    {
        foreach (FieldInfo f in target.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase) && f.FieldType == typeof(double)) { f.SetValue(target, value); return true; }
        return false;
    }
}
