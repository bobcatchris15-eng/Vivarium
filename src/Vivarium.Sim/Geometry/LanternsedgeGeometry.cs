using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Accepted procedural presentation of Lanternsedge. Inspired by purple-stemmed
/// Angelica's hollow wine-coloured culms, inflated leaf sheaths, ternate
/// compound foliage and repeatedly divided umbels, but adapted to this small
/// fictional, luminous shoreline species rather than copying a real plant.
/// UV2.x 1.4 / UV2.y 1.0 exclusively marks its seed lanterns.
/// </summary>
internal static class LanternsedgeGeometry
{
    private static readonly double[] OldCulm={.29,.075,.205};
    private static readonly double[] YoungCulm={.48,.19,.315};
    private static readonly double[] VeinedSheath={.46,.38,.27};
    private static readonly double[] LaminaBase={.13,.30,.15};
    private static readonly double[] LaminaTip={.37,.54,.25};
    private static readonly double[] Lamp={.69,.91,.59};
    private static readonly double[] LampShaded={.45,.67,.42};

    public static void Build(MeshData mesh,Rng rng,bool juvenile)
    {
        int detail=mesh.FloraDetailLevel??0;
        double lod=mesh.Lod?.LeafFraction??1;
        int stalkCount=juvenile?4:detail==2?4:detail==1?5:7;
        int heads=juvenile?0:detail==2?2:detail==1?3:5;
        // Top-tier tubes and lanterns are generated at appropriate densities;
        // botanical leaf spans participate in the existing triangle-budget LOD.
        if(mesh.Lod!=null)
        {
            heads=Math.Max(1,(int)Math.Round(heads*(.34+.66*lod)));
        }
        double scale=juvenile?.68:1;
        for(int k=0;k<stalkCount;k++)
        {
            double az=2*Math.PI*(k+.22*rng.Range(-1,1))/stalkCount;
            var radial=new Vec3(Math.Cos(az),0,Math.Sin(az));
            var lateral=new Vec3(-radial.Z,0,radial.X);
            var foot=radial*rng.Range(.005,.078)+Vec3.Up*(-.012);
            double height=scale*(k>=heads?rng.Range(.43,.59):rng.Range(.67,.83));
            double arch=rng.Range(.10,.26);
            double phase=rng.Range(-1.5,1.5);
            var centre=new Vec3[11];var radii=new double[11];
            for(int j=0;j<centre.Length;j++)
            {
                double t=j/(double)(centre.Length-1);
                centre[j]=foot+Vec3.Up*(height*t)+radial*(arch*(t*t))+
                    lateral*(scale*.023*t*Math.Sin(2.4*t+phase));
                double taper=1-.47*t;
                double nodes=Math.Exp(-Math.Pow((t-.25)/.065,2))+
                    Math.Exp(-Math.Pow((t-.46)/.065,2))+
                    Math.Exp(-Math.Pow((t-.67)/.065,2));
                radii[j]=scale*(.0175+.004*(k%3==0?1:0))*taper*(1+.13*nodes)*
                    (t>.84?1-.78*(t-.84)/.16:1);
            }
            Culm(mesh,centre,radii,k);

            // Alternate leaf insertion along a continuous axis. Broader,
            // more subdivided lower leaves, reduced upper foliage, like angelica.
            int leafNodes=juvenile?2:detail==2?2:3;
            for(int n=0;n<leafNodes;n++)
            {
                double t=.15+n*.18+.018*rng.Range(-1,1);
                int flip=(k+n)%2==0?1:-1;
                var side=(radial*Math.Cos(n*2.43)+lateral*Math.Sin(n*2.43)).Normalized();
                CompoundLeaf(mesh,rng,Sample(centre,t),side*flip,scale*(1-.16*n),
                    detail,n,k);
            }
            // The basal mound is not bare: broad, low leaf fans emerge from
            // additional short clasping petioles between the fertile stems.
            if(k%2==0 || k%3==1)
            {
                var origin=Sample(centre,.06);
                CompoundLeaf(mesh,rng,origin,(radial+lateral*.27).Normalized(),
                    scale*.84,detail,0,k+17);
            }
            if(k>=heads)
            {
                // Vegetative shoots end in living buds and small leaf crowns,
                // never abruptly cut purple cylinders next to fertile umbels.
                CompoundLeaf(mesh,rng,Sample(centre,.94),radial,
                    scale*.58,detail,2,k+71);
                continue;
            }
            var tip=centre[^1];
            var up=(centre[^1]-centre[^2]).Normalized();
            Umbel(mesh,rng,tip,up,radial,lateral,scale*(k%2==0?1:.78),detail,
                k,lod);
            // A smaller, younger axillary umbel belongs to a real upper node.
            if(!juvenile && detail==0 && k==0)
            {
                double t=.71;
                var node=Sample(centre,t);
                var branch=(radial*.70+lateral*.33+Vec3.Up*.58).Normalized();
                var path=new[]{node,node+branch*(scale*.11),node+branch*(scale*.21)+Vec3.Up*(scale*.015)};
                Primitives.Tube(mesh,path,new[]{.006*scale,.004*scale,.0027*scale},6,
                    (j,v)=>(Primitives.Mix(OldCulm,YoungCulm,j*.35),1,j*.10,v,0,0));
                Umbel(mesh,rng,path[^1],branch,radial,lateral,scale*.60,detail,
                    33,lod);
            }
        }
    }

