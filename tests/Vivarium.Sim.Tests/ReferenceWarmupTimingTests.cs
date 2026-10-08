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
    /// </summary>
    [Fact]
    public void FlowingPondAquaticStepIsBoundedAndUnchanged()
    {
        var content = ContentLoader.Load(TestUtil.ContentSource);
        var w = VivariumWorld.Create(content, content.PresetOrThrow("default"));
        var sw = Stopwatch.StartNew();
        w.Step(1945);
        _out.WriteLine($"step 1945 ticks: {sw.Elapsed.TotalSeconds:0.0}s");
        Assert.True(sw.Elapsed.TotalSeconds < 120, $"1945 ticks took {sw.Elapsed.TotalSeconds:0}s (pre-fix ~400s)");
        Assert.Equal("fc6e50079942822c66afcbaceb2b6b9f297948339c42a6fcd946d9d436d4f9e5", WorldSerializer.Digest(w));
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
            if (chunk++ % 5 == 0 || sw.ElapsedMilliseconds > 15000)
                _out.WriteLine($"chunk {chunk} day {w.Clock.BioDays:0.00} {sw.ElapsedMilliseconds}ms flora={w.Flora.Count} fauna={w.Fauna.Count}");
        }
        _out.WriteLine($"TOTAL {total.Elapsed.TotalSeconds:0.0}s chunks={chunk} day={w.Clock.BioDays:0.00}");
        Assert.True(w.Clock.BioDays >= 6, $"warm-up reached only day {w.Clock.BioDays:0.00} in {total.Elapsed.TotalSeconds:0}s");
    }
}
