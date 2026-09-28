using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry.Form;

namespace Vivarium.Sim.Geometry;

public static partial class OrganismMeshes
{
    // Continuous quadratic midrib, tapered margins and a shallow fold. Unlike the old three-triangle
    // blades, these ribbons can bow, twist and roll without producing a sharp elbow in the silhouette.
    private static void CurvedBlade(MeshData m, Vec3 root, Vec3 control, Vec3 tip, Vec3 sideHint,
        double width, double[] baseCol, double[] tipCol, int segments = 8,
        double twist = 0, double shoulder = 1, double serration = 0, double foliage = 1)
    {
        int n = m.FloraDetailLevel switch { 2 => Math.Max(4, segments / 2), 1 => Math.Max(5, segments - 2), _ => segments };
        if (m.Lod is { } lod) n = lod.Segments(n, 3);
        Vec3 At(double t, double x) => CurvedBladePoint(root,control,tip,sideHint,width,t,x,twist,shoulder,serration);
        Vec3 Normal(double t, double x)
        {
            const double e = .001;
            var along = At(Math.Min(1,t+e),x)-At(Math.Max(0,t-e),x);
            var across = At(t,Math.Min(1,x+e))-At(t,Math.Max(-1,x-e));
            var normal = across.Cross(along).Normalized();
            return normal.LengthSq > 1e-8 ? normal : Vec3.Up;
        }
        int vertex = m.VertexCount, index = m.Indices.Count;
        // Both materials render a single thin sheet with explicit backface pigment.
        // Coincident front/back triangles double the work and can fight over their normals.
        for (int face = 0; face < 1; face++)
        {
            double sign = face == 0 ? 1 : -1;
            int V(double t, double x) => m.AddVertex(At(t,x), Normal(t,x)*sign,
                Primitives.Mix(baseCol,tipCol,t),1,t,(x+1)*.5,face,foliage);
            int start = V(0,0);
            for (int row=1;row<n;row++) for(int col=-1;col<=1;col++) V(row/(double)n,col);
            int end = V(1,0);
            void Tri(int a,int b,int c) => Primitives.TriangleFacing(m,a,b,c,
                (m.NormalAt(a)+m.NormalAt(b)+m.NormalAt(c)).Normalized());
            Tri(start,start+1,start+2); Tri(start,start+2,start+3);
            for(int row=0;row<n-2;row++)
            {
                int a=start+1+row*3;
                Tri(a,a+3,a+1); Tri(a+1,a+3,a+4);
                Tri(a+1,a+4,a+2); Tri(a+2,a+4,a+5);
            }
            Tri(end-3,end,end-2); Tri(end-2,end,end-1);
        }
        if (foliage > .5) m.RecordLeaf(vertex,index,root,(tip-root).Length);
    }

    private static Vec3 CurvedBladePoint(Vec3 root, Vec3 control, Vec3 tip, Vec3 sideHint,
        double width, double t, double x, double twist, double shoulder, double serration)
    {
        var side = sideHint.Normalized();
        var centre = root * ((1-t)*(1-t)) + control * (2*t*(1-t)) + tip * (t*t);
        var tangent = ((control-root)*(1-t) + (tip-control)*t).Normalized();
        var across = side - tangent * side.Dot(tangent);
        if (across.LengthSq < 1e-8) across = tangent.Cross(new Vec3(1, 0, 0));
        across = Axis.RotateAround(across.Normalized(), tangent, twist*t);
        var up = across.Cross(tangent).Normalized();
        double envelope = Math.Pow(Math.Max(0, Math.Sin(Math.PI*Math.Pow(t, shoulder))), .75);
        double teeth = 1 + serration * (2*Math.Abs(t*9-Math.Floor(t*9+.5))-.5);
        double half = width * envelope * (1 + .07*x*Math.Sin(t*5));
        return centre + across*(x*half*teeth) + up*(half*(.16*(1-x*x)-.035*Math.Abs(x)));
    }

