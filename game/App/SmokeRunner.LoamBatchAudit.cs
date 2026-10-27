using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Godot;
using Vivarium.Sim.Core;
using Vivarium.Sim.Persistence;

namespace Vivarium.Game.App;

public partial class SmokeRunner
{
    private async Task LoamBatchAuditAsync()
    {
        bool paused = W.Clock.Paused;
        W.Clock.Paused = true;
        string before = WorldSerializer.Digest(W);
        var positions = new List<Vector2>();
        for (int z = -3; z <= 3 && positions.Count < 2; z++)
        for (int x = -3; x <= 3 && positions.Count < 2; x++)
        {
            var p = new Vec2(x + .5, z + .5);
            if (W.Domain.ContainsDisc(p, .7) && !W.Water.IsWet(p) && W.SubstrateAt(p) == Vivarium.Sim.Content.Substrate.Soil)
                positions.Add(new Vector2((float)p.X, (float)p.Z));
        }
        string MeshHash(Node node)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            void Visit(Node current)
            {
                hash.AppendData(Encoding.UTF8.GetBytes(current.Name));
                if (current is MeshInstance3D mi && mi.Mesh is { } mesh)
                    for (int s = 0; s < mesh.GetSurfaceCount(); s++)
                    {
                        using var arrays = mesh.SurfaceGetArrays(s);
                        hash.AppendData(GD.VarToBytes(arrays));
                    }
                foreach (var child in current.GetChildren()) Visit(child);
            }
            Visit(node); return Convert.ToHexString(hash.GetHashAndReset());
        }
        var rows = new List<object>();
        try
        {
            foreach (var position in positions)
            foreach (bool litter in new[] { true, false })
            {
                Node3D? expected = null, actual = null;
                try
                {
                    expected = litter ? LoamLitterBuilder.Build(W, 24601, .4f, patchCenter: position, halfExtent: .5f)
                        : LoamAggregateBuilder.Build(W, 24601, position, .5f);
                    var batches = litter ? LoamLitterBuilder.BuildBatched(W, 24601, n => actual = n, .4f, patchCenter: position, halfExtent: .5f)
                        : LoamAggregateBuilder.BuildBatched(W, 24601, n => actual = n, position, .5f);
                    int steps = 0;
                    using (var iterator = batches.GetEnumerator())
                        while (iterator.MoveNext()) { if (++steps % 8 == 0) await Frames(1); }
                    string reference = MeshHash(expected), candidate = MeshHash(actual!);
                    Check("loam incremental mesh equality " + position + " litter=" + litter, reference == candidate);
                    rows.Add(new { x = position.X, z = position.Y, litter, steps, reference, candidate });
                }
                finally { expected?.Free(); actual?.Free(); }
            }
            Check("loam audit has dry soil samples", positions.Count == 2);
            Check("loam incremental generation preserves world", before == WorldSerializer.Digest(W));
            File.WriteAllText(Path.Combine(OutDir, "loam-batch-audit.json"), System.Text.Json.JsonSerializer.Serialize(rows));
        }
        finally { W.Clock.Paused = paused; }
    }
}
