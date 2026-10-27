using System;
using System.Collections.Generic;
using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.Core;
using Vivarium.Sim.Coverage;
using Vivarium.Sim.Geometry;

namespace Vivarium.Game.Render;

public partial class CoverageRenderer
{
    private ShaderMaterial _aquaticMaterial = null!;
    private static bool IsAquatic(CoverageLayerId id) => id is CoverageLayerId.SurfaceFloat or CoverageLayerId.AlgaeBed or CoverageLayerId.AlgaeFloat;
    private void InitAquaticMaterials() => _aquaticMaterial = Bridge.Shader("res://Shaders/coverage_aquatic.gdshader");
    private Material CoverageMaterial(CoverageLayerId id) => IsAquatic(id) ? _aquaticMaterial :
        DebugMode ? (id == CoverageLayerId.Crust ? _debugCrustMaterial : _debugMatMaterial) :
        id == CoverageLayerId.Crust ? _crustMaterial : _matMaterial;

    // Only authoritative occupied cells produce geometry. No decorative population or food mass.
    // UV stores local current; UV2 stores tissue identity and height relative to the water table.
    private MeshData BuildAquaticTile(CoverageLayer layer, CoverageTile tile)
    {
        var mesh = new MeshData();
        const int edge = CoverageSpec.TileEdge;
        const double cs = CoverageSpec.CellSize;
        double table = _w.Water.WaterTable;
        double x0 = tile.Ti * edge * cs, z0 = tile.Tj * edge * cs;
        var rocks = new List<Vivarium.Sim.World.Rock>();
        foreach (var rock in _w.Props.Rocks)
            if (rock.X + rock.FootprintRadius >= x0 && rock.X - rock.FootprintRadius <= x0 + edge * cs &&
                rock.Z + rock.FootprintRadius >= z0 && rock.Z - rock.FootprintRadius <= z0 + edge * cs) rocks.Add(rock);
        double Bed(Vec2 p)
        {
            double y = _w.Terrain.Height(p);
            foreach (var rock in rocks) { double top = rock.TopAt(p); if (double.IsFinite(top)) y = Math.Max(y, top); }
            return y;
        }
        for (int z = 0; z < edge; z++)
        for (int x = 0; x < edge; x++)
        {
            int index = z * edge + x;
            float biomass = Math.Clamp(tile.B[index], 0, 1);
            if (tile.Occ[index] == 0 || biomass <= 0 || !float.IsFinite(biomass)) continue;
            double wx = x0 + (x + .5) * cs, wz = z0 + (z + .5) * cs;
            var p = new Vec2(wx, wz);
            double water = _w.Water.SurfaceAt(p), ground = Bed(p);
            if (!double.IsFinite(water) || water <= ground + .0008) continue;
            var velocity = _w.Water.StreamVelocity(p);
            if (!double.IsFinite(velocity.X) || !double.IsFinite(velocity.Z)) velocity = Vec2.Zero;
            // A bounded current encodes animation direction without unbounded mesh excursions.
            double speed = Math.Sqrt(velocity.X * velocity.X + velocity.Z * velocity.Z);
            if (speed > 1) velocity /= speed;
            uint hash = ReliefHash(tile.Ti * edge + x, tile.Tj * edge + z);
            double Rand(int n) => ((ReliefHash((int)hash, n) >> 8) & 65535) / 65535.0;
            double stress = tile.Dorm[index] / 255.0;
            if ((tile.Flags[index] & (byte)CoverageFlags.Dead) != 0) stress = 1;
            double[] green = { .20 + stress * .17, .43 - stress * .18, .07 + stress * .02 };
            if (layer.Id == CoverageLayerId.SurfaceFloat)
            {
                int count = 3 + (int)Math.Round(4 * biomass);
                for (int pad = 0; pad < count; pad++)
                {
                    double a = pad * 2.399963 + Rand(1) * Math.Tau;
                    double radius = .002 + .005 * Math.Sqrt((pad + .5) / count);
                    double px = wx + Math.Cos(a) * radius, pz = wz + Math.Sin(a) * radius;
                    double diameter = .0025 + .002 * Rand(20 + pad);
                    var center = new Vec3(px, water + .0006, pz);
                    AquaticEllipsoid(mesh, center, new Vec3(diameter * .5, .00038, diameter * .36), green, 0, table, velocity);
                    double length = Math.Min(.006 + .006 * Rand(50 + pad), water - ground - .0004);
                    if (length > .001)
                        AquaticRibbon(mesh, center - new Vec3(0, .0005, 0), new Vec3(px + .0005, water - length, pz), .00011,
                            new double[] { .29, .34, .13 }, 1, table, velocity, pad);
                }
            }
            else if (layer.Id == CoverageLayerId.AlgaeBed)
            {
                double biofilm = Math.Clamp(_w.Fields.Biofilm.Sample(p) / Math.Max(_w.Fields.Biofilm.Max, 1e-9), 0, 1);
                int tufts = 4 + (int)Math.Round(8 * biomass);
                for (int tuft = 0; tuft < tufts; tuft++)
                {
                    double px = wx + (Rand(100 + tuft) - .5) * cs, pz = wz + (Rand(200 + tuft) - .5) * cs;
                    double bed = Bed(new Vec2(px, pz));
                    if (bed + .001 >= water) continue;
                    double height = Math.Min(.0007 + .0025 * biomass, (water - bed) * .55);
                    var foot = new Vec3(px, bed + .00015, pz);
                    AquaticRibbon(mesh, foot, foot + new Vec3(.0006, height, .0003), .0006 + biofilm * .0005, green, 2, table, velocity, tuft);
                    if (tuft < 1 + (int)(biofilm * 3) && stress < .5)
                    {
                        double r = .00025 + .00035 * Rand(300 + tuft);
                        if (bed + height + 2 * r < water)
                            AquaticEllipsoid(mesh, foot + new Vec3(0, height + r, 0), new Vec3(r, r, r), new double[] { .67, .81, .64 }, 3, table, velocity);
                    }
                }
            }
            else
            {
                int threads = 4 + (int)Math.Round(8 * biomass);
                for (int thread = 0; thread < threads; thread++)
                {
                    double depth = Math.Min(.002 + .013 * Rand(400 + thread), (water - ground) * .75);
                    var a = new Vec3(wx + (Rand(500 + thread) - .5) * cs, water - depth, wz + (Rand(600 + thread) - .5) * cs);
                    var b = a + new Vec3(.006 + .010 * Rand(700 + thread), 0, .004 * Math.Sin(thread));
                    AquaticRibbon(mesh, a, b, .00014 + .00015 * biomass, green, 4, table, velocity, thread);
                }
            }
        }
        return mesh;
    }