    private static void Kinkcane(MeshData m, Rng rng, double[] c1, double[] c2, double aspect)
    {
        var nodeCol = Primitives.Scale(c1,.58);
        var youngCol = Primitives.Mix(c1,c2,.42);
        int canes=14+rng.NextInt(7);
        for(int k=0;k<canes;k++)
        {
            double a=rng.Range(0,Math.PI*2), rr=Math.Sqrt(rng.NextDouble())*.54;
            var outward=new Vec3(Math.Cos(a),0,Math.Sin(a));
            var foot=outward*rr;
            var light=(Vec3.Up+outward*.18).Normalized();
            var direction=(Vec3.Up+outward*rng.Range(.15,.4)).Normalized();
            var physical=new Vec3[7];
            for(int j=1;j<physical.Length;j++)
            {
                if(j>1)
                {
                    // Select a point on a 34..46 degree cone around the preceding internode.
                    // Prefer the open sky; seed variation chooses among similarly exposed directions.
                    double turn=rng.Range(34,46)*Math.PI/180;
                    var toward=light-direction*light.Dot(direction);
                    if(toward.LengthSq<1e-8) toward=outward-direction*outward.Dot(direction);
                    toward=toward.Normalized();
                    var across=direction.Cross(toward).Normalized();
                    double phi=rng.Range(-.5,.5);
                    direction=(direction*Math.Cos(turn)+(toward*Math.Cos(phi)+across*Math.Sin(phi))*Math.Sin(turn)).Normalized();
                }
                physical[j]=physical[j-1]+direction*rng.Range(.15,.21);
            }
            // Convert the physically angled path back into the game's nonuniform unit coordinates.
            // Uniform normalization before conversion preserves all internode angles.
            double scale=rng.Range(.78,1.02)/physical[^1].Y;
            var path=physical.Select(p=>foot+new Vec3(p.X*aspect,p.Y,p.Z*aspect)*scale).ToArray();
            var rad=Enumerable.Range(0,7).Select(j=>MathD.Lerp(.027,.009,j/6.0)).ToArray();
            Primitives.Tube(m,path,rad,7,(i,v)=>(Primitives.Mix(Primitives.Scale(c1,.68),youngCol,i/6.0),1,i/6.0,v,0,0));
            for(int j=1;j<6;j++)
            {
                // Node collars follow their internode, rather than sitting as horizontal beads.
                var tangent=(path[j+1]-path[j-1]).Normalized();
                Primitives.Tube(m,new[]{path[j]-tangent*.008,path[j],path[j]+tangent*.009},
                    new[]{rad[j]*1.05,rad[j]*1.23,rad[j]*1.05},7,(i,v)=>(nodeCol,1,i*.5,v,0,0));
                double az=a+j*2.4+rng.Range(-.4,.4);
                var spray=new Vec3(Math.Cos(az),rng.Range(.03,.15),Math.Sin(az));
                var twigEnd=path[j]+spray*rng.Range(.14,.23);
                Primitives.Tube(m,new[]{path[j],twigEnd},new[]{.006,.002},6,(i,v)=>(youngCol,1,i,v,0,0));
                for(int leaf=0;leaf<3;leaf++)
                {
                    var root=Vec3.Lerp(path[j],twigEnd,.45+leaf*.24);
                    var dir=Axis.RotateAround(spray.Normalized(),Vec3.Up,(leaf-1)*.48);
                    var tip=root+dir*rng.Range(.24,.43)-Vec3.Up*rng.Range(.035,.10);
                    var control=root+dir*.18+Vec3.Up*rng.Range(.015,.05);
                    CurvedBlade(m,root,control,tip,new Vec3(-dir.Z,0,dir.X),rng.Range(.03,.055),
                        Primitives.Scale(c1,rng.Range(.82,1.05)),c2,8,rng.Range(-.5,.5),1.1);
                }
            }
        }
        for(int r=0;r<4;r++)
        {
            double a=rng.Range(0,Math.PI*2);
            var dir=new Vec3(Math.Cos(a),0,Math.Sin(a));
            Primitives.Tube(m,new[]{dir*.1+Vec3.Up*.015,dir*.4+Vec3.Up*.024,dir*.72+Vec3.Up*.012},
                new[]{.015,.012,.006},6,(i,v)=>(nodeCol,1,i,v,0,0));
        }
    }

    private static void Veilblade(MeshData m, Rng rng, double[] c1, double[] c2)
    {
        // Several basal fans, with shorter outer leaves and rising inner spears.
        int fans=5;
        for(int fan=0;fan<fans;fan++)
        {
            double a=fan*Math.PI*2/fans+rng.Range(-.35,.35);
            var baseDir=new Vec3(Math.Cos(a),0,Math.Sin(a));
            var crown=baseDir*rng.Range(.12,.45);
            for(int k=0;k<8;k++)
            {
                double az=a+(k-3.5)*rng.Range(.19,.30)+rng.Range(-.15,.15);
                var dir=new Vec3(Math.Cos(az),0,Math.Sin(az));
                var side=new Vec3(-dir.Z,0,dir.X);
                double age=Math.Abs(k-3.5)/3.5;
                double h=rng.Range(.78,1.03)*(1-.30*age);
                double lean=rng.Range(.12,.26)+age*rng.Range(.3,.55);
                var root=crown+side*rng.Range(-.055,.055);
                var control=root+dir*(lean*.12)+Vec3.Up*(h*1.23)+side*rng.Range(-.09,.09);
                var tip=root+dir*lean+Vec3.Up*(h*(1-age*.6));
                var col=Primitives.Mix(c1,c2,rng.Range(.04,.48));
                CurvedBlade(m,root,control,tip,side,rng.Range(.035,.07),Primitives.Scale(col,.78),col,
                    12,rng.Range(-.55,.55),.8);
            }
        }
        for(int k=0;k<5;k++)
        {
            double a=rng.Range(0,Math.PI*2);
            var foot=new Vec3(Math.Cos(a)*rng.Range(.05,.36),0,Math.Sin(a)*rng.Range(.05,.36));
            var top=foot+new Vec3(rng.Range(-.12,.12),rng.Range(.88,1.10),rng.Range(-.12,.12));
            var path=Enumerable.Range(0,6).Select(i=>Vec3.Lerp(foot,top,i/5.0)+Vec3.Up*(.025*Math.Sin(i*Math.PI/5))).ToArray();
            Primitives.Tube(m,path,new[]{.009,.008,.007,.006,.005,.003},6,(i,v)=>(Primitives.Scale(c1,.76),1,i/5.0,v,0,0));
            Primitives.Ellipsoid(m,top-Vec3.Up*.035,new Vec3(.018,.075,.018),5,8,
                (u,v)=>(Primitives.Mix(c2,new[]{.55,.42,.22},.55),1,u,v,0,0));
        }
    }

