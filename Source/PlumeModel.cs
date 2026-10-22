// Ported from aeronautic (github.com/Romainlg29/aeronautic @ 673b967), packages/afterburner/src/
// types.ts, presets.ts, atmosphere.ts, plume-profile.ts, blackbody.ts, noise-volume.ts and
// packages/core/src/reheat.ts, atmosphere.ts (ram_pressure).
// MIT License, (c) 2026 Romain Le Gall; see THIRD-PARTY-NOTICES.md.
using System;

namespace AeronauticAfterburner
{
    // One engine: its nozzle and the gas leaving it, in SI units. Defaults: the "afterburner" preset.
    public class AfterburnerParams
    {
        public double NozzleRadiusM = 0.5;
        public double ExitMach = 1.45, PressureRatio = 1.35;
        public double ExitTemperatureK = 1700, DryTemperatureK = 1000;
        public double Gamma = 1.3, MolarMassGPerMol = 28.8;
        public double SootPerM = 0.5, SootSurvival = 0.03, ParticlesPerM = 0, ParticleAlbedo = 0;
        public double AfterburningK = 450;
        public double BandR = 0.152, BandG = 0.188, BandB = 1.0, BandStrength = 0.06;
        public double Turbulence = 0.12, Meander = 0.02;
        public double RefractionM = 0.05;
    }

    // The air and the camera, shared by every nozzle. Defaults: the "afterburner" preset's profile.
    public class AfterburnerProfile
    {
        public double AltitudeM, TemperatureOffsetK, AirspeedMPerS;
        public double Exposure = 120, Adaptation = 0.6, DryGlow = 0.35, SkyLight = 0.0005, SunLight = 0;
        public double VisibleTemperatureK = 900, MaxLengthD = 40;
        public double BurnerThreshold = 1, IdlePressure = 0.65, IdleTemperature = 0.4;
        public double CoreScale = 1, SpreadScale = 1;
        public double ShockFloor = 0.1, ShockPersistence = 1, ShockRelaxation = 3.2;
        public double ActivationK = 18000, EddyStretch = 2.5, ShutterS = 1.0 / 60, MeanderScale = 5;
        public double StepSafety = 0.8, DitherScale = 37;
        // Not upstream's dials. MaxCoflow: the most the air may run with the jet, as a share of its speed
        // (upstream's PLUME_MAX_COFLOW). ShockCells: shock cells visible before the train fades, from
        // the cell spacing (0: upstream's, from the jet's supersonic core).
        public double MaxCoflow = AfterburnerJet.MaxCoflow, ShockCells = 0;
        // Not upstream's (shader only): how far the barrel shock bows out mid-cell, and how much the
        // gas between the disks cools in the expansion fan; upstream's 0.35 and 0.6.
        public double BarrelBow = 0.35, FanCooling = 0.6;
        // Not upstream's: the flame's longest, in exit diameters, at the first burner zone, growing to
        // MaxLengthD as the burner lights fully (0: MaxLengthD at any burner).
        public double MinLengthD = 0;
        // Not upstream's: how fast the diamonds fade in with the burner: their strength and number go
        // as burner to this power (upstream: 1 for the strength, none for the number).
        public double DiamondPower = 1;
        public double SteadyFlutter = 0;
    }

    // Everything about one jet that is constant along it.
    public struct JetState
    {
        public double Burner, Breathes, PressureRatio, Mach, TemperatureK, VelocityMPerS, RadiusM;
        public double CoreLengthM, SpreadNear, SpreadFar;
        public double ShockSpacingM, ShockHeat, ShockLengthM, FirstDisk;
        public double AfterburningK, KineticK, Reheat, Thinning, ExitTemperatureK, LipTemperatureK, Compression;
        public double SootSurvival, SootFormed, Adaptation, Glow, PipeFlame, GlowTemperatureK;
        public double ReachM, OuterNearM, OuterFarM;
        // The air, for the shader.
        public double AmbientK, AirDensity;
        // How far the nozzle is open, as passed to State (1: the engine's own).
        public double NozzleScale;
    }

