// Ported from aeronautic (github.com/Romainlg29/aeronautic @ 673b967), packages/afterburner/src/
// afterburner-batch.ts, afterburner-material.ts (vertex stage) and plume-hull.ts.
// MIT License, (c) 2026 Romain Le Gall; see THIRD-PARTY-NOTICES.md.
using System;
using UnityEngine;

namespace AeronauticAfterburner
{
    // The look KSP draws the plume with (docs/afterburner.md), shared with the offline harness.
    public static class AfterburnerLook
    {
        // The plume reads the airspeed only up to this: faster, the ram swells the jet without limit.
        public const double SpeedLimitMPerS = 150;

        // Tuned upstream turbulent profile; steady flutter is a KSP visual departure.
        public static AfterburnerProfile Profile()
        {
            return new AfterburnerProfile
            {
                ShockRelaxation = 4.5, MaxCoflow = 0.35, ShockCells = 5.5, MinLengthD = 3, MaxLengthD = 14, Exposure = 40, SpreadScale = 0.6,
                BarrelBow = 0.18, FanCooling = 0.12, DiamondPower = 2, SteadyFlutter = 0.25,
            };
        }

        // The burner lit for an engine's output (its thrust over its maximum, 0 to 1): the first zone
        // at the least, so a barely running wet engine still shows a short flame, and no diamonds.
        public static double BurnerFor(double output)
        {
            return output <= 0.005 ? 0 : 0.25 + 0.75 * Math.Min(output, 1);
        }

        // How much of the longest flame the airspeed allows: half at standstill, all of it from
        // FullLengthMPerS.
        public const double FullLengthMPerS = 300, StandstillLength = 0.5;
        public static double LengthShare(double speedMPerS)
        {
            double t = Math.Min(Math.Max(speedMPerS / FullLengthMPerS, 0), 1);
            return StandstillLength + (1 - StandstillLength) * t * t * (3 - 2 * t);
        }

        // "afterburner_turbulent": the tail breaks up into the air. A more underexpanded nozzle and
        // less sheath than upstream's: the body fades and the diamonds outlast it. Soot and band
        // emission are per metre of gas, so they go as the reference nozzle's radius over this one's:
        // a bigger engine's flame is as thick (and as orange) as a small one's.
        public const double ReferenceRadiusM = 0.35;
        public static AfterburnerParams Params(double nozzleRadiusM)
        {
            double thick = Math.Min(Math.Max(ReferenceRadiusM / Math.Max(nozzleRadiusM, 0.05), 0.4), 1.5);
            // BandStrength: the blue molecular bands, kept faint so the flame reads orange to white, not magenta.
            var p = new AfterburnerParams { NozzleRadiusM = nozzleRadiusM, Turbulence = 0.8, Meander = 0.12, PressureRatio = 1.7, AfterburningK = 150, BandStrength = 0.03 };
            p.SootPerM *= thick;
            p.BandStrength *= thick;
            return p;
        }
    }

    // Draws afterburner plumes with AeronauticAfterburner/Plume: one hull mesh, one material, a
    // property block per nozzle. Object frame: the nozzle exit at the origin, +x down the plume.
    public class AfterburnerDraw
    {
        public const float DetailDistanceM = 900f, CheapDistanceM = 3000f, MinScreenFraction = 0.001f;
        const int HullSides = 10, Octaves = 2, NearSteps = 64, MidSteps = 24;
        const float StepArea = 0.3f;

        static readonly int JetId = Shader.PropertyToID("_Jet"), HeatId = Shader.PropertyToID("_Heat"),
            CellId = Shader.PropertyToID("_Cell"), OpticsId = Shader.PropertyToID("_Optics"),
            ExitId = Shader.PropertyToID("_Exit"), BandId = Shader.PropertyToID("_Band"),
            MiscId = Shader.PropertyToID("_Misc"), OuterId = Shader.PropertyToID("_Outer"),
            GlowId = Shader.PropertyToID("_Glow"), HullId = Shader.PropertyToID("_Hull"),
            AmbientId = Shader.PropertyToID("_AmbientK");

