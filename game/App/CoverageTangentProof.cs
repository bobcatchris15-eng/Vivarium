using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Vivarium.Sim.Geometry;
using Vec3 = Vivarium.Sim.Core.Vec3;

namespace Vivarium.Game.App;

/// <summary>Native proof for the opt-in coverage tangent preparation; invoke on the Godot main thread.</summary>
public static class CoverageTangentProof
{
    public static string Run()
    {
        var source=Fixture();
        var fallback=new MeshData(); fallback.Append(source);
        var prepared=new MeshData(); prepared.Append(source);
        int mainThread=System.Environment.CurrentManagedThreadId, workerThread=mainThread;
        // Exercise upgrade from an existing packed-only entry, then prepare the exact tangent routine on a worker.
        Bridge.PrepareMeshUpload(prepared);
        using var barrier=new Barrier(2);
        void Prepare()
        {
            if(!barrier.SignalAndWait(TimeSpan.FromSeconds(5))) throw new TimeoutException("concurrent preparation barrier timed out");
            Bridge.PrepareMeshUpload(prepared,prepareTangents:true);
        }
        var first=Task.Factory.StartNew(() => { workerThread=System.Environment.CurrentManagedThreadId; Prepare(); },TaskCreationOptions.LongRunning);
        var second=Task.Factory.StartNew(Prepare,TaskCreationOptions.LongRunning);
        Task.WhenAll(first,second).GetAwaiter().GetResult();
        if(workerThread==mainThread) throw new InvalidOperationException("tangent preparation did not run on a worker");
        // The pre-existing cooperative tangent path remains equivalent to the synchronous reference.
        var packed=Bridge.PackMesh(source);
        float[]? batched=null;
        foreach(var _ in Bridge.GenerateTangentsBatched(packed.Vertices,packed.Normals,packed.UV,source.Indices,source.VertexCount,t => batched=t)) { }
        var direct=Bridge.GenerateTangents(packed.Vertices,packed.Normals,packed.UV,source.Indices,source.VertexCount);
        if(batched==null || !direct.SequenceEqual(batched)) throw new InvalidOperationException("batched tangent fallback changed");
        long before=Bridge.PreparedTangentUploads;
        using var reference=Bridge.ToArrayMesh(fallback);
        if(Bridge.PreparedTangentUploads!=before) throw new InvalidOperationException("fallback unexpectedly used prepared tangents");
        using var candidate=Bridge.ToArrayMesh(prepared);
        if(Bridge.PreparedTangentUploads!=before+1) throw new InvalidOperationException("prepared tangent upload was not consumed");
        using var a=reference.SurfaceGetArrays(0); using var b=candidate.SurfaceGetArrays(0);
        if(!a[(int)Mesh.ArrayType.Vertex].AsVector3Array().SequenceEqual(b[(int)Mesh.ArrayType.Vertex].AsVector3Array())) throw new InvalidOperationException("vertex arrays differ");
        if(!a[(int)Mesh.ArrayType.Normal].AsVector3Array().SequenceEqual(b[(int)Mesh.ArrayType.Normal].AsVector3Array())) throw new InvalidOperationException("normal arrays differ");
        if(!a[(int)Mesh.ArrayType.Color].AsColorArray().SequenceEqual(b[(int)Mesh.ArrayType.Color].AsColorArray())) throw new InvalidOperationException("color arrays differ");
        if(!a[(int)Mesh.ArrayType.TexUV].AsVector2Array().SequenceEqual(b[(int)Mesh.ArrayType.TexUV].AsVector2Array())) throw new InvalidOperationException("UV arrays differ");
        if(!a[(int)Mesh.ArrayType.TexUV2].AsVector2Array().SequenceEqual(b[(int)Mesh.ArrayType.TexUV2].AsVector2Array())) throw new InvalidOperationException("UV2 arrays differ");
        if(!a[(int)Mesh.ArrayType.Tangent].AsFloat32Array().SequenceEqual(b[(int)Mesh.ArrayType.Tangent].AsFloat32Array())) throw new InvalidOperationException("tangent arrays differ");
        if(!a[(int)Mesh.ArrayType.Index].AsInt32Array().SequenceEqual(b[(int)Mesh.ArrayType.Index].AsInt32Array())) throw new InvalidOperationException("index arrays differ");
        return $"COVERAGE_TANGENT_PROOF_OK vertices={source.VertexCount} triangles={source.TriangleCount} worker_thread={workerThread} prepared_uploads=1";
    }
    private static MeshData Fixture()
    {
        var mesh=new MeshData(); var up=new Vec3(0,1,0);
        mesh.AddVertex(new Vec3(0,0,0),up,0.2,0.4,0.1,1,0,0,0.1,0.2);
        mesh.AddVertex(new Vec3(1,0,0),up,0.3,0.5,0.1,1,1,0,0.2,0.2);
        mesh.AddVertex(new Vec3(0,0,1),up,0.4,0.6,0.1,1,0,1,0.1,0.3);
        mesh.AddVertex(new Vec3(1,0,1),up,0.5,0.7,0.1,1,-1,1,0.2,0.3); // mirrored UV orientation
        mesh.AddVertex(new Vec3(2,0,0),up,0.2,0.5,0.2,1,0,0);
        mesh.AddVertex(new Vec3(3,0,0),up,0.2,0.5,0.2,1,0,0);
        mesh.AddVertex(new Vec3(2,0,1),up,0.2,0.5,0.2,1,0,0); // degenerate UVs trigger tangent fallback
        mesh.AddVertex(new Vec3(4,0,1),up,0.2,0.5,0.2,1,0,0); // unreferenced vertex
        mesh.AddTriangle(0,1,2); mesh.AddTriangle(1,3,2); mesh.AddTriangle(4,5,6);
        return mesh;
    }
}
