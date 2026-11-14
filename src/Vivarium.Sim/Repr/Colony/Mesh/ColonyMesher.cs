// Attached colony microstructure only. Coverage is shaded on the substrate's own geometry.
// The rejected standalone sheet experiment is preserved under experiments/representation_rebuild/rejected/C2-sheets.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;

namespace Vivarium.Sim.Repr;

public sealed class ColonyMeshSet
{
    public MeshData Mesh = new();
    public int Triangles => Mesh.TriangleCount;
    public int Detail;
    public int Tx, Tz;
    public ulong TileVersion;
}

public readonly record struct ColonyOpticalStats(
    int Triangles,
    double BaseCoveredAreaM2,
    double MicroAreaM2,
    double MeanReflectance,
    Vec3 MeanNormal);

public static class ColonyMesher
{
    public const int LevelCount = 3;

    // Reuse immutable surface samples across levels and rebuilds. A weak key bounds lifetime to the
    // simulation; a hard cap prevents unbounded growth when colonies explore new positions.
    private sealed class SurfaceSamples
    {
        public ulong Version;
        public readonly Dictionary<(double, double), (double Y, Vec3 Normal)> Normals = new();
        public readonly Dictionary<(double, double), double> Heights = new();
    }
    private static readonly ConditionalWeakTable<ColonySystem, SurfaceSamples> Samples = new();
    private const int MaxSurfaceSamples = 100000;

    /// Builds only attached 3D features; coverage is shaded on the substrate itself.
    public static ColonyMeshSet BuildTileMicrostructure(ColonySystem sys, int tx, int tz, int detail)
    {
        if (sys == null) throw new ArgumentNullException(nameof(sys));
        if (detail < 0 || detail >= LevelCount) throw new ArgumentOutOfRangeException(nameof(detail));
        var tile=sys.Field.FindTile(tx,tz);
        var result=new ColonyMeshSet { Detail=detail, Tx=tx, Tz=tz, TileVersion=tile?.Version ?? 0 };
        if(tile == null || tile.Occupied==0 || detail==2) return result;
        lock(Samples.GetOrCreateValue(sys)) new Builder(sys,detail).BuildMicroTile(tile,result.Mesh);
        return result;
    }

    private sealed class Builder
    {
        private readonly ColonySystem _sys;
        private readonly LayeredSubstrate _sub;
        private readonly ColonyField _field;
        private readonly IReadOnlyList<ColonySpecies> _species;
        private readonly MeshBuilder _subSurface;
        private readonly int _detail;
        private readonly double _cell;
        private readonly ulong _seed;
        private readonly SurfaceSamples _surfaceCache;
        private double Height(double x, double z)
        {
            if (_surfaceCache.Heights.TryGetValue((x,z), out double y)) return y;
            y = _subSurface.SampleTopHeight(x, z);
            if (_surfaceCache.Heights.Count < MaxSurfaceSamples) _surfaceCache.Heights[(x,z)] = y;
            return y;
        }

        private (double Y, Vec3 Normal) Surface(double x, double z)
        {
            if (_surfaceCache.Normals.TryGetValue((x, z), out var sample)) return sample;
            double y = _subSurface.SampleTopSurface(x, z, out Vec3 normal, out _);
            sample = (y, normal); if (_surfaceCache.Normals.Count < MaxSurfaceSamples) _surfaceCache.Normals[(x, z)] = sample; return sample;
        }

        public Builder(ColonySystem sys, int detail)
        {
            _sys = sys;
            _sub = sys.Substrate;
            _field = sys.Field;
            _species = sys.Species;
            _detail = detail;
            _cell = _field.CellSize;
            _seed = sys.WorldSeed;
            _subSurface = new MeshBuilder(_sub, 0.01);
            _surfaceCache = Samples.GetOrCreateValue(sys);
            if (_surfaceCache.Version != _sub.Version)
            {
                _surfaceCache.Normals.Clear(); _surfaceCache.Heights.Clear();
                _surfaceCache.Version = _sub.Version;
            }
        }

        private static void Tri(MeshData m, int a, int b, int c)
        {
            var cr = (m.Position(b) - m.Position(a)).Cross(m.Position(c) - m.Position(a));
            var n = m.NormalAt(a) + m.NormalAt(b) + m.NormalAt(c);
            if (cr.Dot(n) > 0) (b, c) = (c, b);
            m.AddTriangle(a, b, c);
        }