    private static int AquaticVertex(MeshData mesh, Vec3 p, Vec3 n, double[] color, int tissue, double table, Vec2 flow) =>
        mesh.AddVertex(p, n, color, 1, flow.X, flow.Z, tissue, p.Y - table);

    // Godot front faces are clockwise. Orient every triangle against its analytic outward normal.
    private static void AquaticTriangle(MeshData mesh, int a, int b, int c)
    {
        var face = (mesh.Position(b) - mesh.Position(a)).Cross(mesh.Position(c) - mesh.Position(a));
        if (face.LengthSq < 1e-22) return;
        if (face.Dot(mesh.NormalAt(a) + mesh.NormalAt(b) + mesh.NormalAt(c)) > 0) mesh.AddTriangle(a, c, b);
        else mesh.AddTriangle(a, b, c);
    }

    private static void AquaticEllipsoid(MeshData mesh, Vec3 center, Vec3 scale, double[] color, int tissue, double table, Vec2 flow)
    {
        const int sides = 10, rings = 3;
        int top = AquaticVertex(mesh, center + new Vec3(0, scale.Y, 0), Vec3.Up, color, tissue, table, flow);
        int first = mesh.VertexCount;
        for (int ring = 1; ring <= rings; ring++)
        for (int side = 0; side < sides; side++)
        {
            double v = Math.PI * ring / (rings + 1), u = Math.Tau * side / sides;
            var unit = new Vec3(Math.Sin(v) * Math.Cos(u), Math.Cos(v), Math.Sin(v) * Math.Sin(u));
            var point = new Vec3(unit.X * scale.X, unit.Y * scale.Y, unit.Z * scale.Z);
            var normal = new Vec3(unit.X / scale.X, unit.Y / scale.Y, unit.Z / scale.Z).Normalized();
            AquaticVertex(mesh, center + point, normal, color, tissue, table, flow);
        }
        int bottom = AquaticVertex(mesh, center - new Vec3(0, scale.Y, 0), new Vec3(0,-1,0), color, tissue, table, flow);
        for (int side = 0; side < sides; side++)
        {
            int next = (side + 1) % sides;
            AquaticTriangle(mesh, top, first + side, first + next);
            for (int ring = 0; ring < rings - 1; ring++)
            {
                int a = first + ring * sides + side, b = first + ring * sides + next;
                AquaticTriangle(mesh, a, b, a + sides); AquaticTriangle(mesh, b, b + sides, a + sides);
            }
            AquaticTriangle(mesh, first + (rings - 1) * sides + side, bottom, first + (rings - 1) * sides + next);
        }
    }

    private static void AquaticRibbon(MeshData mesh, Vec3 a, Vec3 b, double width, double[] color, int tissue, double table, Vec2 flow, int seed)
    {
        var axis = (b - a).Normalized();
        var side = axis.Cross(Math.Abs(axis.Y) > .9 ? new Vec3(1,0,0) : Vec3.Up).Normalized();
        var normal = side.Cross(axis).Normalized();
        int start = mesh.VertexCount;
        const int segments = 8;
        for (int i = 0; i <= segments; i++)
        {
            double t = i / (double)segments;
            var center = a + (b - a) * t + side * (Math.Sin(t * Math.PI * 2 + seed) * .0006 * Math.Sin(t * Math.PI));
            double taper = .25 + .75 * Math.Sin(Math.PI * (.08 + .84 * t));
            AquaticVertex(mesh, center - side * (width * taper), normal, color, tissue, table, flow);
            AquaticVertex(mesh, center + side * (width * taper), normal, color, tissue, table, flow);
            if (i == 0) continue;
            int k = start + i * 2;
            AquaticTriangle(mesh, k - 2, k, k - 1); AquaticTriangle(mesh, k - 1, k, k + 1);
        }
    }
}

