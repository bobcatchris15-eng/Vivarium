using System;

namespace Vivarium.Sim.Repr.Light;

public sealed class LightCycleSelfTest : IReprSelfTest
{
    public string Name => "light.cycle";
    public string? Run()
    {
        if (Math.Abs(LightCycle.Ambient(.61) - .985f) > .0001f) return "ambient must interpolate between day and dusk";
        if (Math.Abs(LightCycle.Bounce(.81) - 1.25f) > .0001f) return "bounce must interpolate between dusk and night";
        if (Math.Abs(LightCycle.Exposure(.61) - 1.935f) > .0001f) return "exposure must interpolate";
        if (Math.Abs(LightCycle.Ambient(0) - LightCycle.Ambient(1)) > .0001f) return "cycle wrap discontinuity";
        if (Math.Abs(LightCycle.Ambient(-.1) - LightCycle.Ambient(.9)) > .0001f) return "negative phase must wrap";
        return null;
    }
}
