using System;
using System.Collections.Generic;
using System.IO;
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
    private double _bankTable = double.NaN;
    private long _bankLiquidVersion = -1;
    private void BuildBankFringe(ShaderMaterial material)
    {
        _bankMaterial = (ShaderMaterial)material.Duplicate();
        _bankMaterial.SetShaderParameter("sway_strength", 0f);
        _bankNode = new MeshInstance3D { Name="TerrainCreepingBankLeaves", CastShadow=GeometryInstance3D.ShadowCastingSetting.On };
        AddChild(_bankNode);
        _bankTable=double.NaN; _bankLiquidVersion=-1;
    }
    private bool BankWaterChanged()
    {
        long version=_w.Liquid.CaptureRenderState().Version;
        bool changed=_bankTable!=_w.Water.WaterTable || version!=_bankLiquidVersion;
        _bankTable=_w.Water.WaterTable; _bankLiquidVersion=version;
        return changed;
    }
    private bool NearLiquid(Vec2 p)
    {
        foreach(double r in new[]{.16,.32,.52,.72})
        for(int i=0;i<16;i++) {
            double a=i*Math.Tau/16; var q=p+new Vec2(Math.Cos(a),Math.Sin(a))*r;
            if(_w.Domain.Contains(q)&&_w.Water.OpenWaterDepth(q)>.001
                && Math.Abs(_w.Terrain.Height(p)-_w.Terrain.Height(q))<.55) return true;
        }
        return false;
    }
    private void RefreshBankFringe(Vector3 camera,bool haveCamera,List<Rock> rocks,List<LogProp> logs,
        List<GravelPatch> gravel,List<(Vec2 P,double Exclusion)> flora)
    {
        var batch=new MeshData(); var rejected=new Dictionary<string,int>();
        int candidates=0,runners=0,leaves=0; var slopeCounts=new int[4];
        void Reject(string reason) { rejected.TryGetValue(reason,out int n);rejected[reason]=n+1; }
        string? Clear(Vec2 p,double radius)
        {
            if(_w.Domain.SignedDistance(p)>-radius) return "domain";
            foreach(var r in rocks) if(Vec2.Distance(p,r.Position)<r.FootprintRadius+radius) return "rock";
            foreach(var l in logs) if(l.AxisDistance(p)<l.Radius+radius) return "log";
            foreach(var gr in gravel) if(Vec2.Distance(p,gr.Position)<gr.Radius+radius) return "gravel";
            foreach(var f in flora) if(Vec2.Distance(p,f.P)<f.Exclusion+radius) return "flora";
            // Sample the entire conservative disk, including its circumference. Soil and
            // mobile-water queries use the authoritative field; no shoreline approximation.
            int rings=Math.Max(1,(int)Math.Ceiling(radius/.008));
            for(int ring=0;ring<=rings;ring++) {
                double r=radius*ring/rings;int n=ring==0?1:Math.Max(12,(int)Math.Ceiling(Math.Tau*r/.008));
                for(int k=0;k<n;k++) {
                    double a=k*Math.Tau/n;var q=p+new Vec2(Math.Cos(a),Math.Sin(a))*r;
                    // Fine leaf geometry needs the point query; coarse cells can include both dry bank and water.
                    if(_w.SubstrateAt(q)!=Substrate.Soil) return "substrate";
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
                foreach(int edge in new[]{-1,1}) m.AddVertex(Supported(p+side*(half*edge),lift),Vec3.Up,.19,.30,.105,1,(edge+1)*.5,j/(double)steps,.8,1);
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
        foreach(int cell in _w.Grid.DomainCells) {
            var center=_w.Grid.CellCenter(cell);
            if(haveCamera&&Vec2.DistanceSq(center,new Vec2(camera.X,camera.Z))>FadeStart*FadeStart)continue;
            if(_w.Fields.Moisture.Values[cell]<.12||!NearLiquid(center))continue;
            if(Vivarium.Sim.Core.Noise.Gradient(seed,center.X*2.2,center.Z*2.2)<-.38)continue;
            var rng=new CellRng{Seed=Rng.Mix(seed,(ulong)cell)};
            for(int trial=0;trial<16;trial++) {
                var anchor=center+new Vec2((rng.Next01()-.5)*_w.Grid.CellSize,(rng.Next01()-.5)*_w.Grid.CellSize);
                if(Vivarium.Sim.Core.Noise.Gradient(Rng.Mix(seed,17),anchor.X*5.5,anchor.Z*5.5)<-.035){Reject("patch_gap");continue;}
                candidates++;var normal=TerrainMesh.RenderNormal(_w.Terrain,anchor);
                int slope=normal.Y>.94?0:normal.Y>.75?1:normal.Y>.45?2:3;
                if(normal.Y>.94||normal.Y<.16){Reject("slope");continue;}
                double angle=Math.Atan2(normal.Z,normal.X)+(rng.Next01()<.5?-1:1)*(1.05+rng.Next01()*.7);
                var dir=new Vec2(Math.Cos(angle),Math.Sin(angle));var side=new Vec2(-dir.Z,dir.X);
                double length=.22+rng.Next01()*.32,bend=(rng.Next01()-.5)*.13;
                var trail=new List<Vec2>();int stations=(int)Math.Ceiling(length/.012);
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
                if(trail.Count<8){Reject("runner_short_prefix");continue;}
                batch.Append(runner);runners++;slopeCounts[slope]++;
                for(int j=2;j<trail.Count-1;j+=2) {
                    double sign=(j/2%2==0?1:-1),leafLength=.04+rng.Next01()*.06;
                    double yaw=angle+sign*(.65+rng.Next01()*.85);var ld=new Vec2(Math.Cos(yaw),Math.Sin(yaw));var ls=new Vec2(-ld.Z,ld.X);
                    var stem=trail[j];var root=stem+ld*.009;
                    var disk=root+ld*(leafLength*.5);double radius=leafLength*.55;
                    reason=Clear(disk,radius);if(reason!=null){Reject("leaf_"+reason);continue;}
                    // The petiole starts on an accepted runner station. Check its full
                    // width/path too, since the leaf disk need not contain the attachment.
                    for(int part=0;part<=3;part++) {
                        reason=Clear(stem+(root-stem)*(part/3.0),.001);
                        if(reason!=null)break;
                    }
                    if(reason!=null){Reject("petiole_"+reason);continue;}
                    var leaf=new MeshData();Ribbon(leaf,stem,root,.0009,.002);
                    int first=leaf.VertexCount;const int rows=14,lanes=6;
                    double tint=.8+rng.Next01()*.27;
                    for(int row=0;row<=rows;row++) {
                        double t=row/(double)rows;
                        // Nonzero end widths avoid degenerate triangles; rounded blunt tip.
                        double width=leafLength*.29*Math.Sqrt(Math.Max(.0004,1-Math.Pow(2*t-1,2)));
                        for(int lane=0;lane<=lanes;lane++) {
                            double across=2*lane/(double)lanes-1;var q=root+ld*(leafLength*t)+ls*(width*across);
                            double lift=.002+.014*Math.Sin(Math.PI*t)*(1-.6*Math.Abs(across));
                            leaf.AddVertex(Supported(q,lift),Vec3.Up,.20*tint,.43*tint,.18*tint,1,lane/(double)lanes,t,.7+.3*t,0);
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
            batch.RecomputeNormals();
            var faceted=new MeshData();
            for(int t=0;t<batch.Indices.Count;t+=3) {
                int a=batch.Indices[t],b=batch.Indices[t+1],c=batch.Indices[t+2];
                var n=(batch.Position(b)-batch.Position(a)).Cross(batch.Position(c)-batch.Position(a)).Normalized();
                int first=faceted.VertexCount;
                foreach(int v in new[]{a,b,c}) {
                    var smooth=batch.NormalAt(v);
                    // Preserve smooth camber except where a sharp crease reverses a normal.
                    if(smooth.Dot(n)<.2) smooth=(smooth+n*(.2-smooth.Dot(n))).Normalized();
                    faceted.AddVertex(batch.Position(v),smooth,
                    batch.Colors[v*4],batch.Colors[v*4+1],batch.Colors[v*4+2],batch.Colors[v*4+3],
                    batch.UV[v*2],batch.UV[v*2+1],batch.UV2[v*2],batch.UV2[v*2+1]);
                }
                faceted.AddTriangle(first,first+1,first+2);
            }
            batch=faceted;
            for(int i=0;i<batch.VertexCount;i++) {
                var n=batch.NormalAt(i);finite&=Math.Abs(n.Length-1)<.0001;
                var p=batch.Position(i);double gap=p.Y-_w.Terrain.Height(p.XZ);maxGap=Math.Max(maxGap,gap);minGap=Math.Min(minGap,gap);
            }
            foreach(var values in new[]{batch.Positions,batch.Normals,batch.Colors,batch.UV,batch.UV2})foreach(float x in values)finite&=float.IsFinite(x);
            for(int i=0;i<batch.Indices.Count;i+=3) {
                (batch.Indices[i+1],batch.Indices[i+2])=(batch.Indices[i+2],batch.Indices[i+1]);
                int a=batch.Indices[i],b=batch.Indices[i+1],c=batch.Indices[i+2];
                double dot=(batch.Position(b)-batch.Position(a)).Cross(batch.Position(c)-batch.Position(a)).Dot(batch.NormalAt(a)+batch.NormalAt(b)+batch.NormalAt(c));
                minFaceDot=Math.Min(minFaceDot,dot);if(!(dot<0))badFaces++;
                winding&=dot<0;
            }
            _bankNode.Mesh=Bridge.ToArrayMesh(batch,_bankMaterial);
        } else _bankNode.Mesh=null;
        if(Main.ArgAfter("--world-review") is {} output) {
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output,"bank_fringe.json"),JsonSerializer.Serialize(new{
                candidate="terrain_creeping_runners_v4",camera=new[]{camera.X,camera.Y,camera.Z},quality=Quality,candidate_anchors=candidates,runner_instances=runners,leaf_count=leaves,
                slope_bands=new{flat=slopeCounts[0],gentle=slopeCounts[1],steep=slopeCounts[2],very_steep=slopeCounts[3]},rejected,
                vertices=batch.VertexCount,triangles=batch.TriangleCount,finite_unit_normals=finite,clockwise=winding,bad_face_count=badFaces,min_face_dot=batch.TriangleCount==0?0:minFaceDot,
                min_vertex_gap=batch.VertexCount==0?0:minGap,max_vertex_gap=maxGap,complete_triangle_support=SupportedMesh(batch),
                sway_strength=0,scope="Generated world-coordinate geometry; visual and performance acceptance pending"}));
            if(!finite||!winding||!SupportedMesh(batch))throw new InvalidOperationException("Invalid creeping bank geometry");
        }
    }
}


