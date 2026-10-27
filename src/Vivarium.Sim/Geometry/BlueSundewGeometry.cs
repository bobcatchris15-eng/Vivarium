using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// One compact, meter-scale blue sundew clump. Organ roles are encoded in UV2.x:
/// basal foliage = 0, scaled stalks = 1, and blue gland clubs = 2. UV2.y = -8 marks
/// the complete clump for the isolated sundew surface material.
/// </summary>
public static class BlueSundewGeometry
{
    private const double Marker = -8.0;
    private static readonly double[] LeafColor = { .043, .145, .052 };
    private static readonly double[] StalkColor = { .19, .105, .047 };
    private static readonly double[] ClubColor = { .055, .34, .91 };

    /// <summary>Add a deterministic, asymmetric clump rooted at <paramref name="foot"/>.</summary>
    public static void AppendClump(MeshData mesh, Vec3 foot, Vec3 normal, uint seed, double vigor = 1, double scale = 1)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        var up = normal.Normalized();
        if (up.LengthSq < 1e-10) up = Vec3.Up;
        var right = up.Cross(Vec3.Forward);
        if (right.LengthSq < 1e-8) right = up.Cross(Vec3.Right);
        right = right.Normalized();
        var forward = right.Cross(up).Normalized();
        var rng = new Rng(seed);

        vigor = MathD.Clamp(vigor, .5, 1.5);
        scale = MathD.Clamp(scale, .5, 1.5);
        double size = scale * (.88 + .12 * vigor);

        // A low, overlapping rosette gives the upright reproductive stalks a leafy base.
        int leafCount = 24 + rng.NextInt(9);
        double phase = rng.Range(0, Math.PI * 2);
        for (int i = 0; i < leafCount; i++)
        {
            double angle = phase + i * Math.PI * (3 - Math.Sqrt(5)) + rng.Range(-.16, .16);
            var radial = right * Math.Cos(angle) + forward * Math.Sin(angle);
            double length = rng.Range(.0085, .0105) * size;
            double width = rng.Range(.0040, .0052) * size;
            double thickness = rng.Range(.00105, .00155) * size;
            double lean = rng.Range(.28, .52);
            var longAxis = (radial + up * lean).Normalized();
            var widthAxis = up.Cross(longAxis).Normalized();
            var thickAxis = longAxis.Cross(widthAxis).Normalized();
            int tier = i % 3;
            double rootRadius = (tier==0 ? rng.Range(.002,.005) : tier==1 ? rng.Range(.006,.010) : rng.Range(.010,.014)) * size;
            double leafT = (i + rng.NextDouble()) / leafCount;
            var centre = foot + up * ((tier==0 ? .008 : tier==1 ? .0045 : .0015) * size)
                + radial * (rootRadius + length * .30)
                + up * (length * (lean * .30 + .06));
            var color = SlightTint(LeafColor, rng, .055);
            AppendEllipsoid(mesh, centre, longAxis, widthAxis, thickAxis,
                length * .56, width, thickness, color, 0, 6, 10);
        }