    private static void HookPrickle(MeshData m, Vec3 root, Vec3 outward, Vec3 tangent, double size, double[] color)
    {
        var side=outward.Cross(tangent).Normalized()*size*.23;
        var low=root-tangent*size*.25;
        var high=root+tangent*size*.25;
        var elbow=root+outward*size*.72;
        var tip=root+outward*size-tangent*size*.65;
        void Tri(Vec3 a,Vec3 b,Vec3 c)
        {
            var normal=(b-a).Cross(c-a).Normalized();
            int v=m.AddVertex(a,normal,color,1,0,0,0,0);
            m.AddVertex(b,normal,color,1,.5,0,0,0); m.AddVertex(c,normal,color,1,1,1,0,0);
            Primitives.TriangleFacing(m,v,v+1,v+2,normal);
        }
        Tri(low-side,high-side,elbow); Tri(high+side,low+side,elbow);
        Tri(low-side,elbow,tip); Tri(elbow,low+side,tip);
        Tri(high-side,high+side,elbow); Tri(low+side,low-side,tip);
    }

    private static void Hookthicket(MeshData m, Rng rng, double[] c1, double[] c2, bool juvenile)
    {
        var wood=Primitives.Mix(c1,new[]{.32,.16,.12},.6);
        var thorn=new[]{.65,.42,.24};
        int canes=12+rng.NextInt(4);
        for(int k=0;k<canes;k++)
        {
            double az=k*Math.PI*2/canes+rng.Range(-.25,.25);
            var dir=new Vec3(Math.Cos(az),0,Math.Sin(az));
            var side=new Vec3(-dir.Z,0,dir.X);
            var root=dir*rng.Range(.06,.28);
            double height=rng.Range(.62,.88), reach=rng.Range(.90,1.14);
            Vec3 Point(double t)
            {
                // Half-ellipse: continuously turns from rising cane through a broad shoulder
                // into the returning runner. A gentle sideways bow varies its sweep plane.
                return root+dir*(reach*(1-Math.Cos(Math.PI*t))*.5)
                    +Vec3.Up*(height*Math.Sin(t*(juvenile?1.75:Math.PI)))
                    +side*(.13*Math.Sin(k*.7)*Math.Sin(Math.PI*t));
            }
            const int caneSegments=48;
            var path=Enumerable.Range(0,caneSegments+1).Select(j=>Point(j/(double)caneSegments)).ToArray();
            var radius=Enumerable.Range(0,caneSegments+1).Select(j=>MathD.Lerp(.014,.0025,j/(double)caneSegments)).ToArray();
            Primitives.Tube(m,path,radius,7,(i,v)=>(Primitives.Mix(wood,c1,i/(double)caneSegments*.48),1,i/(double)caneSegments,v,0,0));
            for(int j=1;j<10;j++)
            {
                double t=j/10.0;
                var p=Point(t);
                var tangent=(Point(t+.01)-Point(t-.01)).Normalized();
                double nodeRadius=MathD.Lerp(.014,.0025,t);
                var outv=Axis.RotateAround(side,tangent,j*2.4+k*.7).Normalized();
                HookPrickle(m,p+outv*nodeRadius,outv,tangent,rng.Range(.027,.048),thorn);
                if(j%2==0) HookPrickle(m,p-outv*nodeRadius,-outv,tangent,.032,thorn);
                var leafDir=(dir*.55+side*(j%2==0?1:-1)+Vec3.Up*rng.Range(-.05,.15)).Normalized();
                var petiole=p+leafDir*rng.Range(.065,.12)+Vec3.Up*.025;
                Primitives.Tube(m,new[]{p,petiole},new[]{.0035,.0015},6,(i,v)=>(c1,1,i,v,0,0));
                for(int leaflet=-1;leaflet<=1;leaflet++)
                {
                    var forward=Axis.RotateAround(leafDir,Vec3.Up,leaflet*.82);
                    double length=leaflet==0?rng.Range(.21,.29):rng.Range(.15,.22);
                    var attach=petiole+forward*.015;
                    var tip=attach+forward*length-Vec3.Up*rng.Range(.01,.045);
                    CurvedBlade(m,attach,attach+forward*(length*.55)+Vec3.Up*.035,tip,
                        new Vec3(-forward.Z,0,forward.X),length*rng.Range(.29,.38),
                        Primitives.Scale(c1,rng.Range(.85,1.12)),Primitives.Mix(c1,c2,.3),6,
                        rng.Range(-.45,.45),.75,.2);
                }
                // Short leafy lateral shoots knit the thicket together; no floating cross-braces.
                if(j==4||j==7)
                {
                    var end=petiole+leafDir*.17+Vec3.Up*.035;
                    Primitives.Tube(m,new[]{p,petiole,end},new[]{.006,.003,.001},6,(i,v)=>(wood,1,i/2.0,v,0,0));
                    HookPrickle(m,petiole,side,tangent,.035,thorn);
                    CurvedBlade(m,end,end+leafDir*.13+Vec3.Up*.04,end+leafDir*.25,
                        side,.07,c1,c2,6,rng.Range(-.3,.3),.75,.2);
                }
            }
            if(!juvenile)
            {
                // Thin tip roots disappear into soil at the next rooting point of the runner.
                for(int r=0;r<3;r++)
                {
                    var end=path[^1]+dir*(.025+r*.012)+side*((r-1)*.022)-Vec3.Up*.012;
                    Primitives.Tube(m,new[]{path[^1],Vec3.Lerp(path[^1],end,.5),end},new[]{.004,.002,.0008},6,
                        (i,v)=>(Primitives.Mix(wood,new[]{.56,.47,.30},.45),1,i*.5,v,0,0));
                }
            }
        }
    }