    public static class AfterburnerJet
    {
        public const double ThrottleMax = 1.1, ReheatFirstZone = 0.35, ReheatLightOff = 0.03;
        public const double FieldExtent = 2.8, EddyReach = 0.35, QuenchDensity = 0.03, SootPressureExponent = 1.5;
        public const double SootFlameLowK = 1200, SootFlameHighK = 1500, NozzleMaxMach = 2.2, MaxCoflow = 0.85;
        public const double FarSpread = 0.11, ShockDecays = 3, IdleGlow = 0.3, PhotopicK = 25900, MaxAdaptation = 1e8;
        const double AirMolarMass = 28.97, UniversalGas = 8314.46;
        // The train's swing is down to 20% (e^-1.6) this many shock lengths down.
        public const double CellsPerDecay = 1.6;

        static double Clamp(double v, double lo, double hi) { return Math.Max(lo, Math.Min(hi, v)); }

        public static double Smoothstep(double lo, double hi, double v)
        {
            double t = Clamp((v - lo) / (hi - lo), 0, 1);
            return t * t * (3 - 2 * t);
        }

        public static double RamPressure(double mach)
        {
            double m = Math.Max(mach, 0);
            double recovery = m > 1 ? 1 - 0.075 * Math.Pow(m - 1, 1.35) : 1;
            double g = Atmosphere.AirGamma;
            return Math.Pow(1 + (g - 1) / 2 * m * m, g / (g - 1)) * Math.Max(recovery, 0.2);
        }

        public static double ReheatShare(double throttle, double detent)
        {
            if (detent <= 0) return 1;
            double lit = Smoothstep(detent, detent + ReheatLightOff, throttle);
            double staged = Smoothstep(detent, Math.Max(detent + ReheatLightOff, ThrottleMax), throttle);
            return lit * (ReheatFirstZone + (1 - ReheatFirstZone) * staged);
        }

        static double SonicDistance(double excess, double jetK, double ambientK, double gammaR)
        {
            double heat = gammaR * (jetK - ambientK);
            double speed = Math.Max(excess * excess, 1);
            double share = Clamp((heat + Math.Sqrt(heat * heat + 4 * speed * gammaR * ambientK)) / (2 * speed), 0.05, 1);
            return Math.Pow(Math.Max(Math.Pow(share, -4) - 1, 0), 0.25);
        }

        public static double Extent(AfterburnerParams p)
        {
            return (FieldExtent + EddyReach * Math.Max(p.Turbulence, 0) + Math.Max(p.Meander, 0)) * 1.05;
        }

        public static double HalfWidth(double x, JetState s)
        {
            x = Math.Max(x, 0);
            return s.RadiusM + s.SpreadNear * Math.Min(x, s.CoreLengthM) + s.SpreadFar * Math.Max(x - s.CoreLengthM, 0);
        }