        public readonly Material Material;
        readonly Mesh hull;
        readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        static Texture2D blackbody;
        static Texture3D noise;

        public AfterburnerDraw(Shader shader)
        {
            Material = new Material(shader) { name = "Afterburner" };
            Material.SetTexture("_Blackbody", BlackbodyTexture());
            Material.SetTexture("_Noise", NoiseTexture());
            Material.SetFloat("_NearSteps", NearSteps);
            Material.SetFloat("_MidSteps", MidSteps);
            Material.SetFloat("_GammaOut", QualitySettings.activeColorSpace == ColorSpace.Gamma ? 1 : 0);
            hull = Hull(HullSides);
        }

        // The profile's camera and air dials that every nozzle shares.
        public void WriteProfile(AfterburnerProfile p, float time)
        {
            Material.SetFloat("_Exposure", (float)p.Exposure);
            Material.SetFloat("_SkyLight", (float)p.SkyLight);
            Material.SetFloat("_ShockRelaxation", (float)p.ShockRelaxation);
            Material.SetFloat("_ActivationK", (float)p.ActivationK);
            Material.SetFloat("_EddyStretch", (float)p.EddyStretch);
            Material.SetFloat("_ShutterS", (float)p.ShutterS);
            Material.SetFloat("_MeanderScale", (float)p.MeanderScale);
            Material.SetFloat("_BarrelBow", (float)p.BarrelBow);
            Material.SetFloat("_FanCooling", (float)p.FanCooling);
            Material.SetFloat("_SteadyFlutter", (float)p.SteadyFlutter);
            Material.SetFloat("_StepSafety", (float)p.StepSafety);
            Material.SetFloat("_DitherScale", (float)p.DitherScale);
            Material.SetFloat("_PlumeTime", time);
            Material.SetFloat("_Blue", (float)p.Blue);
        }

        // One nozzle from one camera; false when it is not drawn (culled or off screen).
        // surge 0 to 1: how much the burner is changing (the sheath flows aft, the diamonds shiver).
        public bool Draw(Camera camera, int layer, Vector3 exit, Quaternion aft, AfterburnerParams p, JetState s, float seed, float surge = 0, Vector3? shape = null)
        {
            if (p.NozzleRadiusM <= 0 || s.ReachM <= 0) return false;
            Vector3 toCamera = camera.transform.position - exit;
            float distance = toCamera.magnitude;
            float screenScale = camera.orthographic ? 1f : 2f * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            if (s.ReachM < distance * screenScale * MinScreenFraction) return false;
            float lod = distance > CheapDistanceM ? 2 : distance > DetailDistanceM ? 1 : 0;

            // Steps shared out by how much of the screen the plume covers, from the nearest point of its axis.
            Vector3 local = Quaternion.Inverse(aft) * toCamera;
            float along = Mathf.Clamp(local.x, 0, (float)s.ReachM);
            float nearest = new Vector3(local.x - along, local.y, local.z).magnitude;
            float viewHeight = Mathf.Max(nearest, 1e-3f) * screenScale;
            float covered = (float)(s.ReachM * (s.OuterNearM + s.OuterFarM)) / (viewHeight * viewHeight);
            float budget = Mathf.Clamp01(Mathf.Sqrt(StepArea / Mathf.Max(covered, 1e-6f)));

            double bandScale = 1 + (s.Burner - 1) * s.Breathes;
            block.Clear();
            block.SetVector(JetId, V(s.RadiusM, s.CoreLengthM, s.ReachM, s.VelocityMPerS));
            block.SetVector(HeatId, V(s.TemperatureK, s.AfterburningK + s.KineticK, s.ShockHeat, s.ShockLengthM));
            block.SetVector(CellId, V(s.ShockSpacingM, s.FirstDisk, s.SpreadNear, s.SpreadFar));
            block.SetVector(OpticsId, V(p.SootPerM * s.SootFormed, p.SootSurvival, p.ParticlesPerM, p.ParticleAlbedo));
            block.SetVector(ExitId, V(s.LipTemperatureK, s.Compression, s.SootSurvival, s.Adaptation));
            block.SetVector(BandId, V(p.BandR, p.BandG, p.BandB, p.BandStrength * bandScale));
            block.SetVector(MiscId, V(p.Turbulence, p.Meander, p.RefractionM, seed));
            block.SetVector(OuterId, V(s.OuterFarM, s.Reheat, budget, s.Thinning));
            block.SetVector(GlowId, V(s.Glow, p.NozzleRadiusM * s.NozzleScale, s.GlowTemperatureK, s.PipeFlame));
            block.SetVector(HullId, V(s.OuterNearM, lod, Octaves, surge));
            block.SetFloat(AmbientId, (float)s.AmbientK);

            Matrix4x4 objectToWorld = Matrix4x4.TRS(exit, aft, shape ?? Vector3.one);
            Graphics.DrawMesh(hull, objectToWorld, Material, layer, camera, 0, block, false, false);
            return true;
        }