    private static void BlueSundewRosette(MeshData m, Rng rng, double[] c1, double[] c2)
    {
        int leaves=11+rng.NextInt(4);
        for(int k=0;k<leaves;k++)
        {
            double a=k*2.39996+rng.Range(-.15,.15);
            var dir=new Vec3(Math.Cos(a),0,Math.Sin(a));
            var side=new Vec3(-dir.Z,0,dir.X);
            var root=dir*.025+Vec3.Up*.02;
            double reach=rng.Range(.14,.46);
            var neck=root+dir*reach+Vec3.Up*rng.Range(.20,.42);
            CurvedBlade(m,root,root+dir*(reach*.55)+Vec3.Up*.12,neck,side,.025,c1,c2,6,.1,1);
            double length=rng.Range(.20,.27), width=rng.Range(.11,.16);
            var padControl=neck+dir*(length*.5)+Vec3.Up*.035;
            var padTip=neck+dir*length+Vec3.Up*.04;
            Vec3 Pad(double t,double x) => CurvedBladePoint(neck,padControl,padTip,side,width,t,x,0,.8,0);
            CurvedBlade(m,neck,neck+dir*(length*.5)+Vec3.Up*.035,neck+dir*length+Vec3.Up*.04,
                side,width,c1,c2,10,0,.8);
            // Long marginal glands encircle the blade; shorter glands occupy its interior.
            int glands=23;
            for(int d=0;d<glands;d++)
            {
                bool margin=d<16;
                double t=margin?.10+.80*(d%8)/7.0:rng.Range(.18,.86);
                double x=margin?(d<8?-1:1):rng.Range(-.65,.65);
                var baseP=Pad(t,x);
                var outward=(side*x+dir*((t-.5)*.6)).Normalized();
                double h=margin?rng.Range(.055,.095):rng.Range(.025,.048);
                var tip=baseP+outward*(margin?.025:.006)+Vec3.Up*h;
                Primitives.Tube(m,new[]{baseP,Vec3.Lerp(baseP,tip,.55)+outward*.006,tip},
                    new[]{.0035,.0025,.0012},6,(i,v)=>(new[]{.56,.17,.28},1,i*.5,v,0,0));
                Primitives.Ellipsoid(m,tip,new Vec3(.008,.010,.008),3,6,
                    (u,v)=>(new[]{.65,.86,.97},1,u,v,0,0));
            }
        }
        var flowerTop=new Vec3(rng.Range(-.08,.08),.86,rng.Range(-.08,.08));
        Primitives.Tube(m,new[]{new Vec3(0,.02,0),new Vec3(.015,.42,0),flowerTop},new[]{.012,.008,.004},6,
            (i,v)=>(c1,1,i*.5,v,0,0));
        for(int f=0;f<3;f++)
        {
            var centre=flowerTop-Vec3.Up*(f*.09)+dirForFlower(f)*.055;
            Primitives.Tube(m,new[]{flowerTop-Vec3.Up*(f*.09),centre},new[]{.003,.001},6,(i,v)=>(c1,1,i,v,0,0));
            for(int petal=0;petal<5;petal++)
            {
                var forward=dirForFlower(petal*2*Math.PI/5);
                CurvedBlade(m,centre,centre+forward*.045+Vec3.Up*.013,centre+forward*.08,
                    new Vec3(-forward.Z,0,forward.X),.025,new[]{.55,.73,.96},new[]{.76,.86,.98},5,0,.7,0,0);
            }
        }
        static Vec3 dirForFlower(double a) => new(Math.Cos(a),0,Math.Sin(a));
    }

