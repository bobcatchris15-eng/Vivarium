using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Flora;

/// <summary>Class of surface a vine shoot can lie on.</summary>
public enum ClimberSurfaceKind : byte
{
    /// <summary>Open ground (the terrain heightfield).</summary>
    Ground = 0,
    /// <summary>A woody stem: the trunk of a tree or shrub.</summary>
    Woody = 1,
    /// <summary>A fallen log (horizontal cylinder).</summary>
    Log = 2,
    /// <summary>A rock (blob).</summary>
    Rock = 3,
    /// <summary>Open air: the shoot has run off the end of its support.</summary>
    Free = 4,
}

/// <summary>
/// A surface a vine shoot can lie on, together with the local frame the growth law works in.
/// <para>
/// Every projection here answers the same two questions: "where is the closest point of this surface to
/// <c>p</c>" and "which way does the surface face there". That is all the growth law needs, which is what
/// lets one law run on a trunk, on a log, on a boulder and on flat ground without special cases.
/// </para>
/// </summary>
public readonly struct ClimberSurface
{
    public ClimberSurfaceKind Kind { get; }
    /// <summary>Host entity (tree, log or rock); None for ground/free.</summary>
    public EntityId HostId { get; }
    /// <summary>Index into <see cref="PropSet.Logs"/> / <see cref="PropSet.Rocks"/>; -1 otherwise.</summary>
    public int HostIndex { get; }
    /// <summary>Closest point on the surface.</summary>
    public Vec3 Point { get; }
    /// <summary>Unit outward normal at <see cref="Point"/>.</summary>
    public Vec3 Normal { get; }
    /// <summary>
    /// The host's axis, or the direction the shoot wants to travel along it. Zero when the host has no
    /// meaningful axis (ground, rock) or when the surface runs out (log ends).
    /// </summary>
    public Vec3 Axis { get; }
    /// <summary>False when the host has ended and the shoot must leave the surface.</summary>
    public bool Valid { get; }

    public ClimberSurface(ClimberSurfaceKind kind, EntityId hostId, int hostIndex, Vec3 point, Vec3 normal, Vec3 axis, bool valid = true)
    {
        Kind = kind; HostId = hostId; HostIndex = hostIndex;
        Point = point; Normal = normal; Axis = axis; Valid = valid;
    }

    /// <summary>An empty surface: nothing to grow on.</summary>
    public static ClimberSurface None => new(ClimberSurfaceKind.Free, EntityId.None, -1, Vec3.Zero, Vec3.Up, Vec3.Zero, false);

    /// <summary>Distance from a world point to this surface (0 when the point is already on it).</summary>
    public double DistanceTo(Vec3 p) => (p - Point).Length;

    // ------------------------------------------------------------------ projections

    /// <summary>Projects onto the terrain heightfield. The ground always exists, so this never fails.</summary>
    public static ClimberSurface Ground(Vec3 p, Func<Vec2, double> height, Func<Vec2, Vec3> normal)
    {
        var xz = p.XZ;
        return new ClimberSurface(ClimberSurfaceKind.Ground, EntityId.None, -1,
            new Vec3(p.X, height(xz), p.Z), normal(xz), Vec3.Zero);
    }

    /// <summary>
    /// Projects onto a woody stem. The stem is a surface of revolution around a vertical axis whose radius
    /// varies with height, so the outward normal is the gradient of <c>h - r(y)</c> and tilts with the taper.
    /// </summary>
    public static bool TryWoody(Vec3 p, double cx, double cz, double radius, double slope, double topY,
        EntityId hostId, out ClimberSurface surface)
    {
        double dx = p.X - cx, dz = p.Z - cz;
        double h = Math.Sqrt(dx * dx + dz * dz);
        double ux, uz;
        if (h < 1e-7) { ux = 1; uz = 0; }
        else { ux = dx / h; uz = dz / h; }

        var normal = new Vec3(ux, -slope, uz).Normalized();
        surface = new ClimberSurface(ClimberSurfaceKind.Woody, hostId, -1,
            new Vec3(cx + ux * radius, p.Y, cz + uz * radius), normal, Vec3.Up, p.Y <= topY);
        return true;
    }

    /// <summary>
    /// Projects onto a fallen log: a horizontal cylinder. The axis direction is the log's length direction,
    /// so a shoot bound to a log travels along it rather than climbing into the air.
    /// </summary>
    public static bool TryLog(Vec3 p, LogProp log, int index, out ClimberSurface surface)
    {
        var axis = new Vec3(Math.Cos(log.RotationY), 0, Math.Sin(log.RotationY));
        var centre = new Vec3(log.X, log.Y, log.Z);
        var rel = p - centre;
        double t = rel.Dot(axis);
        if (Math.Abs(t) > log.Length * 0.5)
        {
            // Past the end of the log: the shoot has run off the support.
            surface = None;
            return false;
        }
        var perp = rel - axis * t;
        double pl = perp.Length;
        if (pl < 1e-7)
        {
            surface = None;
            return false;
        }
        var n = perp / pl;
        surface = new ClimberSurface(ClimberSurfaceKind.Log, log.Id, index,
            centre + axis * t + n * log.Radius, n, axis);
        return true;
    }

    /// <summary>
    /// Projects onto a rock, modelled as the ellipsoid the rock's footprint and height describe. The ellipsoid is
    /// yawed by <see cref="Rock.RotationY"/> exactly as the renderer yaws the mesh: local +X maps to world
    /// <c>Vec2.FromAngle(RotationY)</c> in XZ (see <see cref="Rock.Covers"/>).
    /// </summary>
    public static bool TryRock(Vec3 p, Rock rock, int index, out ClimberSurface surface)
    {
        const double shrink = 0.92;   // matches Rock.Covers, so the vine sits on the visible stone
        var c = new Vec3(rock.X, rock.Y, rock.Z);
        var a = new Vec3(Math.Max(0.05, rock.SizeX * shrink), Math.Max(0.05, rock.SizeY), Math.Max(0.05, rock.SizeZ * shrink));
        var d = p - c;
        var lxz = d.XZ.Rotated(-rock.RotationY);
        var q = new Vec3(lxz.X, d.Y, lxz.Z);
        double k = 1.0 / Math.Sqrt((q.X / a.X) * (q.X / a.X) + (q.Y / a.Y) * (q.Y / a.Y) + (q.Z / a.Z) * (q.Z / a.Z));
        if (!double.IsFinite(k))
        {
            surface = None;
            return false;
        }
        var lp = q * k;
        var ln = new Vec3(lp.X / (a.X * a.X), lp.Y / (a.Y * a.Y), lp.Z / (a.Z * a.Z)).Normalized();
        var pxz = lp.XZ.Rotated(rock.RotationY);
        var nxz = ln.XZ.Rotated(rock.RotationY);
        var pt = c + new Vec3(pxz.X, lp.Y, pxz.Z);
        var n = new Vec3(nxz.X, ln.Y, nxz.Z);
        surface = new ClimberSurface(ClimberSurfaceKind.Rock, rock.Id, index, pt, n, Vec3.Zero);
        return true;
    }
}

