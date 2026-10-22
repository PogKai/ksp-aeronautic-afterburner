using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace AeronauticAfterburner
{
    // Engines in a wet (afterburner) mode drawn with aeronautic's raymarched plume. The engine's own
    // wet effect (stock particles or a Waterfall module) is hidden while this is on; dry modes keep
    // theirs. See docs/afterburner.md.
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class AfterburnerPlumes : MonoBehaviour
    {
        const float ScanSeconds = 0.5f, RangeM = 5000f;
        // The nozzle exit against the engine's radius across its thrust axis.
        const float NozzleShare = 0.65f;
        // The plume's radius against the exit's.
        const double ExitFill = 0.92;
        // How fast the lit share of the burner moves, per second. Lighting: the first zone pops in
        // within 0.3 s, the rest stages in over about 1.6 s. Going out: about 1.2 s. The flame's
        // length and diamonds follow it (docs/afterburner.md).
        const double LightFirstZone = 1.2, LightStaging = 0.4, GoOut = 0.8;
        // Switching to dry mode: the burner goes out in about 0.35 s while the nozzle closes, the
        // plume shrinking with it to ClosedShare of its size; opening again takes about half a second.
        const double ModeSwitchOut = 3.0, NozzleCloseRate = 3.0, NozzleOpenRate = 2.0, ClosedShare = 0.6;
        // The surge (flow aft, shivering diamonds) eases in and out over about this long.
        const float SurgeEaseS = 0.35f, OutputEaseS = 0.25f;

        class Nozzle
        {
            public Part Part;
            public ModuleEnginesFX Wet;
            public MultiModeEngine Modes;
            public AfterburnerParams Params;
            public float Seed;
            // How much of the burner is lit, ramped: 0 is dry (nothing drawn), 1 full reheat.
            public double Burner;
            // How much the burner is changing, eased: the sheath flows aft and the diamonds shiver.
            public float Surge;
            // How far the nozzle is open, 0 (dry) to 1 (wet), eased.
            public double Open;
            // The engine's output (thrust over maximum), eased: the jet's pressure and heat follow it.
            public float Output;
            public readonly List<NozzlePoint> Points = new List<NozzlePoint>();
            public readonly List<MeshProbe> Meshes = new List<MeshProbe>();
            public readonly List<NozzleLip> Lips = new List<NozzleLip>();
            public readonly List<Quaternion> FitRotations = new List<Quaternion>();
            public float NextFit;
            public float NextFitLog;
            public List<object> HiddenControllers = new List<object>();
            public List<GameObject> HiddenParticles = new List<GameObject>();
            public bool VisualsHidden;
            public readonly Dictionary<object, bool> ControllerOverrides = new Dictionary<object, bool>();
            public readonly Dictionary<object, float> ControllerValues = new Dictionary<object, float>();
            public readonly Dictionary<GameObject, bool> ParticleActive = new Dictionary<GameObject, bool>();
        }

        class MeshProbe
        {
            public Renderer Renderer;
            public Mesh Mesh, Baked;
            public readonly List<Vector3> Vertices = new List<Vector3>();
        }

        readonly Dictionary<Part, Nozzle> nozzles = new Dictionary<Part, Nozzle>();
        readonly AfterburnerProfile profile = AfterburnerLook.Profile(), look = AfterburnerLook.Profile();
        AfterburnerDraw draw;
        float nextScan, time;
        // How blue the flame is now: eased toward the setting, so a switch fades over about half a second.
        double blue;
        bool hooked;

        void Start()
        {
            Shader shader = PlumeShaders.Plume;
            if (shader == null) { Debug.Log("[AFTERBURNER] no shader, plumes off"); enabled = false; return; }
            draw = new AfterburnerDraw(shader);
            Camera.onPreCull += OnPreCull;
            hooked = true;
        }

        void Update()
        {
            time += Time.deltaTime;
            if (Time.time < nextScan) return;
            nextScan = Time.time + ScanSeconds;
            bool on = AfterburnerSettings.On;
            var seen = new HashSet<Part>();
            if (on && FlightGlobals.Vessels != null)
            {
                Vector3 camera = FlightCamera.fetch != null ? FlightCamera.fetch.transform.position : Vector3.zero;
                foreach (Vessel v in FlightGlobals.Vessels)
                {
                    if (v == null || !v.loaded || v.packed || (v.CoMD - (Vector3d)camera).sqrMagnitude > RangeM * RangeM) continue;
                    foreach (Part p in v.parts)
                    {
                        if (!nozzles.ContainsKey(p))
                        {
                            Nozzle n = Find(p);
                            if (n == null) continue;
                            nozzles[p] = n;
                        }
                        seen.Add(p);
                    }
                }
            }
            // Whatever the effect switches back on, off again.
            foreach (Nozzle n in nozzles.Values)
            {
                if (WetMode(n) && !n.VisualsHidden) Hide(n);
                else if (!WetMode(n) && n.VisualsHidden) RestoreEffects(n);
                foreach (GameObject g in n.HiddenParticles) if (g != null && g.activeSelf) g.SetActive(false);
            }
            var gone = new List<Part>();
            foreach (KeyValuePair<Part, Nozzle> kv in nozzles) if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (Part p in gone) { Release(nozzles[p]); nozzles.Remove(p); }
        }

        // The part's afterburning engine: a multi-mode engine's mode named wet, afterburner or reheat.
        Nozzle Find(Part p)
        {
            if (p == null) return null;
            MultiModeEngine modes = p.FindModuleImplementing<MultiModeEngine>();
            foreach (ModuleEnginesFX e in p.FindModulesImplementing<ModuleEnginesFX>())
            {
                string id = (e.engineID ?? "").ToLowerInvariant();
                if (!IsWet(id)) continue;
                if (e.thrustTransforms == null || e.thrustTransforms.Count == 0) continue;
                var n = new Nozzle
                {
                    Part = p, Wet = e, Modes = modes,
                    Params = AfterburnerLook.Params(NozzleShare * PartRadius(p, e.thrustTransforms[0].forward)),
                    Seed = (p.flightID % 1000) * 0.731f,
                };
                foreach (Renderer renderer in p.GetComponentsInChildren<Renderer>())
                {
                    if (renderer.GetComponentInParent<Part>() != p || !renderer.enabled || renderer.sharedMaterial == null) continue;
                    if (renderer.sharedMaterial.shader != null && renderer.sharedMaterial.shader.name.StartsWith("Waterfall")) continue;
                    var skin = renderer as SkinnedMeshRenderer;
                    Mesh mesh = skin != null ? skin.sharedMesh : renderer is MeshRenderer && renderer.GetComponent<MeshFilter>() != null ? renderer.GetComponent<MeshFilter>().sharedMesh : null;
                    if (mesh == null) continue;
                    var probe = new MeshProbe { Renderer = renderer, Mesh = mesh, Baked = skin != null ? new Mesh() : null };
                    if (skin == null) mesh.GetVertices(probe.Vertices);
                    n.Meshes.Add(probe);
                }
                for (int i = 0; i < e.thrustTransforms.Count; i++)
                {
                    n.Lips.Add(new NozzleLip { RadiusUp = n.Params.NozzleRadiusM, RadiusSide = n.Params.NozzleRadiusM });
                    n.FitRotations.Add(Quaternion.identity);
                }
                if (WetMode(n)) Hide(n);
                Debug.Log(string.Format("[AFTERBURNER] {0} ({1}), nozzle {2:F2} m, hid {3} Waterfall controller(s), {4} stock emitter(s)",
                    p.partInfo != null ? p.partInfo.title : p.name, e.engineID, n.Params.NozzleRadiusM, n.HiddenControllers.Count, n.HiddenParticles.Count));
                return n;
            }
            return null;
        }

        // Half the part's width across its thrust axis, in world metres, from its own meshes, skinned
        // ones included (an animated nozzle is skinned), and not effect meshes such as Waterfall's plumes.
        static float PartRadius(Part p, Vector3 axis)
        {
            float radius = 0f;
            Vector3 centre = p.transform.position;
            foreach (Renderer r in p.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || r.sharedMaterial == null) continue;
                if (r.sharedMaterial.shader != null && r.sharedMaterial.shader.name.StartsWith("Waterfall")) continue;
                Mesh mesh = r is SkinnedMeshRenderer ? ((SkinnedMeshRenderer)r).sharedMesh
                          : r is MeshRenderer && r.GetComponent<MeshFilter>() != null ? r.GetComponent<MeshFilter>().sharedMesh : null;
                if (mesh == null) continue;
                Bounds b = mesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((c & 1) != 0 ? 1 : -1, (c & 2) != 0 ? 1 : -1, (c & 4) != 0 ? 1 : -1));
                    Vector3 offset = r.transform.TransformPoint(corner) - centre;
                    radius = Mathf.Max(radius, Vector3.ProjectOnPlane(offset, axis).magnitude);
                }
            }
            // A box's corner stands out √2 past a round part's radius.
            radius /= Mathf.Sqrt(2f);
            if (radius < 0.05f) Debug.Log("[AFTERBURNER] no mesh measured on " + p.name + ", nozzle assumed for 1.25 m");
            return radius >= 0.05f ? radius : 0.625f;
        }

        // The wet engine's own visuals: Waterfall modules driven by it held at zero, stock particle emitters off.
        static void Hide(Nozzle n)
        {
            n.VisualsHidden = true;
            foreach (PartModule m in n.Part.Modules)
            {
                if (m.moduleName != "ModuleWaterfallFX") continue;
                var controllers = Member(m, "Controllers") as IEnumerable;
                if (controllers == null) continue;
                bool wet = Same(Member(m, "engineID"), n.Wet.engineID);
                foreach (object c in controllers) wet |= c != null && Same(Member(c, "engineID"), n.Wet.engineID);
                if (!wet) continue;
                foreach (object c in controllers)
                {
                    if (c == null || !c.GetType().Name.Contains("Throttle")) continue;
                    n.ControllerOverrides[c] = Member(c, "overridden") is bool && (bool)Member(c, "overridden");
                    n.ControllerValues[c] = Member(c, "overrideValue") is float ? (float)Member(c, "overrideValue") : 0;
                    Set(c, "overrideValue", 0f);
                    Set(c, "overridden", true);
                    n.HiddenControllers.Add(c);
                }
            }
            var groups = Member(n.Part.Effects, "effectList") as IDictionary;
            if (groups == null) return;
            foreach (string name in new[] { n.Wet.runningEffectName, n.Wet.powerEffectName })
            {
                if (string.IsNullOrEmpty(name) || !groups.Contains(name)) continue;
                // The effect's own emitter objects (under the part's smokePoint, which its dry effects
                // share, so that one stays on).
                foreach (EffectBehaviour fx in (IEnumerable)groups[name])
                {
                    var emitters = new List<Component>();
                    if (fx is ModelMultiParticleFX)
                    {
                        var list = Member(fx, "emitters") as IEnumerable;
                        if (list != null) foreach (object o in list) emitters.Add(o as Component);
                    }
                    else if (fx is ModelParticleFX || fx is PrefabParticleFX) emitters.Add(Member(fx, "emitter") as Component);
                    foreach (Component c in emitters)
                        if (c != null && !n.HiddenParticles.Contains(c.gameObject)) { n.ParticleActive[c.gameObject] = c.gameObject.activeSelf; c.gameObject.SetActive(false); n.HiddenParticles.Add(c.gameObject); }
                }
            }
        }

        static bool Same(object id, string engineID) { return string.Equals(id as string, engineID, StringComparison.OrdinalIgnoreCase); }
        public static bool IsWet(string id) { id = (id ?? "").ToLowerInvariant(); return id.StartsWith("wet") || id.Contains("afterburn") || id.Contains("reheat"); }

        static void Release(Nozzle n)
        {
            RestoreEffects(n);
            foreach (MeshProbe probe in n.Meshes) if (probe.Baked != null) UnityEngine.Object.Destroy(probe.Baked);
        }

        static void RestoreEffects(Nozzle n)
        {
            foreach (object c in n.HiddenControllers) { Set(c, "overrideValue", n.ControllerValues[c]); Set(c, "overridden", n.ControllerOverrides[c]); }
            foreach (GameObject g in n.HiddenParticles) if (g != null) g.SetActive(n.ParticleActive[g]);
            n.HiddenControllers.Clear();
            n.HiddenParticles.Clear();
            n.ControllerOverrides.Clear(); n.ControllerValues.Clear(); n.ParticleActive.Clear();
            n.VisualsHidden = false;
        }

        static bool WetMode(Nozzle n)
        {
            if (!n.Wet.EngineIgnited) return false;
            return n.Modes == null || Same(n.Modes.runningPrimary ? n.Modes.primaryEngineID : n.Modes.secondaryEngineID, n.Wet.engineID);
        }

        static void FitLips(Nozzle n)
        {
            if (Time.time < n.NextFit)
            {
                bool moved = false;
                for (int i = 0; i < n.Wet.thrustTransforms.Count; i++)
                    if (n.Wet.thrustTransforms[i] != null && Quaternion.Angle(n.FitRotations[i], Quaternion.Inverse(n.Part.transform.rotation) * n.Wet.thrustTransforms[i].rotation) > 0.05f) moved = true;
                if (!moved) return;
            }
            n.NextFit = Time.time + 0.05f;
            foreach (MeshProbe probe in n.Meshes)
            {
                var skin = probe.Renderer as SkinnedMeshRenderer;
                if (skin == null || probe.Baked == null) continue;
                skin.BakeMesh(probe.Baked);
                probe.Baked.GetVertices(probe.Vertices);
            }
            for (int i = 0; i < n.Wet.thrustTransforms.Count; i++)
            {
                Transform t = n.Wet.thrustTransforms[i];
                if (t == null) continue;
                n.FitRotations[i] = Quaternion.Inverse(n.Part.transform.rotation) * t.rotation;
                n.Points.Clear();
                Vector3 side = Vector3.Cross(t.up, t.forward).normalized;
                foreach (MeshProbe probe in n.Meshes)
                {
                    if (probe.Renderer == null) continue;
                    foreach (Vector3 vertex in probe.Vertices)
                    {
                        Transform meshFrame = probe.Renderer.transform;
                        Vector3 world = probe.Baked != null ? meshFrame.position + meshFrame.rotation * vertex : meshFrame.TransformPoint(vertex);
                        Vector3 offset = world - t.position;
                        n.Points.Add(new NozzlePoint { Aft = Vector3.Dot(offset, t.forward), Up = Vector3.Dot(offset, t.up), Side = Vector3.Dot(offset, side) });
                    }
                }
                n.Lips[i] = NozzleLip.Fit(n.Points, n.Params.NozzleRadiusM);
                if (AfterburnerSettings.Verbose && Time.time >= n.NextFitLog)
                {
                    NozzleLip lip = n.Lips[i];
                    Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture, "[AFTERBURNER] nozzle lip: {0}, {1}, exit {2:F3} m, radii {3:F3}/{4:F3} m, measured {5}, wet {6}, center {7:F3}/{8:F3}, slope {9:F3}/{10:F3}", n.Part.partInfo.title, i, lip.Aft, lip.RadiusUp, lip.RadiusSide, lip.Measured, WetMode(n), lip.Up, lip.Side, lip.SlopeUp, lip.SlopeSide));
                }
            }
            if (Time.time >= n.NextFitLog) n.NextFitLog = Time.time + 1f;
        }

        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        static object Member(object o, string name)
        {
            if (o == null) return null;
            for (Type t = o.GetType(); t != null; t = t.BaseType)
            {
                PropertyInfo p = t.GetProperty(name, Any | BindingFlags.DeclaredOnly);
                if (p != null && p.GetIndexParameters().Length == 0) return p.GetValue(o, null);
                FieldInfo f = t.GetField(name, Any | BindingFlags.DeclaredOnly);
                if (f != null) return f.GetValue(o);
            }
            return null;
        }

        static void Set(object o, string name, object value)
        {
            for (Type t = o.GetType(); t != null; t = t.BaseType)
            {
                PropertyInfo p = t.GetProperty(name, Any | BindingFlags.DeclaredOnly);
                if (p != null && p.CanWrite) { p.SetValue(o, value, null); return; }
                FieldInfo f = t.GetField(name, Any | BindingFlags.DeclaredOnly);
                if (f != null) { f.SetValue(o, value); return; }
            }
        }

        void OnPreCull(Camera cam)
        {
            Camera flight = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
            if (cam != flight || draw == null || nozzles.Count == 0) return;
            bool depth = false;
            blue += ((AfterburnerSettings.Blue ? 1 : 0) - blue) * (1 - Math.Exp(-Time.deltaTime / 0.18));
            profile.Blue = blue;
            draw.WriteProfile(profile, time);
            foreach (Nozzle n in nozzles.Values)
            {
                ModuleEnginesFX e = n.Wet;
                if (n.Part == null || e == null) continue;
                // What the engine is making (its thrust over its maximum), spool-up and all, not the throttle.
                bool wetMode = WetMode(n);
                bool running = wetMode && !e.flameout;
                double output = running ? Math.Min(e.finalThrust / Math.Max(e.maxThrust, 1e-3f), 1f) : 0;
                n.Output += ((float)output - n.Output) * (1f - Mathf.Exp(-Time.deltaTime / OutputEaseS));
                double target = running ? AfterburnerLook.BurnerFor(output) : 0;
                double dt = Time.deltaTime;
                double rate = target < n.Burner ? (wetMode ? GoOut : ModeSwitchOut) : n.Burner < AfterburnerJet.ReheatFirstZone ? LightFirstZone : LightStaging;
                double openTarget = wetMode ? 1 : 0;
                n.Open = openTarget > n.Open ? Math.Min(openTarget, n.Open + NozzleOpenRate * dt) : Math.Max(openTarget, n.Open - NozzleCloseRate * dt);
                n.Burner = target > n.Burner ? Math.Min(target, n.Burner + rate * dt) : Math.Max(target, n.Burner - rate * dt);
                n.Surge += ((Math.Abs(target - n.Burner) > 0.02 ? 1f : 0f) - n.Surge) * (1f - Mathf.Exp(-(float)dt / SurgeEaseS));
                if (n.Burner >= 0.01 || AfterburnerSettings.Verbose) FitLips(n);
                if (n.Burner < 0.01) continue;
                Vessel v = n.Part.vessel;
                if (v == null) continue;
                profile.AltitudeM = v.altitude;
                profile.AirspeedMPerS = Math.Min(v.srfSpeed, AfterburnerLook.SpeedLimitMPerS);
                double length = AfterburnerLook.LengthShare(v.srfSpeed);
                profile.MinLengthD = look.MinLengthD * length * AfterburnerSettings.LengthScale;
                profile.MaxLengthD = look.MaxLengthD * length * AfterburnerSettings.LengthScale;
                for (int i = 0; i < e.thrustTransforms.Count; i++)
                {
                    Transform t = e.thrustTransforms[i];
                    if (t == null) continue;
                    NozzleLip lip = n.Lips[i];
                    // A round plume at most the exit's size: a gimballed nozzle's fit is lopsided and an expanded jet balloons past the lip.
                    double exitRadius = lip.Measured ? Math.Max(lip.RadiusUp, lip.RadiusSide) : lip.Radius;
                    var measured = AfterburnerLook.Params(exitRadius);
                    double opening = lip.Measured ? 1 : ClosedShare + (1 - ClosedShare) * n.Open * (0.75 + 0.25 * n.Output);
                    double throttle = AfterburnerJet.ThrottleMax * n.Output;
                    JetState s = AfterburnerJet.State(measured, throttle, profile, opening, n.Burner);
                    double limit = exitRadius * ExitFill;
                    if (s.RadiusM > limit) s = AfterburnerJet.State(measured, throttle, profile, opening * limit / s.RadiusM, n.Burner);
                    Vector3 side = Vector3.Cross(t.up, t.forward).normalized;
                    double inset = lip.Measured ? exitRadius * (Math.Sqrt(lip.SlopeUp * lip.SlopeUp + lip.SlopeSide * lip.SlopeSide) + 0.04) : 0;
                    Vector3 exit = t.position + t.forward * (float)(lip.Aft - inset) + t.up * (float)lip.Up + side * (float)lip.Side;
                    Vector3 shape = Vector3.one;
                    // The exhaust leaves along the thrust transform's forward; the plume's +x.
                    Quaternion aft = Quaternion.LookRotation(t.forward, t.up) * Quaternion.Euler(0, -90, 0);
                    if (draw.Draw(cam, n.Part.gameObject.layer, exit, aft, measured, s, n.Seed + i * 3.1f, n.Surge, shape)) depth = true;
                }
            }
            // The march stops at the craft's own surfaces.
            if (depth && (cam.depthTextureMode & DepthTextureMode.Depth) == 0) cam.depthTextureMode |= DepthTextureMode.Depth;
        }

        void OnDestroy()
        {
            if (hooked) Camera.onPreCull -= OnPreCull;
            foreach (Nozzle n in nozzles.Values) Release(n);
            nozzles.Clear();
            if (draw != null) draw.Destroy(false);
        }
    }
}
