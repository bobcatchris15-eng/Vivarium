using System.Diagnostics;
using Vivarium.Sim.Content;
using Vivarium.Sim.Persistence;
using Vivarium.Sim.World;
using Xunit.Abstractions;

namespace Vivarium.Sim.Tests;

/// <summary>
/// Guards the reference-capture warm-up (default preset stepped in 200-tick chunks, as SmokeRunner.Reference does).
/// Around tick 1880 the default pond starts to flow; aquatic advection then padded its rectangle by up to
/// MaxSubsteps dry cells per side and one AquaticSystem.Step took minutes (2026-10-07: capture &gt;60 min).
/// </summary>
[Trait("Suite", "Perf")]
[Trait("Speed", "Slow")]
public class ReferenceWarmupTimingTests
{
    private readonly ITestOutputHelper _out;
    public ReferenceWarmupTimingTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// Through the first flowing-pond steps: fast, and bit-identical to the pre-fix advection (digest captured
    /// from HEAD 2fdf4f7 before the fix). If content/sim changes legitimately move the digest, re-pin it.
    /// Re-pinned 2026-10-21 for the ten-species new-flora set, which adds starter plants to the default
    /// preset and so moves the world the warm-up runs on. The advection itself is untouched.
    /// Re-pinned 2026-10-24 for aquatic coverage establishment (item 2.5).
    /// </summary>
    [Fact]
    public void FlowingPondAquaticStepIsBoundedAndUnchanged()
    {
        var content = ContentLoader.Load(TestUtil.ContentSource);
        var w = VivariumWorld.Create(content, content.PresetOrThrow("default"));
        var sw = Stopwatch.StartNew();
        w.Step(1945);
        string digest = WorldSerializer.Digest(w);
        _out.WriteLine($"step 1945 ticks: {sw.Elapsed.TotalSeconds:0.0}s digest={digest}");
        Assert.True(sw.Elapsed.TotalSeconds < 120, $"1945 ticks took {sw.Elapsed.TotalSeconds:0}s (pre-fix ~400s)");
        Assert.Equal("3f6769d1cc9e8813e7dbd61b48243e88527e90f38212a380b21fd2201520ed69", digest);
    }

    /// <summary>Diagnostic: per-chunk and per-system timing for the full 6-day warm-up.</summary>
    [Fact]
    public void WarmupStepsAreTimed()
    {
        var content = ContentLoader.Load(TestUtil.ContentSource);
        var w = VivariumWorld.Create(content, content.PresetOrThrow("default"));
        w.Scheduler.SystemTimed += (sys, ms) => { if (ms > 2000) _out.WriteLine($"SLOW {sys.Name} {ms:0}ms tick={w.Clock.Tick}"); };
        long start = w.Clock.Tick; int chunk = 0;
        var total = Stopwatch.StartNew();
        while (w.Clock.BioDays < 6 && w.Clock.Tick - start < 2_000_000 && total.Elapsed.TotalSeconds < 900)
        {
            var sw = Stopwatch.StartNew();
            w.Step(200);
            _out.WriteLine($"chunk {chunk++} 200 ticks: {sw.Elapsed.TotalSeconds:0.0}s (tick {w.Clock.Tick}, bio-days {w.Clock.BioDays:0.0})");
        }
        _out.WriteLine($"total 6 bio-days warm-up: {total.Elapsed.TotalSeconds:0.0}s");
    }
}