        static Vector4 V(double x, double y, double z, double w) { return new Vector4((float)x, (float)y, (float)z, (float)w); }

        // plume-hull.ts: a prism from x 0 to 1 circumscribing the unit circle, caps included, wound
        // outward; Cull Front keeps the far surface, so the camera may stand inside.
        static Mesh Hull(int sides)
        {
            float margin = 1f / Mathf.Cos(Mathf.PI / sides);
            var vertices = new Vector3[2 + sides * 2];
            vertices[0] = Vector3.zero;
            vertices[1] = Vector3.right;
            for (int i = 0; i < sides; i++)
            {
                float angle = i * Mathf.PI * 2 / sides;
                float y = Mathf.Cos(angle) * margin, z = Mathf.Sin(angle) * margin;
                vertices[2 + i] = new Vector3(0, y, z);
                vertices[2 + sides + i] = new Vector3(1, y, z);
            }
            var triangles = new int[sides * 12];
            for (int i = 0; i < sides; i++)
            {
                int next = (i + 1) % sides, near = 2 + i, nearNext = 2 + next, far = 2 + sides + i, farNext = 2 + sides + next, o = i * 12;
                triangles[o] = near; triangles[o + 1] = farNext; triangles[o + 2] = far;
                triangles[o + 3] = near; triangles[o + 4] = nearNext; triangles[o + 5] = farNext;
                triangles[o + 6] = 0; triangles[o + 7] = nearNext; triangles[o + 8] = near;
                triangles[o + 9] = 1; triangles[o + 10] = far; triangles[o + 11] = farNext;
            }
            var mesh = new Mesh { name = "AfterburnerHull", vertices = vertices, triangles = triangles };
            // Drawn wherever the plume goes; the vertex stage opens it far past these bounds.
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e5f);
            return mesh;
        }

        static Texture2D BlackbodyTexture()
        {
            if (blackbody != null) return blackbody;
            float[] lut = Blackbody.Lut();
            blackbody = new Texture2D(Blackbody.Texels, 1, TextureFormat.RGBAFloat, false, true)
            {
                name = "AfterburnerBlackbody", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color[Blackbody.Texels];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(lut[i * 4], lut[i * 4 + 1], lut[i * 4 + 2], lut[i * 4 + 3]);
            blackbody.SetPixels(pixels);
            blackbody.Apply(false, true);
            return blackbody;
        }

        static Texture3D NoiseTexture()
        {
            if (noise != null) return noise;
            byte[] red = NoiseVolume.Build(0), green = NoiseVolume.Build(1);
            int n = NoiseVolume.Texels;
            noise = new Texture3D(n, n, n, TextureFormat.RGBA32, false)
            {
                name = "AfterburnerNoise", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[red.Length];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(red[i], green[i], 0, 255);
            noise.SetPixels32(pixels);
            noise.Apply(false, true);
            return noise;
        }

        public void Destroy(bool immediate)
        {
            Action<UnityEngine.Object> kill = o => { if (o == null) return; if (immediate) UnityEngine.Object.DestroyImmediate(o); else UnityEngine.Object.Destroy(o); };
            kill(Material);
            kill(hull);
        }
    }
}
