// dotnet run -c Release -- --reference afterburner-ref.json   the plume model against upstream's TypeScript
// dotnet run -c Release -- --nozzle                           the nozzle lip fit
// dotnet run -c Release -- --sweep <nozzle m> <coflow> <maxD> <speed limit> <cells>   size, length and diamonds over speed and height
using System;
using System.Globalization;

static class Program
{
    static int Main(string[] args)
    {
        if (args.Length > 1 && args[0] == "--reference") return AfterburnerCheck.Run(args[1]);
        if (args.Length > 0 && args[0] == "--nozzle") return NozzleChecks.Run();
        if (args.Length > 5 && args[0] == "--sweep")
        {
            var n = new double[5];
            for (int i = 0; i < 5; i++) n[i] = double.Parse(args[i + 1], CultureInfo.InvariantCulture);
            AfterburnerCheck.Sweep(n[0], n[1], n[2], n[3], n[4]);
            return 0;
        }
        Console.WriteLine("usage: --reference afterburner-ref.json | --nozzle | --sweep <nozzle m> <coflow> <maxD> <speed limit> <cells>");
        return 2;
    }
}
