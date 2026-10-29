using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry.Form;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Landscape-scale fictional old-growth anatomy. Built once from persistent PilotTreeState;
/// root sections use exactly the same authoritative samples as ground/wood queries.
/// COLOR.a is a material marker: 1 wood, 0 foliage, .35 reproductive/fungal tissue.
/// </summary>
public static class PilotTreeMeshes
{
    /// <summary>
    /// Exact terrain-footprint sampler for the mutable part of old-growth root
    /// rendering. Uses every sampled ring footprint plus endpoint cap centers
    /// from Roots(), not a global terrain revision or a coarse AABB guess.
    /// A root mesh upload is required only if sampled ground geometry differs.
    /// Cache is renderer-owned present state; nothing is persisted in ecology.
    /// </summary>
    public static ulong RootGroundSignature(VivariumWorld world)
    {
        var tree=world.PilotTree;
        if(tree==null)return 0;
        const ulong fnvOffset=14695981039346656037UL;
        const ulong fnvPrime=1099511628211UL;
        ulong hash=fnvOffset;
        void MixHeight(Vec2 p)
        {
            ulong bits=BitConverter.DoubleToUInt64Bits(world.SurfaceHeight(p));
            hash=unchecked((hash^bits)*fnvPrime);
        }
        foreach(var path in tree.Roots)
        {
            var samples=path.Samples;
            for(int i=0;i<samples.Count;i++)
            {
                var s=samples[i];
                var tangent=i==0?samples[1].Position-s.Position:
                    i==samples.Count-1?s.Position-samples[i-1].Position:
                    samples[i+1].Position-samples[i-1].Position;
                var side=tangent.Normalized().Rotated(Math.PI/2);
                for(int j=0;j<=12;j++)
                {
                    double a=Math.PI*j/12;
                    var p=s.Position+side*(Math.Cos(a)*s.Width);
                    MixHeight(p);
                }
            }
            MixHeight(samples[0].Position);
            MixHeight(samples[^1].Position);
        }
        return hash;
    }

    public static MeshData Build(VivariumWorld world, bool includeCrown = true) =>
        BuildParts(world, includeRoots: true, includeTrunk: true, includeCrown);

    /// <summary>Only terrain-dependent root crests, clipped to the island.
    /// Terrain edits may refresh this without remeshing the massive static trunk.</summary>
    public static MeshData BuildRoots(VivariumWorld world) =>
        BuildParts(world, includeRoots: true, includeTrunk: false, includeCrown: false);

    /// <summary>Permanent trunk, boundary cut faces and optional legacy crown.
    /// Rebuild only on a renderer cutover, not on unrelated terrain erosion.</summary>
    public static MeshData BuildTrunkAndCrown(VivariumWorld world, bool includeCrown = true) =>
        BuildParts(world, includeRoots: false, includeTrunk: true, includeCrown);

    private static MeshData BuildParts(VivariumWorld world, bool includeRoots,
        bool includeTrunk, bool includeCrown)
    {
        // Foliage spans are recorded so the renderer can split laminae onto the
        // dedicated leaf shader without one node per leaf. Zero is authored detail.
        var m = new MeshData { FloraDetailLevel = 0, FloraVisualSeed = world.PilotTree?.Seed ?? 1 };
        var tree = world.PilotTree;
        if (tree == null) return m;
        var b = new Builder(m, tree, world.SurfaceHeight);
        if (includeRoots) b.Roots();
        if (includeTrunk) b.Trunk();
        // The ancient boundary root and trunk remain terrain-authoritative.
        // Once new axial/leaf hardware meshes are uploaded, skip legacy crown
        // organs without ever altering the ecosystem's root samples or hull.
        if (!includeCrown) return m;
        switch (tree.Def.Id)
        {
            case "gloomspire": b.Conifer(false, false); break;
            case "needlevault": b.Conifer(true, false); break;
            case "crowncoil": b.Cycad(); break;
            case "emberpillar": b.Redwood(); break;
            case "basinwarden": b.Broadleaf(); break;
            case "palehollow": b.Conifer(false, true); b.Fungi(); break;
            case "cisternvault": b.Baobab(); break;
            case "cinnabarcrest": b.Cinnabar(); break;
            case "rootcloister": b.Banyan(); break;
            case "silkcascade": b.Willow(); break;
            default: throw new ArgumentException("Unknown Pilot Tree form: " + tree.Def.Id);
        }
        return m;
    }

    private sealed class Builder
    {
        private MeshData m;
        private readonly PilotTreeState tree;
        private readonly Rng rng;
        private readonly double r, h;
        private readonly Vec3 origin;
        private readonly double[] bark, green;
        private readonly double phase, facing;
        private readonly Func<Vec2,double> ground;
        private readonly (double T,double A,double Hw,double Aw,double Amp)[] scars;
        private const double Tau = Math.PI * 2;
        public Builder(MeshData mesh, PilotTreeState state, Func<Vec2,double> terrainHeight)
        {
            m = mesh; tree = state; ground = terrainHeight; rng = Rng.Keyed(state.Seed, "pilot.mesh." + state.Def.Id, 0);
            r = state.Def.TrunkRadius; h = state.Def.TrunkHeight;
            origin = Vec3.FromXZ(state.Anchor, state.BaseHeight);
            bark = RGB(state.Def.BarkColor); green = RGB(state.Def.FoliageColor);
            phase = rng.Range(0, Tau); facing = (-state.CornerPoint).Angle;
            // Healing wounds and their callus rolls are an independent ornament
            // stream: they never perturb the shared draws that shape the crown.
            var scarRng = Rng.Keyed(state.Seed, "pilot.bark.scars", 0);
            scars = new (double,double,double,double,double)[3];
            for (int i = 0; i < scars.Length; i++)
                scars[i] = (scarRng.Range(.13, .60), facing + scarRng.Range(-.60, .60),
                    scarRng.Range(.030, .058), scarRng.Range(.16, .30), scarRng.Range(.045, .085));
        }
        private static double[] RGB(Vec3 color) => new[] { color.X, color.Y, color.Z };
        private Vec3 Centre(double t) => origin + Vec3.Up * (h * t);
        private double Radius(double t) => tree.Def.Id switch
        {
            "crowncoil" => r * (.82 + .18 * (1 - t)) * (1 + .13 * Math.Exp(-t * 16)),
            "cisternvault" => r * (.68 + .54 * Math.Sin(Math.PI * Math.Pow(t, 0.72))) * (1 + .18 * Math.Exp(-t * 12)),
            "cinnabarcrest" => r * (.06 + .94 * Math.Pow(1 - t, .82)) * (1 + .24 * Math.Exp(-t * 16)),
            "rootcloister" => r * (.28 + .72 * Math.Pow(1 - t, .65)) * (1 + .35 * Math.Exp(-t * 14)),
            "silkcascade" => r * (.20 + .80 * Math.Pow(1 - t, .70)) * (1 + .30 * Math.Exp(-t * 16)),
            _ => r * (.18 + .82 * Math.Pow(1 - t, .75)) * (1 + .30 * Math.Exp(-t * 16))
        };

        // Flat face normals preserve bark plate/fissure relief, with Godot winding.
        private void Face(Vec3 a, Vec3 b, Vec3 c, double[] color, double material = 1, Vec3? outward = null)
        {
            var n = (b - a).Cross(c - a).Normalized();
            if (n.LengthSq < 1e-12) return;
            if (outward is {} target && n.Dot(target) < 0) n = -n;
            int i = m.AddVertex(a, n, color, material, a.X + a.Z, a.Y);
            m.AddVertex(b, n, color, material, b.X + b.Z, b.Y);
            m.AddVertex(c, n, color, material, c.X + c.Z, c.Y);
            Primitives.TriangleFacing(m, i, i + 1, i + 2, n);
        }
        private void Quad(Vec3 a, Vec3 b, Vec3 c, Vec3 d, double[] color, double material = 1, Vec3? outward = null)
        { Face(a, b, c, color, material, outward); Face(a, c, d, color, material, outward); }