    private static void PitcherPlant(MeshData m, Rng rng, double[] c1, double[] c2)
    {
        int count=6+rng.NextInt(3);
        for(int k=0;k<count;k++)
        {
            double a=k*2.39996+rng.Range(-.18,.18);
            var dir=new Vec3(Math.Cos(a),0,Math.Sin(a));
            var side=new Vec3(-dir.Z,0,dir.X);
            var root=dir*rng.Range(.035,.09)+Vec3.Up*.012;
            double h=rng.Range(.46,.66), lean=rng.Range(.23,.46), breadth=rng.Range(.17,.22);
            double phase=rng.Range(0,Math.PI*2);
            Vec3 Centre(double t) => root+dir*(lean*(1-Math.Cos(t*Math.PI*.5)))+Vec3.Up*(h*t);
            double Radius(double t) => breadth*(.11+.77*Math.Sin(Math.PI*t*.84)+.20*Math.Pow(t,8));
            double[] Tissue(double t,double v)
            {
                double patch=.5+.26*Math.Sin(t*19+Math.Sin(v*17+phase)*1.7+phase)
                    +.22*Math.Sin(v*31-t*13+phase*.7);
                var yellow=new[]{.72,.69,.30}; var green=new[]{.34,.48,.17}; var brown=new[]{.42,.27,.12};
                var pigment=Primitives.Mix(yellow,green,MathD.SmoothStep(.20,.65,patch));
                return Primitives.Mix(pigment,brown,MathD.SmoothStep(.66,.93,patch)*.85);
            }
            int around=m.Lod is {} lod?Math.Max(6,(int)Math.Round(20*lod.Radial)):20;
            int rings=m.Lod is {} lod2?Math.Max(3,(int)Math.Round(12*lod2.Length)):12;
            int lipQ=m.Lod is {} lod3?Math.Max(3,(int)Math.Round(6*lod3.Radial)):6;
            int start=m.VertexCount;
            // Exterior and descending interior meet at an open annular rim, never a filled mouth disc.
            for(int face=0;face<2;face++)
            {
                for(int row=0;row<=rings;row++)
                {
                    double t=face==0?row/(double)rings:1-row/(double)rings*.60;
                    double radius=Radius(t)-(face==0?0:.012);
                    for(int s=0;s<=around;s++)
                    {
                        double v=s/(double)around, angle=v*Math.PI*2;
                        var radial=dir*Math.Cos(angle)+side*Math.Sin(angle)*.84;
                        var p=Centre(t)+radial*radius;
                        var normal=(radial-Vec3.Up*(.14*Math.Cos(t*Math.PI))).Normalized()*(face==0?1:-1);
                        var color=face==0?Tissue(t,v):Primitives.Scale(Tissue(t,v),.50+.20*t);
                        m.AddVertex(p,normal,color,1,t,v,0,face==0?-2:-3);
                    }
                }
                int offset=start+face*(rings+1)*(around+1);
                for(int row=0;row<rings;row++) for(int s=0;s<around;s++)
                {
                    int b=offset+row*(around+1)+s, next=b+around+1;
                    Primitives.TriangleFacing(m,b,next,b+1,m.NormalAt(b));
                    Primitives.TriangleFacing(m,b+1,next,next+1,m.NormalAt(b));
                }
            }
            var mouth=Centre(1);
            double lip=Radius(1)-.006;
            int rim=m.VertexCount;
            for(int s=0;s<=around;s++) for(int q=0;q<=lipQ;q++)
            {
                double angle=s*Math.PI*2/around, theta=q*Math.PI*2/lipQ;
                var radial=dir*Math.Cos(angle)+side*Math.Sin(angle)*.84;
                var normal=(radial*Math.Cos(theta)+Vec3.Up*Math.Sin(theta)).Normalized();
                m.AddVertex(mouth+radial*(lip+.013*Math.Cos(theta))+Vec3.Up*(.008*Math.Sin(theta)),
                    normal,Primitives.Mix(Tissue(1,s/(double)around),new[]{.53,.36,.16},.32),1,1,s/(double)around,0,-2);
            }
            for(int s=0;s<around;s++) for(int q=0;q<lipQ;q++)
            {
                int b=rim+s*(lipQ+1)+q, w=lipQ+1;
                Primitives.TriangleFacing(m,b,b+w,b+1,m.NormalAt(b));
                Primitives.TriangleFacing(m,b+1,b+w,b+w+1,m.NormalAt(b));
            }
            // Liquid sits deep inside the cavity and leaves the inner wall visible.
            Primitives.Ellipsoid(m,Centre(.43),new Vec3(Radius(.43)*.88,.004,Radius(.43)*.75),3,12,
                (u,v)=>(new[]{.075,.10,.055},1,u,v,0,-4));
            var hoodRoot=mouth-dir*lip;
            CurvedBlade(m,hoodRoot,hoodRoot-dir*.13+Vec3.Up*.20,hoodRoot-dir*.09+Vec3.Up*.23,
                side,breadth*.86,Tissue(.85,.1),Tissue(1,.6),10,rng.Range(-.35,.35),.7,0,-2);
            // A leafy keel joins the inflated trap to the common basal crown.
            CurvedBlade(m,root,Centre(.45)+dir*Radius(.45),mouth+dir*lip,
                side,.018,Tissue(.2,.1),Tissue(.9,.3),8,0,1,0,-2);
        }
        // A tall, leafless central scape carries a nodding five-part Sarracenia-like flower.
        var flower=new Vec3(.09,.97,.015);
        var scape=Enumerable.Range(0,9).Select(i=>
        {
            double t=i/8.0;
            return new Vec3(.10*Math.Sin(t*Math.PI*.75),t*.96,.015*t);
        }).Append(flower).ToArray();
        Primitives.Tube(m,scape,Enumerable.Range(0,scape.Length).Select(i=>MathD.Lerp(.018,.007,i/(double)(scape.Length-1))).ToArray(),7,
            (i,v)=>(new[]{.37,.48,.18},1,i/(double)(scape.Length-1),v,0,0));
        Primitives.Ellipsoid(m,flower,new Vec3(.057,.025,.057),5,10,(u,v)=>(new[]{.56,.58,.22},1,u,v,0,0));
        for(int k=0;k<5;k++)
        {
            double a=k*Math.PI*2/5;
            var dir=new Vec3(Math.Cos(a),0,Math.Sin(a)); var side=new Vec3(-dir.Z,0,dir.X);
            CurvedBlade(m,flower,flower+dir*.09+Vec3.Up*.01,flower+dir*.12-Vec3.Up*.045,
                side,.053,new[]{.67,.64,.26},new[]{.46,.42,.16},8,0,.8,0,0);
            CurvedBlade(m,flower+dir*.03,flower+dir*.10-Vec3.Up*.055,flower+dir*.055-Vec3.Up*.16,
                side,.046,new[]{.58,.27,.16},new[]{.67,.43,.20},8,.1,.65,0,0);
        }
    }

