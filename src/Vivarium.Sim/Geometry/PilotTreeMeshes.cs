using Vivarium.Sim.Core;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Landscape-scale fictional old-growth anatomy. Built once from persistent PilotTreeState;
/// root sections use exactly the same authoritative samples as ground/wood queries.
/// COLOR.a is a material marker: 1 wood, 0 foliage, .35 reproductive/fungal tissue.
/// </summary>
public static class PilotTreeMeshes
{
    public static MeshData Build(VivariumWorld world)
    {
        var m = new MeshData();
        var tree = world.PilotTree;
        if (tree == null) return m;
        var b = new Builder(m, tree, world.SurfaceHeight);
        b.Roots();
        b.Trunk();
        switch (tree.Def.Id)
        {
            case "gloomspire": b.Conifer(false, false); break;
            case "needlevault": b.Conifer(true, false); break;
            case "crowncoil": b.Cycad(); break;
            case "emberpillar": b.Redwood(); break;
            case "basinwarden": b.Broadleaf(); break;
            case "palehollow": b.Conifer(false, true); b.Fungi(); break;
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
        private const double Tau = Math.PI * 2;
        public Builder(MeshData mesh, PilotTreeState state, Func<Vec2,double> terrainHeight)
        {
            m = mesh; tree = state; ground = terrainHeight; rng = Rng.Keyed(state.Seed, "pilot.mesh." + state.Def.Id, 0);
            r = state.Def.TrunkRadius; h = state.Def.TrunkHeight;
            origin = Vec3.FromXZ(state.Anchor, state.BaseHeight);
            bark = RGB(state.Def.BarkColor); green = RGB(state.Def.FoliageColor);
            phase = rng.Range(0, Tau); facing = (-state.CornerPoint).Angle;
        }
        private static double[] RGB(Vec3 color) => new[] { color.X, color.Y, color.Z };
        private Vec3 Centre(double t) => origin + Vec3.Up * (h * t);
        private double Radius(double t) => tree.Def.Id == "crowncoil"
            ? r * (.82 + .18 * (1-t)) * (1 + .13*Math.Exp(-t*16))
            : r * (.18 + .82 * Math.Pow(1 - t, .75)) * (1 + .30 * Math.Exp(-t * 16));

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
                destination.Append(IslandMeshClipper.ClipClosed(m, tree.Domain, bark));
                m = destination;
            }
        }

