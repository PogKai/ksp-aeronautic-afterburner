using System;
using System.Collections.Generic;

namespace AeronauticAfterburner
{
    public struct NozzlePoint { public double Aft, Up, Side; }

    public struct NozzleLip
    {
        public double Aft, Up, Side, RadiusUp, RadiusSide, SlopeUp, SlopeSide;
        public bool Measured;
        public double Radius { get { return Math.Sqrt(RadiusUp * RadiusSide); } }

        public static NozzleLip Fit(IList<NozzlePoint> points, double fallbackRadius)
        {
            var result = new NozzleLip { RadiusUp = fallbackRadius, RadiusSide = fallbackRadius };
            double radialLimit = Math.Max(fallbackRadius * 2.5, 0.2);
            const int sectors = 32;
            var rim = new NozzlePoint[sectors];
            var occupied = new bool[sectors];
            foreach (NozzlePoint p in points)
            {
                double radial2 = p.Up * p.Up + p.Side * p.Side;
                if (radial2 > radialLimit * radialLimit || radial2 < 0.0001 || double.IsNaN(radial2) || double.IsNaN(p.Aft) || double.IsInfinity(p.Aft)) continue;
                int sector = Math.Min((int)((Math.Atan2(p.Side, p.Up) + Math.PI) * sectors / (2 * Math.PI)), sectors - 1);
                if (!occupied[sector] || p.Aft > rim[sector].Aft) { rim[sector] = p; occupied[sector] = true; }
            }
            double lowUp = double.PositiveInfinity, lowSide = lowUp, highUp = double.NegativeInfinity, highSide = highUp;
            int count = 0; double meanUp = 0, meanSide = 0, meanAft = 0;
            for (int i = 0; i < sectors; i++)
            {
                if (!occupied[i]) continue;
                NozzlePoint p = rim[i];
                lowUp = Math.Min(lowUp, p.Up); highUp = Math.Max(highUp, p.Up);
                lowSide = Math.Min(lowSide, p.Side); highSide = Math.Max(highSide, p.Side);
                meanUp += p.Up; meanSide += p.Side; meanAft += p.Aft; count++;
            }
            double up = (highUp - lowUp) * 0.5, side = (highSide - lowSide) * 0.5;
            if (count < 8 || up < 0.025 || side < 0.025 || up > radialLimit || side > radialLimit) return result;
            meanUp /= count; meanSide /= count; meanAft /= count;
            double yy = 0, zz = 0, yz = 0, xy = 0, xz = 0;
            for (int i = 0; i < sectors; i++)
            {
                if (!occupied[i]) continue;
                NozzlePoint p = rim[i]; double y = p.Up - meanUp, z = p.Side - meanSide, x = p.Aft - meanAft;
                yy += y * y; zz += z * z; yz += y * z; xy += x * y; xz += x * z;
            }
            double determinant = yy * zz - yz * yz;
            if (determinant <= 1e-12) return result;
            result.SlopeUp = (xy * zz - xz * yz) / determinant;
            result.SlopeSide = (xz * yy - xy * yz) / determinant;
            result.Up = (highUp + lowUp) * 0.5;
            result.Side = (highSide + lowSide) * 0.5;
            result.Aft = meanAft + result.SlopeUp * (result.Up - meanUp) + result.SlopeSide * (result.Side - meanSide);
            double residual = 0;
            for (int i = 0; i < sectors; i++)
                if (occupied[i]) residual = Math.Max(residual, Math.Abs(rim[i].Aft - result.Aft - result.SlopeUp * (rim[i].Up - result.Up) - result.SlopeSide * (rim[i].Side - result.Side)));
            if (residual > Math.Max(fallbackRadius * 0.15, 0.015) || result.SlopeUp * result.SlopeUp + result.SlopeSide * result.SlopeSide > 1) return new NozzleLip { RadiusUp = fallbackRadius, RadiusSide = fallbackRadius };
            double intercept = result.Aft - result.SlopeUp * result.Up - result.SlopeSide * result.Side;
            lowUp = lowSide = double.PositiveInfinity; highUp = highSide = double.NegativeInfinity;
            foreach (NozzlePoint p in points)
            {
                if (p.Up * p.Up + p.Side * p.Side > radialLimit * radialLimit || Math.Abs(p.Aft - intercept - result.SlopeUp * p.Up - result.SlopeSide * p.Side) > Math.Max(fallbackRadius * 0.04, 0.003)) continue;
                lowUp = Math.Min(lowUp, p.Up); highUp = Math.Max(highUp, p.Up);
                lowSide = Math.Min(lowSide, p.Side); highSide = Math.Max(highSide, p.Side);
            }
            up = (highUp - lowUp) * 0.5; side = (highSide - lowSide) * 0.5;
            result.Up = (highUp + lowUp) * 0.5; result.Side = (highSide + lowSide) * 0.5;
            result.Aft = intercept + result.SlopeUp * result.Up + result.SlopeSide * result.Side;
            result.RadiusUp = up * 0.97;
            result.RadiusSide = side * 0.97;
            result.Measured = true;
            return result;
        }
    }
}