        private int Emit(
            MeshData m,
            Vec3 pos,
            Vec3 nrm,
            Vec3 col,
            double cov,
            double u,
            double v,
            double kindCode,
            double partCode,
            double moist,
            double thickMm,
            double health,
            double rimFactor,
            double localU,
            double localV,
            double ao,
            double proxyComp)
        {
            // Project microstructure vertices that intersect the actual relief back onto it.
            // Base vertices are already sampled directly and preserve their prescribed cushion lift.
            if (partCode >= 0.5)
            {
                double x = Math.Clamp(pos.X, _sub.Ox, _sub.Ox + _sub.Nx * _sub.Cell);
                double z = Math.Clamp(pos.Z, _sub.Oz, _sub.Oz + _sub.Nz * _sub.Cell);
                double floor = Height(x, z) + 0.0002;
                if (pos.Y < floor) pos = new Vec3(pos.X, floor, pos.Z);
            }
            int i = m.AddVertex(pos, nrm, MathD.Clamp01(col.X), MathD.Clamp01(col.Y), MathD.Clamp01(col.Z), MathD.Clamp01(cov), u, v, kindCode, partCode);
            m.Custom0.Add((float)MathD.Clamp01(moist)); m.Custom0.Add((float)Math.Max(0, thickMm));
            m.Custom0.Add((float)MathD.Clamp01(health)); m.Custom0.Add((float)MathD.Clamp01(rimFactor));
            m.Custom1.Add((float)MathD.Clamp01(localU)); m.Custom1.Add((float)MathD.Clamp01(localV));
            m.Custom1.Add((float)MathD.Clamp01(ao)); m.Custom1.Add((float)MathD.Clamp01(proxyComp));
            return i;
        }

        private static double KindCode(ColonySpecies sp)
        {
            return sp.Kind switch
            {
                ColonyKind.Moss => sp.Id.Contains("feather", StringComparison.OrdinalIgnoreCase) ? 0.2 : 0.0,
                ColonyKind.CrustoseLichen => 1.0,
                ColonyKind.FolioseLichen => 2.0,
                ColonyKind.FruticoseLichen => 3.0,
                ColonyKind.Litter => 4.0,
                _ => 0.0,
            };
        }

        private static Vec3 Multiply(Vec3 a, Vec3 b) => new(a.X*b.X, a.Y*b.Y, a.Z*b.Z);

        private Vec3 SpeciesColor(ColonySpecies sp, double health, bool dormant, double moist, double x, double z, double rimFactor)
        {
            Vec3 c = Vec3.Lerp(sp.ColourDry, sp.ColourWet, moist);
            double n1 = Noise.Gradient(Rng.Mix(_seed, 0x6A09E667F3BCC908UL), x * 28.0, z * 28.0) - 0.5;
            double n2 = Noise.Gradient(Rng.Mix(_seed, 0xBB67AE8584CAA73BUL), x * 85.0, z * 85.0) - 0.5;

            switch (sp.Kind)
            {
                case ColonyKind.Moss:
                {
                    // Rich hydrated chlorophyll in moist cushions; golden-olive dry tips when desiccated/dormant.
                    Vec3 lush = Multiply(c, new Vec3(0.92, 1.12, 0.85));
                    Vec3 dry = new Vec3(0.28, 0.29, 0.13);
                    double hydration = dormant ? 0.15 : MathD.Clamp01(0.35 + 0.65 * moist);
                    c = Vec3.Lerp(dry, lush, hydration * (0.55 + 0.45 * health));
                    c *= 1.0 + 0.16 * n1 + 0.08 * n2;
                    break;
                }
                case ColonyKind.CrustoseLichen:
                {
                    // Pale mineral-hugging areolate crust with darker prothallus rim and subtle concentric zoning.
                    Vec3 prothallus = Multiply(c, new Vec3(0.46, 0.48, 0.44));
                    c = Vec3.Lerp(c * (1.0 + 0.14 * n1 + 0.10 * n2), prothallus, 0.58 * rimFactor * rimFactor);
                    break;
                }
                case ColonyKind.FolioseLichen:
                {
                    // Grey-sage thallus with pale upturned marginal lobes when dry and deeper green-slate when wet.
                    Vec3 wetSlate = Multiply(c, new Vec3(0.76, 0.92, 0.80));
                    Vec3 margin = Multiply(c, new Vec3(1.16, 1.14, 1.08));
                    c = Vec3.Lerp(c, wetSlate, 0.55 * moist);
                    c = Vec3.Lerp(c, margin, 0.35 * rimFactor);
                    c *= 1.0 + 0.12 * n1;
                    break;
                }
                case ColonyKind.FruticoseLichen:
                {
                    // Pale celadon-silver branching podetia.
                    c *= 1.0 + 0.14 * n1 + 0.07 * n2;
                    break;
                }
                case ColonyKind.Litter:
                {
                    c *= (0.85 - 0.25 * moist) * (1.0 + 0.18 * n1);
                    break;
                }
            }
            return new Vec3(MathD.Clamp01(c.X), MathD.Clamp01(c.Y), MathD.Clamp01(c.Z));
        }