    private static void Culm(MeshData m,IReadOnlyList<Vec3> path,
        IReadOnlyList<double> radii,int k)
    {
        const int sides=10;
        int first=m.VertexCount;
        var tangent=new Vec3[path.Count];
        var frame=new Vec3[path.Count];
        for(int i=0;i<path.Count;i++)
        {
            var a=path[Math.Max(0,i-1)];
            var b=path[Math.Min(path.Count-1,i+1)];
            tangent[i]=(b-a).Normalized();
        }
        var side=tangent[0].Cross(new Vec3(0,0,1)).Normalized();
        if(side.LengthSq<1e-8)side=tangent[0].Cross(new Vec3(1,0,0)).Normalized();
        double len=0;
        for(int i=0;i<path.Count;i++)
        {
            if(i>0)len+=(path[i]-path[i-1]).Length;
            side=(side-tangent[i]*side.Dot(tangent[i])).Normalized();
            if(side.LengthSq<1e-8)side=tangent[i].Cross(new Vec3(1,0,0)).Normalized();
            frame[i]=side;
            var other=side.Cross(tangent[i]).Normalized();
            double v=i/(double)(path.Count-1);
            for(int j=0;j<=sides;j++)
            {
                double a=2*Math.PI*j/sides;
                // Low five-fold vascular fluting; the shallow grooves are
                // slightly unequal, rather than a perfect ornamental star.
                double grain=1+.075*Math.Cos(5*a+.15*k)+.029*Math.Sin(3*a+.4*k);
                var norm=(side*Math.Cos(a)+other*Math.Sin(a)).Normalized();
                var colour=Primitives.Mix(OldCulm,YoungCulm,.14+.44*v);
                colour=Primitives.Mix(colour,VeinedSheath,
                    .17*Math.Max(0,Math.Cos(a*5+.2*k)));
                m.AddVertex(path[i]+norm*(radii[i]*grain),norm,colour,1,len,
                    j/(double)sides,0,0);
            }
        }
        for(int i=0;i<path.Count-1;i++)
        for(int j=0;j<sides;j++)
        {
            int a=first+i*(sides+1)+j,b=a+sides+1;
            m.AddTriangle(a,a+1,b);
            m.AddTriangle(a+1,b+1,b);
        }
        m.Structural.Add((first,m.VertexCount-first,sides,radii[0]));
    }

