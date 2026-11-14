using System;
using System.Diagnostics;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;

namespace Vivarium.Sim.Repr;

internal static class ColonyMeshTestFixture
{
    public static ColonySystem BuildColonizedCube()
    {
        var sub = new LayeredSubstrate(SubstrateSpec.TestCube(7));
        const double rain = 8e-3 / 3600.0;
        for (int i = 0; i < 60; i++) sub.Water.Step(60, _ => rain, 0, 0);

        var sys = new ColonySystem(sub, 7, p => p.X < 0.45 ? 0.45 : 0.78);
        sys.Seed("cushion_moss", new Vec2(0.18, 0.30), 0.10);
        sys.Seed("feather_moss", new Vec2(0.24, 0.72), 0.10);
        sys.Seed("crust_lichen", new Vec2(0.55, 0.48), 0.10);
        sys.Seed("leaf_lichen",  new Vec2(0.54, 0.30), 0.09);
        sys.Seed("branch_lichen", new Vec2(0.76, 0.62), 0.10);
        sys.DepositLitter(new Vec2(0.32, 0.50), 0.08, 0.004);

        const double dt = 6 * 3600.0;
        for (int s = 0; s < 120 * 4; s++) sys.Step(dt);
        return sys;
    }
}

public sealed class ColonyMicrostructureTest : IReprSelfTest
{
    public string Name => "colony.micro.valid_deterministic_lod";
    public string? Run()
    {
        var sys=ColonyMeshTestFixture.BuildColonizedCube(); int[] triangles=new int[3];
        foreach(var tile in sys.Field.Tiles) for(int d=0;d<3;d++)
        {
            var a=ColonyMesher.BuildTileMicrostructure(sys,tile.Tx,tile.Tz,d).Mesh;
            var b=ColonyMesher.BuildTileMicrostructure(sys,tile.Tx,tile.Tz,d).Mesh;
            triangles[d]+=a.TriangleCount;
            if(a.DigestHex()!=b.DigestHex()) return $"tile ({tile.Tx},{tile.Tz}) detail {d} nondeterministic";
            if(a.Colors.Count!=b.Colors.Count || a.Custom0.Count!=b.Custom0.Count || a.Custom1.Count!=b.Custom1.Count) return "nondeterministic attribute counts";
            for(int j=0;j<a.Colors.Count;j++) if(a.Colors[j]!=b.Colors[j]) return "nondeterministic colors";
            for(int j=0;j<a.Custom0.Count;j++) if(a.Custom0[j]!=b.Custom0[j] || a.Custom1[j]!=b.Custom1[j]) return "nondeterministic custom attributes";
            if(a.Indices.Count%3!=0 || a.Normals.Count!=a.Positions.Count || a.Custom0.Count!=a.VertexCount*4 || a.Custom1.Count!=a.VertexCount*4) return "invalid microstructure channels";
            for(int i=0;i<a.VertexCount;i++)
            {
                if(a.UV2[2*i+1]<0.5f) return "base sheet emitted by microstructure-only API";
                var p=a.Position(i);
                double x=Math.Clamp(p.X,sys.Substrate.Ox,sys.Substrate.Ox+sys.Substrate.Nx*sys.Substrate.Cell);
                double z=Math.Clamp(p.Z,sys.Substrate.Oz,sys.Substrate.Oz+sys.Substrate.Nz*sys.Substrate.Cell);
                double h=SubstrateMesher.SurfaceHeightAt(sys.Substrate,x,z,out _,out _);
                if(p.Y<h-0.004 || p.Y>h+0.035) return $"detail {d} detached vertex delta={p.Y-h:F4} m";
            }
        }
        if(!(triangles[0]>triangles[1] && triangles[1]>triangles[2] && triangles[2]==0)) return $"unexpected micro LOD triangles {triangles[0]}/{triangles[1]}/{triangles[2]}";
        return null;
    }
}

public sealed class ColonyVisualFieldTest : IReprSelfTest
{
    public string Name => "colony.visual_field.state";
    public string? Run()
    {
        var sys=ColonyMeshTestFixture.BuildColonizedCube(); bool any=false;
        for(int x=0;x<50;x++) for(int z=0;z<50;z++)
        {
            var p=new Vec2((x+0.5)*0.02,(z+0.5)*0.02);
            var a=ColonyVisualField.Sample(sys,p); var b=ColonyVisualField.Sample(sys,p);
            if(a!=b) return "field sampling is nondeterministic";
            if(a.Moss<0 || a.Moss>1 || a.Crust<0 || a.Crust>1 || a.Foliose<0 || a.Foliose>1 || a.Fruticose<0 || a.Fruticose>1) return "coverage channel out of bounds";
            any |= a.Moss+a.Crust+a.Foliose+a.Fruticose>0;
        }
        if(!any) return "field contained no colony coverage";
        foreach(var tile in sys.Field.Tiles) for(int k=0;k<tile.Species.Length;k++)
        {
            if(tile.Species[k]==0 || tile.Cover(k)<0.1 || tile.HealthQ[k]<32) continue;
            ColonyField.CellCoords(tile,k/2,out int ix,out int iz); var p=sys.Field.CellCentre(ix,iz);
            var before=ColonyVisualField.Sample(sys,p); byte old=tile.HealthQ[k]; tile.HealthQ[k]=0;
            var after=ColonyVisualField.Sample(sys,p); tile.HealthQ[k]=old;
            double first=before.Moss+before.Crust+before.Foliose+before.Fruticose;
            double second=after.Moss+after.Crust+after.Foliose+after.Fruticose;
            if(Math.Abs(first-second)<1e-6 && before.Health-after.Health>0.005) return null;
        }
        return "field failed to separate health from coverage";
    }
}

