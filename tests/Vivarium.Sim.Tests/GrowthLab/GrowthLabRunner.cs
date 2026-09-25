using Vivarium.Sim.Coverage;

namespace Vivarium.Sim.Tests.GrowthLab;

/// <summary>
/// Headless growth-lab harness (docs/overhaul/growth_models.md §12): runs a scenario for N steps and,
/// optionally, rasterizes each frame's occupancy to a PNG timelapse under build/growthlab/&lt;scenario&gt;/.
/// </summary>
public static class GrowthLabRunner
{
    public static string FramesDir(string scenario) => Path.Combine(TestUtil.RepoRoot, "build", "growthlab", scenario);

    /// <summary>Runs <paramref name="stepFn"/> for <paramref name="steps"/> steps, writing one frame per step if <paramref name="writeFrames"/>.</summary>
    public static void Run(string scenario, CoverageLayer layer, int steps, int half, bool writeFrames, Action<long> stepFn)
    {
        string dir = FramesDir(scenario);
        if (writeFrames && Directory.Exists(dir)) Directory.Delete(dir, recursive: true);

        for (long s = 0; s < steps; s++)
        {
            stepFn(s);
            if (writeFrames) WriteFrame(scenario, (int)s, layer, half);
        }
    }

    /// <summary>Rasterizes occupancy of a layer, centred at (0,0), into an RGB PNG frame.</summary>
    public static void WriteFrame(string scenario, int frameIndex, CoverageLayer layer, int half)
    {
        int size = half * 2 + 1;
        var rgb = new byte[size * size * 3];
        for (int gz = -half; gz <= half; gz++)
        for (int gx = -half; gx <= half; gx++)
        {
            byte occ = layer.GetOcc(gx, gz);
            int px = gx + half, py = gz + half;
            int o = (py * size + px) * 3;
            if (occ != 0) { rgb[o] = 40; rgb[o + 1] = 160; rgb[o + 2] = 60; } // green = occupied
            else { rgb[o] = 20; rgb[o + 1] = 20; rgb[o + 2] = 20; }          // dark = empty
        }
        string path = Path.Combine(FramesDir(scenario), $"frame_{frameIndex:D3}.png");
        PngEncoder.WriteRgb(path, size, size, rgb);
    }

    /// <summary>area / (pi * rMax^2) over occupied cells within [-half, half], seed at origin.</summary>
    public static double Roundness(CoverageLayer layer, int half)
    {
        int area = 0;
        double rMax = 0;
        for (int gz = -half; gz <= half; gz++)
        for (int gx = -half; gx <= half; gx++)
        {
            if (layer.GetOcc(gx, gz) == 0) continue;
            area++;
            double r = Math.Sqrt(gx * gx + gz * gz);
            if (r > rMax) rMax = r;
        }
        if (rMax <= 0) return 0;
        return area / (Math.PI * rMax * rMax);
    }
}
