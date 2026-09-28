using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;

namespace Vivarium.Sim.Tests;

[Trait("Suite", "Geometry")]
public class FloraRefinementTests
{
    private static List<Vec3> FirstCanePath(MeshData mesh)
    {
        var centres = new List<Vec3>();
        // Longitudinal tube UVs identify the endpoint independently of tessellation density.
        for(int ring=0;ring<mesh.VertexCount/8;ring++)
        {
            var centre=Vec3.Zero;
            for(int side=0;side<7;side++) centre+=mesh.Position(ring*8+side)/7;
            centres.Add(centre);
            if(mesh.UV[ring*8*2]>=.9999) return centres;
        }
        throw new InvalidOperationException("Cane endpoint is missing.");
    }

    [Fact]
    public void HookthicketCaneSweepsSmoothlyWithoutSharpFacetsAtSpeciesScale()
    {
        var species=new FloraSpeciesDef
        {
            Id="hookthicket", Shape="hookthicket_brake", Height=1.72, RadiusAtMax=.9,
            Color=new[]{.23,.4,.18}, Color2=new[]{.47,.58,.25}
        };
        for(ulong seed=1;seed<=12;seed++)
        {
            var path=FirstCanePath(OrganismMeshes.Flora(species,seed))
                .Select(p=>new Vec3(p.X*.9,p.Y*1.72,p.Z*.9)).ToArray();
            for(int joint=1;joint<path.Length-1;joint++)
            {
                var a=(path[joint]-path[joint-1]).Normalized();
                var b=(path[joint+1]-path[joint]).Normalized();
                double turn=Math.Acos(Math.Clamp(a.Dot(b),-1,1))*180/Math.PI;
                Assert.True(turn<15,$"Seed {seed}, cane step {joint}: abrupt {turn:F1}-degree corner");
            }
        }
    }

    [Fact]
    public void MatureHookthicketCaneReturnsToSoilWhileYoungCaneIsStillRising()
    {
        var species = new FloraSpeciesDef
        {
            Id = "hookthicket", Shape = "hookthicket_brake",
            Color = new[] { .23, .4, .18 }, Color2 = new[] { .47, .58, .25 }
        };
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var mature = FirstCanePath(OrganismMeshes.Flora(species, seed));
            Assert.InRange(mature[^1].Y - mature[0].Y, -.0001, .0001);
            Assert.True(mature.Max(p=>p.Y) > .5, "Mature canes should form a substantial arch.");
            Assert.True((mature[^1] - mature[0]).Length > .8, "Grounded tips should reach out as runners.");
            var young = FirstCanePath(OrganismMeshes.Flora(species, seed, juvenile: true));
            Assert.True(young[^1].Y > .4, "Young cane tips should remain above ground.");
        }
    }

    [Fact]
    public void KinkcaneJointsDivergeAtLeastThirtyDegreesAtSpeciesScale()
    {
        var species = new FloraSpeciesDef
        {
            Id = "kinkcane", Shape = "kinkcane_brake", Height = 2.45, RadiusAtMax = .82,
            Color = new[] { .25, .48, .2 }, Color2 = new[] { .5, .66, .28 }
        };
        for (ulong seed = 1; seed <= 24; seed++)
        {
            var mesh = OrganismMeshes.Flora(species, seed);
            // The first cane is a seven-ring tube with seven unique samples per ring.
            // Recover its centreline from the actual rendered geometry, then apply game scale.
            var centres = new Vec3[7];
            for (int ring = 0; ring < centres.Length; ring++)
            {
                for (int side = 0; side < 7; side++) centres[ring] += mesh.Position(ring * 8 + side) / 7;
                centres[ring] = new Vec3(centres[ring].X * species.RadiusAtMax,
                    centres[ring].Y * species.Height, centres[ring].Z * species.RadiusAtMax);
            }
            for (int joint = 1; joint < centres.Length - 1; joint++)
            {
                var previous = (centres[joint] - centres[joint - 1]).Normalized();
                var next = (centres[joint + 1] - centres[joint]).Normalized();
                double angle = Math.Acos(Math.Clamp(previous.Dot(next), -1, 1)) * 180 / Math.PI;
                Assert.True(angle >= 30 - .001, $"Seed {seed}, joint {joint}: {angle:F2} degrees");
                Assert.True(next.Y > 0, "Canes must continue growing toward the open sky.");
            }
        }
    }
}