public sealed class ColonyHydrationInvalidationTest : IReprSelfTest
{
    public string Name => "colony.mesh.hydration_invalidation";
    private static Vivarium.Sim.Geometry.MeshData BuildMicro(ColonySystem sys)
    {
        var mesh=new Vivarium.Sim.Geometry.MeshData();
        foreach(var tile in sys.Field.Tiles) mesh.Append(ColonyMesher.BuildTileMicrostructure(sys,tile.Tx,tile.Tz,0).Mesh);
        return mesh;
    }
    public string? Run()
    {
        var sys=ColonyMeshTestFixture.BuildColonizedCube();
        var sub=sys.Substrate;
        var before=BuildMicro(sys);
        long cursor=sub.DirtyLog.Head;
        var coverage=new System.Collections.Generic.List<ushort[]>();
        foreach(var tile in sys.Field.Tiles) coverage.Add((ushort[])tile.CoverQ.Clone());
        for(int i=0;i<80;i++) sub.Water.Step(21600, _ => 0, 8e-3/ColonySystem.Day, 0);
        var records=new System.Collections.Generic.List<DirtyRecord>();
        sub.DirtyLog.Read(cursor,DirtyDomain.Water,records);
        if(records.Count==0) return "water change did not publish a Water dirty record";
        int ti=0;
        foreach(var tile in sys.Field.Tiles)
        {
            for(int k=0;k<tile.CoverQ.Length;k++) if(tile.CoverQ[k]!=coverage[ti][k]) return "coverage changed without colony step";
            ti++;
        }
        var after=BuildMicro(sys);
        if(before.Colors.Count!=after.Colors.Count) return "hydration unexpectedly changed mesh topology";
        for(int i=0;i<before.Colors.Count;i++) if(Math.Abs(before.Colors[i]-after.Colors[i])>1e-4) return null;
        return "mesh color did not respond to hydration change with unchanged coverage";
    }
}

public sealed class ColonyAppearanceVersionTest : IReprSelfTest
{
    public string Name => "colony.mesh.appearance_version";
    public string? Run()
    {
        var sys=ColonyMeshTestFixture.BuildColonizedCube();
        var sub=sys.Substrate;
        for(int i=0;i<100;i++) sub.Water.Step(21600,_=>0,8e-3/ColonySystem.Day,0);
        for(int step=0;step<100;step++)
        {
            foreach(var tile in sys.Field.Tiles)
            {
                var cover=(ushort[])tile.CoverQ.Clone(); var health=(byte[])tile.HealthQ.Clone();
                var thickness=(ushort[])tile.ThickUm.Clone(); var flags=(byte[])tile.Flags.Clone();
                ulong version=tile.Version;
                sys.Step(21600);
                for(int k=0;k<tile.Species.Length;k++)
                {
                    bool changed=health[k]!=tile.HealthQ[k] || thickness[k]!=tile.ThickUm[k]
                        || ((flags[k/2]^tile.Flags[k/2]) & ((k&1)==0 ? ColonyTile.FDorm0 : ColonyTile.FDorm1))!=0;
                    if(changed && cover[k]==tile.CoverQ[k])
                        return tile.Version>version ? null : "appearance changed without coverage or tile revision";
                }
                break; // one simulation advance per outer iteration
            }
        }
        return "fixture produced no appearance-only quantized change";
    }
}