        public void Roots()
        {
            foreach (var path in tree.Roots)
            {
                var destination = m;
                m = new MeshData();
                var samples = path.Samples;
                const int sides = 12;
                var rings = new Vec3[samples.Count, sides + 1];
                for (int i = 0; i < samples.Count; i++)
                {
                    var s = samples[i];
                    var tangent = i == 0 ? samples[1].Position - s.Position : i == samples.Count - 1
                        ? s.Position - samples[i - 1].Position : samples[i + 1].Position - samples[i - 1].Position;
                    var side = tangent.Normalized().Rotated(Math.PI / 2);
                    for (int j = 0; j <= sides; j++)
                    {
                        double a = Math.PI * j / sides;
                        var p = s.Position + side * (Math.Cos(a) * s.Width);
                        rings[i, j] = Vec3.FromXZ(p, ground(p) + Math.Sin(a) * s.Height);
                    }
                }
                for (int i = 0; i < samples.Count - 1; i++)
                    for (int j = 0; j < sides; j++)
                    {
                        double shade = .80 + .22 * Math.Sin(j * .86 + i * .27 + phase);
                        var col = Primitives.Scale(bark, shade);
                        var outward = (rings[i,j] - Vec3.FromXZ(samples[i].Position, samples[i].GroundY)).Normalized();
                        Quad(rings[i,j], rings[i+1,j], rings[i+1,j+1], rings[i,j+1], col, 1, outward);
                    }
                // Both end fans are closed; final nonzero sample makes a finite buried taper.
                for (int end = 0; end < 2; end++)
                {
                    int i = end == 0 ? 0 : samples.Count - 1;
                    var center = Vec3.FromXZ(samples[i].Position, ground(samples[i].Position) - .015);
                    var dir = (samples[end == 0 ? 1 : i - 1].Position - samples[i].Position).Normalized();
                    for (int j = 0; j < sides; j++) Face(center, rings[i,j], rings[i,j+1], bark, 1, Vec3.FromXZ(-dir, 0));
                    Face(center, rings[i,sides], rings[i,0], bark, 1, Vec3.FromXZ(-dir, 0));
                }
                // underside hidden just inside the ground; no open geometry at root margins.
                for (int i = 0; i < samples.Count - 1; i++)
                    Quad(rings[i,0], rings[i,sides], rings[i+1,sides], rings[i+1,0], bark, 1, -Vec3.Up);
                // Gametophores are open tubes: keep them out of the closed-solid clip and
                // seed them only well inside the island so nothing crosses the cut wall.
                var moss = new MeshData();
                var mossRng = Rng.Keyed(tree.Seed, "pilot.root.moss", (ulong)destination.VertexCount);
                for(int si=3;si<samples.Count-2;si+=4)
                {
                    var sample=samples[si];
                    if(sample.Height<.06)continue;
                    if(!tree.Domain.ContainsDisc(sample.Position,.04))continue;
                    var anchor=Vec3.FromXZ(sample.Position,ground(sample.Position)+sample.Height);
                    for(int shoot=0;shoot<5;shoot++)
                    {
                        double angle=shoot*2.399+mossRng.Range(-.2,.2);
                        var root=anchor+new Vec3(Math.Cos(angle)*.014,0,Math.Sin(angle)*.014);
                        double height=mossRng.Range(.009,.024);
                        var apex=root+Vec3.Up*height;
                        Primitives.Tube(moss,new[]{root,apex},new[]{.0007,.0003},5,
                            (i,v)=>(new[]{.22,.39,.095},.65,i*height,v,0,0));
                        for(int pin=0;pin<4;pin++)
                        {
                            double pa=angle+pin*2.399;
                            var leaf=root+Vec3.Up*(height*(.25+.17*pin))
                                +new Vec3(Math.Cos(pa)*.0025,.001,Math.Sin(pa)*.0025);
                            // Double-sided lanceolate phyllid (no degenerate ellipsoid poles at mm scale).
                            var outDir=new Vec3(Math.Cos(pa),.35,Math.Sin(pa)).Normalized();
                            var sideDir=new Vec3(-Math.Sin(pa),0,Math.Cos(pa));
                            var upN=outDir.Cross(sideDir).Normalized();
                            if(upN.Y<0)upN=-upN;
                            var leafBase=leaf-outDir*.0013;
                            var leafTip=leaf+outDir*.0024;
                            var leafCol=new[]{.29,.48,.12};
                            for(int face=0;face<2;face++)
                            {
                                var fn=face==0?upN:-upN;
                                var lift=fn*.00008;
                                int v0=moss.AddVertex(leafBase+lift,fn,leafCol,.65,0,.5,0,0);
                                int v1=moss.AddVertex(leaf-sideDir*.0011+lift,fn,leafCol,.65,.45,0,0,0);
                                int v2=moss.AddVertex(leafTip+lift,fn,leafCol,.65,1,.5,0,0);
                                int v3=moss.AddVertex(leaf+sideDir*.0011+lift,fn,leafCol,.65,.45,1,0,0);
                                Primitives.TriangleFacing(moss,v0,v1,v2,fn);
                                Primitives.TriangleFacing(moss,v0,v2,v3,fn);
                            }
                        }
                    }
                }
                destination.Append(IslandMeshClipper.ClipClosed(m, tree.Domain, bark));
                destination.Append(moss);
                m = destination;
            }
        }

        // ---- Bark surface -------------------------------------------------
        // One analytic radial displacement carries the flutes, the basal
        // buttress flare and the healed scars, so the dense grid cannot tear.
        private double BarkRelief(double a, double t)
        {
            double fade = Math.Pow(Math.Max(0, 1 - t), 1.3);
            double drift = .9 * Math.Sin(t * 3.4 + phase * 1.7) + .35 * Math.Sin(t * 8.1);
            // Rounded ridges with narrow valleys: a bark flute is not a sine wave.
            double flute = Math.Pow(.5 + .5 * Math.Cos(a * 17 + phase + drift), .62) - .5;
            double fine = .5 + .5 * Math.Sin(a * 43 - phase * 2.1 + t * 11.7);
            // Basal buttresses are broad radial fins where the root crown meets
            // the bole; they fade out below the first limbs.
            double baseFlare = Math.Exp(-t * 9.5) * (.5 + .5 * Math.Sin(a * 5 + phase * 2.3));
            double relief = flute * (.034 * fade + .014) + (fine - .5) * .008 * fade
                + baseFlare * .030;
            if (tree.Def.Id == "crowncoil") relief *= .45; // armor dominates this bole
            else if (tree.Def.Id == "cisternvault") relief = relief * .35 + .018 * Math.Sin(t * 18.0 + Math.Sin(a * 4.0));
            return relief;
        }

        /// <summary>Healed branch wounds: a shallow pit ringed by a raised callus roll.</summary>
        private double Callus(double a, double t)
        {
            double bump = 0;
            foreach (var scar in scars)
            {
                double dt = (t - scar.T) / scar.Aw;
                double da = MathD.WrapAngle(a - scar.A) / scar.Hw;
                double d2 = dt * dt + da * da;
                if (d2 >= 1) continue;
                double d = Math.Sqrt(d2), window = 1 - d2;
                bump += scar.Amp * window * window
                    * (1.9 * Math.Exp(-Math.Pow((d - .66) / .30, 2)) - .55);
            }
            return bump;
        }

        private Vec3 BarkPoint(double t, double a)
        {
            double radius = Radius(t) * (1 + BarkRelief(a, t) + Callus(a, t));
            double y = t >= 1 && tree.Def.Id == "palehollow"
                ? -.032 * h * (.5 + .5 * Math.Sin(a * 7 + phase)) : 0;
            return Centre(t) + new Vec3(Math.Cos(a) * radius, y, Math.Sin(a) * radius);
        }

        private Vec3 BarkNormal(double t, double a, double da, double dt)
        {
            var alongT = BarkPoint(Math.Min(1, t + dt), a) - BarkPoint(Math.Max(0, t - dt), a);
            var alongA = BarkPoint(t, a + da) - BarkPoint(t, a - da);
            var n = alongT.Cross(alongA).Normalized();
            if (n.LengthSq < 1e-10) n = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            return n;
        }

        private double BarkShade(double a, double t)
        {
            double band = .5 + .5 * Math.Sin(a * 17 + phase + .9 * Math.Sin(t * 3.4 + phase * 1.7));
            double fine = .5 + .5 * Math.Sin(a * 43 - phase * 2.1 + t * 11.7);
            double weather = .5 + .5 * Math.Sin(t * 21.0 + phase * 3.1);
            return .78 + .16 * band + .07 * fine + .05 * weather;
        }

        public void Trunk()
        {
            bool pale = tree.Def.Id == "palehollow", cycad = tree.Def.Id == "crowncoil";
            // Spec density for the three fluted boles; the lighter forms keep a
            // 64x88 grid so their heavy crowns stay inside the native budget.
            bool dense = cycad || tree.Def.Id is "gloomspire" or "emberpillar" or "cisternvault" or "rootcloister";
            int rows = dense ? 120 : 88, sides = dense ? 96 : 64;
            // These spreading species lose apical dominance at the main fork.
            // Their bole ends there; the crown axes supply the remaining height.
            double boleTop = tree.Def.Id switch
            {
                "cinnabarcrest" => .75, "rootcloister" => .79,
                "silkcascade" => .84, "basinwarden" => .84, _ => 1
            };
            double startAngle = facing - Math.PI / 3;
            double sweep = 2 * Math.PI / 3;
            double dA = .08 * sweep / sides, dT = .5 / rows;

            var rings = new Vec3[rows + 1, sides + 1];
            var normals = new Vec3[rows + 1, sides + 1];
            for (int i = 0; i <= rows; i++)
            {
                double t = boleTop * i / rows;
                for (int j = 0; j <= sides; j++)
                {
                    double a = startAngle + sweep * j / sides;
                    rings[i, j] = BarkPoint(t, a);
                    normals[i, j] = BarkNormal(t, a, dA, dT);
                }
            }

            bool Hole(int i, int j)
            {
                if (!pale) return false;
                double t = (i + .5) / rows;
                double a = startAngle + sweep * (j + .5) / sides;
                double delta = Math.Abs(MathD.WrapAngle(a - facing));
                return t > .025 && t < .24 && delta < .37 * Math.Sqrt(Math.Max(0, 1 - Math.Pow((t - .125) / .12, 2)));
            }

            // 1. Fluted front bark surface facing into the island. Vertices are
            // shared per grid point so the analytic surface normal is smooth;
            // the photographed bark and its POM carry the small-scale relief.
            var ids = new int[rows + 1, sides + 1];
            for (int i = 0; i <= rows; i++)
            {
                double t = boleTop * i / rows;
                for (int j = 0; j <= sides; j++)
                {
                    double a = startAngle + sweep * j / sides;
                    var col = Primitives.Scale(bark, BarkShade(a, t));
                    ids[i, j] = m.AddVertex(rings[i, j], normals[i, j], col, 1,
                        a * r, rings[i, j].Y - origin.Y);
                }
            }
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < sides; j++)
                {
                    if (Hole(i, j)) continue;
                    var faceN = normals[i, j] + normals[i, j + 1]
                        + normals[i + 1, j + 1] + normals[i + 1, j];
                    Primitives.TriangleFacing(m, ids[i, j], ids[i, j + 1],
                        ids[i + 1, j + 1], faceN);
                    Primitives.TriangleFacing(m, ids[i, j], ids[i + 1, j + 1],
                        ids[i + 1, j], faceN);
                }
            }

