// Ported from aeronautic (github.com/Romainlg29/aeronautic @ 673b967), packages/core/src/atmosphere.ts.
// MIT License, (c) 2026 Romain Le Gall; see THIRD-PARTY-NOTICES.md.
using System;

namespace AeronauticAfterburner
{
    // The standard atmosphere the plume model reads its ambient air from.
    public static class Atmosphere
    {
        const double Hydrostatic = 0.0341632;
        public const double SeaLevelK = 288.15;
        public const double SeaLevelPa = 101325;
        public const double AirGamma = 1.4;
        public const double AirGasConstant = 287.05;

        static readonly double[,] Layers =
        {
            { 0, -0.0065 }, { 11000, 0 }, { 20000, 0.001 }, { 32000, 0.0028 },
            { 47000, 0 }, { 51000, -0.0028 }, { 71000, -0.002 }, { 86000, 0 },
        };
        const double CeilingM = 120000;

        // ISA temperature (K) and pressure (Pa) at an altitude.
        public static void Standard(double altitudeM, out double temperatureK, out double pressurePa)
        {
            double height = Math.Max(0, Math.Min(altitudeM, CeilingM));
            double t = SeaLevelK, pressure = 1;
            int n = Layers.GetLength(0);
            for (int i = 0; i < n; i++)
            {
                double baseM = Layers[i, 0], lapse = Layers[i, 1];
                double top = i + 1 < n ? Layers[i + 1, 0] : double.PositiveInfinity;
                double rise = Math.Min(height, top) - baseM;
                if (rise <= 0) break;
                double next = t + lapse * rise;
                pressure *= lapse == 0 ? Math.Exp(-Hydrostatic * rise / t) : Math.Pow(next / t, -Hydrostatic / lapse);
                t = next;
            }
            temperatureK = t;
            pressurePa = pressure * SeaLevelPa;
        }

        public static double SpeedOfSound(double temperatureK)
        {
            return Math.Sqrt(AirGamma * AirGasConstant * Math.Max(temperatureK, 1));
        }
    }
}
