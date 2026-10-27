using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;

namespace Vivarium.Game.Render;

public partial class FloraRenderer
{
    public sealed class GillTrial : IDisposable
    {
        private readonly List<(MultiMeshInstance3D Node, ArrayMesh Original, ArrayMesh Candidate,
            int OriginalTris, int CandidateTris, Action<int> SetCount)> _entries = new();
        public readonly List<object> Records = new();
        public int Meshes => _entries.Count;
        public void Add(MultiMeshInstance3D node, ArrayMesh candidate, int oldTris, int newTris, Action<int> count)
            => _entries.Add((node, (ArrayMesh)node.Multimesh.Mesh, candidate, oldTris, newTris, count));
        public void Select(bool candidate)
        {
            foreach (var e in _entries)
            {
                var mesh = candidate ? e.Candidate : e.Original;
                if (e.Node.Multimesh.Mesh.GetRid() != mesh.GetRid()) e.Node.Multimesh.Mesh = mesh;
                e.SetCount(candidate ? e.CandidateTris : e.OriginalTris);
            }
        }
        public void Dispose()
        {
            Select(false);
            foreach (var e in _entries) e.Candidate.Dispose();
            _entries.Clear();
        }
    }

    public IEnumerable<bool> PrepareGillTrial(GillTrial trial, int candidateStations)
    {
        if (!_layers.TryGetValue("dewbonnet", out var layer)) yield break;
        if (layer.Profile != null) throw new InvalidOperationException("Gill trial expects the ordinary fungus material path");
        var species = _w.Content.FloraOrThrow("dewbonnet");
        ulong speciesSeed = Hash.Fnv1a64("flora.visual.dewbonnet");
        for (int v = layer.MorphCount * 2; v < layer.Variants.Length; v++)
        {
            var variant = layer.Variants[v];
            ulong seed = Rng.Mix(speciesSeed, (ulong)(v % layer.MorphCount + 1) * 0x9E3779B97F4A7C15UL);
            var watch = Stopwatch.StartNew();
            var baseline = OrganismMeshes.FloraTier(species, seed, 2, farGillStations: DistantGillStations, farEmberMicroSides: DistantEmberMicroSides);
            if (baseline.TriangleCount != variant.FullTris) throw new InvalidOperationException("Gill baseline mismatch");
            yield return true;
            watch.Restart();
            var candidate = OrganismMeshes.FloraTier(species, seed, 2, farGillStations: candidateStations);
            double generationMs = watch.Elapsed.TotalMilliseconds;
            yield return true;
            watch.Restart(); Bridge.PrepareMeshUpload(candidate);
            double packingMs = watch.Elapsed.TotalMilliseconds;
            yield return true;
            watch.Restart();
            var mesh = Bridge.ToArrayMesh(candidate, variant.Full.Multimesh.Mesh.SurfaceGetMaterial(0));
            double publicationMs = watch.Elapsed.TotalMilliseconds;
            trial.Add(variant.Full, mesh, variant.FullTris, candidate.TriangleCount, n => variant.FullTris = n);
            trial.Records.Add(new { variant = v, baseline_triangles = baseline.TriangleCount,
                candidate_triangles = candidate.TriangleCount, vertices = candidate.VertexCount,
                generation_ms = generationMs, packing_ms = packingMs, publication_ms = publicationMs });
            yield return true;
        }
    }
    public IEnumerable<bool> PrepareEmbercupTrial(GillTrial trial, int candidateSides)
    {
        if (!_layers.TryGetValue("embercup", out var layer)) yield break;
        if (layer.Profile != null) throw new InvalidOperationException("Embercup trial expects the ordinary fungus material path");
        var species = _w.Content.FloraOrThrow("embercup");
        ulong speciesSeed = Hash.Fnv1a64("flora.visual.embercup");
        for (int v = layer.MorphCount * 2; v < layer.Variants.Length; v++)
        {
            var variant = layer.Variants[v];
            ulong seed = Rng.Mix(speciesSeed, (ulong)(v % layer.MorphCount + 1) * 0x9E3779B97F4A7C15UL);
            var watch = Stopwatch.StartNew();
            var baseline = OrganismMeshes.FloraTier(species, seed, 2, farGillStations: DistantGillStations, farEmberMicroSides: DistantEmberMicroSides);
            if (baseline.TriangleCount != variant.FullTris) throw new InvalidOperationException("Embercup baseline mismatch");
            yield return true;
            watch.Restart();
            var candidate = OrganismMeshes.FloraTier(species, seed, 2, farEmberMicroSides: candidateSides);
            double generationMs = watch.Elapsed.TotalMilliseconds;
            yield return true;
            watch.Restart(); Bridge.PrepareMeshUpload(candidate);
            double packingMs = watch.Elapsed.TotalMilliseconds;
            yield return true;
            watch.Restart();
            var mesh = Bridge.ToArrayMesh(candidate, variant.Full.Multimesh.Mesh.SurfaceGetMaterial(0));
            double publicationMs = watch.Elapsed.TotalMilliseconds;
            trial.Add(variant.Full, mesh, variant.FullTris, candidate.TriangleCount, n => variant.FullTris = n);
            trial.Records.Add(new { variant = v, baseline_triangles = baseline.TriangleCount,
                candidate_triangles = candidate.TriangleCount, vertices = candidate.VertexCount,
                generation_ms = generationMs, packing_ms = packingMs, publication_ms = publicationMs });
            yield return true;
        }
    }
}
