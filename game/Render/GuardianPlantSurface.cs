using System;
using Godot;

namespace Vivarium.Game.Render;

/// <summary>Shared triangle hierarchy for contacts with the rendered plant mesh.
/// Instance bounds are only a broadphase: empty space between leaves cannot bend
/// the whole plant. World-space distance remains exact under nonuniform scale.</summary>
internal sealed class GuardianPlantSurface
{
    private sealed class Branch
    {
        public Aabb Bounds;
        public Branch? Left, Right;
        public int Start, Count;
    }
    private readonly Vector3[] _faces;
    private readonly int[] _triangles;
    private readonly Branch? _root;

    public GuardianPlantSurface(Vector3[] faces)
    {
        _faces = faces;
        _triangles = new int[faces.Length / 3];
        for (int i = 0; i < _triangles.Length; i++) _triangles[i] = i;
        if (_triangles.Length > 0) _root = Build(0, _triangles.Length);
    }

    private Vector3 Center(int triangle) =>
        (_faces[triangle * 3] + _faces[triangle * 3 + 1] + _faces[triangle * 3 + 2]) / 3;

    private Branch Build(int start, int count)
    {
        var first = _faces[_triangles[start] * 3];
        var bounds = new Aabb(first, Vector3.Zero);
        for (int i = start; i < start + count; i++)
            for (int j = 0; j < 3; j++) bounds = bounds.Expand(_faces[_triangles[i] * 3 + j]);
        var node = new Branch { Bounds = bounds, Start = start, Count = count };
        if (count <= 12) return node;
        int axis = (int)bounds.Size.MaxAxisIndex();
        Array.Sort(_triangles, start, count,
            System.Collections.Generic.Comparer<int>.Create((a, b) => Center(a)[axis].CompareTo(Center(b)[axis])));
        int half = count / 2;
        node.Left = Build(start, half);
        node.Right = Build(start + half, count - half);
        node.Count = 0;
        return node;
    }

    public bool TryClosest(Transform3D world, Vector3 point, float radius, out Vector3 closest)
    {
        closest = point;
        if (_root == null || !point.IsFinite() || !float.IsFinite(radius) || radius <= 0) return false;
        float determinant = world.Basis.Determinant();
        if (!float.IsFinite(determinant) || MathF.Abs(determinant) < 1e-12f) return false;
        var inverse = world.AffineInverse();
        var local = inverse * point;
        // Frobenius norm bounds inverse stretch, including shear. It is only a
        // conservative pruning radius; triangle distances below use world units.
        float localRadius = radius * MathF.Sqrt(inverse.Basis.X.LengthSquared() +
            inverse.Basis.Y.LengthSquared() + inverse.Basis.Z.LengthSquared());
        float best = radius * radius;
        bool found = false;
        Search(_root, world, point, local, localRadius * localRadius, ref closest, ref best, ref found);
        return found;
    }

    private void Search(Branch node, Transform3D world, Vector3 point, Vector3 local,
        float localRadiusSquared, ref Vector3 closest, ref float best, ref bool found)
    {
        var b = node.Bounds;
        var q = new Vector3(Math.Clamp(local.X, b.Position.X, b.End.X),
            Math.Clamp(local.Y, b.Position.Y, b.End.Y), Math.Clamp(local.Z, b.Position.Z, b.End.Z));
        if (q.DistanceSquaredTo(local) > localRadiusSquared) return;
        if (node.Left != null)
        {
            Search(node.Left, world, point, local, localRadiusSquared, ref closest, ref best, ref found);
            Search(node.Right!, world, point, local, localRadiusSquared, ref closest, ref best, ref found);
            return;
        }
        for (int i = node.Start; i < node.Start + node.Count; i++)
        {
            int t = _triangles[i] * 3;
            var a = world * _faces[t]; var c = world * _faces[t + 2]; var v = world * _faces[t + 1];
            var candidate = ClosestTriangle(point, a, v, c);
            float distance = point.DistanceSquaredTo(candidate);
            if (distance < best) { best = distance; closest = candidate; found = true; }
        }
    }

    private static Vector3 Segment(Vector3 p, Vector3 a, Vector3 b)
    {
        var d = b - a;
        float size = d.LengthSquared();
        return size < 1e-16f ? a : a + d * Math.Clamp((p - a).Dot(d) / size, 0, 1);
    }

    private static Vector3 ClosestTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        var ab = b - a; var ac = c - a;
        if (ab.Cross(ac).LengthSquared() < 1e-16f)
        {
            var q = Segment(p, a, b); var r = Segment(p, b, c); var s = Segment(p, c, a);
            return p.DistanceSquaredTo(q) < p.DistanceSquaredTo(r)
                ? (p.DistanceSquaredTo(q) < p.DistanceSquaredTo(s) ? q : s)
                : (p.DistanceSquaredTo(r) < p.DistanceSquaredTo(s) ? r : s);
        }
        var ap = p - a; float d1 = ab.Dot(ap), d2 = ac.Dot(ap);
        if (d1 <= 0 && d2 <= 0) return a;
        var bp = p - b; float d3 = ab.Dot(bp), d4 = ac.Dot(bp);
        if (d3 >= 0 && d4 <= d3) return b;
        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0) return a + ab * (d1 / (d1 - d3));
        var cp = p - c; float d5 = ab.Dot(cp), d6 = ac.Dot(cp);
        if (d6 >= 0 && d5 <= d6) return c;
        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0) return a + ac * (d2 / (d2 - d6));
        float va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
            return b + (c - b) * ((d4 - d3) / (d4 - d3 + d5 - d6));
        float denominator = 1 / (va + vb + vc);
        return a + ab * (vb * denominator) + ac * (vc * denominator);
    }
}