        // jet_state, with plume_jet's extra outputs (reheat, glow temperature). Round nozzle.
        // burnerOverride 0 to 1 sets how much of the burner is lit, in place of the throttle's (-1: the throttle's).
        public static JetState State(AfterburnerParams p, double throttle, AfterburnerProfile profile, double scale = 1, double burnerOverride = -1)
        {
            double travel = double.IsNaN(throttle) ? 0 : Clamp(throttle, 0, ThrottleMax);
            double setting = Math.Min(travel, 1);
            double burner = burnerOverride >= 0 ? Clamp(burnerOverride, 0, 1) : ReheatShare(travel, profile.BurnerThreshold);

            double stdK, stdPa;
            Atmosphere.Standard(profile.AltitudeM, out stdK, out stdPa);
            double ambientK = Math.Max(stdK + profile.TemperatureOffsetK, 1);
            double airPressure = stdPa / Atmosphere.SeaLevelPa;
            double airDensity = airPressure * Atmosphere.SeaLevelK / ambientK;
            double airSound = Atmosphere.SpeedOfSound(ambientK);
            double ram = RamPressure(Math.Max(profile.AirspeedMPerS, 0) / airSound);

            double dryK = ambientK + (p.DryTemperatureK - ambientK) * (profile.IdleTemperature + (1 - profile.IdleTemperature) * setting);
            double exitK = dryK + (p.ExitTemperatureK - dryK) * burner;
            double breathes = Smoothstep(0, 0.2, 1 - p.DryTemperatureK / Math.Max(p.ExitTemperatureK, 1));
            double ambientScale = breathes * ram + (1 - breathes) / airPressure;
            double builtRatio = Math.Max(p.PressureRatio * (profile.IdlePressure + (1 - profile.IdlePressure) * setting) * ambientScale, 0.05);

            double gamma = Math.Max(p.Gamma, 1.05);
            double k = (gamma - 1) / 2;
            double exponent = gamma / (gamma - 1);
            double exitMach = Math.Max(p.ExitMach, 0.2);
            double exitStag = 1 + k * exitMach * exitMach;
            double totalOverAmbient = Math.Pow(exitStag, exponent) * builtRatio;
            double widest = 1 + k * NozzleMaxMach * NozzleMaxMach;
            double lipStag = Math.Max(Math.Min(exitStag * Math.Pow(Math.Max(ram, 1), breathes / exponent), widest), exitStag);
            double lipK = exitK * exitStag / lipStag;
            double pressureRatio = Math.Max(totalOverAmbient / Math.Pow(lipStag, exponent), 0.05);
            double mach = Math.Max(Math.Sqrt(Math.Max(Math.Pow(totalOverAmbient, 1 / exponent) - 1, 0) / k), 0.2);
            double jetStag = 1 + k * mach * mach;
            double temperatureK = exitK * exitStag / jetStag;
            double area = exitMach / mach * Math.Pow(jetStag / exitStag, (gamma + 1) / (2 * (gamma - 1)));
            double expanded = Clamp(area, 0.25, 36);
            double radius = p.NozzleRadiusM * scale * Math.Sqrt(expanded);
            double gasConstant = UniversalGas / Math.Max(p.MolarMassGPerMol, 1);
            double sound = Math.Sqrt(gamma * gasConstant * temperatureK);
            double velocity = mach * sound;
            double exitVelocity = exitMach * Math.Sqrt(gamma * gasConstant * exitK);
            double thinning = exitVelocity / Math.Max(expanded * velocity, 1e-3);

            double airspeed = Math.Max(profile.AirspeedMPerS, 0);
            double convective = Math.Max(velocity - airspeed, 0) / (sound + airSound);
            double coflow = Clamp(airspeed / velocity, 0, profile.MaxCoflow);
            double shear = (1 - coflow) / (1 + coflow);
            double compressibility = 0.23 + 0.77 * Math.Exp(-3.4 * convective * convective);
            double density = ambientK / Math.Max(temperatureK, 1) * (p.MolarMassGPerMol / AirMolarMass);
            double diameter = radius * 2;
            double coreLength = profile.CoreScale * diameter * (4.2 + 1.1 * mach * mach) * Math.Pow(Clamp(density, 0.01, 10), 0.28) / shear;
            double spreadFar = profile.SpreadScale * FarSpread * shear;
            double spreadNear = spreadFar * compressibility;

            double supersonic = Smoothstep(1, 1.2, mach);
            double shockSpacing = 1.306 * diameter * Math.Sqrt(Math.Max(mach * mach - 1, 0.04));
            double mismatch = 1 - Math.Exp(-4 * Math.Abs(Math.Log(pressureRatio)));
            double diamonds = 1 + (Math.Pow(burner, profile.DiamondPower) - 1) * breathes;
            double strength = (profile.ShockFloor + (1 - profile.ShockFloor) * mismatch) * supersonic * diamonds;
            double shockHeat = strength * (jetStag - 1);
            double sonic = coreLength * SonicDistance(velocity - airspeed, temperatureK, ambientK, gamma * gasConstant);
            double shockLength = profile.ShockCells > 0 ? profile.ShockCells * Math.Pow(burner, profile.DiamondPower) * shockSpacing / CellsPerDecay : profile.ShockPersistence * sonic / ShockDecays;
            double normal = (gamma + 1) * mach * mach / ((gamma - 1) * mach * mach + 2);
            double compression = 1 + (normal - 1) * strength;
            double firstDisk = 0.5 + 0.3 * Smoothstep(0.9, 1.3, pressureRatio);

            double visibleExcess = Math.Max(profile.VisibleTemperatureK - ambientK, 1);
            double quench = airDensity * (1 + QuenchDensity) / (airDensity + QuenchDensity);
            double afterburning = Math.Max(p.AfterburningK * burner, 0) * quench;
            double sootSurvival = 1 + (Clamp(p.SootSurvival, 0, 1) - 1) * quench;
            double pressed = Math.Min(Math.Pow(Math.Max(airPressure * ram, 0), SootPressureExponent), 3);
            double sootFormed = 1 + (pressed * Smoothstep(SootFlameLowK, SootFlameHighK, exitK) - 1) * breathes;

            double excessSpeed = Math.Max(velocity - airspeed, 0);
            double kinetic = excessSpeed * excessSpeed * (gamma - 1) / (8 * gamma * gasConstant);
            double hottestExcess = Math.Max(temperatureK - ambientK, 0) + 4 * (afterburning + kinetic);
            double thermal = coreLength / Math.Max(visibleExcess / Math.Max(hottestExcess, 1e-3), 1e-3);
            double extinction = (Math.Max(p.SootPerM, 0) * sootFormed * sootSurvival + Math.Max(p.ParticlesPerM, 0)) * thinning;
            double smokeDepth = extinction * coreLength * spreadFar * 2.13;
            double shortest = Math.Max(8 * radius, (firstDisk + 1.5) * shockSpacing);
            double lengthD = profile.MinLengthD > 0 ? profile.MinLengthD + (profile.MaxLengthD - profile.MinLengthD) * Smoothstep(0, 1, burner) : profile.MaxLengthD;
            double longest = Math.Max(lengthD * 2 * p.NozzleRadiusM * scale, shortest);
            double reach = Clamp(Math.Max(thermal, Smoothstep(0.01, 0.06, smokeDepth) * longest), shortest, longest);

            double lighting = Math.Min(burner / ReheatFirstZone, 1);
            double glow = breathes * (1 - lighting) * (IdleGlow + (1 - IdleGlow) * setting) * Math.Max(profile.DryGlow, 0) / Math.Max(profile.Exposure, 1e-6);
            double pipeFlame = breathes * lighting * Math.Max(p.SootPerM, 0) * sootFormed;
            double adaptation = Clamp(Math.Exp(Clamp(profile.Adaptation * lighting, 0, 1) * PhotopicK
                * (1 / Math.Max(exitK, 1) - 1 / Math.Max(p.ExitTemperatureK, 1))), 1, MaxAdaptation);

            var s = new JetState
            {
                Burner = burner, Breathes = breathes, PressureRatio = pressureRatio, Mach = mach,
                TemperatureK = temperatureK, VelocityMPerS = velocity, RadiusM = radius,
                CoreLengthM = coreLength, SpreadNear = spreadNear, SpreadFar = spreadFar,
                ShockSpacingM = shockSpacing, ShockHeat = shockHeat, ShockLengthM = shockLength, FirstDisk = firstDisk,
                AfterburningK = afterburning, KineticK = kinetic, Reheat = afterburning * breathes, Thinning = thinning,
                ExitTemperatureK = exitK, LipTemperatureK = lipK, Compression = compression,
                SootSurvival = sootSurvival, SootFormed = sootFormed, Adaptation = adaptation,
                Glow = glow, PipeFlame = pipeFlame, GlowTemperatureK = exitK + (exitK * exitStag - exitK) * lighting,
                ReachM = reach, AmbientK = ambientK, AirDensity = airDensity, NozzleScale = scale,
            };
            double extent = Extent(p);
            s.OuterNearM = radius * extent;
            s.OuterFarM = HalfWidth(reach, s) * extent;
            return s;
        }
    }