        private void ReadCellSlot(int ix, int iz, int targetSid, out double cov, out double thick, out double health, out bool dormant)
        {
            cov = 0; thick = 0; health = 1; dormant = false;
            var t = _field.FindCell(ix, iz, out int cell);
            if (t == null) return;
            for (int s = 0; s < 2; s++)
            {
                int k = cell * 2 + s;
                if (t.Species[k] == targetSid)
                {
                    cov = t.Cover(k);
                    thick = t.Thick(k);
                    health = t.Health(k);
                    dormant = t.Dormant(k);
                    return;
                }
            }
        }

        /// Evaluates interpolated coverage, thickness, health, and outward coverage gradient at a cell corner (ix, iz).
        private void SampleCorner(
            int ix,
            int iz,
            int sid,
            out double cov,
            out double thick,
            out double health,
            out bool dormant,
            out double rimFactor,
            out Vec2 frontDir)
        {
            ReadCellSlot(ix - 1, iz - 1, sid, out double c00, out double t00, out double h00, out bool d00);
            ReadCellSlot(ix,     iz - 1, sid, out double c10, out double t10, out double h10, out bool d10);
            ReadCellSlot(ix - 1, iz,     sid, out double c01, out double t01, out double h01, out bool d01);
            ReadCellSlot(ix,     iz,     sid, out double c11, out double t11, out double h11, out bool d11);

            int active = (c00 > 1e-4 ? 1 : 0) + (c10 > 1e-4 ? 1 : 0) + (c01 > 1e-4 ? 1 : 0) + (c11 > 1e-4 ? 1 : 0);
            cov = 0.25 * (c00 + c10 + c01 + c11);
            double wSum = c00 + c10 + c01 + c11;
            if (wSum > 1e-6)
            {
                thick = (c00 * t00 + c10 * t10 + c01 * t01 + c11 * t11) / wSum;
                health = (c00 * h00 + c10 * h10 + c01 * h01 + c11 * h11) / wSum;
            }
            else
            {
                thick = 0;
                health = 1;
            }
            dormant = (d00 && c00 > 0) || (d10 && c10 > 0) || (d01 && c01 > 0) || (d11 && c11 > 0);

            // Outward front direction points from high coverage toward low coverage.
            double gx = 0.5 * ((c00 + c01) - (c10 + c11));
            double gz = 0.5 * ((c00 + c10) - (c01 + c11));
            double gLen = Math.Sqrt(gx * gx + gz * gz);
            frontDir = gLen > 1e-5 ? new Vec2(gx / gLen, gz / gLen) : new Vec2(1, 0);

            // Rim factor is high when a corner touches empty cells or has a steep coverage gradient.
            double edgeTouch = active < 4 ? (4 - active) / 3.0 : 0.0;
            rimFactor = MathD.Clamp01(Math.Max(edgeTouch, gLen * 2.2));
        }

        public void BuildMicroTile(ColonyTile tile, MeshData mesh)
        {
            int bx=tile.Tx*ColonyTile.Side, bz=tile.Tz*ColonyTile.Side;
            for(int z=0;z<ColonyTile.Side;z++) for(int x=0;x<ColonyTile.Side;x++)
            {
                int cell=ColonyField.CellIndex(x,z), ix=bx+x, iz=bz+z;
                Vec2 ctr=_field.CellCentre(ix,iz); double moist=_sys.MoistureAt(ctr);
                for(int slot=0;slot<2;slot++)
                {
                    int k=cell*2+slot, sid=tile.Species[k];
                    if(sid==0 || tile.CoverQ[k]<=64 || tile.Cover(k)<=0.08) continue;
                    var sp=_species[sid-1];
                    EmitCellMicrostructure(mesh,sp,KindCode(sp),ix,iz,ctr,tile.Cover(k),tile.Thick(k),tile.Health(k),tile.Dormant(k),moist,sid);
                }
            }
        }