/// <summary>
/// The surface-bound growth law for climbing and creeping vines.
/// <para>
/// <b>Why the species should not place the stem.</b> The previous climber rules told each species exactly
/// where to put the next node: spiral at 47 degrees around a cylinder of assumed radius, wander by a random
/// angle, or step straight up until the host ran out. Those are three different models that only coincidentally
/// agreed with a trunk. A real vine does not know the host's radius; it lays stem down on whatever it is
/// touching and grows, and the winding, the hugging and the sprawl all fall out of the host's geometry.
/// </para>
/// <para><b>The law.</b> A shoot carries two vectors inside the surface's tangent plane:</para>
/// <list type="bullet">
/// <item>its <b>heading</b>, and</item>
/// <item>a <b>lateral reference</b> that is parallel-transported along the surface as the shoot advances.</item>
/// </list>
/// <para>The heading is the weighted sum of a climb direction, the lateral reference and the light, rotated
/// within the tangent plane by an oscillating circumnutation angle. The species supplies only the weights and
/// the oscillation; the surface supplies the frame.</para>
/// <para><b>Why the transported reference is the whole trick.</b> Parallel transport is the identity on a flat
/// plane, so a shoot on a flat wall or on open ground keeps the heading it had and runs straight (or holds a
/// consistent course). On a cylinder the transported frame rotates as the shoot travels around it, so the same
/// heading produces a helix. One law, one parameter, and the same vine winds around a round trunk while
/// climbing a flat wall in a straight line, because the wall gives it nothing to wind around.</para>
/// </summary>
public static class ClimberGrowth
{
    /// <summary>
    /// The plant's basal stem radius, in metres. This is the cap: no node of the stem is ever thicker, and the
    /// plant approaches it as it matures.
    /// </summary>
    public static double BasalRadius(FloraIndividual f, FloraSpeciesDef sp, ClimberDef cd) =>
        cd.BaseRadius * (0.25 + 0.75 * Math.Sqrt(f.BiomassFraction(sp)));

