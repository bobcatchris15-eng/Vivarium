using System;
using System.Collections.Concurrent;
using System.Linq;
using Godot;
using Vivarium.Sim.Core;
using Vivarium.Sim.Coverage;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.World;

namespace Vivarium.Game.Render;

public partial class CoverageRenderer
{
    private static readonly ConcurrentDictionary<(ulong, double, double, int), Lazy<TriangleSurfaceSampler>> LogSupports = new();

    private static CoveragePlantForm PlantForm(SpeciesRenderInfo species) => species.FloraType switch
    {
        CoverageFloraType.Sphagnum => CoveragePlantForm.Bogglass,
        CoverageFloraType.PearlCushion => CoveragePlantForm.PearlCushion,
        CoverageFloraType.Floodlace => CoveragePlantForm.Floodlace,
        CoverageFloraType.Antlerlace => CoveragePlantForm.Antlerlace,
        CoverageFloraType.RuffleLichen => CoveragePlantForm.Ruffle,
        CoverageFloraType.Embercrust => CoveragePlantForm.Embercrust,
        _ => CoveragePlantForm.Velvetweave,
    };

    // This is a render-only support query. Ecological LogProp.TopAt is unchanged.
    private static double RenderLogTop(LogProp log, Vec2 p)
    {
        if (Vec2.Distance(p, log.Position) > log.FootprintRadius + .1) return double.NaN;
        var sampler = LogSupports.GetOrAdd((log.VariantSeed, log.Length, log.Radius, log.DecayClass),
            key => new Lazy<TriangleSurfaceSampler>(() => new TriangleSurfaceSampler(
                PropMeshes.Log(key.Item1, key.Item2, key.Item3, key.Item4)))).Value;
        double c = Math.Cos(log.RotationY), s = Math.Sin(log.RotationY);
        double x = p.X - log.X, z = p.Z - log.Z;
        double height = sampler.HeightAt(x*c + z*s, -x*s + z*c);
        return double.IsFinite(height) ? height + log.Y : double.NaN;
    }

    private bool PlantAnchor(CoverageLayer layer, int x, int z, byte occupant, uint hash)
    {
        for (int dz=-1; dz<=1; dz++) for (int dx=-1; dx<=1; dx++)
            if ((dx!=0 || dz!=0) && layer.GetOcc(x+dx,z+dz)==occupant
                && layer.GetB(x+dx,z+dz)>.001 && ReliefHash(x+dx,z+dz)<hash) return false;
        return true;
    }