/// Samples actual substrate triangles at matching LOD; it does not assert that independently selected
/// renderer levels match or prove that every microstructure triangle interior clears the surface.
public sealed class ColonyMicroRenderedContactTest : IReprSelfTest
{
    public string Name => "colony.micro.rendered_contact";
    public string? Run()
    {
        var sys=ColonyMeshTestFixture.BuildColonizedCube();
        for(int d=0;d<2;d++)
        {
            var surface=SubstrateMesher.Build(sys.Substrate,sys.Substrate.Bounds,d==0?0.01:0.03,false);
            var bins=new System.Collections.Generic.Dictionary<(int,int),System.Collections.Generic.List<(Vec3 A,Vec3 B,Vec3 C)>>();
            void Add(Vivarium.Sim.Geometry.MeshData mesh)
            {
                for(int i=0;i<mesh.Indices.Count;i+=3)
                {
                    var a=mesh.Position(mesh.Indices[i]); var b=mesh.Position(mesh.Indices[i+1]); var c=mesh.Position(mesh.Indices[i+2]);
                    int x0=(int)Math.Floor(Math.Min(a.X,Math.Min(b.X,c.X))*100),x1=(int)Math.Floor(Math.Max(a.X,Math.Max(b.X,c.X))*100);
                    int z0=(int)Math.Floor(Math.Min(a.Z,Math.Min(b.Z,c.Z))*100),z1=(int)Math.Floor(Math.Max(a.Z,Math.Max(b.Z,c.Z))*100);
                    for(int x=x0;x<=x1;x++) for(int z=z0;z<=z1;z++)
                    { if(!bins.TryGetValue((x,z),out var list)) bins[(x,z)]=list=new(); list.Add((a,b,c)); }
                }
            }
            Add(surface.Ground); Add(surface.Rock);
            bool Height(Vec3 p,out double y)
            {
                y=double.NegativeInfinity;
                if(!bins.TryGetValue(((int)Math.Floor(p.X*100),(int)Math.Floor(p.Z*100)),out var list)) return false;
                foreach(var (a,b,c) in list)
                {
                    double den=(b.Z-c.Z)*(a.X-c.X)+(c.X-b.X)*(a.Z-c.Z); if(Math.Abs(den)<1e-12) continue;
                    double u=((b.Z-c.Z)*(p.X-c.X)+(c.X-b.X)*(p.Z-c.Z))/den;
                    double v=((c.Z-a.Z)*(p.X-c.X)+(a.X-c.X)*(p.Z-c.Z))/den, w=1-u-v;
                    if(u>=-1e-6 && v>=-1e-6 && w>=-1e-6) y=Math.Max(y,u*a.Y+v*b.Y+w*c.Y);
                }
                return double.IsFinite(y);
            }
            int count=0,buried=0; double min=double.PositiveInfinity,max=double.NegativeInfinity;
            foreach(var tile in sys.Field.Tiles)
            {
                var mesh=ColonyMesher.BuildTileMicrostructure(sys,tile.Tx,tile.Tz,d).Mesh;
                for(int v=0;v<mesh.VertexCount;v++)
                {
                    var p=mesh.Position(v); if(!Height(p,out double h)) continue;
                    double gap=p.Y-h; count++; min=Math.Min(min,gap); max=Math.Max(max,gap);
                    if(gap < -0.004 || gap > 0.035) buried++;
                }
            }
            if(count==0) return $"detail {d} no overlapping samples";
            if(buried>0) return $"detail {d}: {buried}/{count} micro vertices outside [-4,+35] mm (min {min:F4}, max {max:F4} m)";
        }
        return null;
    }
}

public sealed class ColonyMicroCostTest : IReprSelfTest
{
    private string _name="colony.micro.cost";
    public string Name => _name;
    public string? Run()
    {
        var sys=ColonyMeshTestFixture.BuildColonizedCube();
        int Build()
        {
            int tris=0;
            foreach(var tile in sys.Field.Tiles) for(int d=0;d<3;d++)
                tris+=ColonyMesher.BuildTileMicrostructure(sys,tile.Tx,tile.Tz,d).Triangles;
            return tris;
        }
        _=Build(); var sw=Stopwatch.StartNew(); int count=0;
        for(int i=0;i<5;i++) count=Build();
        double ms=sw.Elapsed.TotalMilliseconds/5;
        _name=$"colony.micro.cost ms_per_all_levels={ms:0.0} triangles={count}";
        return ms<=80.0 ? null : $"microstructure build too slow: {ms:0.0} ms for {count} triangles across 3 levels";
    }
}

public sealed class ColonyThicknessInterpolationTest : IReprSelfTest
{
    public string Name => "colony.visual_field.edge_thickness";
    public string? Run()
    {
        var sys=ColonyMeshTestFixture.BuildColonizedCube();
        foreach(var tile in sys.Field.Tiles) for(int cell=0;cell<ColonyTile.N;cell++) for(int slot=0;slot<2;slot++)
        {
            int k=2*cell+slot, sid=tile.Species[k];
            if(sid==0 || tile.Cover(k)<0.1 || tile.Thick(k)<0.0001) continue;
            ColonyField.CellCoords(tile,cell,out int ix,out int iz);
            var neighbor=sys.Field.FindCell(ix+1,iz,out int nc);
            bool same=neighbor!=null && (neighbor.Species[nc*2]==sid || neighbor.Species[nc*2+1]==sid);
            if(same) continue;
            var center=sys.Field.CellCentre(ix,iz);
            var middle=new Vec2(center.X+0.5*sys.Field.CellSize,center.Z);
            string id=sys.Species[sid-1].Id;
            double Find(Vec2 p)
            {
                foreach(var o in sys.Sample(p).Occupants) if(o.SpeciesId==id) return o.Thickness;
                return -1;
            }
            double a=Find(center), b=Find(middle);
            if(a<0 || b<0) return "fixture species missing at center or rim";
            if(Math.Abs(a-b)>1e-7) return $"thickness changed at half coverage: {a:F6} -> {b:F6} m";
            return null;
        }
        return "fixture has no occupied-to-empty species edge";
    }
}