        private static void BuildTangentBasis(Vec3 n, out Vec3 t1, out Vec3 t2)
        {
            Vec3 up = Math.Abs(n.Y) < 0.92 ? new Vec3(0, 1, 0) : new Vec3(1, 0, 0);
            t1 = up.Cross(n).Normalized();
            t2 = n.Cross(t1).Normalized();
        }

        private void EmitCellMicrostructure(
            MeshData m,
            ColonySpecies sp,
            double kindCode,
            int ix,
            int iz,
            Vec2 ctr,
            double cov,
            double thick,
            double health,
            bool dormant,
            double moist,
            int sid)
        {
            double cx = Math.Clamp(ctr.X, _sub.Ox, _sub.Ox + _sub.Nx * _sub.Cell);
            double cz = Math.Clamp(ctr.Z, _sub.Oz, _sub.Oz + _sub.Nz * _sub.Cell);
            var sample = Surface(cx, cz);
            double baseSurfY = sample.Y; Vec3 surfN = sample.Normal;
            SampleCorner(ix, iz, sid, out _, out _, out _, out _, out double rimFactor, out Vec2 frontDir);
            // The substrate material now carries coverage; structures start at the surface itself.
            Vec3 cellOrigin = new Vec3(ctr.X, baseSurfY, ctr.Z) + surfN * 0.0005;
            BuildTangentBasis(surfN, out Vec3 t1, out Vec3 t2);

            ulong cellHash = ReprId.Hash(_seed, (ulong)(uint)ix, (ulong)((uint)iz * 73856093u + (uint)sid * 19349663u));

            switch (sp.Kind)
            {
                case ColonyKind.Moss:
                    EmitMossShoots(m, sp, kindCode, cellHash, cellOrigin, surfN, t1, t2, cov, thick, health, dormant, moist, rimFactor);
                    break;
                case ColonyKind.CrustoseLichen:
                    EmitCrustoseApothecia(m, sp, kindCode, cellHash, cellOrigin, surfN, t1, t2, cov, thick, health, dormant, moist, rimFactor);
                    break;
                case ColonyKind.FolioseLichen:
                    EmitFolioseLobes(m, sp, kindCode, cellHash, cellOrigin, surfN, t1, t2, frontDir, cov, thick, health, dormant, moist, rimFactor);
                    break;
                case ColonyKind.FruticoseLichen:
                    EmitFruticosePodetia(m, sp, kindCode, cellHash, cellOrigin, surfN, t1, t2, cov, thick, health, dormant, moist, rimFactor);
                    break;
            }
        }