    private MeshData BuildTerrestrialTile(CoverageLayer layer, CoverageTile tile)
    {
        var mesh = new MeshData();
        const double cs = CoverageSpec.CellSize;
        const int edge = CoverageSpec.TileEdge;
        double x0=tile.Ti*edge*cs, z0=tile.Tj*edge*cs;
        var rocks = _w.Props.Rocks.Where(p => p.X+p.FootprintRadius>=x0-.08 && p.X-p.FootprintRadius<=x0+edge*cs+.08
            && p.Z+p.FootprintRadius>=z0-.08 && p.Z-p.FootprintRadius<=z0+edge*cs+.08).ToArray();
        var logs = _w.Props.Logs.Where(p => p.X+p.FootprintRadius>=x0-.08 && p.X-p.FootprintRadius<=x0+edge*cs+.08
            && p.Z+p.FootprintRadius>=z0-.08 && p.Z-p.FootprintRadius<=z0+edge*cs+.08).ToArray();
        double Support(Vec2 p)
        {
            double y=_w.Terrain.Height(p);
            foreach(var rock in rocks) { double top=rock.TopAt(p); if(double.IsFinite(top)) y=Math.Max(y,top); }
            foreach(var log in logs) { double top=RenderLogTop(log,p); if(double.IsFinite(top)) y=Math.Max(y,top); }
            return y;
        }
        Vec3 Normal(Vec2 p)
        {
            const double e=.003;
            return new Vec3(Support(p-new Vec2(e,0))-Support(p+new Vec2(e,0)),2*e,
                Support(p-new Vec2(0,e))-Support(p+new Vec2(0,e))).Normalized();
        }
        for(int z=0;z<edge;z++) for(int x=0;x<edge;x++)
        {
            int index=z*edge+x, gx=tile.Ti*edge+x, gz=tile.Tj*edge+z;
            byte occupant=tile.Occ[index];
            float biomass=Math.Clamp(tile.B[index],0,1);
            bool fringe=occupant==0 || biomass<=.001;
            if(fringe)
            {
                // A visual-only halo lets the contour cross cell boundaries naturally.
                for(int dz=-1;dz<=1;dz++) for(int dx=-1;dx<=1;dx++)
                {
                    byte candidate=layer.GetOcc(gx+dx,gz+dz);
                    float b=layer.GetB(gx+dx,gz+dz);
                    if(candidate!=0 && b>biomass && GetSpecies(layer.Id,candidate).FloraType!=CoverageFloraType.Sundew)
                    { occupant=candidate; biomass=b; }
                }
            }
            if(occupant==0 || biomass<=.001 || !float.IsFinite(biomass)) continue;
            var species=GetSpecies(layer.Id,occupant);
            var form=PlantForm(species);
            uint hash=ReliefHash(gx,gz);
            var centre=new Vec2((gx+.5)*cs,(gz+.5)*cs);
            bool dead=(tile.Flags[index]&(byte)CoverageFlags.Dead)!=0;
            double health=.35+.65*Math.Sqrt(biomass);
            double life=dead ? 0 : 1-tile.Dorm[index]/255.0*.85;
            double lift=layer.Id==CoverageLayerId.Mat ? .0008+Math.Min(.005,species.MaxHeightM*.14)*Math.Sqrt(biomass) : .0008;
            double marker=-20-(int)form;
            double Density(Vec2 p)
            {
                double wx=p.X+(ValueNoise(p.X+1.3,p.Z,.027)-.5)*.012;
                double wz=p.Z+(ValueNoise(p.X,p.Z+3.7,.027)-.5)*.012;
                double fx=wx/cs-.5, fz=wz/cs-.5;
                int ix=(int)Math.Floor(fx), iz=(int)Math.Floor(fz);
                double u=fx-ix,v=fz-iz; u=u*u*(3-2*u);v=v*v*(3-2*v);
                double At(int xx,int zz) => layer.GetOcc(xx,zz)==occupant && layer.GetB(xx,zz)>.001 ? 1 : 0;
                return (At(ix,iz)*(1-u)+At(ix+1,iz)*u)*(1-v)+(At(ix,iz+1)*(1-u)+At(ix+1,iz+1)*u)*v;
            }
            double SmoothState(Vec2 p,bool livingState)
            {
                double fx=p.X/cs-.5,fz=p.Z/cs-.5;
                int ix=(int)Math.Floor(fx),iz=(int)Math.Floor(fz);
                double u=fx-ix,v=fz-iz;
                double sum=0,weight=0;
                for(int dz=0;dz<2;dz++) for(int dx=0;dx<2;dx++)
                {
                    int xx=ix+dx,zz=iz+dz;
                    if(layer.GetOcc(xx,zz)!=occupant) continue;
                    double w=(dx==0?1-u:u)*(dz==0?1-v:v);
                    var (ti,tj)=CoverageSpec.TileOf(xx,zz);
                    double value=Math.Clamp(layer.GetB(xx,zz),0,1);
                    if(livingState && layer.TryGetTile(ti,tj,out var state) && state!=null)
                    {
                        int cell=CoverageSpec.LocalIndex(xx,zz);
                        value=(state.Flags[cell]&(byte)CoverageFlags.Dead)!=0 ? 0 : 1-state.Dorm[cell]/255.0*.85;
                    }
                    sum+=w*value;weight+=w;
                }
                return weight>1e-8 ? sum/weight : (livingState?life:biomass);
            }
            double Relief(Vec2 p) => layer.Id==CoverageLayerId.Mat
                ? .0025*SurfaceRelief(CoverageLayerId.Mat,p.X,p.Z,1f,1.0)*Density(p)
                : .00045*ValueNoise(p.X,p.Z,.035)*Density(p);
            double Height(Vec2 p) => Support(p)+.0008+(layer.Id==CoverageLayerId.Mat
                ? Math.Min(.005,species.MaxHeightM*.14)*Math.Sqrt(SmoothState(p,false)) : 0)+Relief(p);
            Vec3 Point(Vec2 p) => new Vec3(p.X,Height(p),p.Z);
            int Vertex(Vec2 p)
            {
                const double e=.001;
                var n=new Vec3(Height(p-new Vec2(e,0))-Height(p+new Vec2(e,0)),2*e,
                    Height(p-new Vec2(0,e))-Height(p+new Vec2(0,e))).Normalized();
                return mesh.AddVertex(Point(p),n,1,1,1,1,.35+.65*Math.Sqrt(SmoothState(p,false)),
                    dead?0:SmoothState(p,true),-1-Density(p),marker);
            }
            void Triangle(Vec2 a,Vec2 b,Vec2 c,int depth=0)
            {
                var ab=(a+b)*.5; var bc=(b+c)*.5; var ca=(c+a)*.5;
                double ha=Support(a),hb=Support(b),hc=Support(c);
                double error=Math.Max(Math.Abs(Support(ab)-(ha+hb)*.5),
                    Math.Max(Math.Abs(Support(bc)-(hb+hc)*.5),Math.Abs(Support(ca)-(hc+ha)*.5)));
                if(error>.001 && depth<2)
                {
                    Triangle(a,ab,ca,depth+1); Triangle(ab,b,bc,depth+1);
                    Triangle(ca,bc,c,depth+1); Triangle(ab,bc,ca,depth+1); return;
                }
                if(error>.003 || Math.Max(ha,Math.Max(hb,hc))-Math.Min(ha,Math.Min(hb,hc))>.035) return;
                int ia=Vertex(a),ib=Vertex(b),ic=Vertex(c);
                var cross=(mesh.Position(ib)-mesh.Position(ia)).Cross(mesh.Position(ic)-mesh.Position(ia));
                if(cross.LengthSq<1e-18) return;
                if(cross.Y>0) mesh.AddTriangle(ia,ic,ib); else mesh.AddTriangle(ia,ib,ic);
            }
            if(species.FloraType!=CoverageFloraType.Sundew)
            {
                // Half-cell geometry carries a smooth field; the shader feathers the
                // final irregular millimetre fringe, retaining opaque surviving fragments.
                const int subdivisions=2;
                for(int iz=0;iz<subdivisions;iz++) for(int ix=0;ix<subdivisions;ix++)
                {
                    var a=new Vec2((gx+ix/(double)subdivisions)*cs,(gz+iz/(double)subdivisions)*cs);
                    var b=a+new Vec2(cs/subdivisions,0); var c=a+new Vec2(cs/subdivisions,cs/subdivisions);
                    var d=a+new Vec2(0,cs/subdivisions);
                    if(Math.Max(Math.Max(Density(a),Density(b)),Math.Max(Density(c),Density(d)))<.08) continue;
                    Triangle(a,b,c);Triangle(a,c,d);
                }
            }
            if(dead || fringe) continue;
            bool anchor=species.FloraType==CoverageFloraType.Sundew ? SundewClumpAnchor(layer,gx,gz,hash)
                : PlantAnchor(layer,gx,gz,occupant,hash) || (hash>>24)<(form switch
                {
                    CoveragePlantForm.Bogglass=>70u,CoveragePlantForm.PearlCushion=>110u,
                    CoveragePlantForm.Floodlace=>95u,CoveragePlantForm.Velvetweave=>100u,
                    CoveragePlantForm.Antlerlace=>85u,CoveragePlantForm.Ruffle=>90u,_=>120u,
                });
            if(!anchor) continue;
            var normal=Normal(centre);
            if(normal.Y<.42) continue;
            var offset=new Vec2(((hash&255)/255.0-.5)*cs*.55,(((hash>>8)&255)/255.0-.5)*cs*.55);
            var foot=Point(centre+offset)-normal*.001;
            int first=mesh.VertexCount;
            if(species.FloraType==CoverageFloraType.Sundew)
                BlueSundewGeometry.AppendClump(mesh,foot,normal,hash,Math.Clamp(biomass*3,.72,1));
            else
            {
                var color1=species.Color1; var color2=species.Color2;
                float vigor=(float)Math.Clamp(.72+.28*Math.Sqrt(biomass),.5,1.5);
                switch(species.FloraType)
                {
                    case CoverageFloraType.Sphagnum: AppendSphagnumShoot(mesh,foot,color1,color2,hash,vigor); break;
                    case CoverageFloraType.PearlCushion: AppendPearlCushionStructures(mesh,foot,normal,color1,color2,hash,vigor); break;
                    case CoverageFloraType.Floodlace: AppendFloodlaceFronds(mesh,foot,normal,color1,color2,hash,vigor); break;
                    case CoverageFloraType.Antlerlace: AppendAntlerlacePodetia(mesh,foot,normal,color1,color2,hash,vigor); break;
                    case CoverageFloraType.RuffleLichen: AppendRuffleLobes(mesh,foot,normal,color1,color2,hash,vigor); break;
                    case CoverageFloraType.Embercrust: AppendEmbercrustApothecia(mesh,foot,normal,color1,color2,hash,vigor); break;
                    default: AppendVelvetweaveTurf(mesh,foot,normal,color1,color2,hash,vigor); break;
                }
                float structureMarker=(float)(-40-(int)form);
                for(int v=first;v<mesh.VertexCount;v++)
                {
                    int u=v*2,c=v*4;
                    float growth=(float)Math.Clamp(mesh.UV2[u+1],0,1);
                    mesh.Colors[c+3]=growth;
                    mesh.UV[u]=(float)health; mesh.UV[u+1]=(float)life;
                    mesh.UV2[u]=0; mesh.UV2[u+1]=structureMarker;
                }
            }
        }
        return mesh;
    }
}