    /// <summary>
    /// The stem radius at a node, in metres. The stem tapers from the base of the plant to the growing tip by the
    /// species' per-metre taper, and never falls below the species' tip radius.
    /// </summary>
    public static double StemRadius(double basal, ClimberDef cd, int depth) =>
        Math.Max(basal * Math.Pow(cd.StemTaper, depth * cd.SegmentLength), cd.TipRadius);

    /// <summary>
    /// The local frame of a stem node: forward along the stem, up the upper side of the stem.
    /// <para>
    /// The foliage always grows on the upper side, whatever the stem happens to be doing. A stem that is spiralling
    /// around a trunk still carries its leaves on top of it, so "up" is world up projected perpendicular to the
    /// stem rather than the host's surface normal. Everything a node does is measured in this frame, so the stem
    /// may twist and turn without the foliage turning with it.
    /// </para>
    /// </summary>
    public static void StemFrame(Vec3 forward, Vec3 normal, out Vec3 side, out Vec3 up)
    {
        var f = forward.LengthSq > 1e-12 ? forward.Normalized() : Vec3.Up;
        var u = Vec3.Up - f * f.Dot(Vec3.Up);
        if (u.LengthSq < 1e-8)
        {
            // A stem running straight up has no "upper side" of its own: fall back to the surface it lies on.
            u = normal - f * normal.Dot(f);
            if (u.LengthSq < 1e-8) u = Perpendicular(f);
        }
        up = u.Normalized();
        side = f.Cross(up).Normalized();
    }

    /// <summary>The component of <paramref name="v"/> lying in the tangent plane, normalised; zero when v is normal.</summary>
    public static Vec3 TangentPart(Vec3 v, Vec3 normal)
    {
        var t = v - normal * v.Dot(normal);
        return t.LengthSq > 1e-14 ? t.Normalized() : Vec3.Zero;
    }

    /// <summary>Any unit vector perpendicular to <paramref name="n"/>.</summary>
    public static Vec3 Perpendicular(Vec3 n)
    {
        var seed = Math.Abs(n.Y) < 0.9 ? Vec3.Up : Vec3.Right;
        var p = n.Cross(seed);
        return p.LengthSq > 1e-12 ? p.Normalized() : Vec3.Right;
    }

    /// <summary>Rodrigues rotation of <paramref name="v"/> about <paramref name="axis"/>.</summary>
    public static Vec3 RotateAround(Vec3 v, Vec3 axis, double angle)
    {
        var k = axis.LengthSq > 1e-18 ? axis.Normalized() : Vec3.Up;
        double c = Math.Cos(angle), s = Math.Sin(angle);
        return v * c + k.Cross(v) * s + k * (k.Dot(v) * (1 - c));
    }

