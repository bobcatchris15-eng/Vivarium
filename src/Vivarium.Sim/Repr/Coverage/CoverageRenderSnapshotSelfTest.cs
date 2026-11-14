using System.Threading;
using System.Threading.Tasks;
using Vivarium.Sim.Coverage;
using Vivarium.Sim.Repr;

namespace Vivarium.Sim.Repr.Coverage;

public sealed class CoverageRenderSnapshotSelfTest : IReprSelfTest
{
    public string Name => "coverage.renderSnapshot";

    public string? Run()
    {
        var layer = new CoverageLayer(CoverageLayerId.Mat, 123UL);
        layer.SetCell(0, 0, 2, 0.4f, 90, 7, 3, 0, 2);
        layer.SetCell(32, 0, 1, 0.7f, 120, 4, 0, 0, 1);
        if (!CoverageRenderSnapshot.TryCapture(layer, 0, 0, out var snapshot) || snapshot == null)
            return "could not capture populated target and halo";

        if (!snapshot.IsCurrent(6, 6, 9, 9)) return "unchanged captured inputs were marked stale";

        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var pendingBuild = Task.Run(() =>
        {
            started.Set();
            release.Wait();
            return (snapshot.Tile.Occ[0], snapshot.Tile.B[0], snapshot.Layer.GetOcc(32, 0), snapshot.Layer.GetB(32, 0));
        });
        if (!started.Wait(2000)) return "pending build did not reach its captured input";

        // Mutate both the requested tile and a tile sampled through its border halo.
        layer.SetCell(0, 0, 3, 0.9f, 20, 8, 1, (byte)CoverageFlags.Dead, 0);
        layer.SetCell(32, 0, 4, 0.2f, 30, 5, 2, 0, 0);
        release.Set();
        if (!pendingBuild.Wait(2000)) return "pending snapshot worker failed to finish";
        var built = pendingBuild.Result;

        if (built.Item1 != 2 || built.Item2 != 0.4f || built.Item3 != 1 || built.Item4 != 0.7f)
            return "pending build observed mutated live coverage arrays instead of its immutable snapshot";
        if (snapshot.IsCurrent(6, 6, 9, 9))
            return "changed target/halo revisions were accepted as current";

        if (!CoverageRenderSnapshot.TryCapture(layer, 0, 0, out var afterMutation) || afterMutation == null)
            return "could not capture after source mutation";
        if (!afterMutation.IsCurrent(6, 6, 9, 9))
            return "newly captured inputs were not current before another mutation";

        // Some biology paths write SoA arrays before publishing the tile revision. The captured
        // input fingerprint must still prevent an unchanged Version from blessing changed values.
        layer.TryGetTile(0, 0, out var directTile);
        directTile!.B[0] += 0.01f;
        if (afterMutation.IsCurrent(6, 6, 9, 9))
            return "in-place array mutation without Touch was accepted as current";

        // Previously absent halo tiles are dependencies too: their later creation changes corner support.
        if (!CoverageRenderSnapshot.TryCapture(layer, 1, 0, out var missingHalo) || missingHalo == null)
            return "could not capture tile with absent halo";
        layer.SetCell(64, 0, 1, 0.1f, 40, 1, 0, 0, 1);
        if (missingHalo.IsCurrent(6, 6, 9, 9))
            return "creation of a previously absent halo tile was not rejected";

        if (afterMutation.IsCurrent(7, 6, 9, 9) || afterMutation.IsCurrent(6, 6, 10, 9))
            return "terrain or props dependency version change was not rejected";

        var writerLayer = new CoverageLayer(CoverageLayerId.Mat, 456UL);
        writerLayer.SetCell(0, 0, 2, 0.3f, 80, 1, 0, 0, 1);
        if (!CoverageRenderSnapshot.TryCapture(writerLayer, 0, 0, out var beforeWrite) || beforeWrite == null)
            return "could not capture before active-writer regression";
        writerLayer.TryGetTile(0, 0, out var writerTile);
        using var writePaused = new ManualResetEventSlim();
        using var publishWrite = new ManualResetEventSlim();
        var writer = Task.Run(() =>
        {
            writerLayer.BeginWriter();
            try
            {
                writerTile!.Occ[0] = 5;
                writerTile.B[0] = 0.85f;
                writePaused.Set(); // deliberately stop before Touch/Version publication
                publishWrite.Wait();
                writerTile.Touch();
            }
            finally { writerLayer.EndWriter(); }
        });
        if (!writePaused.Wait(2000))
        {
            publishWrite.Set();
            writer.Wait(2000);
            return "writer did not pause before publishing its tile revision";
        }
        bool acceptedDuringWrite = beforeWrite.IsCurrent(3, 3, 4, 4);
        bool capturedDuringWrite = CoverageRenderSnapshot.TryCapture(writerLayer, 0, 0, out _);
        bool acquiredCommitDuringWrite = writerLayer.TryBeginRenderCommit();
        if (acquiredCommitDuringWrite) writerLayer.EndRenderCommit();
        publishWrite.Set();
        if (!writer.Wait(2000)) return "paused writer did not complete";
        if (acceptedDuringWrite || capturedDuringWrite || acquiredCommitDuringWrite)
            return "active writer window allowed mesh commit or snapshot capture before tile Version publication";
        if (!CoverageRenderSnapshot.TryCapture(writerLayer, 0, 0, out var afterWrite) || afterWrite == null ||
            afterWrite.Tile.Occ[0] != 5 || afterWrite.Tile.B[0] != 0.85f || !afterWrite.IsCurrent(3, 3, 4, 4))
            return "stable post-writer snapshot did not contain the complete published state";
        return null;
    }
}
