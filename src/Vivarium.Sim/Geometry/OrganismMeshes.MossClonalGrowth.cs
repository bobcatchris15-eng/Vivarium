using System;
using System.Collections.Generic;
﻿using Vivarium.Sim.Core;
namespace Vivarium.Sim.Geometry;

/// <summary>Draw-only clonal growth morphologies; no historical state.</summary>
public static partial class OrganismMeshes
{
    /// <summary>
    /// An overlapping union of localized established moss growth points.
    /// Each focus forms a radial cushion whose edge grows toward nearby foci.
    /// Stable from the geometry seed, not dependent on build order or sim RNG.
    /// All three height samples (mat, shoots, fruiting structures) share this field.
    /// </summary>
    private static double MossClonalCoverage(double x, double z, ulong seed)
    {
        double union = 0;
        for (int k = 0; k < 13; k++)
        {
            uint h = unchecked((uint)seed) ^ (uint)((k + 1) * 2654435761u);
            h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15; h *= 0x846ca68bu; h ^= h >> 16;
            double a = (h & 65535u) / 65535.0 * Math.PI * 2;
            double r = Math.Sqrt(((h >> 16) & 65535u) / 65535.0) * .77;
            double sx = Math.Cos(a) * r, sz = Math.Sin(a) * r;
            double extent = .24 + (k % 4) * .075;
            double d = Math.Sqrt((x - sx) * (x - sx) + (z - sz) * (z - sz)) / extent;
            if (d >= 1) continue;
            double t = 1 - d * d;
            double cap = t * t * (3 - 2 * t);
            union = 1 - (1 - union) * (1 - cap * .81);
        }
        return union;
    }

    private static double MossClonalHeight(double x, double z, ulong seed, double heightScale = .35)
    {
        return .005 + heightScale * MossClonalCoverage(x, z, seed);
    }

    /// <summary>
    /// Dense opaque shoot leaflets replace screen-space fur shells. Density follows
    /// merged growing-point occupancy; an uncolonized hole draws no leaflets.
    /// Golden-angle dispersion prevents radial row artifacts at grazing angles.
    /// </summary>
    private static void MossClonalLeaflets(MeshData mesh, ulong seed,
        double[] oldColor, double[] liveColor)
    {
        const int attempts = 1800;
        double phase = (seed & 65535UL) / 65535.0 * Math.PI * 2;
        for (int i=0; i<attempts; i++)
        {
            double f = (i+.5)/attempts;
            double azimuth = i*2.399963229728653+phase;
            double r = Math.Sqrt(f)*1.04;
            double x=Math.Cos(azimuth)*r, z=Math.Sin(azimuth)*r;
            double colony = MossClonalCoverage(x,z,seed);
            if (colony < .14) continue;
            uint hash=(uint)seed^(uint)(i*747796405u+2891336453u);
            hash ^= hash>>16;hash*=0x7feb352du;hash ^= hash>>15;
            if ((hash&255) > colony*255.0) continue;
            double dir=azimuth+((hash>>8)&255)/255.0*Math.PI;
            var axis=new Vec3(Math.Cos(dir),0,Math.Sin(dir));
            var side=new Vec3(-axis.Z,0,axis.X);
            var foot=new Vec3(x,MossClonalHeight(x,z,seed)-.007,z);
            double size=.016+.019*((hash>>16)&255)/255.0;
            var apex=foot+axis*size+Vec3.Up*size*(.35+.6*colony);
            var shoulder=foot+axis*(size*.52)+Vec3.Up*(size*.30);
            double width=size*.32;
            var dull=Primitives.Mix(oldColor,liveColor,.25+.3*colony);
            var fresh=Primitives.Mix(oldColor,liveColor,.66+.27*colony);
            int a=mesh.AddVertex(foot-side*(width*.25),Vec3.Up,dull,1,x,z);
            int b=mesh.AddVertex(foot+side*(width*.25),Vec3.Up,dull,1,x,z);
            int c=mesh.AddVertex(shoulder-side*width,Vec3.Up,fresh,1,x,z);
            int d=mesh.AddVertex(shoulder+side*width,Vec3.Up,fresh,1,x,z);
            int e=mesh.AddVertex(apex,Vec3.Up,fresh,1,x,z);
            Primitives.TriangleFacing(mesh,a,b,c,Vec3.Up);
            Primitives.TriangleFacing(mesh,b,d,c,Vec3.Up);
            Primitives.TriangleFacing(mesh,c,d,e,Vec3.Up);
        }
    }

    /// <summary>
    /// Clip the triangles of the colonization field to an iso-coverage boundary.
    /// Marching-triangle interpolation gives continuous ragged botanical contours
    /// instead of stair-stepped grid cells with their outer triangles discarded.
    /// </summary>
    private static void MossClonalMat(MeshData mesh, ulong seed,
        double[] baseColor, double[] crownColor, double heightScale = .35)
    {
        int n = mesh.FloraDetailLevel == 2 ? 17 : mesh.FloraDetailLevel == 1 ? 27 : 45;
        const double min = -1.10, span = 2.20, iso = .026;
        int side = n + 1;
        int count = side * side;
        var masses = new double[count];
        var positions = new Vec3[count];
        var ids = new int[count];
        Array.Fill(ids, -1);
        for (int z = 0; z <= n; z++)
        for (int x = 0; x <= n; x++)
        {
            int i = z * side + x;
            double px = min + x * span / n, pz = min + z * span / n;
            masses[i] = MossClonalCoverage(px, pz, seed);
            positions[i] = new Vec3(px, .005 + heightScale * masses[i], pz);
        }

        int Vertex(Vec3 point, double mass)
        {
            double[] color = Primitives.Mix(baseColor, crownColor,
                Math.Clamp(.20 + .75 * mass, 0, 1));
            return mesh.AddVertex(point, Vec3.Up, color, 1.0,
                (point.X - min) / span, (point.Z - min) / span, 0, 0);
        }
        int Corner(int id)
        {
            if (ids[id] < 0)
                ids[id] = Vertex(positions[id], masses[id]);
            return ids[id];
        }
        var edges = new Dictionary<long, int>();
        int Crossing(int a, int b)
        {
            int lo = Math.Min(a, b), hi = Math.Max(a, b);
            long key = ((long)lo << 32) | (uint)hi;
            if (edges.TryGetValue(key, out int known)) return known;
            double denominator = masses[b] - masses[a];
            double t = Math.Abs(denominator) > 1e-10
                ? Math.Clamp((iso - masses[a]) / denominator, 0.0, 1.0) : .5;
            var pa = positions[a];
            var pb = positions[b];
            int id = Vertex(pa + (pb - pa) * t, iso);
            edges[key] = id;
            return id;
        }
        void Clip(int a, int b, int c)
        {
            int[] corners = { a, b, c };
            Span<int> polygon = stackalloc int[4];
            int count = 0;
            for (int k = 0; k < 3; k++)
            {
                int i = corners[k], j = corners[(k + 1) % 3];
                bool inside = masses[i] >= iso, next = masses[j] >= iso;
                if (inside) polygon[count++] = Corner(i);
                if (inside != next) polygon[count++] = Crossing(i, j);
            }
            for (int k = 1; k + 1 < count; k++)
                Primitives.TriangleFacing(mesh, polygon[0], polygon[k],
                    polygon[k + 1], Vec3.Up);
        }

        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
        {
            int a = z * side + x, b = a + 1, c = a + side, d = c + 1;
            Clip(a, b, d);
            Clip(a, d, c);
        }
        mesh.RecomputeNormals();
    }

}
