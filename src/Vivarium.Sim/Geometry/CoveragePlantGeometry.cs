using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>Coverage-clump silhouettes, ordered to match coverage species routing.</summary>
public enum CoveragePlantForm
{
    Bogglass = 0,
    PearlCushion = 1,
    Floodlace = 2,
    Velvetweave = 3,
    Antlerlace = 4,
    Ruffle = 5,
    Embercrust = 6,
}

/// <summary>Deterministic first-pass volumetric geometry for compact moss and lichen clumps.</summary>
public static class CoveragePlantGeometry
{
    private static readonly double[][] Colors =
    {
        new[] { .13, .34, .23 }, new[] { .075, .24, .12 }, new[] { .12, .36, .27 },
        new[] { .11, .29, .16 }, new[] { .23, .37, .16 }, new[] { .16, .34, .18 },
        new[] { .28, .24, .12 },
    };
    private static readonly double[] Accent = { .78, .43, .075 };
    private const int Rings = 3;
    private const int Segments = 8;

    /// <summary>Append one rooted clump. UV2.y marks the form; UV2.x is 0 for body and 1 for accent.</summary>
    public static void AppendClump(MeshData mesh, Vec3 foot, Vec3 normal, CoveragePlantForm form, uint seed, double vigor = 1, double scale = 1)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if ((uint)form > 6) throw new ArgumentOutOfRangeException(nameof(form));
        if (!Finite(foot) || !Finite(normal)) throw new ArgumentException("Coverage geometry requires finite placement vectors.");
        var up = normal.Normalized();
        if (up.LengthSq < 1e-10) up = Vec3.Up;
        var refAxis = Math.Abs(up.Dot(Vec3.Forward)) < .9 ? Vec3.Forward : Vec3.Right;
        var right = up.Cross(refAxis).Normalized();
        var forward = right.Cross(up).Normalized();
        var rng = new Rng(seed);
        vigor = MathD.Clamp(double.IsFinite(vigor) ? vigor : 1, .5, 1.5);
        scale = MathD.Clamp(double.IsFinite(scale) ? scale : 1, .5, 1.5);
        double s = scale * (.9 + .1 * vigor) * (form == CoveragePlantForm.Floodlace ? .8 : 1);
        var color = Colors[(int)form];
        double marker = -20 - (int)form;
        double phase = rng.Range(0, Math.PI * 2);

        void Organ(Vec3 centre, Vec3 axis, double length, double width, double thick, double role = 0, double colorScale = 1)
        {
            axis = axis.Normalized();
            var side = up.Cross(axis).Normalized();
            if (side.LengthSq < 1e-8) side = right;
            var depth = axis.Cross(side).Normalized();
            var c = role == 1 ? Accent : color;
            if (colorScale != 1) c = new[] { c[0] * colorScale, c[1] * colorScale, c[2] * colorScale };
            Ellipsoid(mesh, centre, axis, side, depth, length, width, thick, c, role, marker);
        }

        void Stem(Vec3 start, Vec3 direction, double height, double radius, int branches, double bend)
        {
            var d = (up + direction * bend).Normalized();
            var mid = start + d * (height * .55);
            var end = start + d * height;
            Organ((start + mid) * .5, mid - start, (mid - start).Length * .56, radius, radius, 0);
            Organ((mid + end) * .5, end - mid, (end - mid).Length * .56, radius * .82, radius * .82, 0);
            if (branches > 0)
            {
                for (int b = 0; b < branches; b++)
                {
                    double a = phase + b * Math.PI * 2 / branches;
                    var branch = (d * .52 + (right * Math.Cos(a) + forward * Math.Sin(a)) * .85).Normalized();
                    var joint = start + d * (height * .48);
                    var tip = joint + branch * (height * .45);
                    Organ((joint + tip) * .5, tip - joint, (tip - joint).Length * .56,
                        radius * .56, radius * .56, 0);
                    Organ(tip, branch, .0025 * s, .00065 * s, .00065 * s);
                }
            }
            Organ(end, d, .0025 * s, .0008 * s, .0008 * s);
        }

        void BasalLeaves(int count, double radialMin, double radialMax, double lenMin, double lenMax, double thickness)
        {
            for (int i = 0; i < count; i++)
            {
                double a = phase + i * Math.PI * (3 - Math.Sqrt(5)) + rng.Range(-.16, .16);
                var radial = right * Math.Cos(a) + forward * Math.Sin(a);
                double len = rng.Range(lenMin, lenMax) * s;
                double r = rng.Range(radialMin, radialMax) * s;
                var axis = (radial * .9 + up * rng.Range(.25, .52)).Normalized();
                var center = foot + up * rng.Range(.003, .009) * s + radial * r;
                Organ(center, axis, len, len * rng.Range(.48, .66), thickness * s);
            }
        }