            // 2. Dark inner cavity for palehollow
            if (pale)
            {
                Vec3 Inner(Vec3 p) => new(origin.X + (p.X - origin.X) * .64, p.Y, origin.Z + (p.Z - origin.Z) * .64);
                for (int i = 0; i < rows; i++)
                {
                    for (int j = 0; j < sides; j++)
                    {
                        if (!Hole(i, j)) continue;
                        int k = j + 1;
                        var dark = new[] { .10, .078, .055 };
                        double midA = startAngle + sweep * (j + .5) / sides;
                        var outN = new Vec3(-Math.Cos(midA), 0, -Math.Sin(midA));
                        Quad(Inner(rings[i, j]), Inner(rings[i, k]), Inner(rings[i + 1, k]), Inner(rings[i + 1, j]), dark, 1, outN);
                        if (i == 0 || !Hole(i - 1, j)) Quad(rings[i, j], rings[i, k], Inner(rings[i, k]), Inner(rings[i, j]), Primitives.Scale(bark, .65));
                        if (i == rows - 1 || !Hole(i + 1, j)) Quad(rings[i + 1, j], Inner(rings[i + 1, j]), Inner(rings[i + 1, k]), rings[i + 1, k], Primitives.Scale(bark, .7));
                        if (j > 0 && !Hole(i, j - 1)) Quad(rings[i, j], Inner(rings[i, j]), Inner(rings[i + 1, j]), rings[i + 1, j], Primitives.Scale(bark, .6));
                        if (j < sides - 1 && !Hole(i, k)) Quad(rings[i, k], rings[i + 1, k], Inner(rings[i + 1, k]), Inner(rings[i, k]), Primitives.Scale(bark, .6));
                    }
                }
            }

            // 3. Top and bottom sector caps
            for (int j = 0; j < sides; j++)
            {
                int k = j + 1;
                Face(Centre(boleTop), rings[rows, j], rings[rows, k], Primitives.Scale(bark, .75), 0.95, Vec3.Up);
                Face(origin - Vec3.Up * .04, rings[0, k], rings[0, j], bark, 0.95, -Vec3.Up);
            }

            // 4. Solid flat back planes along the two island boundary edges meeting at CornerPoint
            var cutCol = Primitives.Scale(bark, .95);
            var outN1 = new Vec3(tree.CutNormals[1].X, 0, tree.CutNormals[1].Z);
            var outN0 = new Vec3(tree.CutNormals[0].X, 0, tree.CutNormals[0].Z);

            int cutStart1 = m.VertexCount;
            for (int i = 0; i < rows; i++)
            {
                double t0 = boleTop * i / rows, t1 = boleTop * (i + 1) / rows;
                var c0 = Centre(t0);
                var c1 = Centre(t1);
                var p0 = rings[i, 0];
                var p1 = rings[i + 1, 0];
                Quad(c0, p0, p1, c1, cutCol, 0.95, outN1);
            }
            for (int v = cutStart1; v < m.VertexCount; v++)
            {
                var p = m.Position(v);
                double dist = (new Vec2(p.X - origin.X, p.Z - origin.Z)).Length;
                m.UV[v * 2] = (float)dist;
                m.UV[v * 2 + 1] = (float)(p.Y - origin.Y);
            }

            int cutStart0 = m.VertexCount;
            for (int i = 0; i < rows; i++)
            {
                double t0 = boleTop * i / rows, t1 = boleTop * (i + 1) / rows;
                var c0 = Centre(t0);
                var c1 = Centre(t1);
                var p0 = rings[i, sides];
                var p1 = rings[i + 1, sides];
                Quad(c0, c1, p1, p0, cutCol, 0.95, outN0);
            }
            for (int v = cutStart0; v < m.VertexCount; v++)
            {
                var p = m.Position(v);
                double dist = (new Vec2(p.X - origin.X, p.Z - origin.Z)).Length;
                m.UV[v * 2] = (float)dist;
                m.UV[v * 2 + 1] = (float)(p.Y - origin.Y);
            }