    /// <summary>
    /// Parallel-transports a tangent vector from one surface normal to another. This is the rotation-minimising
    /// frame of the surface: it is the identity across a flat plane and turns with the surface around a host.
    /// </summary>
    public static Vec3 Transport(Vec3 lateral, Vec3 fromNormal, Vec3 toNormal)
    {
        var axis = fromNormal.Cross(toNormal);
        double s = axis.Length, c = fromNormal.Dot(toNormal);
        if (s > 1e-9)
        {
            lateral = RotateAround(lateral, axis, Math.Atan2(s, c));
        }
        else if (c < 0)
        {
            // Normals flipped: any perpendicular axis will do.
            lateral = RotateAround(lateral, Perpendicular(fromNormal), Math.PI);
        }
        lateral -= toNormal * toNormal.Dot(lateral);
        double l = lateral.Length;
        if (l <= 1e-12)
        {
            lateral = Perpendicular(toNormal);
        }
        return lateral / lateral.Length;
    }

    /// <summary>
    /// Builds the heading a shoot wants to travel in, inside the tangent plane at <paramref name="normal"/>.
    /// </summary>
    /// <param name="normal">Surface normal at the shoot tip.</param>
    /// <param name="lateral">The shoot's transported lateral reference.</param>
    /// <param name="arc">Arc length travelled so far, which drives the circumnutation phase.</param>
    /// <param name="climbDir">World direction the shoot wants to travel (host axis, or the shoot's course on open ground).</param>
    /// <param name="climbWeight">Weight of the climb direction.</param>
    /// <param name="lightDir">World direction of the light the shoot seeks.</param>
    /// <param name="lightWeight">Weight of the light direction.</param>
    /// <param name="driftWeight">Weight of the lateral reference. Negative winds the other way.</param>
    /// <param name="wanderAmp">Circumnutation amplitude in radians.</param>
    /// <param name="wanderPeriod">Circumnutation wavelength along the stem, in metres.</param>
    public static Vec3 Heading(
        Vec3 normal, Vec3 lateral, double arc,
        Vec3 climbDir, double climbWeight,
        Vec3 lightDir, double lightWeight,
        double driftWeight, double wanderAmp, double wanderPeriod)
    {
        // The light tilts the direction of travel; it never competes with it. A shoot that is heading for a
        // support is still heading for it, just a little off line.
        Vec3 travel = climbDir;
        if (lightWeight > 1e-9 && lightDir.LengthSq > 1e-12)
        {
            var lt = TangentPart(lightDir, normal);
            if (lt.LengthSq > 1e-12)
            {
                var tilted = climbDir + lt * lightWeight;
                travel = tilted.LengthSq > 1e-14 ? tilted.Normalized() : climbDir;
            }
        }

        var climbT = TangentPart(travel, normal);
        var side = normal.Cross(lateral);
        side = side.LengthSq > 1e-14 ? side.Normalized() : Perpendicular(normal);

        // The drift direction is what the lateral reference has to offer once the climb direction is taken out.
        // It is deliberately left un-normalised: its length is how perpendicular the reference happens to be, so
        // a shoot whose reference runs along its climb direction (open ground, where the surface offers no
        // sideways direction at all) gets no sideways push, while a shoot on a round host gets the full one.
        Vec3 driftT;
        if (climbT.LengthSq > 1e-12)
        {
            driftT = lateral - climbT * climbT.Dot(lateral);
        }
        else
        {
            // Flat ground: world up projects to nothing, so the lateral reference leads.
            driftT = lateral;
        }

        var h = climbT * climbWeight + driftT * driftWeight;
        if (h.LengthSq <= 1e-14)
        {
            h = lateral;
        }

        if (wanderAmp > 1e-6 && wanderPeriod > 1e-6)
        {
            // Circumnutation: an oscillation of the heading inside the surface, not a random kick. This is
            // what makes a vine weave rather than zigzag at random.
            double phase = 2 * Math.PI * arc / wanderPeriod;
            h = RotateAround(h, normal, wanderAmp * Math.Sin(phase));
        }

        return h.Normalized();
    }

    /// <summary>The tangent direction of a displacement on a surface with the given normal.</summary>
    public static Vec3 TangentOf(Vec3 displacement, Vec3 normal)
    {
        var t = displacement - normal * displacement.Dot(normal);
        return t.LengthSq > 1e-18 ? t.Normalized() : Vec3.Zero;
    }
}