        public void Trunk()
        {
            bool pale = tree.Def.Id == "palehollow", cycad = tree.Def.Id == "crowncoil";
            const int rows = 32, sides = 32;
            var rings = new Vec3[rows + 1, sides + 1];
            var shade = new double[rows + 1, sides + 1];
            double startAngle = facing - Math.PI / 3;
            double sweep = 2 * Math.PI / 3;

            for (int i = 0; i <= rows; i++)
            {
                double t = (double)i / rows;
                for (int j = 0; j <= sides; j++)
                {
                    double a = startAngle + sweep * j / sides;
                    double edgeBlend = Math.Sin(Math.PI * j / sides);
                    double furrow = cycad ? .012 : .045;
                    double relief = (furrow * Math.Sin(a * 19 + phase + .18 * Math.Sin(t * 18))
                        + .017 * Math.Sin(a * 31 - phase + t * 9)) * edgeBlend;
                    double radius = Radius(t) * (1 + relief);
                    double y = i == rows && pale ? -.032 * h * (.5 + .5 * Math.Sin(a * 7 + phase)) : 0;
                    rings[i, j] = Centre(t) + new Vec3(Math.Cos(a) * radius, y, Math.Sin(a) * radius);
                    shade[i, j] = .76 + .22 * (.5 + .5 * Math.Sin(a * 19 + phase)) + .10 * rng.NextDouble();
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

            // 1. Curved front bark surface facing into the island
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < sides; j++)
                {
                    int k = j + 1;
                    if (Hole(i, j)) continue;
                    double midA = startAngle + sweep * (j + .5) / sides;
                    var outN = new Vec3(Math.Cos(midA), 0, Math.Sin(midA));
                    var col = Primitives.Scale(bark, shade[i, j]);
                    int first = m.VertexCount;
                    Quad(rings[i, j], rings[i, k], rings[i + 1, k], rings[i + 1, j], col, 1, outN);
                    for (int v = first; v < m.VertexCount; v++)
                    {
                        var p = m.Position(v);
                        double angle = Math.Atan2(p.Z - origin.Z, p.X - origin.X);
                        angle = midA + MathD.WrapAngle(angle - midA);
                        m.UV[v * 2] = (float)(angle * r);
                        m.UV[v * 2 + 1] = (float)(p.Y - origin.Y);
                    }
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
                Face(Centre(1), rings[rows, j], rings[rows, k], Primitives.Scale(bark, .75), 0.95, Vec3.Up);
                Face(origin - Vec3.Up * .04, rings[0, k], rings[0, j], bark, 0.95, -Vec3.Up);
            }

            // 4. Solid flat back planes along the two island boundary edges meeting at CornerPoint
            var cutCol = Primitives.Scale(bark, .95);
            var outN1 = new Vec3(tree.CutNormals[1].X, 0, tree.CutNormals[1].Z);
            var outN0 = new Vec3(tree.CutNormals[0].X, 0, tree.CutNormals[0].Z);

            int cutStart1 = m.VertexCount;
            for (int i = 0; i < rows; i++)
            {
                double t0 = (double)i / rows, t1 = (double)(i + 1) / rows;
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
                double t0 = (double)i / rows, t1 = (double)(i + 1) / rows;
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
                    var n = new Vec3(Math.Cos(a), 0, Math.Sin(a)); var side = new Vec3(-n.Z, 0, n.X);
                    var c = Centre(t) + n * Radius(t);
                    double w = r * .19, hh = h * .024;
                    Vec3 top = c + Vec3.Up * hh, right = c + side * w, low = c - Vec3.Up * hh + n * r * .022, left = c - side * w;
                    var ridge = c + n * r * .085 + Vec3.Up * hh * .18;
                    var col = Primitives.Scale(bark, rng.Range(.78, 1.18));
                    Face(top, right, ridge, col, 1, n); Face(right, low, ridge, col, 1, n);
                    Face(low, left, ridge, Primitives.Scale(col, .88), 1, n); Face(left, top, ridge, col, 1, n);
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
        private Vec3 Branch(double t,double angle,double length,double rise,double droop,double radius)
        {
            var start=Centre(t);var dir=new Vec3(Math.Cos(angle),0,Math.Sin(angle));
            var path=new Vec3[7];var widths=new double[7];
            for(int i=0;i<path.Length;i++)
            {
                double q=i/(double)(path.Length-1);
                path[i]=start+dir*(Radius(t)*.65+q*length)+Vec3.Up*(rise*q-droop*q*q);
                widths[i]=Math.Max(radius*.07,radius*Math.Pow(1-q,.85));
            }
            Tube(path,widths,Primitives.Scale(bark,.84));return path[^1];
        }

        public void Conifer(bool spruce,bool broken)
        {
            int levels=broken?6:10;
            for(int level=0;level<levels;level++)
            {
                double t=.24+level*(broken?.095:.073);
                int branches=broken?5:7;
                for(int j=0;j<branches;j++)
                {
                    if(broken&&rng.Chance(.28)) continue;
                    double a=facing+(.86*(2.0*j/(branches-1)-1))+Math.Sin(level*1.7)*.10+rng.Range(-.05,.05);
                    double len=tree.Def.CanopyRadius*(.16+.84*(1-t))*rng.Range(.8,1.05);
                    var dir=new Vec3(Math.Cos(a),0,Math.Sin(a));
                    double rise=len*(spruce?.20:.13),droop=len*(spruce?.19:.30);
                    Branch(t,a,len,rise,droop,r*(.11-.045*t));
                    for(int twig=1;twig<=5;twig++)
                    {
                        double q=twig/6.0;
                        var at=Centre(t)+dir*(Radius(t)*.65+q*len)+Vec3.Up*(rise*q-droop*q*q);
                        for(int s=-1;s<=1;s+=2)
                        {
                            double ta=a+s*(spruce?.78:.98);
                            var td=new Vec3(Math.Cos(ta),0,Math.Sin(ta));
                            double tl=len*(1-q)*rng.Range(.38,.55)+r*.20;
                            var end=at+td*tl-Vec3.Up*tl*(spruce?.12:.28);
                            Tube(new[]{at,Vec3.Lerp(at,end,.55)+Vec3.Up*tl*.06,end},
                                new[]{r*.019,r*.012,r*.002},Primitives.Scale(bark,.79),6);
                            NeedleSpray(at,end,spruce);
                            var fork=Vec3.Lerp(at,end,.45);
                            var cross=td.Cross(Vec3.Up).Normalized();
                            for(int sprig=-1;sprig<=1;sprig+=2)
                                NeedleSpray(fork,end+cross*(tl*.30*sprig)+td*tl*.12,spruce);
                        }
                    }
                    if(spruce&&level<5&&j%2==0)
                    {
                        var tip=Centre(t)+dir*(len*.78)-Vec3.Up*len*.03;
                        Cone(tip-Vec3.Up*r*.18,new Vec3(r*.10,r*.30,r*.10),new[]{.32,.24,.13});
                    }
                }
            }
            if(!broken)
            {
                var top=Centre(.83);NeedleSpray(top,Centre(1)+Vec3.Up*h*.03,spruce);
            }
            else
            {
                for(int j=0;j<3;j++)Branch(.85+j*.045,phase+j*1.9,r*1.5,h*.04,h*.015,r*.07);
            }
        }

        private void NeedleSpray(Vec3 start,Vec3 end,bool spruce)
        {
            var axis=(end-start).Normalized();
            var side=axis.Cross(Vec3.Up).Normalized();if(side.LengthSq<.1)side=new Vec3(1,0,0);
            int leaves=spruce?16:14;
            for(int i=0;i<leaves;i++)
            {
                double q=(i+.35)/leaves;
                var at=Vec3.Lerp(start,end,q);
                double size=(end-start).Length*(.18+.22*Math.Sin(q*Math.PI));
                for(int sign=-1;sign<=1;sign+=2)
                {
                    var tip=at+side*(sign*size)+axis*size*.33-Vec3.Up*size*(spruce?.15:.30);
                    NeedleBlade(at,tip,size*(spruce?.10:.17),Primitives.Scale(green,rng.Range(.80,1.20)));
                }
            }
        }

        private void NeedleBlade(Vec3 baseP, Vec3 tip, double width, double[] color)
        {
            var axis = (tip-baseP).Normalized();
            var side = axis.Cross(Vec3.Up).Normalized();
            if (side.LengthSq < .1) side = new Vec3(1,0,0);
            var normal = side.Cross(axis).Normalized();
            if (normal.Y < 0) normal = -normal;
            var center = Vec3.Lerp(baseP,tip,.43)+normal*width*.15;
            var left = center-side*width;
            var right = center+side*width;
            Face(baseP,left,tip,color,0,normal);
            Face(baseP,tip,right,Primitives.Scale(color,.88),0,normal);
        }

        // A cambered solid blade with a midrib fold and a serrated asymmetric outline.
        // Two faces with different normals provide readable foliage from above and below.
        private void Blade(Vec3 baseP,Vec3 tip,double width,double[] color,int segments=4)
        {
            var axis=(tip-baseP).Normalized();var side=axis.Cross(Vec3.Up).Normalized();
            if(side.LengthSq<.1)side=new Vec3(1,0,0);
            var normal=side.Cross(axis).Normalized();if(normal.Y<0)normal=-normal;
            var mid=new Vec3[segments+1];var left=new Vec3[segments+1];var right=new Vec3[segments+1];
            for(int i=0;i<=segments;i++)
            {
                double t=i/(double)segments;
                double w=width*Math.Pow(Math.Sin(Math.PI*t),.8)+width*.016;
                mid[i]=Vec3.Lerp(baseP,tip,t)+normal*width*.18*Math.Sin(t*Math.PI);
                left[i]=mid[i]-side*w-normal*w*.12;
                right[i]=mid[i]+side*w*(.9+.12*Math.Sin(t*8+phase))-normal*w*.12;
            }
            for(int i=0;i<segments;i++)
            {
                Quad(left[i],mid[i],mid[i+1],left[i+1],color,0,normal);
                Quad(mid[i],right[i],right[i+1],mid[i+1],Primitives.Scale(color,.93),0,normal);
                Quad(left[i+1],mid[i+1]-normal*.002,mid[i]-normal*.002,left[i],Primitives.Scale(color,.74),0,-normal);
                Quad(mid[i+1]-normal*.002,right[i+1],right[i],mid[i]-normal*.002,Primitives.Scale(color,.74),0,-normal);
            }
        }

        public void Cycad()
        {
            // Unequal double crown, arching rachises, separated stiff pinnae. No umbrella disks.
            for(int tier=0;tier<2;tier++) for(int f=0;f<24;f++)
            {
                double a=phase+Tau*(f+tier*.43)/24+rng.Range(-.08,.08);
                double length=tree.Def.CanopyRadius*rng.Range(.72,1.10);
                var dir=new Vec3(Math.Cos(a),0,Math.Sin(a));var side=new Vec3(-dir.Z,0,dir.X);
                var start=Centre(.88-tier*.09)+dir*r*.18;
                var path=new Vec3[13];var rad=new double[13];
                for(int i=0;i<path.Length;i++)
                {
                    double t=i/12.0;
                    path[i]=start+dir*(length*t)+Vec3.Up*(length*(.61*t-.70*t*t));
                    rad[i]=r*(.028*(1-t)+.003);
                }
                Tube(path,rad,Primitives.Scale(green,.73),8,0);
                for(int i=1;i<12;i++)
                {
                    double t=i/12.0,leaf=length*.20*Math.Pow(Math.Sin(t*Math.PI),.55);
                    for(int sign=-1;sign<=1;sign+=2)
                    {
                        var end=path[i]+side*(leaf*sign)+dir*leaf*.34-Vec3.Up*leaf*.13;
                        Blade(path[i],end,leaf*.065,Primitives.Scale(green,rng.Range(.84,1.13)),3);
                    }
                }
            }
            Cone(Centre(.96)+Vec3.Up*r*.55,new Vec3(r*.43,r*1.1,r*.43),new[]{.65,.38,.16});
            Cone(Centre(.91)+new Vec3(r*.55,r*.3,r*.18),new Vec3(r*.32,r*.70,r*.32),new[]{.42,.44,.20});
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
            // High, irregular reiterated crown above a long fibrous bole, with sparse dead lower limbs.
            for(int j=0;j<4;j++)Branch(.31+j*.06,phase+j*1.7,r*rng.Range(1.1,1.8),r*.3,r*.1,r*.08);
            for(int crown=0;crown<12;crown++)
            {
                double a=phase+crown*2.399;
                double t=.67+crown*.020;
                double len=tree.Def.CanopyRadius*rng.Range(.48,.85);
                var tip=Branch(t,a,len,len*.34,len*.18,r*.13);
                var dir=new Vec3(Math.Cos(a),0,Math.Sin(a));
                for(int twig=0;twig<5;twig++)
                {
                    var at=Vec3.Lerp(Centre(t),tip,(twig+.5)/6);
                    for(int sign=-1;sign<=1;sign+=2)
                    {
                        var td=new Vec3(Math.Cos(a+sign*.92),0,Math.Sin(a+sign*.92));
                        var end=at+td*len*.26+Vec3.Up*len*.10;
                        Tube(new[]{at,Vec3.Lerp(at,end,.6),end},new[]{r*.02,r*.009,r*.002},bark,6);
                        NeedleSpray(at,end,false);
                        var cross=td.Cross(Vec3.Up).Normalized();
                        NeedleSpray(Vec3.Lerp(at,end,.35),end+cross*len*.12,false);
                        NeedleSpray(Vec3.Lerp(at,end,.45),end-cross*len*.12,false);
                    }
                }
            }
        }

        public void Broadleaf()
        {
            for(int limb=0;limb<12;limb++)
            {
                double a=facing+.95*Math.Sin(limb*2.399), t=.55+limb*.034;
                double length=tree.Def.CanopyRadius*rng.Range(.68,.95);
                var tip=Branch(t,a,length,length*.36,length*.12,r*.20);
                for(int twig=0;twig<10;twig++)
                {
                    double q=(twig+1)/11.0;
                    var at=Vec3.Lerp(Centre(t),tip,q);
                    double ta=a+(twig%2==0?1:-1)*rng.Range(.6,1.15);
                    var dir=new Vec3(Math.Cos(ta),0,Math.Sin(ta));
                    var end=at+dir*length*.25+Vec3.Up*length*.12;
                    Tube(new[]{at,Vec3.Lerp(at,end,.5)+Vec3.Up*r*.16,end},new[]{r*.046,r*.023,r*.004},bark,8);
                    for(int leaf=0;leaf<7;leaf++)
                    {
                        var attach=Vec3.Lerp(at,end,(leaf+1)/8.0);
                        double la=ta+(leaf%2==0?1:-1)*.85;
                        double ll=r*rng.Range(.43,.69);
                        var leafTip=attach+new Vec3(Math.Cos(la)*ll,ll*rng.Range(.15,.7),Math.Sin(la)*ll);
                        Blade(attach,leafTip,ll*.31,Primitives.Scale(green,rng.Range(.78,1.18)),4);
                    }
                    if(twig%3==0) Cone(end-Vec3.Up*r*.13,new Vec3(r*.085,r*.12,r*.085),new[]{.57,.32,.15});
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
                for(int i=0;i<shelves;i++)
                {
                    double y=t+i*rng.Range(.014,.027);
                    var n=new Vec3(Math.Cos(a),0,Math.Sin(a));
                    var c=Centre(y)+n*Radius(y)*.96;
                    Shelf(c,n,r*rng.Range(.13,.34)*(1-.07*i));
                }
            }
        }
        private void Shelf(Vec3 center,Vec3 outward,double size)
        {
            var side=new Vec3(-outward.Z,0,outward.X);
            const int count=24, rings=7;
            var top=new Vec3[rings+1,count+1];var bottom=new Vec3[rings+1,count+1];
            double asym=rng.Range(.85,1.15),phase2=rng.Range(0,Tau);
            for(int ring=0;ring<=rings;ring++) for(int j=0;j<=count;j++)
            {
                double q=ring/(double)rings, a=Math.PI*j/count;
                double scallop=1+.025*Math.Sin(a*9+phase2)+.017*Math.Sin(a*17-phase2);
                var p=center+side*(Math.Cos(a)*size*q*asym*scallop)+outward*(Math.Sin(a)*size*q*.72*scallop);
                double crown=size*(.20*(1-q*q)+.015*Math.Sin(q*34+phase2)*(1-q));
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
    }
}