    private static void CompoundLeaf(MeshData m,Rng rng,Vec3 node,Vec3 outDir,
        double size,int detail,int rank,int identity)
    {
        var up=Vec3.Up;
        var tangential=(new Vec3(-outDir.Z,0,outDir.X)).Normalized();
        double reach=size*(rank==0?.46:rank==1?.34:.26)*rng.Range(.82,1.17);
        var joint=node+outDir*(reach*.22)+up*(size*.045);
        var tip=node+outDir*reach+up*(size*(.035+.03*rank));
        var shaft=new[]{node,joint,Vec3.Lerp(joint,tip,.58)+up*(size*.024),tip};
        Primitives.Tube(m,shaft,new[]{size*.0105,size*.0075,size*.0042,size*.0022},6,
            (j,v)=>(Primitives.Mix(OldCulm,VeinedSheath,.25+j*.18),1,j*reach/3,v,0,0));
        // Fleshy clasping sheaths swell at insertion and taper into the petiole.
        // The widening is along the vascular insertion, not a floating node ring.
        var collarEnd=Vec3.Lerp(node,joint,.66);
        Primitives.Tube(m,new[]{node-outDir*(.011*size),node,
            Vec3.Lerp(node,collarEnd,.5),collarEnd},
            new[]{size*.018,size*.035,size*.026,size*.008},8,
            (j,v)=>(Primitives.Mix(OldCulm,VeinedSheath,.26+j*.22),1,j*.008,v,0,0));
        int pairs=detail==2?1:2;
        for(int pair=0;pair<pairs;pair++)
        {
            double at=.41+pair*.29;
            var basePoint=Sample(shaft,at);
            double ageScale=size*(1-.12*rank);
            for(int sign=-1;sign<=1;sign+=2)
            {
                double len=ageScale*(pair==0?.27:.20)*rng.Range(.84,1.19);
                var aim=(tangential*(sign*.91)+outDir*.41+
                    up*rng.Range(-.17,.10)).Normalized();
                var start=basePoint+aim*(size*.012);
                Primitives.Tube(m,new[]{basePoint,start},
                    new[]{size*.0028,size*.0016},6,
                    (i,v)=>(VeinedSheath,1,i*.012,v,0,0));
                Leaflet(m,rng,start,start+aim*len,tangential,ageScale*.040,
                    detail,identity+pair*3+sign);
            }
        }
        // A slightly larger apical leaflet gives each compound leaf a legible
        // asymmetric three-to-five-part silhouette rather than a flat pinwheel.
        double terminalLen=size*(detail==2?.18:.23);
        var apical=tip+(outDir+up*.10).Normalized()*terminalLen;
        Leaflet(m,rng,tip,apical,tangential,size*.047,detail,identity+43);
    }

    private static void Leaflet(MeshData m,Rng rng,Vec3 root,Vec3 tip,
        Vec3 sideHint,double halfWidth,int detail,int identity)
    {
        var axis=tip-root;
        var forward=axis.Normalized();
        var side=(sideHint-forward*sideHint.Dot(forward)).Normalized();
        if(side.LengthSq<1e-8)side=forward.Cross(Vec3.Up).Normalized();
        var normal=forward.Cross(side).Normalized();
        int rows=detail==2?7:detail==1?9:11;
        int first=m.VertexCount,firstIndex=m.Indices.Count;
        Vec3 At(double t,double x)
        {
            // Long pointed ovate/compound leaflet with small irregular margin
            // teeth that fade toward the attachment and sharp distal tip.
            double width=Math.Pow(Math.Max(0,Math.Sin(Math.PI*Math.Pow(t,.81))),.86);
            double serration=Math.Pow(Math.Sin(Math.PI*t),1.1)*
                (.045*Math.Sin(t*16*Math.PI+identity*.23)+
                 .035*Math.Sin(t*10*Math.PI+identity*.53));
            width=Math.Max(0,width*(1+serration*(x>0?1:-.8)));
            double midrib=.012*Math.Sin(Math.PI*t)*(1-x*x);
            double roll=.035*Math.Sin(t*4.0+identity*.34)*x*Math.Sin(Math.PI*t);
            return root+axis*t+side*(x*halfWidth*width)+
                normal*(halfWidth*(midrib+roll)+axis.Length*.018*Math.Sin(Math.PI*t)*(1-t));
        }
        int surfaces=m.FloraDetailLevel.HasValue?1:2;
        for(int face=0;face<surfaces;face++)
        {
            int begin=m.VertexCount;
            for(int i=0;i<=rows;i++)
            {
                double t=i/(double)rows;
                foreach(double x in new[]{-1.0,0.0,1.0})
                {
                    var p=At(t,x);
                    var n=face==0?normal:-normal;
                    // Encode leaf top/bottom exactly as the living-leaf shader expects.
                    m.AddVertex(p,n,Primitives.Mix(LaminaBase,LaminaTip,t*.65),
                        1,t,(x+1)*.5,face,1);
                }
            }
            for(int i=0;i<rows;i++)
            for(int j=0;j<2;j++)
            {
                int a=begin+i*3+j,b=a+3;
                Primitives.TriangleFacing(m,a,b,a+1,face==0?normal:-normal);
                Primitives.TriangleFacing(m,a+1,b,b+1,face==0?normal:-normal);
            }
        }
        m.RecordLeaf(first,firstIndex,root,axis.Length);
    }