        private void EmitMossShoots(MeshData m, ColonySpecies sp, double kindCode, ulong cellHash,
            Vec3 cellOrigin, Vec3 surfN, Vec3 t1, Vec3 t2, double cov, double thick,
            double health, bool dormant, double moist, double rimFactor)
        {
            bool feather=kindCode>0.1;
            int full=Math.Max(1,(int)Math.Round((feather?4.0:4.0)*Math.Pow(cov,0.65)));
            int count=_detail==0 ? full : Math.Max(1,(full+2)/3);
            double scale=_detail==0 ? 1.0 : 1.5;
            Vec3 green=SpeciesColor(sp,health,dormant,moist,cellOrigin.X,cellOrigin.Z,rimFactor);
            double mm=thick*1000.0;
            for(int i=0;i<count;i++)
            {
                ulong h=Rng.Mix(cellHash,(ulong)(i+1));
                double ox=ReprId.Range(h,1,-0.42*_cell,0.42*_cell), oz=ReprId.Range(h,2,-0.42*_cell,0.42*_cell);
                double yaw=ReprId.Range(h,3,0,2*Math.PI);
                double height=ReprId.Range(h,4,0.0020,0.0038)*(0.75+0.25*cov);
                Vec3 root=cellOrigin+t1*ox+t2*oz;
                // Central stem inclines slightly; leaves attach to two nodes on that axis.
                Vec3 lean=(t1*Math.Cos(yaw)+t2*Math.Sin(yaw))*height*0.13;
                Vec3 tip=root+surfN*height+lean;
                Vec3 axisSide=(t1*Math.Sin(yaw)-t2*Math.Cos(yaw))*0.00012*scale;
                int stem0=Emit(m,root-axisSide,surfN,green*0.70,cov,root.X,root.Z,kindCode,1,moist,mm,health,rimFactor,0,0,0.75,0);
                int stem1=Emit(m,root+axisSide,surfN,green*0.70,cov,root.X,root.Z,kindCode,1,moist,mm,health,rimFactor,1,0,0.75,0);
                int stem2=Emit(m,tip,surfN,green*0.95,cov,tip.X,tip.Z,kindCode,2,moist,mm,health,rimFactor,0.5,1,0.95,0);
                Tri(m,stem0,stem1,stem2);
                int leaves=_detail==0 ? 4 : 2;
                for(int leaf=0;leaf<leaves;leaf++)
                {
                    double t=0.25+0.65*((leaf/2)/(double)Math.Max(1,(leaves/2)-1));
                    double ang=yaw+leaf*2.3999632297;
                    Vec3 outward=(t1*Math.Cos(ang)+t2*Math.Sin(ang)).Normalized();
                    Vec3 side=surfN.Cross(outward).Normalized();
                    Vec3 node=root+surfN*(height*t)+lean*t;
                    double len=height*(feather?0.62:0.52)*scale*(1.1-0.45*t);
                    double width=len*0.20;
                    Vec3 mid=node+outward*(len*0.50)+surfN*(len*0.16);
                    Vec3 end=node+outward*len+surfN*(len*0.22);
                    Vec3 normal=(surfN+outward*0.25).Normalized();
                    int l0=Emit(m,node,normal,green*0.75,cov,node.X,node.Z,kindCode,1.2,moist,mm,health,rimFactor,0.5,0,0.70,0);
                    int l1=Emit(m,mid-side*width,normal,green,cov,mid.X,mid.Z,kindCode,1.5,moist,mm,health,rimFactor,0,0.5,0.85,0);
                    int l2=Emit(m,mid+side*width,normal,green,cov,mid.X,mid.Z,kindCode,1.5,moist,mm,health,rimFactor,1,0.5,0.85,0);
                    int l3=Emit(m,end,normal,green*1.06,cov,end.X,end.Z,kindCode,2,moist,mm,health,rimFactor,0.5,1,0.95,0);
                    Tri(m,l0,l1,l2); Tri(m,l1,l3,l2);
                }
            }
        }

        private void EmitCrustoseApothecia(
            MeshData m,
            ColonySpecies sp,
            double kindCode,
            ulong cellHash,
            Vec3 cellOrigin,
            Vec3 surfN,
            Vec3 t1,
            Vec3 t2,
            double cov,
            double thick,
            double health,
            bool dormant,
            double moist,
            double rimFactor)
        {
            // Crustose apothecia (rimmed fruiting cups) form in mature interior thallus cells (cov > 0.35, away from the raw prothallus edge).
            if (cov < 0.35 || rimFactor > 0.65) return;
            int count = _detail == 0 ? (cov > 0.72 ? 2 : 1) : 1;
            if (ReprId.Unit(cellHash, 10) > (0.65 * cov)) return;

            double scale = _detail == 0 ? 1.0 : 1.55;
            double proxyComp = _detail == 0 ? 0.0 : 0.45;
            Vec3 rimCol = SpeciesColor(sp, health, dormant, moist, cellOrigin.X, cellOrigin.Z, 0.0) * 1.08;
            // Apothecial hymenium disc is darker amber-ochre / slate-brown inside the pale thalline exciple rim.
            Vec3 discCol = new Vec3(0.24, 0.19, 0.14) * (0.85 + 0.20 * health);
            double thickMm = thick * 1000.0;

            var rIds = new int[5];
            for (int i = 0; i < count; i++)
            {
                ulong h = Rng.Mix(cellHash, (ulong)(100 + i));
                double ox = ReprId.Range(h, 1, -0.32 * _cell, 0.32 * _cell);
                double oz = ReprId.Range(h, 2, -0.32 * _cell, 0.32 * _cell);
                double rad = ReprId.Range(h, 3, 0.0011, 0.0019) * scale;
                double rimH = ReprId.Range(h, 4, 0.00055, 0.00095);

                Vec3 ctr = cellOrigin + t1 * ox + t2 * oz;
                int cId = Emit(m, ctr + surfN * (0.45 * rimH), surfN, discCol, cov, ctr.X, ctr.Z, kindCode, 1.8, moist, thickMm, health, rimFactor, 0.5, 0.5, 0.74, proxyComp);

                const int segs = 5;

                for (int s = 0; s < segs; s++)
                {
                    double ang = 2.0 * Math.PI * s / segs;
                    Vec3 radial = t1 * Math.Cos(ang) + t2 * Math.Sin(ang);
                    Vec3 pRim = ctr + radial * rad + surfN * rimH;
                    Vec3 nRim = (surfN * 0.78 + radial * 0.62).Normalized();
                    rIds[s] = Emit(m, pRim, nRim, rimCol, cov, pRim.X, pRim.Z, kindCode, 1.2, moist, thickMm, health, rimFactor, 0.5 + 0.5 * Math.Cos(ang), 0.5 + 0.5 * Math.Sin(ang), 0.94, proxyComp);
                }
                for (int s = 0; s < segs; s++)
                {
                    Tri(m, cId, rIds[s], rIds[(s + 1) % segs]);
                }
            }
        }

