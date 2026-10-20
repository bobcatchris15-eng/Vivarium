using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
// SPDX-License-Identifier: MIT
using System.Text.Json;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;
namespace Vivarium.VisualLab;
/// <summary>Strict render-only importer for the authored branch-module v1 payload. No Godot dependency.</summary>
public static class BranchModuleLoader
{
    public static MeshData Load(string path)
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(path)); return Load(doc.RootElement);
    }
    static void Require(bool condition,string message) { if(!condition) throw new InvalidDataException(message); }
    static float[] Buffer(JsonElement root,string name,int width,int count=-1)
    {
        var b=root.GetProperty(name).EnumerateArray().Select(x=>x.GetSingle()).ToArray();
        Require(b.Length%width==0 && (count<0 || b.Length==count*width),$"{name} shape");
        Require(b.All(float.IsFinite),$"{name} nonfinite"); return b;
    }
    static Vec3 V(JsonElement x) { var a=x.EnumerateArray().Select(v=>v.GetDouble()).ToArray(); Require(a.Length==3 && a.All(double.IsFinite),"vector shape/finite"); return new(a[0],a[1],a[2]); }
    public static MeshData Load(JsonElement root)
    {
        Require(root.GetProperty("schema").GetString()=="vivarium.branch-module.v1","schema");
        var p=Buffer(root,"positions",3); int nv=p.Length/3; Require(nv>0,"empty mesh");
        var n=Buffer(root,"normals",3,nv); var uv=Buffer(root,"uv",2,nv); var uv2=Buffer(root,"uv2",2,nv); var c=Buffer(root,"colors",4,nv);
        var ix=root.GetProperty("indices").EnumerateArray().Select(x=>x.GetInt32()).ToArray();
        Require(ix.Length>0 && ix.Length%3==0 && ix.All(x=>x>=0 && x<nv),"index shape/range");
        Require(ix.Length/3==root.GetProperty("triangles").GetInt32() && ix.Length/3<=10000,"triangle count/budget");
        Require(c.All(x=>x>=0 && x<=1),"color range");
        var mesh=new MeshData { FloraDetailLevel=0, FloraVisualSeed=root.GetProperty("seed").GetUInt64() };
        for(int v=0;v<nv;v++)
        {
            var normal=new Vec3(n[v*3],n[v*3+1],n[v*3+2]); Require(Math.Abs(normal.LengthSq-1)<.003,"unit normal");
            mesh.AddVertex(new(p[v*3],p[v*3+1],p[v*3+2]),normal,c[v*4],c[v*4+1],c[v*4+2],c[v*4+3],uv[v*2],uv[v*2+1],uv2[v*2],uv2[v*2+1]);
        }
        for(int i=0;i<ix.Length;i+=3) { Require((mesh.Position(ix[i+1])-mesh.Position(ix[i])).Cross(mesh.Position(ix[i+2])-mesh.Position(ix[i])).LengthSq>1e-24,"degenerate triangle"); mesh.AddTriangle(ix[i],ix[i+1],ix[i+2]); }
        var parts=root.GetProperty("parts").EnumerateArray().ToArray();
        var names=parts.Select(x=>x.GetProperty("name").GetString()!).ToArray(); Require(names.Distinct().Count()==parts.Length,"duplicate part name");
        var vertexOwner=Enumerable.Repeat(-1,nv).ToArray(); var indexOwner=Enumerable.Repeat(-1,ix.Length).ToArray();
        var ids=names.Select((s,i)=>(s,id:Rng.Mix(mesh.FloraVisualSeed,(ulong)i+1))).ToDictionary(x=>x.s,x=>x.id);
        for(int k=0;k<parts.Length;k++)
        {
            var part=parts[k]; int fv=part.GetProperty("first_vertex").GetInt32(),vc=part.GetProperty("vertex_count").GetInt32(),fi=part.GetProperty("first_index").GetInt32(),ic=part.GetProperty("index_count").GetInt32();
            Require(fv>=0 && vc>0 && fv<=nv-vc && fi>=0 && ic>0 && ic%3==0 && fi%3==0 && fi<=ix.Length-ic,"part span range");
            for(int v=fv;v<fv+vc;v++){Require(vertexOwner[v]<0,"overlapping vertices");vertexOwner[v]=k;}
            for(int i=fi;i<fi+ic;i++){Require(indexOwner[i]<0 && ix[i]>=fv && ix[i]<fv+vc,"overlap or cross-part index");indexOwner[i]=k;}
            var role=part.GetProperty("role").GetString(); Require(role is "foliage" or "wood" or "stem","unsupported role");
            var anchor=V(part.GetProperty("attachment")); double len=part.GetProperty("length").GetDouble(); Require(double.IsFinite(len)&&len>0,"length");
            string parentName=part.GetProperty("parent").GetString()!; Require(parentName=="module_root" || ids.ContainsKey(parentName),"missing parent"); ulong parent=parentName=="module_root"?0:ids[parentName];
            var min=new Vec3(double.PositiveInfinity,double.PositiveInfinity,double.PositiveInfinity); var max=new Vec3(double.NegativeInfinity,double.NegativeInfinity,double.NegativeInfinity);
            for(int v=fv;v<fv+vc;v++){var q=mesh.Position(v); min=new(Math.Min(min.X,q.X),Math.Min(min.Y,q.Y),Math.Min(min.Z,q.Z));max=new(Math.Max(max.X,q.X),Math.Max(max.Y,q.Y),Math.Max(max.Z,q.Z));}
            Vec3 tangent=part.TryGetProperty("tip",out var tip)?V(tip)-anchor:mesh.Position(fv+vc/2-1)-anchor;
            if(tangent.LengthSq<1e-12)tangent=Vec3.Up; tangent=tangent.Normalized();
            Vec3 normal=role=="foliage"?mesh.NormalAt(fv+vc/2):new Vec3(0,0,1);
            normal=normal-tangent*(normal.X*tangent.X+normal.Y*tangent.Y+normal.Z*tangent.Z);
            if(normal.LengthSq<1e-12)normal=tangent.Cross(new Vec3(1,0,0)); if(normal.LengthSq<1e-12)normal=tangent.Cross(Vec3.Up); normal=normal.Normalized();
            double radius=part.TryGetProperty("radius",out var rad)?rad.GetDouble():part.GetProperty("thickness").GetDouble()/2;
            Require(double.IsFinite(radius)&&radius>0,"radius"); double flex=0;
            if(role=="foliage")
            {
                int variant=part.GetProperty("material_variant").GetInt32(); double age=part.GetProperty("age").GetDouble();flex=part.GetProperty("flexibility").GetDouble();
                Require(variant>=0&&variant<4&&double.IsFinite(age)&&age>=0&&age<=1&&double.IsFinite(flex)&&flex>=0&&flex<=1,"leaf material payload");
                Require(!part.GetProperty("volumetric").GetBoolean(),"v1 thin-leaf semantics");
                for(int v=fv;v<fv+vc;v++)Require(uv[v*2]>=0&&uv[v*2]<=1&&uv[v*2+1]>=0&&uv[v*2+1]<=1&&uv2[v*2]==0&&uv2[v*2+1]==1,"leaf UV contract");
                var parentPart=parts[Array.IndexOf(names,parentName)]; Require((V(parentPart.GetProperty("tip"))-anchor).LengthSq<1e-14,"petiole attachment gap");
                mesh.LeafSpans.Add((fv,vc));mesh.Leaves.Add(new(fv,vc,fi,ic,part.GetProperty("identity").GetUInt64(),anchor,len,variant,age,flex,false));
            }
            else
            {
                int sides=part.GetProperty("sides").GetInt32(); Require(sides>=3&&vc%sides==0,"structural tube shape");mesh.Structural.Add((fv,vc,sides,radius));
                for(int v=fv;v<fv+vc;v++)Require(uv2[v*2]==(role=="wood"?2:0)&&uv2[v*2+1]==0,"structural UV contract");
            }
            FloraVisualCompiler.RecordPart(mesh,new(ids[names[k]],parent,role=="foliage"?FloraTissueSlot.Foliage:role=="wood"?FloraTissueSlot.Wood:FloraTissueSlot.Stem,FloraAttachmentFrame.Create(anchor,normal,tangent),len,radius,flex,role=="foliage"?part.GetProperty("material_variant").GetInt32():0,(min,max)));
        }
        Require(vertexOwner.All(x=>x>=0)&&indexOwner.All(x=>x>=0),"unowned mesh data"); return mesh;
    }
}