    private static void SnapTrap(MeshData m, Rng rng, double[] c1, double[] c2)
    {
        int traps=8+rng.NextInt(3);
        for(int k=0;k<traps;k++)
        {
            double a=k*2.39996+rng.Range(-.15,.15);
            var dir=new Vec3(Math.Cos(a),0,Math.Sin(a)); var side=new Vec3(-dir.Z,0,dir.X);
            var root=dir*.025+Vec3.Up*.02;
            var hinge=root+dir*rng.Range(.40,.65)+Vec3.Up*rng.Range(.04,.20);
            CurvedBlade(m,root,Vec3.Lerp(root,hinge,.5)+Vec3.Up*.045,hinge,side,
                rng.Range(.04,.065),c1,c2,8,rng.Range(-.2,.2),.85);
            double length=rng.Range(.24,.31), half=rng.Range(.12,.16), open=rng.Range(.32,.80);
            var forward=(dir+Vec3.Up*rng.Range(-.12,.16)).Normalized();
            var up=side.Cross(forward).Normalized();
            for(int sign=-1;sign<=1;sign+=2)
            {
                Vec3 At(double t,double x)
                {
                    double envelope=Math.Pow(Math.Max(0,Math.Sin(Math.PI*t)),.65);
                    return hinge+forward*(length*t)+side*(sign*half*envelope*x*Math.Cos(open))
                        +up*(half*envelope*(x*Math.Sin(open)+.12*x*x));
                }
                const int rows=10,cols=4;
                int first=m.VertexCount;
                for(int row=0;row<=rows;row++) for(int col=0;col<=cols;col++)
                {
                    double t=.025+.95*row/rows, x=col/(double)cols;
                    var along=At(Math.Min(.99,t+.001),x)-At(Math.Max(.01,t-.001),x);
                    var cross=At(t,Math.Min(1,x+.001))-At(t,Math.Max(0,x-.001));
                    var normal=cross.Cross(along).Normalized()*sign;
                    var red=Primitives.Mix(new[]{.43,.20,.17},new[]{.61,.31,.23},t);
                    var color=Primitives.Mix(red,c1,MathD.SmoothStep(.72,1,x)*.8);
                    m.AddVertex(At(t,x),normal,color,1,t,x,0,-5);
                }
                for(int row=0;row<rows;row++) for(int col=0;col<cols;col++)
                {
                    int b=first+row*(cols+1)+col;
                    Primitives.TriangleFacing(m,b,b+cols+1,b+1,m.NormalAt(b));
                    Primitives.TriangleFacing(m,b+1,b+cols+1,b+cols+2,m.NormalAt(b));
                }
                for(int tooth=0;tooth<11;tooth++)
                {
                    double t=.08+tooth*.084;
                    var p=At(t,1);
                    // Marginal cilia curve upward and inward, framing the trap's two lobes.
                    var end=p+side*(sign*.02)+up*.075;
                    Primitives.Tube(m,new[]{p,p+side*(sign*.025)+up*.035,end},new[]{.0035,.002,.0007},6,
                        (i,v)=>(Primitives.Mix(c2,new[]{.68,.74,.36},.65),1,i*.5,v,0,0));
                }
                for(int hair=0;hair<3;hair++)
                {
                    var p=At(.30+hair*.20,.48);
                    Primitives.Tube(m,new[]{p,p+up*.035-side*(sign*.009)},new[]{.002,.0005},6,
                        (i,v)=>(new[]{.33,.20,.13},1,i,v,0,0));
                }
            }
            Primitives.Tube(m,new[]{hinge,hinge+forward*length},new[]{.006,.003},6,(i,v)=>(c1,1,i,v,0,0));
        }
    }
}