    private static void Umbel(MeshData m,Rng rng,Vec3 origin,Vec3 axial,
        Vec3 radial,Vec3 lateral,double size,int detail,int identity,double lod)
    {
        int rays=detail==2?5:detail==1?6:9;
        int podlets=detail==2?2:3;
        if(m.Lod!=null)
        {
            rays=Math.Max(4,(int)Math.Round(rays*(.57+.43*lod)));
            podlets=Math.Max(1,(int)Math.Round(podlets*(.42+.58*lod)));
        }
        // A visibly branching umbel: rays radiate from one insertion;
        // secondary stalklets terminate in discrete luminous swollen fruits.
        double dome=size*rng.Range(.092,.112);
        var axis=(axial+Vec3.Up*.38).Normalized();
        for(int i=0;i<rays;i++)
        {
            double angle=2*Math.PI*(i+.12*rng.Range(-1,1))/rays;
            var horizontal=(radial*Math.Cos(angle)+lateral*Math.Sin(angle)).Normalized();
            // Variable lengths and strongly graduated heights make a domed,
            // irregular botanical umbel rather than a flat round brush.
            double extent=dome*rng.Range(.57,1.15);
            double rise=dome*(1.36-.82*extent/dome)+dome*rng.Range(-.14,.17);
            var joint=origin+horizontal*extent+axis*rise;
            Primitives.Tube(m,new[]{origin,Vec3.Lerp(origin,joint,.48)+axis*dome*.055,joint},
                new[]{size*.0036,size*.0023,size*.0012},6,
                (j,v)=>(Primitives.Mix(OldCulm,YoungCulm,.35+j*.22),1,j*extent/2,v,0,0));
            // Small umbels-within-umbels instead of the old solitary cattails.
            for(int p=0;p<podlets;p++)
            {
                double theta=2*Math.PI*(p+.28*rng.Range(-1,1))/podlets;
                var across=(new Vec3(-horizontal.Z,0,horizontal.X)).Normalized();
                var spread=horizontal*Math.Cos(theta)*.55+
                    across*Math.Sin(theta)*.72+axis*rng.Range(.30,1.12);
                var pos=joint+spread*(size*rng.Range(.038,.070));
                Primitives.Tube(m,new[]{joint,pos},
                    new[]{size*.00115,size*.00055},6,
                    (j,v)=>(YoungCulm,1,j*.02,v,0,0));
                // Lamps are fleshy capsule-berries, not incandescent leaves.
                var tint=Primitives.Mix(LampShaded,Lamp,rng.Range(.55,.93));
                Primitives.Ellipsoid(m,pos,new Vec3(size*.0105,size*.0090,
                    size*.0105),4,6,(u,v)=>(tint,1,u,v,1.4,1));
            }
        }
    }

    private static Vec3 Sample(IReadOnlyList<Vec3> points,double t)
    {
        double f=Math.Clamp(t,0,1)*(points.Count-1);
        int i=Math.Min(points.Count-2,(int)f);
        return Vec3.Lerp(points[i],points[i+1],f-i);
    }
}