            if (cycad) Armor();
            if (pale) Plates();
        }

        private void Armor()
        {
            // Persistent leaf bases form offset shield rows. Their raised keels and curled lower tips
            // are intentionally different from a regular real cycad diamond tessellation.
            for (int row = 0; row < 29; row++)
            {
                for (int j = 0; j < 18; j++)
                {
                    double t = .022 + row * .030, a = Tau * (j + (row % 2) * .5) / 18 + phase;
                    double delta = Math.Abs(MathD.WrapAngle(a - facing));
                    if (delta > Math.PI / 3 - 0.12) continue;
                    // Independent ornament stream: preserve the shared draws that define crown/support anatomy.
                    var detail = Rng.Keyed(tree.Seed, "pilot.armor.breakup", (ulong)row, (ulong)j);
                    t += detail.Range(-.005, .005);
                    a = facing + Math.Clamp(MathD.WrapAngle(a - facing) + detail.Range(-.027, .027), -Math.PI / 3 + .14, Math.PI / 3 - .14);
                    var n = new Vec3(Math.Cos(a), 0, Math.Sin(a)); var side = new Vec3(-n.Z, 0, n.X);
                    var c = Centre(t) + n * Radius(t);
                    double w = Math.Min(r * .19 * detail.Range(.72, 1.12), Radius(t) * (Math.PI / 3 - Math.Abs(MathD.WrapAngle(a - facing))) * .88);
                    double hh = h * .024 * detail.Range(.76, 1.15);
                    Vec3 top = c + Vec3.Up * hh + side * w * detail.Range(-.22, .22),
                        right = c + side * w * detail.Range(.80, 1.08) + Vec3.Up * hh * detail.Range(-.25, .25),
                        low = c - Vec3.Up * hh * detail.Range(.82, 1.12) + n * r * detail.Range(.009, .024) + side * w * detail.Range(-.18, .18),
                        left = c - side * w * detail.Range(.80, 1.08) + Vec3.Up * hh * detail.Range(-.25, .25);
                    var ridge = c + n * r * detail.Range(.035, .078) + Vec3.Up * hh * detail.Range(-.12, .26) + side * w * detail.Range(-.16, .16);
                    var col = Primitives.Scale(bark, rng.Range(.78, 1.18));
                    const int plateRows = 8, plateCols = 8;
                    Vec3 Plate(double u, double v)
                    {
                        double envelope = .16 + .84 * Math.Sin(Math.PI * u);
                        double curled = Math.Sin(Math.PI * u) * Math.Pow(1-v*v,.7);
                        return c + Vec3.Up * (hh * (1-2*u)) + side * (w*v*envelope)
                            + n * (r * (.008 + .042*curled + .010*u*u));
                    }
                    int start = m.VertexCount;
                    for (int pi=0;pi<=plateRows;pi++) for(int pj=0;pj<=plateCols;pj++)
                    {
                        double u=(double)pi/plateRows,v=2.0*pj/plateCols-1;
                        var dpU=Plate(Math.Min(1,u+.01),v)-Plate(Math.Max(0,u-.01),v);
                        var dpV=Plate(u,Math.Min(1,v+.01))-Plate(u,Math.Max(-1,v-.01));
                        var normal=dpU.Cross(dpV).Normalized();
                        if(normal.Dot(n)<0)normal=-normal;
                        m.AddVertex(Plate(u,v),normal,col,1,v*w,u*hh,0,0);
                    }
                    for(int pi=0;pi<plateRows;pi++)for(int pj=0;pj<plateCols;pj++)
                    {
                        int q=start+pi*(plateCols+1)+pj, next=q+plateCols+1;
                        Primitives.TriangleFacing(m,q,next,q+1,m.NormalAt(q));
                        Primitives.TriangleFacing(m,q+1,next,next+1,m.NormalAt(q+1));
                    }
                }
            }
        }

        private void Plates()
        {
            for (int row = 0; row < 17; row++)
            {
                for (int j = 0; j < 14; j++)
                {
                    double t = .27 + row * .037, a = Tau * (j + (row % 2) * .5) / 14 + phase;
                    double delta = Math.Abs(MathD.WrapAngle(a - facing));
                    if (delta > Math.PI / 3 - 0.12) continue;
                    var n = new Vec3(Math.Cos(a), 0, Math.Sin(a)); var side = new Vec3(-n.Z, 0, n.X);
                    var c = Centre(t) + n * Radius(t) * 1.025;
                    double w = Radius(t) * .15, hh = h * .018;
                    var tip = c + n * r * .022;
                    var col = Primitives.Scale(bark, rng.Range(.82, 1.12));
                    Face(c - side * w - Vec3.Up * hh, c + side * w - Vec3.Up * hh, tip, col, 1, n);
                    Face(c + side * w - Vec3.Up * hh, c + side * w * .9 + Vec3.Up * hh, tip, col, 1, n);
                    Face(c + side * w * .9 + Vec3.Up * hh, c - side * w * .8 + Vec3.Up * hh, tip, col, 1, n);
                    Face(c - side * w * .8 + Vec3.Up * hh, c - side * w - Vec3.Up * hh, tip, col, 1, n);
                }
            }
        }

        private void Tube(IReadOnlyList<Vec3> path, IReadOnlyList<double> radius, double[] color, int sides=10, double material=1)
        {
            var destination=m;
            m=new MeshData();
            // Primitives.Tube is transported and has analytic taper normals.
            var lengths=new double[path.Count];
            for(int i=1;i<path.Count;i++) lengths[i]=lengths[i-1]+(path[i]-path[i-1]).Length;
            int first = m.VertexCount;
            Primitives.Tube(m,path,radius,sides,(i,v)=>(color,material,v*Tau*radius[i],lengths[i],0,0));
            for(int end=0;end<2;end++)
            {
                int row=end==0?0:path.Count-1;
                var outward=end==0?(path[0]-path[1]).Normalized():(path[^1]-path[^2]).Normalized();
                for(int j=0;j<sides;j++)
                    Face(path[row],m.Position(first+row*(sides+1)+j),m.Position(first+row*(sides+1)+j+1),color,material,outward);
            }
            if(material>.5 && Enumerable.Range(0,m.VertexCount).Any(i=>tree.Domain.SignedDistance(m.Position(i).XZ)>2e-6))
                destination.Append(IslandMeshClipper.ClipClosed(m,tree.Domain,color));
            else destination.Append(m);
            m=destination;
        }
        // Every daughter axis samples the same polyline used by the woody mesh.
        // Endpoint chords float inside/outside an arched bough, especially on old wood.
        private static Vec3 OnAxis(IReadOnlyList<Vec3> path, double q)
        {
            double scaled = Math.Clamp(q, 0, 1) * (path.Count - 1);
            int i = Math.Min((int)scaled, path.Count - 2);
            return Vec3.Lerp(path[i], path[i + 1], scaled - i);
        }

        private double CrownAngle(int index, double spread) => facing + spread *
            (2 * ((index * .61803398875 + phase / Tau) % 1) - 1);

        private Vec3[] BranchAxis(double t,double angle,double length,double rise,double droop,double radius)
        {
            var start=Centre(t);var dir=new Vec3(Math.Cos(angle),0,Math.Sin(angle));
            var side=new Vec3(-dir.Z,0,dir.X);
            var path=new Vec3[9];var widths=new double[9];
            double sweep=Math.Sin(angle*3.1+t*17+phase)*length*.065;
            for(int i=0;i<path.Length;i++)
            {
                double q=i/(double)(path.Length-1);
                path[i]=start+dir*(Radius(t)*.65+q*length)
                    +side*(sweep*q*q)+Vec3.Up*(rise*q-droop*q*q);
                // A short swollen collar gives way to tapering load-bearing wood.
                widths[i]=radius*(.045+.955*Math.Pow(1-q,1.12))*(1+.16*Math.Exp(-q*18));
            }
            Tube(path,widths,Primitives.Scale(bark,.84));return path;
        }
        private Vec3 Branch(double t,double angle,double length,double rise,double droop,double radius) =>
            BranchAxis(t,angle,length,rise,droop,radius)[^1];

        public void Conifer(bool spruce,bool broken)
        {
            int levels=broken?8:spruce?12:11;
            for(int level=0;level<levels;level++)
            {
                double t=.24+(broken?.61:.73)*(level+rng.Range(.12,.88))/levels;
                int branches=broken?3+rng.NextInt(3):4+rng.NextInt(3);
                for(int j=0;j<branches;j++)
                {
                    bool dead=broken && (j==0 || (level>4 && rng.Chance(.45)));
                    if(broken&&rng.Chance(.12)) continue;
                    // Deliberately uneven branch insertion and angle avoid the
                    // mechanical whorl ladder while keeping crowns inside the cutaway wedge.
                    double a=CrownAngle(level*7+j,broken?.68:.88)+rng.Range(-.07,.07);
                    double heightFraction=Math.Clamp((t-.24)/.73,0,1);
                    double crown=broken?.66-.30*heightFraction:spruce?.84*Math.Pow(1-heightFraction,.72)+.08:.74*Math.Pow(1-heightFraction,.48)+.12;
                    double len=tree.Def.CanopyRadius*crown*rng.Range(.78,1.12);
                    var dir=new Vec3(Math.Cos(a),0,Math.Sin(a));
                    // Lower limbs fall away from the bole; upper tiers turn back
                    // toward light and close the crown into a broken tapered spire.
                    double rise=len*(broken?.18:spruce?.24+.52*heightFraction:.22+.42*heightFraction);
                    double droop=len*(broken?.42:spruce?.22-.20*heightFraction:.28-.20*heightFraction);
                    var axis=BranchAxis(t,a,len,rise,droop,r*(.15-.085*heightFraction));
                    var tip=axis[^1];
                    if(!broken)
                    {
                        for(int run=0;run<2;run++)
                        {
                            double q=run==0?.48:.80;
                            var root=OnAxis(axis,q);
                            var side=dir.Cross(Vec3.Up).Normalized()*(run==0?1:-1);
                            // Sprays continue along the arm, masking its rigid
                            // scaffold and joining adjacent tier clusters.
                            double reach=run==0?.29:.24;
                            var foliageTip=root+dir*len*reach+side*len*(run==0?.18:.15)+Vec3.Up*len*rng.Range(-.035,.075)-Vec3.Up*len*(spruce?.035:.08);
                            NeedleSpray(root,foliageTip,spruce);
                        }
                    }
                    int secondaries=dead?2:spruce?2:3;
                    for(int twig=1;twig<=secondaries;twig++)
                    {
                        double q=(twig+rng.Range(-.22,.22))/(secondaries+1.0);
                        var at=OnAxis(axis,q);
                        for(int s=-1;s<=1;s+=2)
                        {
                            double ta=a+s*rng.Range(.62,1.24)+rng.Range(-.20,.20);
                            var td=new Vec3(Math.Cos(ta),0,Math.Sin(ta));
                            double tl=len*rng.Range(.25,.40)*(1-.16*q)+r*.12;
                            var end=at+td*tl-Vec3.Up*tl*(broken?.42:spruce?.12:.22);
                            var twigAxis=new[]{at,Vec3.Lerp(at,end,.55)+Vec3.Up*tl*.08,end};
                            double twigRadius=r*.045*(1-.65*q);
                            Tube(twigAxis,
                                new[]{twigRadius,twigRadius*.55,twigRadius*.12},Primitives.Scale(bark,.79),7);
                            if(dead) continue;
                            double sl=tl*rng.Range(.58,.84);
                            for(int spray=0;spray<2;spray++)
                            {
                                double u=spray==0?.34:.88;
                                double sideA=ta+(twig%2==0?1:-1)*(spray==0?1:-1)*rng.Range(.46,.82);
                                var sd=new Vec3(Math.Cos(sideA),0,Math.Sin(sideA));
                                var root=OnAxis(twigAxis,u);
                                var se=root+sd*sl-Vec3.Up*sl*(broken?.48:spruce?.15:.34);
                                NeedleSpray(root,se,spruce);
                            }
                        }
                    }
                    if(spruce&&level%3==1&&j%3==0)
                    {
                        var coneTip=OnAxis(axis,.72)-Vec3.Up*r*.08;
                        Cone(coneTip,new Vec3(r*.075,r*.24,r*.075),new[]{.32,.24,.13});
                    }
                }
            }
            if(!broken)
            {
                for(int leader=0;leader<3;leader++)
                {
                    double a=facing+(leader-1)*.48;
                    var d=new Vec3(Math.Cos(a),0,Math.Sin(a));
                    var baseP=Centre(.88)+d*r*.08;
                    var top=Centre(leader==1?1.06:.97)+d*rng.Range(.10,.23);
                    Tube(new[]{baseP,Vec3.Lerp(baseP,top,.56)+d*r*.05,top},
                        new[]{r*.08,r*.045,r*.008},Primitives.Scale(bark,.78),7);
                    NeedleSpray(baseP,top,spruce);
                    NeedleSpray(Vec3.Lerp(baseP,top,.38)+d*r*.12,top+d*r*.22,spruce);
                }
            }
            else
            {
                for(int j=0;j<5;j++)
                {
                    double a=phase+j*2.37;
                    var end=Branch(.78+j*.027,a,r*rng.Range(1.8,3.6),h*.025,h*.035,r*.07);
                    if(j%2==0) NeedleSpray(end,end+new Vec3(Math.Cos(a),.08,Math.Sin(a))*r*.72,false);
                }
            }
        }

        private void NeedleSpray(Vec3 start,Vec3 end,bool spruce)
        {
            var axis=(end-start).Normalized();
            var side=axis.Cross(Vec3.Up).Normalized();if(side.LengthSq<.1)side=new Vec3(1,0,0);
            int leaves=spruce?10:9;
            double clump=rng.Range(.82,1.18);
            for(int i=0;i<leaves;i++)
            {
                // Small irregular gaps between short, overlapping needle fans
                // keep the spray from reading as a perfectly combed ladder.
                if(i>1&&i<leaves-2&&rng.Chance(.12))continue;
                double q=Math.Clamp((i+rng.Range(.12,.84))/leaves,0,1);
                var at=Vec3.Lerp(start,end,q);
                double size=(end-start).Length*(.31+.27*Math.Sin(q*Math.PI))*clump*rng.Range(.82,1.18);
                for(int sign=-1;sign<=1;sign+=2)
                {
                    double lift=rng.Range(-.18,.20);
                    var lateral=at+side*(sign*size*rng.Range(.66,.88))+axis*size*.20+Vec3.Up*size*lift-Vec3.Up*size*(spruce?.08:.18);
                    var tip=lateral+axis*size*rng.Range(.27,.42)+side*(sign*size*rng.Range(.18,.32))-Vec3.Up*size*(spruce?.16:.38);
                    // One continuous needle per side avoids doubled base faces
                    // while the curled centerline still gives each spray depth.
                    NeedleBlade(at,tip,size*(spruce?.16:.28),Primitives.Scale(green,rng.Range(.72,1.22)));
                }
            }
        }

        private void NeedleBlade(Vec3 baseP, Vec3 tip, double width, double[] color)
        {
            CurvedBlade(baseP,tip,width,color,2,rng.Range(-.72,.72),width*rng.Range(.12,.32),rng.Range(-.12,.14),1);
        }

        // A single cull-disabled surface has a rolled, folded cross section and
        // gently swept centerline. Per-vertex differential normals preserve the
        // blade's curved volume without coincident backfaces fighting the shader.
        private void Blade(Vec3 baseP,Vec3 tip,double width,double[] color,int segments=4)
        {
            var axis = tip - baseP;
            double length = axis.Length;
            if (length < 1e-6) return;
            // Keep the crown's existing placement draws; geometry detail has an independent seed.
            ulong key = Rng.Mix(tree.Seed, (ulong)m.Leaves.Count + 0x50494C4FUL);
            int leafCount = m.Leaves.Count;
            var leafOutline = tree.Def.Id is "crowncoil" or "cinnabarcrest" or "silkcascade"
                ? BroadleafOutline.Lanceolate : (tree.Def.Id == "rootcloister" ? BroadleafOutline.Cordate : BroadleafOutline.Ovate);
            Vivarium.Sim.Geometry.Form.Broadleaf.BuildLeaf(m, baseP, axis / length,
                length * .92, width * 2, leafOutline,
                key, color, Primitives.Scale(color, 1.08), camber: .20, midribFold: .16,
                petioleLength: length * .08, petioleRadius: Math.Max(.0007, length * .005),
                petioleKink: .12, detailOverride: 1);
            for (int k = leafCount; k < m.Leaves.Count; k++)
            {
                var leaf = m.Leaves[k];
                for (int i = leaf.FirstVertex; i < leaf.FirstVertex + leaf.VertexCount; i++)
                    m.Colors[i * 4 + 3] = 0;
            }
            // Small persistent stipules at the petiole insertion; woody tissue retains its shader.
            var side = (axis / length).Cross(Vec3.Up).Normalized();
            if (side.LengthSq < 1e-8) side = new Vec3(1,0,0);
            foreach (int sign in new[] { -1, 1 })
                Primitives.Tube(m, new[] { baseP, baseP + axis * .035 + side * (sign * length * .022),
                    baseP + axis * .07 + side * (sign * length * .027) },
                    new[] { length * .006, length * .005, length * .0008 }, 5,
                    (i,v)=>(Primitives.Scale(color,.7),1,i*.02,v,0,0));
        }

        private void CurvedBlade(Vec3 baseP,Vec3 tip,double halfWidth,double[] color,int longitudinal,double roll,double camber,double sweep,int across=2)
        {
            var axis=tip-baseP;double length=axis.Length;
            if(length<1e-8||halfWidth<=0)return;
            var dir=axis/length;
            var side=dir.Cross(Vec3.Up);
            if(side.LengthSq<1e-8)side=dir.Cross(new Vec3(1,0,0));
            side=side.Normalized();
            var normal=side.Cross(dir).Normalized();
            // Roll the blade around its own growth axis, independently from the
            // pitch/curl that places the leaflet in the crown.
            var rolledSide=side*Math.Cos(roll)+normal*Math.Sin(roll);
            var rolledNormal=normal*Math.Cos(roll)-side*Math.Sin(roll);
            int first=m.VertexCount;
            int firstIndex=m.Indices.Count;
            Vec3 At(double t,double x)
            {
                double envelope=Math.Max(.045,Math.Pow(Math.Max(0,Math.Sin(Math.PI*t)),.68));
                double width=halfWidth*envelope*(1+.11*x*Math.Sin(phase+baseP.X*1.7));
                double twist=roll+(t-.5)*sweep;
                var localSide=side*Math.Cos(twist)+normal*Math.Sin(twist);
                var localNormal=normal*Math.Cos(twist)-side*Math.Sin(twist);
                double fold=halfWidth*.19*(1-x*x)*Math.Sin(Math.PI*t);
                double curl=camber*Math.Sin(Math.PI*t)+length*.055*t*t;
                double lateralSweep=length*.018*Math.Sin(2*Math.PI*t+phase+tip.Z)*t;
                return baseP+axis*t+localSide*(width*x+lateralSweep)+localNormal*(fold+curl);
            }
            for(int i=0;i<=longitudinal;i++)
            {
                double t=i/(double)longitudinal;
                for(int j=0;j<=across;j++)
                {
                    double x=j/(double)across*2-1;
                    var p=At(t,x);
                    var along=At(Math.Min(1,t+1.0/longitudinal),x)-At(Math.Max(0,t-1.0/longitudinal),x);
                    var lateral=At(t,Math.Min(1,x+.25))-At(t,Math.Max(-1,x-.25));
                    var n=along.Cross(lateral).Normalized();
                    if(n.LengthSq<1e-8)n=rolledNormal;
                    if(n.Dot(rolledNormal)<0)n=-n;
                    double shade=.86+.14*(1-Math.Abs(x))+.05*Math.Sin(t*Math.PI+phase);
                    m.AddVertex(p,n,Primitives.Scale(color,shade),0,t,.5+.5*x);
                }
            }
            int row=across+1;
            for(int i=0;i<longitudinal;i++)for(int j=0;j<across;j++)
            {
                int a=first+i*row+j,b=a+row;
                Primitives.TriangleFacing(m,a,b,a+1,m.NormalAt(a));
                Primitives.TriangleFacing(m,a+1,b,b+1,m.NormalAt(a+1));
            }
            m.RecordLeaf(first,firstIndex,baseP,length);
            // Dedicated leaf.gdshader consumes base-tip in U and across in V.
            for(int i=first;i<m.VertexCount;i++)m.Colors[i*4+3]=0;
        }

        // Foliage shaders consume a stable leaf-local coordinate: U crosses the
        // blade and U runs from attachment to tip, independent of world placement.
        private void SetLeafUv(int first,Vec3 baseP,Vec3 tip,Vec3 across,double width)
        {
            var axis=tip-baseP;double lengthSq=Math.Max(1e-12,axis.LengthSq);
            for(int i=first;i<m.VertexCount;i++)
            {
                var p=m.Position(i)-baseP;
                double v=Math.Clamp(p.Dot(axis)/lengthSq,0,1);
                double u=Math.Clamp(.5+p.Dot(across)/Math.Max(1e-9,2*width*across.LengthSq),0,1);
                m.UV[i*2]=(float)v;m.UV[i*2+1]=(float)u;
                m.Colors[i*4+3]=0;
            }
        }

        public void Cycad()
        {
            // Dense, staggered crown of arching pinnate fronds. Fronds vary in age,
            // lean and droop; each carries many paired leathery leaflets on a stout rachis.
            for(int tier=0;tier<3;tier++) for(int f=0;f<13;f++)
            {
                double a=phase+Tau*(f+tier*.37)/13+rng.Range(-.07,.07);
                double age=rng.Range(.78,1.13);
                double length=tree.Def.CanopyRadius*rng.Range(.74,1.04)*age*(tier==0?.76:1);
                var dir=new Vec3(Math.Cos(a),0,Math.Sin(a));var side=new Vec3(-dir.Z,0,dir.X);
                var start=Centre(.91-tier*.055)+dir*r*.16;
                double tipFall=rng.Range(.04,tier==2?.30:.12);
                double sweep=rng.Range(-.10,.10);
                var path=new Vec3[19];var rad=new double[19];
                for(int i=0;i<path.Length;i++)
                {
                    double t=i/18.0;
                    double rise=tier==0?.94:tier==1?.66:.34;
                    double fall=tier==0?.62:tier==1?.70:.88;
                    double arch=length*(rise*t-fall*t*t)-length*Math.Pow(t,3)*tipFall;
                    path[i]=start+dir*(length*t)+side*(length*sweep*t*t)+Vec3.Up*arch;
                    rad[i]=r*(.037*(1-t)+.0025);
                }
                Tube(path,rad,Primitives.Scale(green,.71),9,1);
                for(int i=1;i<18;i++)
                {
                    double t=i/18.0;
                    double leaflet=length*(.025+.23*Math.Pow(Math.Sin(Math.PI*t),.7))*rng.Range(.88,1.13);
                    for(int sign=-1;sign<=1;sign+=2)
                    {
                        double cant=tier==0?rng.Range(-.28,.58):tier==1?rng.Range(-.48,.48):rng.Range(-.92,-.24);
                        cant = tier==2 ? -Math.Abs(cant) : cant*.55;
                        var end=path[i]+side*(leaflet*sign)+dir*leaflet*.20+Vec3.Up*leaflet*cant;
                        // The leaflet blade grows directly from the rachis. A stalk
                        // tube at every pinna only reads as wire at normal camera scale.
                        Blade(path[i],end,leaflet*rng.Range(.18,.23),Primitives.Scale(green,rng.Range(.78,1.12)),5);
                    }
                }
            }
            Cone(Centre(.96)+Vec3.Up*r*.42,new Vec3(r*.48,r*.94,r*.48),new[]{.65,.38,.16});
            Cone(Centre(.93)+new Vec3(r*.48,r*.22,-r*.26),new Vec3(r*.34,r*.64,r*.34),new[]{.42,.44,.20});
        }

        private void Cone(Vec3 centre,Vec3 radii,double[] color)
        {
            var (verts,tris)=Primitives.Icosphere(1);
            int offset=m.VertexCount;
            foreach(var v in verts)
            {
                var p=centre+new Vec3(v.X*radii.X,v.Y*radii.Y,v.Z*radii.Z);
                var n=new Vec3(v.X/radii.X,v.Y/radii.Y,v.Z/radii.Z).Normalized();
                m.AddVertex(p,n,Primitives.Scale(color,.83+.17*(v.Y+1)/2),.35,v.X,v.Y);
            }
            foreach(int t in tris)m.Indices.Add(offset+t);
        }

        public void Redwood()
        {
            // Tall, broken, tiered crown. Reiterated leaders emerge from a compact
            // crown mass while old lower limbs remain as blunt, bark-heavy stubs.
            for(int j=0;j<7;j++)
            {
                double a=phase+j*2.399+rng.Range(-.18,.18);
                var end=Branch(.30+j*.052,a,r*rng.Range(1.3,2.3),r*.25,r*.18,r*.10);
                if(j%2==0) Tube(new[]{end,end+Vec3.Up*r*.25,end+Vec3.Up*r*.42},
                    new[]{r*.075,r*.047,r*.006},Primitives.Scale(bark,.72),8);
            }
            // Reiterated leaders rise above the bole rather than leaving a bare spike.
            for(int leader=0;leader<3;leader++)
            {
                double a=facing+(-.58+leader*.58)+rng.Range(-.10,.10);
                var d=new Vec3(Math.Cos(a),0,Math.Sin(a));
                var start=Centre(.80+rng.Range(-.035,.035))+d*r*.10;
                var end=Centre(1.02+leader*.065+rng.Range(-.02,.02))+d*r*rng.Range(.48,1.12);
                var mid=Vec3.Lerp(start,end,.55)+d*r*.16;
                var leaderAxis=new[]{start,mid,end};
                Tube(leaderAxis,new[]{r*.16,r*.085,r*.012},Primitives.Scale(bark,.76),8);
                NeedleSpray(start,end,false);
                for(int fork=0;fork<3;fork++)
                {
                    double q=.35+fork*.24+rng.Range(-.05,.05);
                    var root=OnAxis(leaderAxis,q);
                    double fa=a+(fork%2==0?1:-1)*rng.Range(.56,.95);
                    var fd=new Vec3(Math.Cos(fa),0,Math.Sin(fa));
                    var forkTip=root+fd*r*rng.Range(.70,1.30)+Vec3.Up*r*rng.Range(.34,.76);
                    Tube(new[]{root,Vec3.Lerp(root,forkTip,.58)+Vec3.Up*r*.08,forkTip},
                        new[]{r*.06,r*.032,r*.006},Primitives.Scale(bark,.73),7);
                    NeedleSpray(root,forkTip,false);
                    NeedleSpray(Vec3.Lerp(root,forkTip,.52),forkTip+fd*r*.16,false);
                }
            }
            for(int tier=0;tier<5;tier++)
            {
                int limbs=4+tier%2;
                for(int crown=0;crown<limbs;crown++)
                {
                    double a=facing+rng.Range(-.78,.78)+Math.Sin(tier*1.9+crown*2.3)*.12;
                    double t=.58+tier*.082+rng.Range(-.028,.028);
                    double len=tree.Def.CanopyRadius*rng.Range(.82,1.18)*(1-.10*tier);
                    var axis=BranchAxis(t,a,len,len*.37,len*.16,r*.14*(1-.12*tier));
                    var tip=axis[^1];
                    var d=new Vec3(Math.Cos(a),0,Math.Sin(a));
                    for(int run=0;run<2;run++)
                    {
                        double u=run==0?.66:.90;
                        var root=OnAxis(axis,u);
                        var lateral=d.Cross(Vec3.Up).Normalized()*(run==0?1:-1);
                        NeedleSpray(root,root+d*len*.12+lateral*len*.10-Vec3.Up*len*.035,false);
                    }
                    for(int twig=0;twig<5;twig++)
                    {
                        double q=(twig+.7)/5.8;
                        var at=OnAxis(axis,q);
                        for(int sign=-1;sign<=1;sign+=2)
                        {
                            double ta=a+sign*rng.Range(.74,1.02);
                            var td=new Vec3(Math.Cos(ta),0,Math.Sin(ta));
                            double sl=len*rng.Range(.20,.34);
                            var se=at+td*sl+Vec3.Up*sl*.08;
                            Tube(new[]{at,Vec3.Lerp(at,se,.55),se},new[]{r*.019,r*.009,r*.0015},Primitives.Scale(bark,.78),6);
                            NeedleSpray(at,se,false);
                            var cross=td.Cross(Vec3.Up).Normalized();
                            NeedleSpray(Vec3.Lerp(at,se,.42),se+cross*sl*.28,false);
                            NeedleSpray(Vec3.Lerp(at,se,.58),se-cross*sl*.24,false);
                        }
                    }
                }
            }
        }

        public void Broadleaf()
        {
            // Irregular, tiered crown with sweeping structural limbs, fine twigs,
            // overlapping broad blades and small fruit on outer branchlets.
            for(int limb=0;limb<12;limb++)
            {
                double a=CrownAngle(limb,.86)+rng.Range(-.06,.06);
                double t=.46+(limb%5)*.083+rng.Range(-.025,.025);
                double tier=(t-.54)/.40;
                double length=tree.Def.CanopyRadius*(.90-.16*tier)*rng.Range(.78,1.04);
                var axis=BranchAxis(t,a,length,length*rng.Range(.38,.60),length*rng.Range(.16,.32),r*(.25-.10*tier));
                for(int twig=0;twig<8;twig++)
                {
                    double q=.24+.71*(twig+.5)/8;
                    var at=OnAxis(axis,q);
                    double ta=a+(twig%2==0?1:-1)*rng.Range(.58,1.02);
                    var dir=new Vec3(Math.Cos(ta),0,Math.Sin(ta));
                    double twigLength=length*rng.Range(.20,.31);
                    var end=at+dir*twigLength+Vec3.Up*twigLength*rng.Range(.20,.47);
                    var twigAxis=new[]{at,Vec3.Lerp(at,end,.48)+Vec3.Up*r*.12,end};
                    Tube(twigAxis,new[]{r*.060*(1-.45*q),r*.021,r*.002},Primitives.Scale(bark,.82),7);
                    for(int leaf=0;leaf<7;leaf++)
                    {
                        double u=(leaf+.5)/7.0;
                        var attach=OnAxis(twigAxis,u);
                        double la=ta+(leaf%2==0?1:-1)*rng.Range(.72,1.18);
                        double ll=r*rng.Range(.38,.66);
                        double pitch=rng.Range(-.38,.62);
                        var leafTip=attach+new Vec3(Math.Cos(la)*ll*Math.Cos(pitch),ll*Math.Sin(pitch),Math.Sin(la)*ll*Math.Cos(pitch))-Vec3.Up*ll*rng.Range(.02,.15);
                        Blade(attach,leafTip,ll*rng.Range(.25,.34),Primitives.Scale(green,rng.Range(.76,1.20)),5);
                        if(leaf%2==0)
                        {
                            double opposite=la+(leaf%4==0?.72:-.72);
                            double secondPitch=pitch+rng.Range(-.25,.25);
                            var second=attach+new Vec3(Math.Cos(opposite)*ll*.82*Math.Cos(secondPitch),ll*.82*Math.Sin(secondPitch),Math.Sin(opposite)*ll*.82*Math.Cos(secondPitch))-Vec3.Up*ll*.07;
                            Blade(attach,second,ll*.25,Primitives.Scale(green,rng.Range(.82,1.16)),5);
                        }
                    }
                    if(twig%3==0) Cone(end-Vec3.Up*r*.13,new Vec3(r*.09,r*.14,r*.09),new[]{.57,.32,.15});
                }
            }
        }

        public void Fungi()
        {
            // Uneven groups of persistent woody polypores, anchored into bark rather than floating disks.
            for(int group=0;group<8;group++)
            {
                double a=facing+rng.Range(-.80,.80), t=rng.Range(.035,.35);
                int shelves=2+rng.NextInt(4);
                double attachment = t;
                for(int i=0;i<shelves;i++)
                {
                    var detail = Rng.Keyed(tree.Seed, "pilot.fungi.breakup", (ulong)group, (ulong)i);
                    double step = rng.Range(.014,.027); // Retain the original shared RNG consumption.
                    if (i > 0) attachment += step * detail.Range(.72, 1.28);
                    double y = attachment + detail.Range(-.003, .003);
                    double angle = a + detail.Range(-.07, .07);
                    var n=new Vec3(Math.Cos(angle),0,Math.Sin(angle));
                    var c=Centre(y)+n*Radius(y)*.96;
                    Shelf(c,n,r*rng.Range(.13,.34)*(1-.07*i)*detail.Range(.82,1.10), detail.NextULong());
                }
            }
        }
        private void Shelf(Vec3 center,Vec3 outward,double size, ulong ornamentKey)
        {
            var side=new Vec3(-outward.Z,0,outward.X);
            const int count=48, rings=24;
            var top=new Vec3[rings+1,count+1];var bottom=new Vec3[rings+1,count+1];
            double asym=rng.Range(.85,1.15),phase2=rng.Range(0,Tau);
            var detail = Rng.Keyed(tree.Seed, "pilot.shelf.breakup", ornamentKey);
            double depth = detail.Range(.60, .81), skew = detail.Range(-.13, .13), crownHeight = detail.Range(.15, .24);
            double lobe = detail.Range(.012, .046), scallopPhase = detail.Range(0, Tau);
            for(int ring=0;ring<=rings;ring++) for(int j=0;j<=count;j++)
            {
                double q=ring/(double)rings, a=Math.PI*j/count;
                double scallop=1+lobe*Math.Sin(a*7+scallopPhase)+.017*Math.Sin(a*13-phase2)+.035*Math.Sin(a*2+scallopPhase);
                var p=center+side*((Math.Cos(a)*asym+Math.Sin(a)*skew)*size*q*scallop)+outward*(Math.Sin(a)*size*q*depth*scallop);
                double crown=size*(crownHeight*(1-q*q)+.015*Math.Sin(q*34+phase2)*(1-q)+.025*Math.Sin(a*2+scallopPhase)*q);
                double thick=size*(.032+.09*(1-q));
                top[ring,j]=p+Vec3.Up*crown;
                bottom[ring,j]=p+Vec3.Up*(crown-thick);
            }
            for(int ring=0;ring<rings;ring++) for(int j=0;j<count;j++)
            {
                double q=(ring+.5)/rings;
                double band=.77+.15*Math.Sin(q*35+phase2)+.055*Math.Sin(q*73);
                var color=q>.84?new[]{.69,.60,.43}:Primitives.Scale(new[]{.36,.22,.12},band);
                Quad(top[ring,j],top[ring+1,j],top[ring+1,j+1],top[ring,j+1],color,.35,Vec3.Up);
                Quad(bottom[ring,j+1],bottom[ring+1,j+1],bottom[ring+1,j],bottom[ring,j],new[]{.54,.48,.34},.25,-Vec3.Up);
            }
            for(int j=0;j<count;j++)
                Quad(top[rings,j],bottom[rings,j],bottom[rings,j+1],top[rings,j+1],new[]{.74,.65,.47},.35,outward);
            for(int ring=0;ring<rings;ring++)foreach(int j in new[]{0,count})
                Quad(top[ring,j],bottom[ring,j],bottom[ring+1,j],top[ring+1,j],new[]{.27,.18,.10},.35);
        }

        public void Baobab()
        {
            // Massive, crooked "upside-down" boughs emerging from the swollen crown table,
            // clustered broadleaf foliage and pendulous gourd capsules on long cords.
            int limbs = 8;
            for (int limb = 0; limb < limbs; limb++)
            {
                double a = CrownAngle(limb, .83) + rng.Range(-.045, .045);
                double t = .73 + (limb % 3) * .087 + rng.Range(-.025, .025);
                double length = tree.Def.CanopyRadius * rng.Range(.48, .79);
                var dir = new Vec3(Math.Cos(a), 0, Math.Sin(a));
                var side = new Vec3(-dir.Z, 0, dir.X);
                var path = new Vec3[8];
                var rad = new double[8];
                var start = Centre(t) + dir * (Radius(t) * .62);
                for (int i = 0; i < path.Length; i++)
                {
                    double q = i / 7.0;
                    double kinkA = Math.Sin(q * 4.2 + limb * 1.7) * .08;
                    double kinkY = Math.Cos(q * 3.5 + limb) * .08;
                    path[i] = start + dir * (length * q) + side * (length * kinkA * q)
                        + Vec3.Up * (length * (.42 * q + .15 * q * q) + length * kinkY * q);
                    rad[i] = Math.Max(r * .035, r * .22 * Math.Pow(1 - q, .70));
                }
                Tube(path, rad, Primitives.Scale(bark, .84), 8);

                for (int fork = 0; fork < 3; fork++)
                {
                    double q = .45 + fork * .24 + rng.Range(-.04, .04);
                    var forkRoot = OnAxis(path,q);
                    double fa = facing + Math.Clamp(a - facing + (fork % 2 == 0 ? 1 : -1) * rng.Range(.22, .42), -Math.PI / 3 + .16, Math.PI / 3 - .16);
                    var fdir = new Vec3(Math.Cos(fa), 0, Math.Sin(fa));
                    double flen = length * rng.Range(.20, .30);
                    var fmid = forkRoot + fdir * (flen * .55) + Vec3.Up * (flen * .28);
                    var fend = forkRoot + fdir * flen + Vec3.Up * (flen * .48);
                    Tube(new[] { forkRoot, fmid, fend }, new[] { r * .055, r * .028, r * .006 }, Primitives.Scale(bark, .82), 7);

                    for (int lf = 0; lf < 6; lf++)
                    {
                        double la = facing + Math.Clamp(fa - facing + rng.Range(-.40, .40), -Math.PI / 3 + .14, Math.PI / 3 - .14);
                        double pitch = rng.Range(-.15, .65);
                        double ll = r * rng.Range(.28, .44);
                        var leafBase=Vec3.Lerp(fmid,fend,.35+.65*(lf+.5)/6);
                        var ltip = leafBase + new Vec3(Math.Cos(la) * ll * Math.Cos(pitch), ll * Math.Sin(pitch), Math.Sin(la) * ll * Math.Cos(pitch));
                        Blade(leafBase, ltip, ll * .26, Primitives.Scale(green, rng.Range(.80, 1.18)), 4);
                    }

                    if (fork == 1)
                    {
                        double cordLen = r * rng.Range(.40, .75);
                        var cordTop = forkRoot - Vec3.Up * .02;
                        var cordBot = cordTop - Vec3.Up * cordLen;
                        Tube(new[] { cordTop, cordBot }, new[] { .006, .005 }, Primitives.Scale(bark, .70), 7);
                        Cone(cordBot - Vec3.Up * (r * .08), new Vec3(r * .075, r * .15, r * .075), new[] { .44, .38, .22 });
                    }
                }
            }
        }

        public void Cinnabar()
        {
            // Dichotomous candelabra umbrella canopy: primary scaffold boughs fork
            // repeatedly to form a dense hemispherical dome crowned with stiff rosettes.
            int primaries = 6;
            for (int p = 0; p < primaries; p++)
            {
                double a = CrownAngle(p, .78) + rng.Range(-.04, .04);
                double t0 = .61 + (p%3)*.055 + rng.Range(-.02, .03);
                var dir0 = new Vec3(Math.Cos(a), 0, Math.Sin(a));
                double r0 = Radius(t0) * .65;
                var baseP = Centre(t0) + dir0 * r0;
                double totalR = tree.Def.CanopyRadius * rng.Range(.68, .82);
                double len0 = totalR * .42;
                var fork0 = baseP + dir0 * (len0 * .85) + Vec3.Up * (h * .10);
                Tube(new[] { baseP, Vec3.Lerp(baseP, fork0, .52) + Vec3.Up * .06, fork0 },
                    new[] { r * .16, r * .11, r * .068 }, Primitives.Scale(bark, .85), 8);

                for (int s1 = -1; s1 <= 1; s1 += 2)
                {
                    double a1 = facing + Math.Clamp(a - facing + s1 * .32 + rng.Range(-.03, .03), -Math.PI / 3 + .16, Math.PI / 3 - .16);
                    var dir1 = new Vec3(Math.Cos(a1), 0, Math.Sin(a1));
                    double len1 = totalR * (s1<0?.37:.28);
                    var fork1 = fork0 + dir1 * (len1 * .85) + Vec3.Up * (h * .08);
                    Tube(new[] { fork0, Vec3.Lerp(fork0, fork1, .55) + Vec3.Up * .04, fork1 },
                        new[] { r * .068, r * .044, r * .026 }, Primitives.Scale(bark, .82), 7);

                    for (int s2 = -1; s2 <= 1; s2 += 2)
                    {
                        double a2 = facing + Math.Clamp(a1 - facing + s2 * .24 + rng.Range(-.02, .02), -Math.PI / 3 + .16, Math.PI / 3 - .16);
                        var dir2 = new Vec3(Math.Cos(a2), 0, Math.Sin(a2));
                        double len2 = totalR * rng.Range(.20,.30);
                        var tip = fork1 + dir2 * len2 + Vec3.Up * (h * .05);
                        Tube(new[] { fork1, tip }, new[] { r * .026, r * .012 }, Primitives.Scale(bark, .79), 7);

                        int leaves = 7;
                        for (int lf = 0; lf < leaves; lf++)
                        {
                            double leafA = a2 + lf * 2.399963 + rng.Range(-.12,.12);
                            double leafPitch = .22 + .85*(lf%3)/2.0 + rng.Range(-.12,.12);
                            double leafLen = r * rng.Range(.30, .48);
                            var ldir = new Vec3(Math.Cos(leafA) * Math.Cos(leafPitch), Math.Sin(leafPitch), Math.Sin(leafA) * Math.Cos(leafPitch));
                            var ltip = tip + ldir * leafLen;
                            Blade(tip, ltip, leafLen * .18, Primitives.Scale(green, rng.Range(.82, 1.15)), 4);
                        }

                        Cone(tip - Vec3.Up * .02, new Vec3(r * .04, r * .06, r * .04), new[] { .74, .14, .09 });
                    }
                }
            }
        }

        public void Banyan()
        {
            // Wide sprawling horizontal limbs with vertical aerial drop-roots forming a vaulted cloister,
            // dense broadleaf foliage and small paired fig fruit.
            int limbs = 8;
            for (int limb = 0; limb < limbs; limb++)
            {
                double a = CrownAngle(limb, .83) + rng.Range(-.045, .045);
                double t = .56 + (limb % 4) * .065 + rng.Range(-.018, .018);
                double length = tree.Def.CanopyRadius * rng.Range(.68, .84);
                var dir = new Vec3(Math.Cos(a), 0, Math.Sin(a));
                var start = Centre(t) + dir * (Radius(t) * .65);
                double rise = length * .14, droop = length * .08;
                var axis = BranchAxis(t, a, length, rise, droop, r * .21);

                for (int drop = 0; drop < 1+limb%3; drop++)
                {
                    double q = .30 + drop*.22 + rng.Range(-.05,.05);
                    var limbPt = OnAxis(axis,q);
                    var footXZ=limbPt.XZ + dir.XZ*rng.Range(-.15,.25);
                    var groundPt = Vec3.FromXZ(footXZ, ground(footXZ));
                    if (tree.Domain.ContainsDisc(footXZ, r * .18) && limbPt.Y > groundPt.Y + .3)
                    {
                        var mid = Vec3.Lerp(limbPt, groundPt, .50) + dir * (r * .16 * (drop == 0 ? 1 : -1));
                        var heel = Vec3.Lerp(mid,groundPt,.88);
                        double girth=rng.Range(.035,.065)*r;
                        Tube(new[] { limbPt, mid, heel, groundPt },
                            new[] { girth, girth*.85, girth*1.25, girth*1.85 }, Primitives.Scale(bark, .78), 7);
                    }
                }

                for (int twig = 0; twig < 8; twig++)
                {
                    double q = .20+.76*(twig + .6) / 8.8;
                    var at = OnAxis(axis,q);
                    double ta = facing + Math.Clamp(a - facing + (twig % 2 == 0 ? 1 : -1) * rng.Range(.28, .48), -Math.PI / 3 + .16, Math.PI / 3 - .16);
                    var tdir = new Vec3(Math.Cos(ta), 0, Math.Sin(ta));
                    double tl = length * rng.Range(.18, .28);
                    var end = at + tdir * tl + Vec3.Up * (tl * .32);
                    var twigAxis=new[] { at, Vec3.Lerp(at, end, .5) + Vec3.Up * .06, end };
                    Tube(twigAxis,
                        new[] { r * .030, r * .015, r * .003 }, Primitives.Scale(bark, .80), 7);

                    for (int lf = 0; lf < 8; lf++)
                    {
                        double u = (lf + .5) / 8.0;
                        var attach = OnAxis(twigAxis,u);
                        double la = ta + (lf % 2 == 0 ? 1 : -1) * rng.Range(.65, 1.35);
                        double ll = r * rng.Range(.30, .50);
                        double pitch = rng.Range(-.50, .70);
                        var ltip = attach + new Vec3(Math.Cos(la) * ll * Math.Cos(pitch), ll * Math.Sin(pitch), Math.Sin(la) * ll * Math.Cos(pitch));
                        Blade(attach, ltip, ll * .28, Primitives.Scale(green, rng.Range(.78, 1.18)), 4);
                    }

                    if (twig % 2 == 0)
                        Cone(end - Vec3.Up * (r * .05), new Vec3(r * .04, r * .05, r * .04), new[] { .52, .24, .28 });
                }
            }
        }

        public void Willow()
        {
            // High arching structural cantilever boughs with long pendulous weeping streamers
            // and cascading linear blades trailing toward the ground.
            int limbs = 8;
            for (int limb = 0; limb < limbs; limb++)
            {
                double a = CrownAngle(limb, .83) + rng.Range(-.045, .045);
                double t = .62 + (limb % 4) * .065 + rng.Range(-.015, .015);
                double length = tree.Def.CanopyRadius * rng.Range(.68, .84);
                var dir = new Vec3(Math.Cos(a), 0, Math.Sin(a));
                var side = new Vec3(-dir.Z, 0, dir.X);
                var axis = BranchAxis(t, a, length, length * .50, length * .36, r * .19);

                for (int s = 0; s < 7; s++)
                {
                    double q = .27 + s * .105 + rng.Range(-.025, .025);
                    var attach = OnAxis(axis,q);
                    double hangLen = length * rng.Range(.25, .68)*( .75+.25*q);
                    var sEnd = attach - Vec3.Up * hangLen + dir * (hangLen * .08) + side * (hangLen * .06 * (s % 2 == 0 ? 1 : -1));
                    var shoulder=attach+dir*hangLen*.13+side*hangLen*.10*(s%2==0?1:-1)-Vec3.Up*hangLen*.12;
                    var sMid = Vec3.Lerp(shoulder, sEnd, .5) + dir * (hangLen * .04);
                    var pendantAxis=new[] { attach, shoulder, sMid, sEnd };
                    Tube(pendantAxis, new[] { r * .025, r*.018, r * .009, r * .002 }, Primitives.Scale(bark, .78), 7);

                    int leaves = 6;
                    for (int lf = 0; lf < leaves; lf++)
                    {
                        double u = (lf + .5) / 6.0;
                        var lat = OnAxis(pendantAxis,u);
                        double la = facing + Math.Clamp(a - facing + (lf % 2 == 0 ? 1 : -1) * rng.Range(.30, .60), -Math.PI / 3 + .14, Math.PI / 3 - .14);
                        double ll = r * rng.Range(.26, .45);
                        var ltip = lat + new Vec3(Math.Cos(la) * ll * .4, -ll * .88, Math.Sin(la) * ll * .4);
                        Blade(lat, ltip, ll * .20, Primitives.Scale(green, rng.Range(.80, 1.18)), 4);
                    }

                    if (s % 2 == 0)
                        Cone(attach - Vec3.Up * .04, new Vec3(r * .03, r * .08, r * .03), new[] { .64, .58, .22 });
                }
            }
        }
    }
}
