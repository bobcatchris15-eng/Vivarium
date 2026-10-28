using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.World;
namespace Vivarium.Game.Render;

// Disposable presentation only. Fine complete-leaf envelopes intentionally differ from
// the unchanged ambient/tall-cover whole-clump exclusions in the parent file.
public partial class AmbientGroundCoverRenderer
{
    private MeshInstance3D _bankNode = null!;
    private ShaderMaterial _bankMaterial = null!;

private sealed class BankPatch
    {
        public MeshInstance3D Node = null!;
        public int Epoch = -1;
    }
    private readonly Dictionary<(int X, int Z), BankPatch> _bankPatches = new();
    private Dictionary<(int X, int Z), int[]> _bankCells = new();
    private int _bankEpoch;
    public int BankPatchBuildCount { get; private set; }
    private void BuildBankFringe(ShaderMaterial material)
    {
        _bankMaterial = (ShaderMaterial)material.Duplicate();
        _bankMaterial.SetShaderParameter("sway_strength", 0f);
        _bankMaterial.SetShaderParameter("instance_shape_variation", false);
        _bankNode = new MeshInstance3D { Name="TerrainCreepingBankLeaves", CastShadow=GeometryInstance3D.ShadowCastingSetting.On };
        AddChild(_bankNode);
        _bankPatches.Clear(); _bankEpoch = 0; BankPatchBuildCount = 0;
        _bankCells = _w.Grid.DomainCells.GroupBy(cell => {
            var p = _w.Grid.CellCenter(cell);
            return ((int)Math.Floor(p.X / 2), (int)Math.Floor(p.Z / 2));
        }).ToDictionary(group => group.Key, group => group.ToArray());
    }

    private IEnumerable<bool> RefreshBankFringeBatched(Vector3 camera,bool haveCamera,List<Rock> rocks,List<LogProp> logs,
        List<GravelPatch> gravel,List<(Vec2 P,double Exclusion)> flora)
    {
        // Geometry belongs to world-space patches. Moving the camera only changes which
        // cached patches are visible and prepares first visits; it never rebuilds old ones.
        int epoch = _bankEpoch;
        bool Wanted((int X, int Z) key)
        {
            if (!haveCamera) return true;
            double x = Math.Clamp(camera.X, key.X * 2, (key.X + 1) * 2);
            double z = Math.Clamp(camera.Z, key.Z * 2, (key.Z + 1) * 2);
            return (x - camera.X) * (x - camera.X) + (z - camera.Z) * (z - camera.Z) <= FadeStart * FadeStart;
        }
        foreach (var (key, patch) in _bankPatches) patch.Node.Visible = Wanted(key);
        // Full footprints matter when preparing a whole patch near the view boundary.
        // Each patch is culled below to its own bounds plus the farthest reach of any query.
        var allRocks = _w.Props.Rocks.ToList(); var allLogs = _w.Props.Logs.ToList(); var allGravel = _w.Props.Gravel.ToList();
        // Reach from patch centre: half-diagonal + anchor jitter + runner/leaf extent (<=~.46m) + slack.
        double reach = Math.Sqrt(2) + _w.Grid.CellSize * Math.Sqrt(.5) + .6;
        foreach (var (key, cells) in _bankCells)
        {
            if (!Wanted(key)) continue;
            if (!_bankPatches.TryGetValue(key, out var patch))
            {
                patch = new BankPatch { Node = new MeshInstance3D {
                    Name = $"BankPatch_{key.X}_{key.Z}", CastShadow = GeometryInstance3D.ShadowCastingSetting.On
                }};
                _bankNode.AddChild(patch.Node); _bankPatches.Add(key, patch);
            }
            patch.Node.Visible = true;
            if (patch.Epoch == epoch) continue;
            var pc = new Vec2(key.X * 2 + 1, key.Z * 2 + 1);
            var patchRocks = allRocks.Where(r => Vec2.Distance(pc, r.Position) < r.FootprintRadius + reach).ToList();
            var patchLogs = allLogs.Where(l => l.AxisDistance(pc) < l.Radius + reach).ToList();
            var patchGravel = allGravel.Where(g => Vec2.Distance(pc, g.Position) < g.Radius + .20 + reach).ToList();
            var patchFlora = flora.Where(f => Vec2.Distance(pc, f.P) < f.Exclusion + reach).ToList();
            foreach (bool step in BuildBankPatchBatched(camera, false, patchRocks, patchLogs, patchGravel, patchFlora, cells, patch.Node))
                yield return step;
            patch.Epoch = epoch; BankPatchBuildCount++;
        }
        if (Main.ArgAfter("--world-review") is { } output)
            File.WriteAllText(Path.Combine(output, "bank_fringe.json"), JsonSerializer.Serialize(new {
                candidate = "terrain_creeping_runners_v4_cached_patches", patch_size_m = 2,
                cached_patches = _bankPatches.Count, visible_patches = _bankPatches.Values.Count(p => p.Node.Visible),
                patch_reports = _bankPatches.Keys.Select(k => $"bank_fringe_BankPatch_{k.X}_{k.Z}.json").ToArray()
            }));
    }