    // blackbody.ts: Planck's law against the CIE 1931 observer, in linear sRGB, a luminance of one at 2000 K.
    public static class Blackbody
    {
        public const double ReferenceK = 2000, MinK = 500, MaxK = 6000;
        public const int Texels = 256;

        static double Lobe(double nm, double mean, double below, double above)
        {
            double t = (nm - mean) / (nm < mean ? below : above);
            return Math.Exp(-0.5 * t * t);
        }

        static void Raw(double k, out double r, out double g, out double b, out double lum)
        {
            double x = 0, y = 0, z = 0;
            for (int nm = 380; nm <= 780; nm += 2)
            {
                double m = nm * 1e-9;
                double radiance = 3.7418e-16 / Math.Pow(m, 5) / (Math.Exp(0.014388 / (m * k)) - 1);
                x += radiance * (1.056 * Lobe(nm, 599.8, 37.9, 31.0) + 0.362 * Lobe(nm, 442.0, 16.0, 26.7) - 0.065 * Lobe(nm, 501.1, 20.4, 26.2));
                y += radiance * (0.821 * Lobe(nm, 568.8, 46.9, 40.5) + 0.286 * Lobe(nm, 530.9, 16.3, 31.1));
                z += radiance * (1.217 * Lobe(nm, 437.0, 11.8, 36.0) + 0.681 * Lobe(nm, 459.0, 26.0, 13.8));
            }
            r = Math.Max(0, 3.2406 * x - 1.5372 * y - 0.4986 * z);
            g = Math.Max(0, -0.9689 * x + 1.8758 * y + 0.0415 * z);
            b = Math.Max(0, 0.0557 * x - 0.204 * y + 1.057 * z);
            lum = y;
        }