        switch (form)
        {
            case CoveragePlantForm.Bogglass:
                BasalLeaves(4, .003, .009, .004, .006, .0007);
                for (int i = 0; i < 4; i++)
                {
                    double a = phase + i * Math.PI * .5;
                    var radial=right*Math.Cos(a)+forward*Math.Sin(a);
                    var start=foot+radial*.006*s;
                    var direction=(up+radial*.16).Normalized();
                    double height=rng.Range(.019,.030)*s;
                    var end=start+direction*height;
                    Organ((start+end)*.5,direction,height*.54,.00065*s,.00065*s);
                    for(int leaf=0;leaf<4;leaf++)
                    {
                        double angle=a+leaf*Math.PI*.72;
                        var leafDir=(right*Math.Cos(angle)+forward*Math.Sin(angle)+up*.32).Normalized();
                        var joint=start+direction*(height*(.32+.14*leaf));
                        Organ(joint+leafDir*.0033*s,leafDir,.0044*s,.0015*s,.0005*s);
                    }
                    for(int leaf=0;leaf<3;leaf++)
                    {
                        double angle=a+leaf*Math.PI*2/3;
                        var leafDir=(right*Math.Cos(angle)+forward*Math.Sin(angle)+up*.55).Normalized();
                        Organ(end+leafDir*.0025*s,leafDir,.0043*s,.0016*s,.0005*s);
                    }
                }
                break;
            case CoveragePlantForm.PearlCushion:
                Organ(foot + up * .007 * s, up, .013 * s, .012 * s, .0065 * s);
                BasalLeaves(18, .002, .013, .006, .009, .0021);
                break;
            case CoveragePlantForm.Floodlace:
                for (int i = 0; i < 5; i++)
                {
                    double a = phase + i * Math.PI * 2 / 5;
                    var radial = right * Math.Cos(a) + forward * Math.Sin(a);
                    var blade = (radial * .88 + up * .55).Normalized();
                    var basePt = foot + radial * .003 * s;
                    Organ(basePt + blade * .018 * s, blade, .020 * s, .0044 * s, .0015 * s);
                    var tip = basePt + blade * .034 * s;
                    foreach (double side in new[] { -.55, .55 })
                    {
                        var fork = (blade * .55 + (up.Cross(blade).Normalized() * side)).Normalized();
                        Organ(tip + fork * .004 * s, fork, .009 * s, .0024 * s, .0012 * s);
                    }
                }
                break;
            case CoveragePlantForm.Velvetweave:
                Organ(foot + up * .005 * s, up, .011 * s, .010 * s, .005 * s);
                BasalLeaves(20, .002, .012, .0055, .008, .0024);
                break;
            case CoveragePlantForm.Antlerlace:
                BasalLeaves(7, .004, .013, .006, .008, .0018);
                for (int i = 0; i < 4; i++)
                {
                    double a = phase + i * Math.PI * .5;
                    var radial = right * Math.Cos(a) + forward * Math.Sin(a);
                    Stem(foot + radial * .004 * s, radial, rng.Range(.041, .054) * s,
                        .00175 * s, 3, rng.Range(.08, .20));
                }
                break;
            case CoveragePlantForm.Ruffle:
                for (int i = 0; i < 8; i++)
                {
                    double a = phase + i * Math.PI / 4;
                    var radial = right * Math.Cos(a) + forward * Math.Sin(a);
                    var axis = (radial * .74 + up * .30).Normalized();
                    var center = foot + up * (.006 + .0015 * (i % 3)) * s + radial * .006 * s;
                    Organ(center, axis, .016 * s, .0072 * s, .0024 * s, 0, .92 + .03 * (i % 3));
                }
                break;
            case CoveragePlantForm.Embercrust:
                for (int i = 0; i < 7; i++)
                {
                    double a = phase + i * Math.PI * 2 / 7;
                    var radial = right * Math.Cos(a) + forward * Math.Sin(a);
                    Organ(foot + up * .0028 * s + radial * .006 * s, up, .0021 * s, .008 * s, .0065 * s);
                }
                for (int i = 0; i < 5; i++)
                {
                    double a = phase + i * Math.PI * 2 / 5;
                    var radial = right * Math.Cos(a) + forward * Math.Sin(a);
                    var center = foot + up * (.004 + (i % 2) * .001) * s + radial * (.008 + .002 * (i % 3)) * s;
                    Organ(center, up, .0013 * s, .0032 * s, .0032 * s, 1);
                }
                break;
        }

    }

    private static void Ellipsoid(MeshData mesh, Vec3 centre, Vec3 axis, Vec3 side, Vec3 depth,
        double along, double across, double thick, double[] color, double role, double marker)
    {
        int[][] rows = new int[Rings + 1][];
        for (int r = 0; r <= Rings; r++)
        {
            double phi = Math.PI * r / Rings, ca = Math.Cos(phi), sa = Math.Sin(phi);
            int count = r == 0 || r == Rings ? 1 : Segments;
            rows[r] = new int[count];
            for (int q = 0; q < count; q++)
            {
                double theta = count == 1 ? 0 : 2 * Math.PI * q / Segments;
                double cb = Math.Cos(theta), sb = Math.Sin(theta);
                var p = centre + axis * (along * ca) + side * (across * sa * cb) + depth * (thick * sa * sb);
                var n = (axis * (ca / along) + side * (sa * cb / across) + depth * (sa * sb / thick)).Normalized();
                rows[r][q] = mesh.AddVertex(p, n, color, 1, (double)r / Rings, (double)q / Segments, role, marker);
            }
        }
        for (int r = 0; r < Rings; r++)
        {
            var top = rows[r]; var bottom = rows[r + 1];
            if (top.Length == 1)
                for (int q = 0; q < Segments; q++) Primitives.TriangleFacing(mesh, top[0], bottom[(q + 1) % Segments], bottom[q], mesh.NormalAt(top[0]));
            else if (bottom.Length == 1)
                for (int q = 0; q < Segments; q++) Primitives.TriangleFacing(mesh, top[q], top[(q + 1) % Segments], bottom[0], mesh.NormalAt(bottom[0]));
            else
                for (int q = 0; q < Segments; q++)
                {
                    int n = (q + 1) % Segments;
                    Primitives.TriangleFacing(mesh, top[q], top[n], bottom[q], mesh.NormalAt(top[q]));
                    Primitives.TriangleFacing(mesh, top[n], bottom[n], bottom[q], mesh.NormalAt(top[n]));
                }
        }
    }

    private static bool Finite(Vec3 v) => double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);
}