        private void EmitFolioseLobes(MeshData m, ColonySpecies sp, double kindCode, ulong cellHash,
            Vec3 cellOrigin, Vec3 surfN, Vec3 t1, Vec3 t2, Vec2 frontDir, double cov, double thick,
            double health, bool dormant, double moist, double rimFactor)
        {
            // Appressed, scalloped thallus lobes with a gently raised margin.
            int full=Math.Max(1,(int)Math.Round(3.5*Math.Pow(cov,0.6)));
            int count=_detail==0 ? full : Math.Max(1,(full+1)/2);
            double scale=_detail==0 ? 1.0 : 1.35;
            Vec3 baseCol=SpeciesColor(sp,health,dormant,moist,cellOrigin.X,cellOrigin.Z,rimFactor);
            Vec3 rimCol=Vec3.Lerp(baseCol,new Vec3(0.42,0.46,0.37),0.25);
            double mm=thick*1000.0, baseYaw=Math.Atan2(frontDir.Z,frontDir.X);
            const int segments=6;
            var border=new int[segments+1];
            for(int i=0;i<count;i++)
            {
                ulong h=Rng.Mix(cellHash,(ulong)(200+i));
                double ox=ReprId.Range(h,1,-0.36*_cell,0.36*_cell),oz=ReprId.Range(h,2,-0.36*_cell,0.36*_cell);
                double yaw=baseYaw+ReprId.Range(h,3,-0.85,0.85);
                double length=ReprId.Range(h,4,0.0038,0.0062)*scale*(0.75+0.35*cov);
                Vec3 fwd=(t1*Math.Cos(yaw)+t2*Math.Sin(yaw)).Normalized();
                Vec3 side=surfN.Cross(fwd).Normalized();
                Vec3 root=cellOrigin+t1*ox+t2*oz;
                int center=Emit(m,root,surfN,baseCol*0.88,cov,root.X,root.Z,kindCode,1,moist,mm,health,rimFactor,0.5,0,0.78,0);
                for(int j=0;j<=segments;j++)
                {
                    double angle=-1.05+2.10*j/segments;
                    double scallop=0.88+0.12*Math.Cos(j*Math.PI);
                    double forward=Math.Cos(angle)*length*scallop;
                    double lateral=Math.Sin(angle)*length*0.70*scallop;
                    double lift=0.00018+0.00035*Math.Abs(Math.Sin(angle));
                    Vec3 point=root+fwd*forward+side*lateral+surfN*lift;
                    Vec3 normal=(surfN+fwd*0.08+side*(0.08*Math.Sin(angle))).Normalized();
                    border[j]=Emit(m,point,normal,rimCol,cov,point.X,point.Z,kindCode,1.8,moist,mm,health,rimFactor,j/(double)segments,1,0.94,0);
                }
                for(int j=0;j<segments;j++) Tri(m,center,border[j],border[j+1]);
            }
        }

