// Checks the nozzle lip fit (Source/NozzleLip.cs): it follows the aperture, not the casing, and keeps a tilted rim's centre.
using System;
using AeronauticAfterburner;

static class NozzleChecks
{
    public static int Run()
    {
        int failures = 0;
        Action<string, bool> check = (name, ok) => { Console.WriteLine((ok ? "PASS " : "FAIL ") + name); if (!ok) failures++; };
        foreach (double radius in new[] { 0.1, 0.3, 0.6, 1.0 })
            foreach (double opening in new[] { 0.6, 1.0 })
            {
                var points = new System.Collections.Generic.List<NozzlePoint>();
                for (int i = 0; i < 64; i++)
                {
                    double angle = i * 2 * Math.PI / 64;
                    points.Add(new NozzlePoint { Aft = 0.2, Up = radius * opening * Math.Cos(angle), Side = radius * opening * Math.Sin(angle) });
                    points.Add(new NozzlePoint { Aft = -1, Up = radius * 1.4 * Math.Cos(angle), Side = radius * 1.4 * Math.Sin(angle) });
                }
                NozzleLip lip = NozzleLip.Fit(points, radius);
                check("lip follows aperture, not wider casing " + radius + "/" + opening, lip.Measured && Math.Abs(lip.Radius - radius * opening * 0.97) < 1e-6 && Math.Abs(lip.Aft - 0.2) < 1e-9);
            }
        check("missing lip returns explicit fallback", !NozzleLip.Fit(new NozzlePoint[0], 0.3).Measured);
        foreach (double radius in new[] { 0.1, 0.3, 0.6, 1.0 })
            foreach (double tilt in new[] { -0.35, 0.0, 0.35 })
            {
                var points = new System.Collections.Generic.List<NozzlePoint>();
                double center = 0.7, up = radius * 0.12, side = radius * -0.08;
                for (int i = 0; i < 128; i++)
                {
                    double angle = i * 2 * Math.PI / 128, y = radius * Math.Cos(angle), z = radius * Math.Sin(angle);
                    points.Add(new NozzlePoint { Aft = center + tilt * y - tilt * z, Up = up + y, Side = side + z });
                    points.Add(new NozzlePoint { Aft = center - 2 * radius, Up = up + y * 1.4, Side = side + z * 1.4 });
                }
                NozzleLip lip = NozzleLip.Fit(points, radius);
                check("tilted lip retains complete center " + radius + "/" + tilt, lip.Measured && Math.Abs(lip.Aft - center) < 0.015 * radius && Math.Abs(lip.Up - up) < 0.015 * radius && Math.Abs(lip.Side - side) < 0.015 * radius);
                check("tilted lip retains aperture radius " + radius + "/" + tilt, Math.Abs(lip.Radius - radius * 0.97) < 0.02 * radius);
                double inset = Math.Max(lip.RadiusUp, lip.RadiusSide) * (Math.Sqrt(lip.SlopeUp * lip.SlopeUp + lip.SlopeSide * lip.SlopeSide) + 0.04);
                bool inside = true;
                for (int i = 0; i < 128; i++)
                {
                    double angle = i * 2 * Math.PI / 128, plumeY = 0.92 * lip.Radius * Math.Cos(angle), plumeZ = 0.92 * lip.Radius * Math.Sin(angle);
                    inside &= lip.Aft - inset <= center + tilt * (lip.Up + plumeY - up) - tilt * (lip.Side + plumeZ - side);
                }
                check("plume root overlaps tilted nozzle around full rim " + radius + "/" + tilt, inside);
            }
        Console.WriteLine(failures == 0 ? "PASS: nozzle lip checks" : "FAIL: " + failures + " nozzle lip checks");
        return failures;
    }
}