        // Stalks rise at different angles and heights, with broad low-relief scale rings.
        int stalkCount = 4 + rng.NextInt(4);
        double stalkPhase = phase + .31;
        for (int i = 0; i < stalkCount; i++)
        {
            double angle = stalkPhase + i * Math.PI * (3 - Math.Sqrt(5)) + rng.Range(-.22, .22);
            var radial = right * Math.Cos(angle) + forward * Math.Sin(angle);
            double height = rng.Range(.041, .054) * size * (.94 + .08 * vigor);
            double baseRadius = rng.Range(.0011, .0014) * size;
            double lean = rng.Range(.10, .26) * size;
            var root = foot + radial * (rng.Range(.005, .012) * size) + up * (rng.Range(.001, .002) * size);
            var dir = (up + radial * lean).Normalized();
            var path = new Vec3[7];
            var radii = new double[7];
            double[] scales = { .82, 1.05, .88, 1.10, .91, 1.06, .76 };
            for (int row = 0; row < path.Length; row++)
            {
                double t = row / (double)(path.Length - 1);
                path[row] = root + dir * (height * t);
                // Small rounded swellings read as overlapping scales without sharp collars.
                double taper = 1.0 - .18 * t;
                radii[row] = baseRadius * scales[row] * taper;
            }
            var stalkColor = SlightTint(StalkColor, rng, .075);
            Primitives.Tube(mesh, path, radii, 8,
                (row, around) => (stalkColor, 1, row / 6.0, around, 1, Marker), up);

            // Rounded overlapping scale plates give the stalk a real silhouette,
            // rather than relying on painted stripes or knife-thin fins.
            var stalkSide=dir.Cross(right).Normalized();
            if(stalkSide.LengthSq<1e-8) stalkSide=dir.Cross(forward).Normalized();
            var stalkOther=dir.Cross(stalkSide).Normalized();
            for(int band=0;band<4;band++) for(int plate=0;plate<3;plate++)
            {
                double theta=phase+band*.7+plate*Math.PI*2/3;
                var outward=stalkSide*Math.Cos(theta)+stalkOther*Math.Sin(theta);
                var longAxis=(dir*.94+outward*.34).Normalized();
                var plateSide=dir.Cross(outward).Normalized();
                var plateDepth=longAxis.Cross(plateSide).Normalized();
                var centre=root+dir*(height*(.14+band*.19))+outward*(baseRadius*.88);
                AppendEllipsoid(mesh,centre,longAxis,plateSide,plateDepth,.0015*size,.00072*size,.00028*size,stalkColor,1,4,6);
            }

            // A fleshy blue club and four attached rounded lobes form a readable crown.
            double clubLength = rng.Range(.0105, .0135) * size;
            double clubWidth = rng.Range(.00215, .00275) * size;
            var clubAxis = (dir + radial * rng.Range(-.10, .10)).Normalized();
            var clubSide = clubAxis.Cross(up).Normalized();
            if (clubSide.LengthSq < 1e-8) clubSide = clubAxis.Cross(right).Normalized();
            var clubDepth = clubAxis.Cross(clubSide).Normalized();
            var clubCentre = path[^1] + clubAxis * (clubLength * .29);
            var blue = SlightTint(ClubColor, rng, .035);
            AppendEllipsoid(mesh, clubCentre, clubAxis, clubSide, clubDepth,
                clubLength * .53, clubWidth, clubWidth * .82, blue, 2, 7, 12);

            double lobeStart = clubLength * .30;
            double lobeOffset = clubWidth * .48;
            for (int lobe = 0; lobe < 4; lobe++)
            {
                double around = Math.PI * .5 * lobe + rng.Range(-.12, .12);
                var wing = (clubSide * Math.Cos(around) + clubDepth * Math.Sin(around)).Normalized();
                var lobeAxis = (clubAxis * .84 + wing * .54).Normalized();
                var lobeSide = lobeAxis.Cross(up).Normalized();
                if (lobeSide.LengthSq < 1e-8) lobeSide = lobeAxis.Cross(right).Normalized();
                var lobeDepth = lobeAxis.Cross(lobeSide).Normalized();
                var lobeCentre = clubCentre + clubAxis * lobeStart + wing * lobeOffset;
                AppendEllipsoid(mesh, lobeCentre, lobeAxis, lobeSide, lobeDepth,
                    clubLength * .20, clubWidth * .43, clubWidth * .42, blue, 2, 5, 9);
            }
        }

        // Retain essential silhouette geometry when the host applies flora LOD culling.
        mesh.PreserveUpperCanopyOnLod = true;
    }

    private static void AppendEllipsoid(MeshData mesh, Vec3 centre, Vec3 axis, Vec3 side, Vec3 depth,
        double along, double across, double thick, double[] color, double role, int rings, int segments)
    {
        var rows = new int[rings + 1][];
        for (int r = 0; r <= rings; r++)
        {
            double phi = Math.PI * r / rings;
            double ca = Math.Cos(phi), sa = Math.Sin(phi);
            int count = r == 0 || r == rings ? 1 : segments;
            rows[r] = new int[count];
            for (int s = 0; s < count; s++)
            {
                double theta = count == 1 ? 0 : 2 * Math.PI * s / segments;
                double cb = Math.Cos(theta), sb = Math.Sin(theta);
                var p = centre + axis * (along * ca) + side * (across * sa * cb) + depth * (thick * sa * sb);
                var n = (axis * (ca / along) + side * (sa * cb / across) + depth * (sa * sb / thick)).Normalized();
                rows[r][s] = mesh.AddVertex(p, n, color, 1, (double)r / rings,
                    count == 1 ? .5 : (double)s / segments, role, Marker);
            }
        }

        for (int r = 0; r < rings; r++)
        {
            var upper = rows[r];
            var lower = rows[r + 1];
            if (upper.Length == 1)
            {
                for (int s = 0; s < segments; s++)
                    Primitives.TriangleFacing(mesh, upper[0], lower[(s + 1) % segments], lower[s], mesh.NormalAt(upper[0]));
            }
            else if (lower.Length == 1)
            {
                for (int s = 0; s < segments; s++)
                    Primitives.TriangleFacing(mesh, upper[s], upper[(s + 1) % segments], lower[0], mesh.NormalAt(lower[0]));
            }
            else
            {
                for (int s = 0; s < segments; s++)
                {
                    int next = (s + 1) % segments;
                    Primitives.TriangleFacing(mesh, upper[s], upper[next], lower[s], mesh.NormalAt(upper[s]));
                    Primitives.TriangleFacing(mesh, upper[next], lower[next], lower[s], mesh.NormalAt(upper[next]));
                }
            }
        }
    }

    private static double[] SlightTint(double[] baseColor, Rng rng, double amount)
    {
        double tint = rng.Range(1 - amount, 1 + amount);
        return new[] { baseColor[0] * tint, baseColor[1] * tint, baseColor[2] * tint };
    }
}
