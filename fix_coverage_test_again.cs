using System;
using System.Linq;
using Vivarium.Sim.Tests;
using Vivarium.Sim.Persistence;

class Program {
    static void Main() {
        var original = TestUtil.DefaultWorld();
        original.Step(95);
        var originalJson = WorldSerializer.Serialize(original);
        var loaded = WorldSerializer.Deserialize(original.Content, originalJson);
        original.Step(60);
        loaded.Step(60);

        var originalDigests = WorldSerializer.SubsystemDigests(original);
        var loadedDigests = WorldSerializer.SubsystemDigests(loaded);

        foreach (var (name, digest) in originalDigests) {
            if (digest != loadedDigests[name]) {
                Console.WriteLine($"{name} differs: original {digest}, loaded {loadedDigests[name]}");
            }
        }
    }
}