/// <summary>
/// Render-only parametric detail tiers for flora. Every tier is the same generator run with the same seed and
/// individual form, only with coarser <see cref="FloraLodParams"/>: fewer radial sides and length rings on
/// tubes (never below 5 sides), fewer blade segments (never below 2), and a deterministic fraction of leaf
/// blades kept (innermost/smallest dropped first). Nothing is clustered or collapsed, so trunks and stems stay
/// closed solid tubes at every tier. Nothing here is simulation state.
/// </summary>
public static partial class OrganismMeshes
{
    public const int FloraLodTiers = 3;
    /// <summary>Hard cap on the top tier of every species.</summary>
    public const int FloraTopTriangleCap = 10000;
    public const double FloraMidRatio = 0.35, FloraLowRatio = 0.12;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string, ulong, int, bool, int), double> LodScaleCache = new();

    /// <summary>Triangle target for a tier, given the top-tier triangle count.</summary>
    public static int FloraTierBudget(int topTris, int tier) => tier switch
    {
        <= 0 => Math.Min(topTris, FloraTopTriangleCap),
        1 => Math.Max(1, (int)(topTris * FloraMidRatio)),
        _ => Math.Max(1, (int)(topTris * FloraLowRatio)),
    };

    /// <summary>Flora mesh for one detail tier. <paramref name="tieredSource"/> also selects the dedicated leaf
    /// system's own tessellation tier (visual-profile species).</summary>
    public static MeshData FloraTier(FloraSpeciesDef sp, ulong seed, int tier, bool juvenile = false, bool tieredSource = false) =>
        BuildTier(sp.Id, seed, juvenile ? 1 : 0, tieredSource, tier,
            (t, lod) => Flora(sp, seed, juvenile, tieredSource ? t : null, lod))!;

    public static MeshData? FloraFruitingTier(FloraSpeciesDef sp, ulong seed, int tier, bool tieredSource = false) =>
        BuildTier(sp.Id, seed, 2, tieredSource, tier, (t, lod) => FloraFruiting(sp, seed, tieredSource ? t : null, lod));

    public static MeshData ClimberNodeTier(FloraSpeciesDef sp, ulong seed, bool attached, int tier) =>
        BuildTier(sp.Id, seed, attached ? 3 : 4, false, tier, (_, lod) => ClimberNode(sp, seed, attached, lod))!;

    private static MeshData? BuildTier(string id, ulong seed, int kind, bool tiered, int tier, Func<int, FloraLodParams, MeshData?> gen)
    {
        tier = Math.Clamp(tier, 0, FloraLodTiers - 1);
        MeshData? Make(int t, double s) => gen(t, FloraLodParams.FromScale(s)) is { } raw ? FinishTier(raw) : null;
        double S(int t)
        {
            var key = (id, seed, kind, tiered, t);
            if (LodScaleCache.TryGetValue(key, out double cached)) return cached;
            int target;
            double hiS;
            if (t == 0) { target = FloraTopTriangleCap; hiS = 1; }
            else
            {
                double sTop = S(0);
                var top = Make(0, sTop);
                if (top == null) return 0;
                target = FloraTierBudget(top.TriangleCount, t);
                hiS = t == 1 ? sTop : S(1);
            }
            double s = SearchScale(x => Make(t, x)?.TriangleCount ?? 0, target, hiS);
            LodScaleCache[key] = s;
            return s;
        }
        return Make(tier, S(tier));
    }

    /// <summary>Largest scale in [0, hi] whose triangle count is within <paramref name="target"/> (0 if none).</summary>
    private static double SearchScale(Func<double, int> tris, int target, double hi)
    {
        if (tris(hi) <= target) return hi;
        double lo = 0;
        if (tris(lo) > target) return 0;
        for (int it = 0; it < 9; it++)
        {
            double mid = (lo + hi) * 0.5;
            if (tris(mid) <= target) lo = mid; else hi = mid;
        }
        return lo;
    }

    /// <summary>Twice the triangle area below which a triangle counts as degenerate.</summary>
    public const double DegenerateCross = 1e-12;
    /// <summary>Unit-scale tube radius below which a tube is foliage-like (petiole, filament, needle).</summary>
    public const double MinorTubeRadius = 0.006;

    public static bool IsDegenerate(MeshData m, int tri)
    {
        var a = m.Position(m.Indices[tri * 3]);
        var cross = (m.Position(m.Indices[tri * 3 + 1]) - a).Cross(m.Position(m.Indices[tri * 3 + 2]) - a);
        return cross.Length < DegenerateCross;
    }

    /// <summary>Drops leaf blades beyond <see cref="FloraLodParams.LeafFraction"/> (innermost, then smallest,
    /// first) and zero-area triangles, then compacts the mesh. Tubes are never dropped.</summary>
    private static MeshData FinishTier(MeshData src)
    {
        double keepFraction = src.Lod?.LeafFraction ?? 1;
        int nv = src.VertexCount;
        var idx = src.Indices;
        // owner: -2 structural tube, >= 0 candidate part, -1 unassigned
        var owner = new int[nv];
        Array.Fill(owner, -1);
        foreach (var st in src.Structural)
            for (int v = st.FirstVertex; v < st.FirstVertex + st.VertexCount && v < nv; v++) owner[v] = -2;
        int parts = 0;
        foreach (var ls in src.LeafSpans)
        {
            bool any = false;
            for (int v = ls.FirstVertex; v < ls.FirstVertex + ls.VertexCount && v < nv; v++)
                if (owner[v] == -1) { owner[v] = parts; any = true; }
            if (any) parts++;
        }
        // Unrecorded small loose pieces (lobes, petals, beads) are candidates too; big ones are structure.
        var parent = new int[nv];
        for (int i = 0; i < nv; i++) parent[i] = i;
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        for (int t = 0; t < idx.Count; t += 3)
        {
            int a = idx[t];
            for (int j = 1; j < 3; j++)
            {
                int b = idx[t + j];
                if (owner[a] != -1 || owner[b] != -1) continue;
                int ra = Find(a), rb = Find(b);
                if (ra != rb) { if (ra < rb) parent[rb] = ra; else parent[ra] = rb; }
            }
        }
        var compTris = new SortedDictionary<int, int>();
        for (int t = 0; t < idx.Count; t += 3)
            if (owner[idx[t]] == -1) { int r = Find(idx[t]); compTris[r] = compTris.GetValueOrDefault(r) + 1; }
        var compPart = new Dictionary<int, int>();
        foreach (var (root, count) in compTris)
            if (count <= 240) compPart[root] = parts++;
        for (int v = 0; v < nv; v++)
            if (owner[v] == -1 && compPart.TryGetValue(Find(v), out int pp)) owner[v] = pp;

        // Fine tubes are foliage, not wood: a petiole goes with the blade it carries; loose filaments, hairs
        // and needles are candidates of their own (dropped after blades). Trunks and limbs always stay.
        var tubePart = new HashSet<int>();
        double maxTube = src.Structural.Count > 0 ? src.Structural.Max(s => s.Radius) : 0;
        double minor = Math.Max(MinorTubeRadius, maxTube * 0.12);
        // Petioles are judged before the trunkless bump, or a reed's main culm rides along with the collar after it.
        double petiole = minor * 2;
        // Without a real trunk every tube is a stem or shoot; the finer side branches go before the main stems.
        if (maxTube < 0.03) minor = Math.Max(minor, maxTube * 0.6);
        foreach (var st in src.Structural)
        {
            int end = st.FirstVertex + st.VertexCount;
            int p;
            if (st.Radius < petiole && end < nv && owner[end] >= 0) p = owner[end];
            else if (st.Radius < minor) { p = parts++; tubePart.Add(p); }
            else continue;
            for (int v = st.FirstVertex; v < end && v < nv; v++) owner[v] = p;
        }
        var drop = new bool[Math.Max(1, parts)];
        if (keepFraction < 1 && parts > 1)
        {
            var mn = new Vec3[parts]; var mx = new Vec3[parts]; var size = new int[parts];
            for (int p = 0; p < parts; p++)
            {
                mn[p] = new Vec3(double.MaxValue, double.MaxValue, double.MaxValue);
                mx[p] = new Vec3(double.MinValue, double.MinValue, double.MinValue);
            }
            for (int t = 0; t < idx.Count; t += 3)
            {
                int p = owner[idx[t]];
                if (p < 0) continue;
                size[p]++;
                for (int j = 0; j < 3; j++)
                {
                    var q = src.Position(idx[t + j]);
                    mn[p] = new Vec3(Math.Min(mn[p].X, q.X), Math.Min(mn[p].Y, q.Y), Math.Min(mn[p].Z, q.Z));
                    mx[p] = new Vec3(Math.Max(mx[p].X, q.X), Math.Max(mx[p].Y, q.Y), Math.Max(mx[p].Z, q.Z));
                }
            }
            var (bmin, bmax) = src.Bounds();
            var c = (bmin + bmax) * 0.5; var ext = bmax - bmin;
            double sx = Math.Max(1e-4, ext.X * 0.5), sy = Math.Max(1e-4, ext.Y * 0.5), sz = Math.Max(1e-4, ext.Z * 0.5);
            double maxSpan = 1e-9;
            for (int p = 0; p < parts; p++) if (size[p] > 0) maxSpan = Math.Max(maxSpan, (mx[p] - mn[p]).Length);
            var order = Enumerable.Range(0, parts).Where(p => size[p] > 0).Select(p =>
            {
                var q = (mn[p] + mx[p]) * 0.5 - c;
                double exposure = Math.Sqrt(q.X / sx * (q.X / sx) + q.Z / sz * (q.Z / sz)) + 0.3 * (q.Y / sy + 1);
                return (p, score: exposure + 0.5 * (mx[p] - mn[p]).Length / maxSpan + (tubePart.Contains(p) ? 4 : 0));
            }).OrderBy(x => x.score).ThenBy(x => x.p).ToList();
            int dropCount = Math.Min(order.Count - 1, (int)Math.Round(order.Count * (1 - keepFraction)));
            for (int k = 0; k < dropCount; k++) drop[order[k].p] = true;
        }

        var map = new int[nv];
        var dst = new MeshData { FloraDetailLevel = src.FloraDetailLevel, FloraVisualSeed = src.FloraVisualSeed, Lod = src.Lod };
        bool c0 = nv > 0 && src.Custom0.Count >= nv * 4, c1 = nv > 0 && src.Custom1.Count >= nv * 4;
        for (int v = 0; v < nv; v++)
        {
            if (owner[v] >= 0 && drop[owner[v]]) { map[v] = -1; continue; }
            map[v] = dst.VertexCount;
            for (int j = 0; j < 3; j++) { dst.Positions.Add(src.Positions[v * 3 + j]); dst.Normals.Add(src.Normals[v * 3 + j]); }
            for (int j = 0; j < 4; j++) dst.Colors.Add(src.Colors[v * 4 + j]);
            for (int j = 0; j < 2; j++) { dst.UV.Add(src.UV[v * 2 + j]); dst.UV2.Add(src.UV2[v * 2 + j]); }
            if (c0) for (int j = 0; j < 4; j++) dst.Custom0.Add(src.Custom0[v * 4 + j]);
            if (c1) for (int j = 0; j < 4; j++) dst.Custom1.Add(src.Custom1[v * 4 + j]);
        }
        int triCount = idx.Count / 3;
        var triStart = new int[triCount + 1];
        for (int t = 0; t < triCount; t++)
        {
            triStart[t] = dst.Indices.Count;
            int a = map[idx[t * 3]], b = map[idx[t * 3 + 1]], cc = map[idx[t * 3 + 2]];
            if (a < 0 || b < 0 || cc < 0 || IsDegenerate(src, t)) continue;
            dst.Indices.Add(a); dst.Indices.Add(b); dst.Indices.Add(cc);
        }
        triStart[triCount] = dst.Indices.Count;
        foreach (var leaf in src.Leaves)
        {
            if (leaf.FirstVertex >= nv || map[leaf.FirstVertex] < 0) continue;
            int t0 = Math.Min(triCount, leaf.FirstIndex / 3), t1 = Math.Min(triCount, (leaf.FirstIndex + leaf.IndexCount) / 3);
            dst.Leaves.Add(leaf with { FirstVertex = map[leaf.FirstVertex], FirstIndex = triStart[t0], IndexCount = triStart[t1] - triStart[t0] });
        }
        foreach (var st in src.Structural)
            if (st.FirstVertex < nv && map[st.FirstVertex] >= 0) dst.Structural.Add((map[st.FirstVertex], st.VertexCount, st.Sides, st.Radius));
        foreach (var ls in src.LeafSpans)
            if (ls.FirstVertex < nv && map[ls.FirstVertex] >= 0) dst.LeafSpans.Add((map[ls.FirstVertex], ls.VertexCount));
        return dst;
    }
}