        // RGBA per texel: rgb over luminance, a = log2 of luminance over the reference's.
        public static float[] Lut()
        {
            double rr, rg, rb, reference;
            Raw(ReferenceK, out rr, out rg, out rb, out reference);
            var data = new float[Texels * 4];
            for (int i = 0; i < Texels; i++)
            {
                double k = MinK + (MaxK - MinK) * i / (Texels - 1);
                double r, g, b, y;
                Raw(k, out r, out g, out b, out y);
                double lum = Math.Max(y, 1e-30);
                data[i * 4] = (float)(r / lum);
                data[i * 4 + 1] = (float)(g / lum);
                data[i * 4 + 2] = (float)(b / lum);
                data[i * 4 + 3] = (float)Math.Log(lum / reference, 2);
            }
            return data;
        }
    }

    // noise-volume.ts: one tiling volume of gradient noise per channel, graded to value noise's spread.
    public static class NoiseVolume
    {
        public const int Texels = 64, Period = 16;
        static readonly uint[] Seeds = { 0x5eed1e55, 0x0dd5eed5 };

        static Func<double> Mulberry32(uint seed)
        {
            uint state = seed;
            return () =>
            {
                state += 0x6d2b79f5;
                uint t = state;
                t = (t ^ (t >> 15)) * (t | 1);
                t ^= t + (t ^ (t >> 7)) * (t | 61);
                return ((t ^ (t >> 14))) / 4294967296.0;
            };
        }

        static double Fade(double t) { return t * t * t * (t * (t * 6 - 15) + 10); }
        static double Lerp(double a, double b, double t) { return a + (b - a) * t; }

        public static byte[] Build(int channel)
        {
            int period = Period, texels = Texels;
            Func<double> random = Mulberry32(Seeds[channel]);
            var grad = new double[period * period * period * 3];
            for (int i = 0; i < period * period * period; i++)
            {
                double z = random() * 2 - 1, angle = random() * Math.PI * 2, ring = Math.Sqrt(1 - z * z);
                grad[i * 3] = ring * Math.Cos(angle);
                grad[i * 3 + 1] = ring * Math.Sin(angle);
                grad[i * 3 + 2] = z;
            }
            var raw = new double[texels * texels * texels];
            double sum = 0, squares = 0;
            for (int kk = 0; kk < texels; kk++)
                for (int j = 0; j < texels; j++)
                    for (int i = 0; i < texels; i++)
                    {
                        double x = (i + 0.5) * period / texels, y = (j + 0.5) * period / texels, z = (kk + 0.5) * period / texels;
                        int cx = (int)Math.Floor(x), cy = (int)Math.Floor(y), cz = (int)Math.Floor(z);
                        double fx = x - cx, fy = y - cy, fz = z - cz;
                        Func<int, int, int, double> corner = (a, b, c) =>
                        {
                            int index = (((cx + a) % period) * period + (cy + b) % period) * period + (cz + c) % period;
                            return grad[index * 3] * (fx - a) + grad[index * 3 + 1] * (fy - b) + grad[index * 3 + 2] * (fz - c);
                        };
                        double u = Fade(fx), v = Fade(fy), w = Fade(fz);
                        double value = Lerp(
                            Lerp(Lerp(corner(0, 0, 0), corner(1, 0, 0), u), Lerp(corner(0, 1, 0), corner(1, 1, 0), u), v),
                            Lerp(Lerp(corner(0, 0, 1), corner(1, 0, 1), u), Lerp(corner(0, 1, 1), corner(1, 1, 1), u), v), w);
                        raw[(kk * texels + j) * texels + i] = value;
                        sum += value;
                        squares += value * value;
                    }
            double mean = sum / raw.Length;
            double gain = 0.184 / Math.Sqrt(Math.Max(squares / raw.Length - mean * mean, 1e-12));
            var bytes = new byte[raw.Length];
            for (int i = 0; i < raw.Length; i++)
                bytes[i] = (byte)Math.Floor(Math.Min(Math.Max((raw[i] - mean) * gain + 0.5, 0), 1) * 255 + 0.5);
            return bytes;
        }
    }
}