    private IEnumerable<bool> BuildBankPatchBatched(Vector3 camera,bool haveCamera,List<Rock> rocks,List<LogProp> logs,
        List<GravelPatch> gravel,List<(Vec2 P,double Exclusion)> flora,IEnumerable<int> cells,MeshInstance3D target)
    {
        var batch=new MeshData(); var rejected=new Dictionary<string,int>();
        int candidates=0,runners=0,leaves=0; var slopeCounts=new int[4];
        void Reject(string reason) { rejected.TryGetValue(reason,out int n);rejected[reason]=n+1; }
        string? Clear(Vec2 p,double radius)
        {
            if(_w.Domain.SignedDistance(p)>-radius) return "domain";
            foreach(var r in rocks) if(Vec2.Distance(p,r.Position)<r.FootprintRadius+radius) return "rock";
            foreach(var l in logs) if(l.AxisDistance(p)<l.Radius+radius) return "log";
            if(GravelCoverWeight(p,gravel)<=0) return "gravel";
            foreach(var f in flora) if(Vec2.Distance(p,f.P)<f.Exclusion+radius) return "flora";
            // GravelCoverWeight ignores patches beyond Radius+.20; only those that can reach the disk matter.
            List<GravelPatch>? nearGravel=null;
            foreach(var g in gravel) if(Vec2.Distance(p,g.Position)<g.Radius+.20+radius) (nearGravel??=new()).Add(g);
            // Sample the entire conservative disk, including its circumference. Soil and
            // mobile-water queries use the authoritative field; no shoreline approximation.
            int rings=Math.Max(1,(int)Math.Ceiling(radius/.008));
            for(int ring=0;ring<=rings;ring++) {
                double r=radius*ring/rings;int n=ring==0?1:Math.Max(12,(int)Math.Ceiling(Math.Tau*r/.008));
                for(int k=0;k<n;k++) {
                    double a=k*Math.Tau/n;var q=p+new Vec2(Math.Cos(a),Math.Sin(a))*r;
                    // Fine leaf geometry needs the point query; coarse cells can include both dry bank and water.
                    if(_w.SubstrateAt(q)!=Substrate.Soil) return "substrate";
                    if(nearGravel!=null&&GravelCoverWeight(q,nearGravel)<=0) return "gravel";
                    if(_w.Water.OpenWaterDepth(q)>.0003) return "water";
                    if(_w.GroundHeight(q)-_w.Terrain.Height(q)>.002) return "raised_support";
                }
            }
            return null;
        }
        Vec3 Supported(Vec2 p,double lift) => Vec3.FromXZ(p,_w.Terrain.Height(p)+lift);
        // All triangles get positive-Y mathematical normals, then reverse for Godot.
        void Face(MeshData m,int a,int b,int c) {
            if((m.Position(b)-m.Position(a)).Cross(m.Position(c)-m.Position(a)).Y<0) (b,c)=(c,b);
            m.AddTriangle(a,b,c);
        }
        void Ribbon(MeshData m,Vec2 a,Vec2 b,double half,double lift)
        {
            var d=b-a;double len=Math.Sqrt(d.X*d.X+d.Z*d.Z);if(len<.00001)return;
            var side=new Vec2(-d.Z/len,d.X/len);int steps=Math.Max(1,(int)Math.Ceiling(len/.006));int start=m.VertexCount;
            for(int j=0;j<=steps;j++) {
                var p=a+d*(j/(double)steps);
                foreach(int edge in new[]{-1,1}) m.AddVertex(Supported(p+side*(half*edge),lift),Vec3.Up,.75,.72,.60,1,(edge+1)*.5,j/(double)steps,.8,1);
                if(j>0) {int n=start+j*2; Face(m,n-2,n-1,n);Face(m,n-1,n+1,n);}
            }
        }
        bool SupportedMesh(MeshData m)
        {
            for(int i=0;i<m.Indices.Count;i+=3) {
                var a=m.Position(m.Indices[i]);var b=m.Position(m.Indices[i+1]);var c=m.Position(m.Indices[i+2]);
                foreach(var q in new[]{(a+b+c)/3,(a+b)/2,(b+c)/2,(a+c)/2}) {
                    double gap=q.Y-_w.Terrain.Height(q.XZ);
                    if(gap<.0005||gap>.021||_w.Water.OpenWaterDepth(q.XZ)>.0003)return false;
                }
            }
            return true;
        }
        ulong seed=Rng.Mix(_w.Seed,0x435245455042414EUL);
        foreach(int cell in cells) {
            yield return true;
            var center=_w.Grid.CellCenter(cell);
            if(haveCamera&&Vec2.DistanceSq(center,new Vec2(camera.X,camera.Z))>FadeStart*FadeStart)continue;
            // The creeping layer supplies the steep-ground counterpart to the turf.
            // It must also reach dry bank shoulders beyond the old narrow water halo.
            var rng=new CellRng{Seed=Rng.Mix(seed,(ulong)cell)};
            double phaseX=rng.Next01(),phaseZ=rng.Next01();
            // Additional attempts compensate for the wider scatter and whole-leaf
            // rejections on narrow banks, retaining the approved dense fringe.
            for(int trial=0;trial<16;trial++) {
                yield return true;
                double u=(trial%4+rng.Next01())/4+phaseX,v=(trial/4+rng.Next01())/4+phaseZ;
                var anchor=center+new Vec2((u-Math.Floor(u)-.5)*_w.Grid.CellSize,
                    (v-Math.Floor(v)-.5)*_w.Grid.CellSize);
                if(rng.Next01()>GravelCoverWeight(anchor,gravel)){Reject("gravel_taper");continue;}
                candidates++;var normal=TerrainMesh.RenderNormal(_w.Terrain,anchor);
                int slope=normal.Y>.94?0:normal.Y>.75?1:normal.Y>.45?2:3;
                if(normal.Y>.78||normal.Y<.12){Reject("slope");continue;}
                double angle=Math.Atan2(normal.Z,normal.X)+(rng.Next01()<.5?-1:1)*(1.05+rng.Next01()*.7);
                var dir=new Vec2(Math.Cos(angle),Math.Sin(angle));var side=new Vec2(-dir.Z,dir.X);
                double length=.20+rng.Next01()*.12,bend=(rng.Next01()-.5)*.09;
                var trail=new List<Vec2>();int stations=(int)Math.Ceiling(length/.016);
                string? reason=Clear(anchor,.002);
                if(reason!=null){Reject("anchor_"+reason);continue;}
                trail.Add(anchor);var runner=new MeshData();
                // Grow one connected checked prefix. A later obstruction terminates the
                // runner; it never removes an interior segment or strands its leaves.
                for(int j=1;j<=stations;j++) {
                    double t=j/(double)stations;var p=anchor+dir*(length*t)+side*(Math.Sin(t*Math.PI)*bend);
                    reason=Clear(p,.002);
                    if(reason!=null){Reject("prefix_stop_"+reason);break;}
                    var segment=new MeshData();Ribbon(segment,trail[^1],p,.0012,.002);
                    if(!SupportedMesh(segment)){Reject("prefix_stop_crease");break;}
                    runner.Append(segment);trail.Add(p);
                }
                if(trail.Count<4){Reject("runner_short_prefix");continue;}
                batch.Append(runner);runners++;slopeCounts[slope]++;
                for(int j=2;j<trail.Count-1;j+=2) {
                    yield return true;
                    double sign=(j/2%2==0?1:-1),leafLength=.045+rng.Next01()*.03;
                    double yaw=angle+sign*(.65+rng.Next01()*.85);var ld=new Vec2(Math.Cos(yaw),Math.Sin(yaw));var ls=new Vec2(-ld.Z,ld.X);
                    var stem=trail[j];var root=stem+ld*.009;
                    var disk=root+ld*(leafLength*.5);double radius=leafLength*.55;
                    if(rng.Next01()>GravelCoverWeight(disk,gravel)){Reject("leaf_gravel_taper");continue;}
                    reason=Clear(disk,radius);if(reason!=null){Reject("leaf_"+reason);continue;}
                    // The petiole starts on an accepted runner station. Check its full
                    // width/path too, since the leaf disk need not contain the attachment.
                    for(int part=0;part<=3;part++) {
                        reason=Clear(stem+(root-stem)*(part/3.0),.001);
                        if(reason!=null)break;
                    }
                    if(reason!=null){Reject("petiole_"+reason);continue;}
                    var leaf=new MeshData();Ribbon(leaf,stem,root,.0009,.002);
                    int first=leaf.VertexCount;const int rows=8,lanes=4;
                    double tint=.88+rng.Next01()*.16;
                    double breadth=.22+rng.Next01()*.12,tipShape=.5+rng.Next01()*.65;
                    double arch=.009+rng.Next01()*.006,asymmetry=(rng.Next01()-.5)*.10;
                    for(int row=0;row<=rows;row++) {
                        double t=row/(double)rows;
                        // Nonzero end widths avoid degenerate triangles; rounded blunt tip.
                        double width=leafLength*breadth*Math.Pow(Math.Max(.0004,Math.Sin(Math.PI*t)),tipShape);
                        for(int lane=0;lane<=lanes;lane++) {
                            double across=2*lane/(double)lanes-1;var q=root+ld*(leafLength*t)
                                +ls*(width*across+leafLength*asymmetry*Math.Sin(Math.PI*t));
                            double lift=.002+arch*Math.Sin(Math.PI*t)*(1-.6*Math.Abs(across));
                            leaf.AddVertex(Supported(q,lift),Vec3.Up,tint,tint,tint,1,lane/(double)lanes,t,.7+.3*t,0);
                            if(row>0&&lane>0){int n=first+row*(lanes+1)+lane;Face(leaf,n-lanes-2,n-lanes-1,n-1);Face(leaf,n-lanes-1,n,n-1);}
                        }
                    }
                    if(!SupportedMesh(leaf)){Reject("leaf_crease");continue;}
                    batch.Append(leaf);leaves++;
                }
            }
        }
        bool finite=true,winding=true;double maxGap=0,minGap=double.MaxValue,minFaceDot=double.MaxValue;int badFaces=0;
        if(batch.TriangleCount>0) {
            // Terrain creases can oppose a smooth vertex normal to a small incident
            // triangle. Split by actual face, preserving every attribute and triangle.
            // Keep smooth camber normals in each face hemisphere, then reverse Godot indices.
            using (FrameProfiler.Measure("AmbientGround.BankNormals")) batch.RecomputeNormals();
            yield return true;
            var faceted=new MeshData();
            // Corners whose smooth normal needs no crease correction share one vertex per source vertex.
            var shared=new int[batch.VertexCount];Array.Fill(shared,-1);
            for(int t=0;t<batch.Indices.Count;t+=3) {
                if (t % 768 == 0) yield return true;
                int a=batch.Indices[t],b=batch.Indices[t+1],c=batch.Indices[t+2];
                var n=(batch.Position(b)-batch.Position(a)).Cross(batch.Position(c)-batch.Position(a)).Normalized();
                var corner=new int[3];int ci=0;
                foreach(int v in new[]{a,b,c}) {
                    var smooth=batch.NormalAt(v);
                    // Preserve smooth camber except where a sharp crease reverses a normal.
                    bool bent=smooth.Dot(n)<.2;
                    if(bent) smooth=(smooth+n*(.2-smooth.Dot(n))).Normalized();
                    else if(shared[v]>=0){corner[ci++]=shared[v];continue;}
                    int idx=faceted.VertexCount;if(!bent)shared[v]=idx;corner[ci++]=idx;
                    faceted.AddVertex(batch.Position(v),smooth,
                    batch.Colors[v*4],batch.Colors[v*4+1],batch.Colors[v*4+2],batch.Colors[v*4+3],
                    batch.UV[v*2],batch.UV[v*2+1],batch.UV2[v*2],batch.UV2[v*2+1]);
                }
                faceted.AddTriangle(corner[0],corner[1],corner[2]);
            }
            batch=faceted;
            for(int i=0;i<batch.VertexCount;i++) {
                if (i % 256 == 0) yield return true;
                var n=batch.NormalAt(i);finite&=Math.Abs(n.Length-1)<.0001;
                var p=batch.Position(i);double gap=p.Y-_w.Terrain.Height(p.XZ);maxGap=Math.Max(maxGap,gap);minGap=Math.Min(minGap,gap);
            }
            foreach(var values in new[]{batch.Positions,batch.Normals,batch.Colors,batch.UV,batch.UV2})foreach(float x in values)finite&=float.IsFinite(x);
            for(int i=0;i<batch.Indices.Count;i+=3) {
                if (i % 768 == 0) yield return true;
                (batch.Indices[i+1],batch.Indices[i+2])=(batch.Indices[i+2],batch.Indices[i+1]);
                int a=batch.Indices[i],b=batch.Indices[i+1],c=batch.Indices[i+2];
                double dot=(batch.Position(b)-batch.Position(a)).Cross(batch.Position(c)-batch.Position(a)).Dot(batch.NormalAt(a)+batch.NormalAt(b)+batch.NormalAt(c));
                minFaceDot=Math.Min(minFaceDot,dot);if(!(dot<0))badFaces++;
                winding&=dot<0;
            }
            Bridge.PackedMesh packed = null!;
            using (var packing = Bridge.PackMeshBatched(batch, p => packed = p).GetEnumerator())
            while (true)
            {
                bool more;
                using (FrameProfiler.Measure("AmbientGround.BankPacking")) more = packing.MoveNext();
                if (!more) break;
                yield return true;
            }
            float[] tangents = null!;
            using (var preparation = Bridge.GenerateTangentsBatched(packed.Vertices, packed.Normals, packed.UV, batch.Indices, batch.VertexCount, t => tangents = t).GetEnumerator())
            while (true)
            {
                bool more;
                using (FrameProfiler.Measure("AmbientGround.BankTangents")) more = preparation.MoveNext();
                if (!more) break;
                yield return true;
            }

            using (FrameProfiler.Measure("AmbientGround.BankUpload"))
            {
                Godot.Collections.Array arrays;
                using (FrameProfiler.Measure("AmbientGround.BankNativeArrays"))
                    arrays = Bridge.CreateNativeArrays(packed, tangents, batch.Indices);
                ArrayMesh mesh;
                using (FrameProfiler.Measure("AmbientGround.BankMeshCreate"))
                {
                    // Reuse the patch's mesh resource; a fresh ArrayMesh per rebuild leaks the old RID until GC.
                    if (target.Mesh is ArrayMesh existing) { mesh = existing; mesh.ClearSurfaces(); }
                    else mesh = new ArrayMesh();
                    mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
                    if (_bankMaterial != null) mesh.SurfaceSetMaterial(0, _bankMaterial);
                    arrays.Dispose();
                }
                using (FrameProfiler.Measure("AmbientGround.BankAssignment"))
                    if (target.Mesh != mesh) target.Mesh = mesh;
            }
        } else target.Mesh=null;
        if(Main.ArgAfter("--world-review") is {} output) {
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output,$"bank_fringe_{target.Name}.json"),JsonSerializer.Serialize(new{
                candidate="terrain_creeping_runners_v4",camera=new[]{camera.X,camera.Y,camera.Z},quality=Quality,candidate_anchors=candidates,runner_instances=runners,leaf_count=leaves,
                slope_bands=new{flat=slopeCounts[0],gentle=slopeCounts[1],steep=slopeCounts[2],very_steep=slopeCounts[3]},rejected,
                vertices=batch.VertexCount,triangles=batch.TriangleCount,finite_unit_normals=finite,clockwise=winding,bad_face_count=badFaces,min_face_dot=batch.TriangleCount==0?0:minFaceDot,
                min_vertex_gap=batch.VertexCount==0?0:minGap,max_vertex_gap=maxGap,complete_triangle_support=SupportedMesh(batch),
                sway_strength=0,scope="Generated world-coordinate geometry; visual and performance acceptance pending"}));
            if(!finite||!winding||!SupportedMesh(batch))throw new InvalidOperationException("Invalid creeping bank geometry");
        }
    }
}