        private void EmitFruticosePodetia(
            MeshData m,
            ColonySpecies sp,
            double kindCode,
            ulong cellHash,
            Vec3 cellOrigin,
            Vec3 surfN,
            Vec3 t1,
            Vec3 t2,
            double cov,
            double thick,
            double health,
            bool dormant,
            double moist,
            double rimFactor)
        {
            // Bifurcating shrubby podetia/antlers (Cladonia-style) rising from the squamulose mat.
            int baseCount = Math.Max(1, (int)Math.Round(3.5 * Math.Pow(cov, 0.65)));
            int count = _detail == 0 ? baseCount : Math.Max(1, (baseCount + 1) / 2);
            double widthScale = _detail == 0 ? 1.0 : 1.75;
            double proxyComp = _detail == 0 ? 0.0 : 0.45;

            Vec3 baseCol = SpeciesColor(sp, health, dormant, moist, cellOrigin.X, cellOrigin.Z, rimFactor);
            Vec3 tipCol = Vec3.Lerp(baseCol * 1.08, new Vec3(0.44, 0.48, 0.39), 0.25);
            double thickMm = thick * 1000.0;

            for (int i = 0; i < count; i++)
            {
                ulong h = Rng.Mix(cellHash, (ulong)(300 + i));
                double ox = ReprId.Range(h, 1, -0.38 * _cell, 0.38 * _cell);
                double oz = ReprId.Range(h, 2, -0.38 * _cell, 0.38 * _cell);
                double yaw = ReprId.Range(h, 3, 0, 2.0 * Math.PI);
                double podH = Math.Max(0.0045, Math.Min(sp.MaxThickness * 1.15, thick * 1.1 + 0.0045 * cov))
                            * ReprId.Range(h, 4, 0.85, 1.25)
                            * (1.0 - 0.30 * rimFactor);
                double stalkW = 0.00040 * widthScale * ReprId.Range(h, 5, 0.9, 1.15);

                Vec3 root = cellOrigin + t1 * ox + t2 * oz - surfN * 0.0003;
                Vec3 dirH = (t1 * Math.Cos(yaw) + t2 * Math.Sin(yaw)).Normalized();
                Vec3 side = surfN.Cross(dirH).Normalized();
                Vec3 fork = root + surfN * (0.55 * podH) + dirH * (0.12 * podH);
                Vec3 tipA = fork + (surfN * 0.82 - side * 0.48 + dirH * 0.22).Normalized() * (0.52 * podH);
                Vec3 tipB = fork + (surfN * 0.82 + side * 0.48 - dirH * 0.15).Normalized() * (0.48 * podH);

                int b0 = Emit(m, root - side * stalkW, (surfN - side * 0.4).Normalized(), baseCol * 0.78, cov, root.X, root.Z, kindCode, 1.0, moist, thickMm, health, rimFactor, 0.0, 0.0, 0.60, proxyComp);
                int b1 = Emit(m, root + side * stalkW, (surfN + side * 0.4).Normalized(), baseCol * 0.78, cov, root.X, root.Z, kindCode, 1.0, moist, thickMm, health, rimFactor, 1.0, 0.0, 0.60, proxyComp);
                int f0 = Emit(m, fork - side * (0.85 * stalkW), (surfN - side * 0.3).Normalized(), Vec3.Lerp(baseCol, tipCol, 0.55), cov, fork.X, fork.Z, kindCode, 1.5, moist, thickMm, health, rimFactor, 0.2, 0.55, 0.85, proxyComp);
                int f1 = Emit(m, fork + side * (0.85 * stalkW), (surfN + side * 0.3).Normalized(), Vec3.Lerp(baseCol, tipCol, 0.55), cov, fork.X, fork.Z, kindCode, 1.5, moist, thickMm, health, rimFactor, 0.8, 0.55, 0.85, proxyComp);
                int ta = Emit(m, tipA, (surfN - side * 0.35).Normalized(), tipCol, cov, tipA.X, tipA.Z, kindCode, 2.0, moist, thickMm, health, rimFactor, 0.15, 1.0, 1.00, proxyComp);
                int tb = Emit(m, tipB, (surfN + side * 0.35).Normalized(), tipCol, cov, tipB.X, tipB.Z, kindCode, 2.0, moist, thickMm, health, rimFactor, 0.85, 1.0, 1.00, proxyComp);

                Tri(m, b0, b1, f1);
                Tri(m, b0, f1, f0);
                Tri(m, f0, f1, ta);
                Tri(m, f0, f1, tb);
            }
        }
    }
}
