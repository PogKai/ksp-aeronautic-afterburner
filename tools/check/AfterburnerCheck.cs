// Checks the plume model (Source/PlumeModel.cs) against afterburner-ref.json,
// written by afterburner-ref.ts from upstream's TypeScript.
using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using AeronauticAfterburner;

static class AfterburnerCheck
{
    // The jet at full reheat over speed and height as KSP's plume profile sets it: radius, length, shock cells.
    public static void Sweep(double nozzleM, double coflow, double maxD, double limit, double cells)
    {
        Console.WriteLine("nozzle {0} m: alt, speed (Mach) -> radius x nozzle, reach D, core D, spacing D, cells to 20% swing", nozzleM);
        foreach (double alt in new[] { 300.0, 3000, 8000, 12000 })
            foreach (double speed in new[] { 0.0, 150, 250, 340, 450, 600, 800, 1100 })
            {
                var p = new AfterburnerParams { NozzleRadiusM = nozzleM, Turbulence = 0.8, Meander = 0.12 };
                var prof = new AfterburnerProfile { AltitudeM = alt, AirspeedMPerS = Math.Min(speed, limit), ShockRelaxation = 4.5, MaxCoflow = coflow, ShockCells = cells, MaxLengthD = maxD };
                JetState s = AfterburnerJet.State(p, 1.1, prof);
                double d = 2 * nozzleM;
                double sound = Atmosphere.SpeedOfSound(s.AmbientK);
                Console.WriteLine("{0,6:F0} m {1,5:F0} m/s (M {2:F2}): radius x{3:F2} reach {4:F1} D core {5:F1} D spacing {6:F2} D shock length {7:F1} D, cells {8:F1}",
                    alt, speed, speed / sound, s.RadiusM / nozzleM, s.ReachM / d, s.CoreLengthM / d, s.ShockSpacingM / d, s.ShockLengthM / d, 1.6 * s.ShockLengthM / Math.Max(s.ShockSpacingM, 1e-6));
            }
    }

    public static int Run(string path)
    {
        int failures = 0, values = 0;
        double worst = 0;
        Action<string, double, double> check = (what, expected, actual) =>
        {
            values++;
            double error = Math.Abs(expected - actual) / Math.Max(Math.Abs(expected), 1e-9);
            if (Math.Abs(expected - actual) < 1e-12) error = 0;
            worst = Math.Max(worst, error);
            double tolerance = what.StartsWith("lum ") ? 1e-6 : 1e-9;
            if (error > tolerance && failures++ < 20) Console.WriteLine("  {0}: expected {1}, got {2}", what, expected, actual);
        };
        using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path)))
        {
            JsonElement root = doc.RootElement;
            int index = 0;
            foreach (JsonElement c in root.GetProperty("cases").EnumerateArray())
            {
                var p = new AfterburnerParams { NozzleRadiusM = c.GetProperty("nozzleRadiusM").GetDouble() };
                var prof = new AfterburnerProfile { AltitudeM = c.GetProperty("altitudeM").GetDouble(), AirspeedMPerS = c.GetProperty("airspeedMPerS").GetDouble() };
                JetState s = AfterburnerJet.State(p, c.GetProperty("throttle").GetDouble(), prof);
                object boxed = s;
                foreach (JsonProperty field in c.GetProperty("state").EnumerateObject())
                {
                    string name = char.ToUpperInvariant(field.Name[0]) + field.Name.Substring(1);
                    FieldInfo f = typeof(JetState).GetField(name);
                    if (f == null) { Console.WriteLine("  no field " + name); failures++; continue; }
                    check("case " + index + " " + name, field.Value.GetDouble(), (double)f.GetValue(boxed));
                }
                index++;
            }
            float[] lut = Blackbody.Lut();
            foreach (JsonElement e in root.GetProperty("lut").EnumerateArray())
            {
                double k = e.GetProperty("k").GetDouble();
                double texel = (k - Blackbody.MinK) / (Blackbody.MaxK - Blackbody.MinK) * (Blackbody.Texels - 1);
                if (Math.Abs(texel - Math.Round(texel)) > 1e-9) continue;
                int i = (int)Math.Round(texel);
                double lum = Math.Pow(2, lut[i * 4 + 3]);
                check("lum " + k, e.GetProperty("lum").GetDouble(), lum);
            }
            string[] channels = { "red", "green" };
            for (int ch = 0; ch < 2; ch++)
            {
                byte[] b = NoiseVolume.Build(ch);
                int i = 0;
                foreach (JsonElement e in root.GetProperty(channels[ch]).EnumerateArray())
                {
                    check("noise " + ch + "/" + i, e.GetDouble(), b[i * 4099 % b.Length]);
                    i++;
                }
            }
        }
        Console.WriteLine("afterburner: {0} values, {1} failures, worst relative error {2:E2}", values, failures, worst);
        return failures;
    }
}
