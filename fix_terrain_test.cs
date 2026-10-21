using System;
using Vivarium.Sim.Tests;
using Vivarium.Sim.World;

class Program {
    static void Main() {
        var w = TestUtil.FlatWorld(edit: d => { d.Terrain.RockExposure = 1; d.Terrain.Relief = 1; });
        Console.WriteLine(w.Fields.BaseSubstrate[0]);
    }
}
