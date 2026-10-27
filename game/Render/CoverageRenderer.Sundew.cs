using System;
using System.Collections.Generic;
using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.Coverage;
using Vivarium.Sim.Geometry;

namespace Vivarium.Game.Render;

public partial class CoverageRenderer
{
    private ShaderMaterial[]? _sundewTissues;
    private ShaderMaterial[]? _coverageTissues;

    private void InitCoverageTissues()
    {
        if(_coverageTissues!=null) return;
        _coverageTissues=new ShaderMaterial[28];
        for(int form=0;form<7;form++) for(int role=0;role<2;role++)
        {
            SpeciesRenderInfo? species=null;
            foreach(var candidate in _matSpecies.Values)
                if(candidate.FloraType!=CoverageFloraType.Sundew && (int)PlantForm(candidate)==form) { species=candidate; break; }
            if(species==null) foreach(var candidate in _crustSpecies.Values)
                if((int)PlantForm(candidate)==form) { species=candidate; break; }
            species??=_defaultSpecies;
            var material=Bridge.Shader("res://Shaders/coverage_tissue.gdshader");
            material.SetShaderParameter("primary_tint",species.Color1);
            material.SetShaderParameter("secondary_tint",species.Color2);
            material.SetShaderParameter("accent_tint",form==6 ? new Color(.72f,.28f,.07f) : species.Color2.Darkened(.22f));
            material.SetShaderParameter("form",(float)form);
            material.SetShaderParameter("organ",(float)role);
            var moss=Bridge.ResolveSurfaceSet(Bridge.Surfaces.Moss);
            material.SetShaderParameter("moss_col",Bridge.LoadTex(moss.Color));
            material.SetShaderParameter("moss_nrm",Bridge.LoadTex(moss.Normal));
            material.SetShaderParameter("moss_rgh",Bridge.LoadTex(moss.Roughness));
            material.SetShaderParameter("lichen_col",Bridge.LoadTex("res://Textures/LichenThallus.png"));
            _coverageTissues[form*2+role]=material;
            var structure=Bridge.Shader(form==5 ? "res://Shaders/coverage_structure_ruffle.gdshader" : "res://Shaders/coverage_structure.gdshader");
            structure.SetShaderParameter("primary_tint",species.Color1);
            structure.SetShaderParameter("secondary_tint",species.Color2);
            structure.SetShaderParameter("form",(float)form);
            structure.SetShaderParameter("organ",(float)role);
            _coverageTissues[14+form*2+role]=structure;
        }
    }

    private bool SundewClumpAnchor(CoverageLayer layer, int gx, int gz, uint hash)
    {
        // Sparse deterministic local minima: a lone occupied cell remains visible,
        // while established colonies form distinct clumps rather than one per 2cm cell.
        for(int z=-1;z<=1;z++) for(int x=-1;x<=1;x++)
        {
            if(x==0 && z==0) continue;
            byte occupant=layer.GetOcc(gx+x,gz+z);
            if(occupant!=0 && GetSpecies(layer.Id,occupant).FloraType==CoverageFloraType.Sundew
                && ReliefHash(gx+x,gz+z)<hash) return false;
        }
        return true;
    }

    private ArrayMesh UploadCoverageMesh(CoverageLayerId layer, MeshData source, Material ordinary, ArrayMesh? reuse=null)
    {
        bool hasSundew=false, hasCoverage=false;
        for(int i=0;i<source.VertexCount;i++) if(source.UV2[i*2+1]<-19.5f) { hasCoverage=true; break; }
        if(layer==CoverageLayerId.Mat)
            for(int i=0;i<source.VertexCount;i++) if(source.UV2[i*2+1]<-7.5f && source.UV2[i*2+1]>-8.5f) { hasSundew=true; break; }
        if(!hasSundew && !hasCoverage) return Bridge.ToArrayMesh(source,ordinary,reuse);
        if(hasCoverage) InitCoverageTissues();
        if(_sundewTissues==null)
        {
            _sundewTissues=new ShaderMaterial[3];
            for(int i=0;i<3;i++)
            {
                _sundewTissues[i]=Bridge.Shader("res://Shaders/sundew.gdshader");
                _sundewTissues[i].SetShaderParameter("tissue",i);
            }
        }
        var pieces=new MeshData[32];
        var maps=new Dictionary<int,int>[32];
        for(int i=0;i<pieces.Length;i++) { pieces[i]=new MeshData(); maps[i]=new Dictionary<int,int>(); }
        for(int t=0;t<source.Indices.Count;t+=3)
        {
            int first=source.Indices[t];
            float marker=source.UV2[first*2+1];
            int part=marker<-39.5f ? 18+Math.Clamp((int)Math.Round(-40-marker),0,6)*2+Math.Clamp((int)Math.Round(source.UV2[first*2]),0,1)
                : marker<-19.5f ? 4+Math.Clamp((int)Math.Round(-20-marker),0,6)*2+Math.Clamp((int)Math.Round(source.UV2[first*2]),0,1)
                : marker<-7.5f ? 1+Math.Clamp((int)Math.Round(source.UV2[first*2]),0,2) : 0;
            int Copy(int old)
            {
                if(maps[part].TryGetValue(old,out int found)) return found;
                int c=old*4,u=old*2;
                int v=pieces[part].AddVertex(source.Position(old),source.NormalAt(old),source.Colors[c],source.Colors[c+1],source.Colors[c+2],source.Colors[c+3],source.UV[u],source.UV[u+1],source.UV2[u],source.UV2[u+1]);
                maps[part][old]=v; return v;
            }
            pieces[part].AddTriangle(Copy(first),Copy(source.Indices[t+1]),Copy(source.Indices[t+2]));
        }
        var result=reuse??new ArrayMesh(); result.ClearSurfaces();
        for(int i=0;i<pieces.Length;i++)
        {
            if(pieces[i].TriangleCount==0) continue;
            var material=i==0 || DebugMode ? ordinary : i>=4 ? _coverageTissues![i-4] : _sundewTissues![i-1];
            using var uploaded=Bridge.ToArrayMesh(pieces[i],material);
            using var arrays=uploaded.SurfaceGetArrays(0);
            result.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays);
            result.SurfaceSetMaterial(result.GetSurfaceCount()-1,material);
        }
        return result;
    }
}
