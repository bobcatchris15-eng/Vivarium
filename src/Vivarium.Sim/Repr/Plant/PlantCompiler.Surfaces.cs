using System;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;

namespace Vivarium.Sim.Repr.Plant;

public static partial class PlantCompiler
{
    private static MeshData BuildRadialSurface(OrganShape shape,int detail)
    {
        if (shape.Name.StartsWith("emberfan_bracket_", StringComparison.Ordinal))
            return BuildEmberfanBracket(shape, detail);
        if (shape.Name.StartsWith("pondstars_star_", StringComparison.Ordinal))
            return BuildPondstarLeaf(shape);
        if (shape.SurfaceForm == OrganSurfaceForm.SucculentBlade)
            return BuildSucculentBlade(shape, detail);
        var mesh=new MeshData();
        bool pitcherVessel = shape.Name.StartsWith("pitcher_plant_curved_vessel_", StringComparison.Ordinal);
        bool puff = shape.Name.StartsWith("duskpuff_body_",StringComparison.Ordinal);
        bool bonnet = shape.Name is "ghostbonnet_cap" or "dewbonnet_cap" or "dewbonnet_cap_open" or "barrowbole_cap" or "barrowbole_cap_young";
        bool mushroomCap = shape.SurfaceForm == OrganSurfaceForm.MushroomCap;
        bool starsplit = shape.Name == "starsplit_open_body";
        bool needle=shape.SurfaceForm==OrganSurfaceForm.Needle;
        bool open=shape.SurfaceForm is OrganSurfaceForm.Cup or OrganSurfaceForm.Trumpet or OrganSurfaceForm.MushroomCap or OrganSurfaceForm.Shelf;
        int radial=needle?(detail==0?6:detail==1?4:3):(detail==0?20:detail==1?12:8);
        int rows=needle?(detail==0?7:detail==1?4:2):(detail==0?10:detail==1?6:3);
        if (puff || bonnet) { radial=detail==0?36:detail==1?20:10; rows=detail==0?18:detail==1?10:5; }
        if (starsplit) { radial=detail==0?40:detail==1?20:10; rows=detail==0?20:detail==1?10:5; }
        if(shape.RadialPitDepth>0) { radial=detail==0?48:detail==1?24:12; rows=detail==0?32:detail==1?16:8; }
        if(shape.RadialLattice) { radial=detail==0?48:detail==1?24:12; rows=detail==0?18:detail==1?12:6; }
        // Ten longitudinal ribs need a radial count that samples them without aliasing.
        if(shape.Name=="carrion_bell_cap") radial=detail==0?20:10;
        Vec3 Bend(double x,double y,double z)
        {
            double k=shape.MidribCurl;
            if(Math.Abs(k)<1e-5)return new Vec3(x,y,z);
            double angle=k*z;
            double aspect=Math.Max(.1,shape.LengthToWidth);
            return new Vec3(x,-(1-Math.Cos(angle))/k*aspect+y*Math.Cos(angle),Math.Sin(angle)/k+y*Math.Sin(angle)/aspect);
        }
        var color=EncRgb(shape.ColorAdaxial);var innerColor=EncRgb(shape.ColorAbaxial);
        double peak=ProfileMax(shape);
        double Radius(double t)
        {
            if(shape.WidthProfile.Length>0) return Math.Max(0,.5*ProfileAt(shape,t)/peak);
            return shape.SurfaceForm switch {
                OrganSurfaceForm.Cup or OrganSurfaceForm.Trumpet=>.12+.38*Math.Pow(t,.8),
                OrganSurfaceForm.MushroomCap or OrganSurfaceForm.Shelf=>.5*Math.Sqrt(Math.Max(0,1-t*t)),
                _=>.5*Math.Pow(Math.Max(0,Math.Sin(Math.PI*t)),needle?.4:.65) };
        }
        var outer=new int[rows+1,radial];var inner=open?new int[rows+1,radial]:null;
        var axialZ=new double[rows+1];
        for(int i=0;i<=rows;i++)for(int j=0;j<radial;j++)
        {
            double t=i/(double)rows,theta=j*Math.Tau/radial;
            if(shape.Name=="reekhorn_veil") theta += .18*Math.Sin(t*Math.Tau)+.045*Math.Sin(j*3*Math.Tau/radial+t*7);
            double rim=starsplit ? Math.Pow(t,2.2) : shape.SurfaceForm is OrganSurfaceForm.MushroomCap or OrganSurfaceForm.Shelf?Math.Pow(1-t,4):
                shape.SurfaceForm is OrganSurfaceForm.Star or OrganSurfaceForm.Disc?1:Math.Pow(t,4);
            if(shape.Name=="carrion_bell_cap") rim=1; // longitudinal relief follows the complete head
            double wave=shape.LobeCount>0?Math.Cos(shape.LobeCount*theta):0;
            double radius=Math.Max(.00001,Radius(t)*(1+shape.LobeDepth*rim*wave));
            if (shape.Name is "emberbracken_runner" or "ringreed_ribbed_shaft" or "rainshield_ribbed_stem")
                radius *= 1 + .08 * Math.Cos(10 * theta + .45 * Math.Sin(t * Math.Tau));
            if(shape.RadialPitDepth>0)
            {
                // Staggered cells with raised dividing ridges; angular seam remains periodic.
                double cellY=t*6+.22*Math.Sin(theta*3+2*t), row=Math.Floor(cellY), fy=cellY-row-.5;
                double cellX=j*8.0/radial + ((int)row%2)*.5 + .18*Math.Sin(t*8+theta*2);
                double fx=cellX-Math.Floor(cellX)-.5;
                double edge=Math.Max(Math.Abs(fx)*2, Math.Abs(fy)*2);
                double pit=1-Math.Clamp((edge-.62)/.26,0,1);
                radius*=1-Math.Clamp(shape.RadialPitDepth,0,.5f)*pit*Math.Sin(Math.PI*t);
            }
            // Broad unequal shoulders break lathed symmetry without adding facets or
            // independent noise. The inner wall follows the same outline and rim.
            if(shape.RadialAsymmetry!=0)
                radius*=1+Math.Clamp(shape.RadialAsymmetry,-.3f,.3f)
                    *(.6*Math.Sin(2*theta+.4)+.4*Math.Sin(3*theta-.7));
            if (puff || starsplit) radius *= 1 + .045*Math.Sin(theta*7+t*9)*Math.Sin(Math.PI*t) + .022*Math.Cos(theta*11-t*16);
            if (bonnet) radius *= 1 + .035*Math.Cos(9*theta+t)*Math.Pow(1-t,2);
            double yScale=shape.SurfaceForm==OrganSurfaceForm.Shelf?.55:1;
            double lobeVar = 1 + .18 * Math.Sin(theta * 2 + .7);
            double z=starsplit ? t - .14*rim*(.5 - .5*wave)*lobeVar : t+shape.RimCurl*rim*(1-wave)*.08;
            if(shape.Name=="reekhorn_veil") z += .025*Math.Sin(theta*7+t*9)*Math.Sin(Math.PI*t);
            if(j==0)axialZ[i]=z;
            var p=Bend(radius*Math.Cos(theta),radius*Math.Sin(theta)*yScale,z);
            double striation = shape.Name is "emberbracken_runner" or "ringreed_ribbed_shaft" or "rainshield_ribbed_stem" ? .68 + .32 * (.5 + .5 * Math.Cos(10 * theta + .45 * Math.Sin(t * Math.Tau))) : 1;
            if (puff || starsplit) striation *= .90+.10*Math.Sin(theta*13+t*25)*Math.Cos(theta*5-t*17);
            double outerU2x = mushroomCap ? 0.0 : shape.Type==OrganType.Fruit?.75:shape.Type==OrganType.Flower?1.45:0;
            outer[i,j]=mesh.AddVertex(p,new Vec3(Math.Cos(theta),Math.Sin(theta),0),(float)(color[0]*striation),(float)(color[1]*striation),(float)(color[2]*striation),1,t,j/(double)radial,
                outerU2x,pitcherVessel ? -2 : shape.Type==OrganType.Fruit?0:1);
            if(inner!=null)
            {
                double thickness=Math.Clamp(shape.ShellThickness,.002,.08);
                double rr=Math.Max(.000005,radius-thickness);
                double zInner = starsplit ? .12 + .88 * z : z - (bonnet ? .028 * Math.Sin(Math.PI * t) * Math.Cos(Math.Min(radial / 2, 16) * theta) : 0);
                var q=Bend(rr*Math.Cos(theta),rr*Math.Sin(theta)*yScale,zInner);
                double iu = mushroomCap ? j / (double)radial : t;
                double iv = mushroomCap ? t : j / (double)radial;
                double innerU2x = mushroomCap ? 1.0 : shape.Type==OrganType.Fruit?.75:shape.Type==OrganType.Flower?1.45:0;
                inner[i,j]=mesh.AddVertex(q,new Vec3(-Math.Cos(theta),-Math.Sin(theta),0),innerColor[0],innerColor[1],innerColor[2],1,iu,iv,
                    innerU2x,pitcherVessel ? -3 : shape.Type==OrganType.Fruit?0:1);
            }
        }
        bool Hole(int row,int col)=>shape.RadialLattice&&row>=1&&row<rows-1
            &&row%3!=0&&((col+radial+(shape.Name=="reekhorn_veil" ? row/3 : 0))%radial)%3!=0
            &&!(shape.Name=="reekhorn_veil" && (row*5+col*3)%7==2);
        void Bridge(int rowA,int colA,int rowB,int colB)
        {
            if(inner==null)return;
            int a=outer[rowA,colA],b=outer[rowB,colB],c=inner[rowA,colA],d=inner[rowB,colB];
            mesh.AddTriangle(a,b,c);mesh.AddTriangle(b,d,c);
        }
        for(int i=0;i<rows;i++)for(int j=0;j<radial;j++)
        {
            int n=(j+1)%radial,a=outer[i,j],b=outer[i,n],c=outer[i+1,j],d=outer[i+1,n];
            if(Hole(i,j))
            {
                if(!Hole(i-1,j))Bridge(i,j,i,n);
                if(!Hole(i+1,j))Bridge(i+1,n,i+1,j);
                if(!Hole(i,j-1))Bridge(i+1,j,i,j);
                if(!Hole(i,j+1))Bridge(i,n,i+1,n);
                continue;
            }
            mesh.AddTriangle(a,c,b);mesh.AddTriangle(b,c,d);
            if(inner!=null){a=inner[i,j];b=inner[i,n];c=inner[i+1,j];d=inner[i+1,n];mesh.AddTriangle(a,b,c);mesh.AddTriangle(b,d,c);}
        }
        if(inner!=null)
        {
            foreach(int row in new[]{0,rows})for(int j=0;j<radial;j++)
            {
                int n=(j+1)%radial,a=outer[row,j],b=outer[row,n],c=inner[row,j],d=inner[row,n];
                if(row==0){mesh.AddTriangle(a,b,c);mesh.AddTriangle(b,d,c);}
                else{mesh.AddTriangle(a,c,b);mesh.AddTriangle(b,c,d);}
            }
        }
        else
        {
            foreach(int row in new[]{0,rows})
            {
                var centerPoint=Bend(0,0,axialZ[row]);
                int center=mesh.AddVertex(centerPoint,row==0?-Vec3.Forward:Vec3.Forward,color[0],color[1],color[2],1,
                    0,0,shape.Type==OrganType.Fruit?.75:0,0);
                for(int j=0;j<radial;j++)
                    if(row==0)mesh.AddTriangle(center,outer[row,j],outer[row,(j+1)%radial]);
                    else mesh.AddTriangle(center,outer[row,(j+1)%radial],outer[row,j]);
            }
        }
        mesh.RecomputeNormals();
        for(int i=0;i<mesh.Normals.Count;i++)mesh.Normals[i]=-mesh.Normals[i];
        for(int i=0;i<mesh.VertexCount;i++)if(mesh.NormalAt(i).LengthSq<.25)
        {
            var p=mesh.Position(i);var n=PlantMath.SafeNormal(new Vec3(p.X,p.Y,p.Z<.5?-1:1),Vec3.Up);
            mesh.Normals[i*3]=(float)n.X;mesh.Normals[i*3+1]=(float)n.Y;mesh.Normals[i*3+2]=(float)n.Z;
        }
        return mesh;
    }

    // One star is one flat leaf, with alternating pointed tips and recessed sinuses.
    private static MeshData BuildPondstarLeaf(OrganShape shape)
    {
        var mesh = new MeshData();
        var color = EncRgb(shape.ColorAdaxial);
        int points = Math.Clamp(shape.LobeCount, 4, 6);
        mesh.AddVertex(new Vec3(0,0,.5), Vec3.Up, color[0],color[1],color[2],1, .5,.5,0,1);
        for (int i=0; i<points*2; i++)
        {
            double angle=i*Math.PI/points;
            double radius=(i%2==0 ? .5 : .23);
            double x=radius*Math.Sin(angle), z=.5+radius*Math.Cos(angle);
            mesh.AddVertex(new Vec3(x,0,z),Vec3.Up,color[0],color[1],color[2],1,z,x+.5,0,1);
        }
        for (int i=0; i<points*2; i++)
            mesh.AddTriangle(0,1+(i+1)%(points*2),1+i);
        return mesh;
    }

    // A sessile fan grows out from one narrow bark attachment, with a thick
    // cream pore face and concentric rust bands. It is not a tilted mushroom.
    private static MeshData BuildEmberfanBracket(OrganShape shape, int detail)
    {
        var mesh = new MeshData();
        int rings = detail == 0 ? 12 : detail == 1 ? 8 : 5;
        int spans = detail == 0 ? 28 : detail == 1 ? 18 : 12;
        var top = new int[rings + 1, spans + 1];
        var bottom = new int[rings + 1, spans + 1];
        var rust = EncRgb(shape.ColorAdaxial); var cream = EncRgb(shape.ColorAbaxial);
        for (int i = 0; i <= rings; i++) for (int j = 0; j <= spans; j++)
        {
            double u = i / (double)rings, angle = (j / (double)spans - .5) * Math.PI * 1.16;
            double phase=shape.RadialAsymmetry*30;
            double irregular = 1 + shape.RadialAsymmetry * (.55 * Math.Sin(angle * 3 + phase) + .45 * Math.Sin(angle * 5 - phase));
            double radius = u * irregular;
            double x = .5 * radius * Math.Sin(angle), z = radius * Math.Cos(angle);
            double curl = .035 * u * u + .045 * Math.Sin(angle * 5 + phase) * Math.Pow(u, 3);
            double thickness = .022 * (.65 + .35 * Math.Sin(Math.PI * u));
            double band = .5 + .5 * Math.Cos(u * Math.PI * 10 + .45 * Math.Sin(angle * 3));
            double margin = .15 + .065 * Math.Sin(angle * 3 + phase) + .035 * Math.Cos(angle * 5 - phase);
            double edge = Math.Clamp((u - (1-margin)) / margin, 0, 1);
            float C(int k) => (float)((rust[k] * (.58 + .42 * band)) * (1 - edge) + cream[k] * edge);
            top[i,j] = mesh.AddVertex(new Vec3(x, curl + thickness, z), Vec3.Up,
                C(0), C(1), C(2), 1, u, j / (double)spans, 1.45, 1);
            bottom[i,j] = mesh.AddVertex(new Vec3(x, curl - thickness, z), Vec3.Down,
                cream[0], cream[1], cream[2], 1, u, j / (double)spans, 1.45, 1);
        }
        for (int i = 0; i < rings; i++) for (int j = 0; j < spans; j++)
        {
            int a=top[i,j],b=top[i,j+1],c=top[i+1,j],d=top[i+1,j+1];
            mesh.AddTriangle(a,c,b); mesh.AddTriangle(b,c,d);
            a=bottom[i,j];b=bottom[i,j+1];c=bottom[i+1,j];d=bottom[i+1,j+1];
            mesh.AddTriangle(a,b,c); mesh.AddTriangle(b,d,c);
        }
        for (int j = 0; j < spans; j++)
        {
            int a=top[rings,j],b=top[rings,j+1],c=bottom[rings,j],d=bottom[rings,j+1];
            mesh.AddTriangle(a,b,c); mesh.AddTriangle(b,d,c);
        }
        for (int i = 0; i < rings; i++) foreach (int j in new[] {0, spans})
        {
            int a=top[i,j],b=top[i+1,j],c=bottom[i,j],d=bottom[i+1,j];
            if (j==0) { mesh.AddTriangle(a,c,b); mesh.AddTriangle(b,c,d); }
            else { mesh.AddTriangle(a,b,c); mesh.AddTriangle(b,d,c); }
        }
        mesh.RecomputeNormals();
        for (int i = 0; i < mesh.VertexCount; i++) if (mesh.NormalAt(i).LengthSq < .25)
        {
            mesh.Normals[i*3]=0; mesh.Normals[i*3+1]=mesh.Position(i).Y >= 0 ? 1 : -1; mesh.Normals[i*3+2]=0;
        }
        return mesh;
    }

    // Closed 3D spatulate/triangular fleshy succulent leaf solid with gently cupped adaxial face,
    // thick water-storing parenchyma boat keel underneath, tapered mucronate apex, and basal AO.
    private static MeshData BuildSucculentBlade(OrganShape shape, int detail)
    {
        var mesh = new MeshData();
        int rows = detail == 0 ? 12 : detail == 1 ? 7 : 4;
        int radial = detail == 0 ? 10 : detail == 1 ? 6 : 4;
        double peak = shape.WidthProfile.Length > 0 ? ProfileMax(shape) : 0.5;
        double WidthAt(double t)
        {
            if (shape.WidthProfile.Length > 0) return Math.Max(0.02 * (1.0 - t), 0.5 * ProfileAt(shape, t) / peak);
            return Math.Max(0.02 * (1.0 - t), 0.5 * Math.Pow(Math.Max(0.0, Math.Sin(Math.PI * t)), 0.65));
        }

        var cTop = EncRgb(shape.ColorAdaxial);
        var cBot = EncRgb(shape.ColorAbaxial);
        double roughAlpha = ShapeRoughAlpha(shape);
        double k = shape.MidribCurl;
        double thickScale = Math.Clamp(shape.LengthToWidth > 0 ? 0.26 : 0.26, 0.14, 0.36);

        var rings = new int[rows + 1, radial];
        var axial = new Vec3[rows + 1];
        for (int i = 0; i <= rows; i++)
        {
            double t = i / (double)rows;
            double angle = k * t;
            double zc = Math.Abs(k) < 1e-4 ? t : Math.Sin(angle) / k;
            double yc = Math.Abs(k) < 1e-4 ? 0.0 : (1.0 - Math.Cos(angle)) / k * 0.22; // upturned rosette blade curve
            double w = Math.Max(0.002, WidthAt(t));
            if (i == 0) w = Math.Max(w, 0.18);
            if (i == rows) w = 0.002;
            // Fleshy basal parenchyma thickening smoothly toward a thin sharp margin/apex.
            double th = thickScale * Math.Max(0.04, Math.Sin(Math.PI * Math.Pow(t, 0.68)) * (1.0 - 0.35 * t));
            if (i == 0) th = thickScale * 0.42;
            if (i == rows) th = 0.002;
            axial[i] = new Vec3(0, yc, zc);

            double basalAo = 0.52 + 0.48 * Math.Pow(t, 0.55);
            double tipFlush = Math.Pow(Math.Clamp((t - 0.55) / 0.45, 0.0, 1.0), 1.4) * 0.60;

            for (int j = 0; j < radial; j++)
            {
                double theta = j * Math.Tau / radial;
                double cx = Math.Cos(theta);
                double sx = Math.Sin(theta);
                double x = w * cx;
                // Gentle concavity on adaxial face, deep keeled parenchyma ridge on abaxial face.
                double cupY = sx > 0 ? shape.CrossCurl * 0.65 * (cx * cx) : 0.0;
                double y = yc + cupY + (sx >= 0 ? th * 0.48 * sx : th * 0.98 * sx);
                double abMix = Math.Max(tipFlush, Math.Clamp(-sx, 0.0, 1.0) * 0.65);
                var col = Mix(cTop, cBot, abMix);
                col[0] *= basalAo; col[1] *= basalAo; col[2] *= basalAo;
                rings[i, j] = mesh.AddVertex(new Vec3(x, y, zc), new Vec3(cx, sx, 0),
                    col[0], col[1], col[2], roughAlpha, t, 0.5 + 0.5 * cx, 0, 1);
            }
        }

        for (int i = 0; i < rows; i++)
        for (int j = 0; j < radial; j++)
        {
            int n = (j + 1) % radial;
            int a = rings[i, j], b = rings[i, n], c = rings[i + 1, j], d = rings[i + 1, n];
            mesh.AddTriangle(a, c, b);
            mesh.AddTriangle(b, c, d);
        }

        foreach (int row in new[] { 0, rows })
        {
            double ao = row == 0 ? 0.52 : 1.0;
            var col = row == 0 ? cTop : Mix(cTop, cBot, 0.65);
            int center = mesh.AddVertex(axial[row], row == 0 ? -Vec3.Forward : Vec3.Forward,
                col[0] * ao, col[1] * ao, col[2] * ao, roughAlpha, row == 0 ? 0.0 : 1.0, 0.5, 0, 1);
            for (int j = 0; j < radial; j++)
            {
                int n = (j + 1) % radial;
                if (row == 0) mesh.AddTriangle(center, rings[row, j], rings[row, n]);
                else mesh.AddTriangle(center, rings[row, n], rings[row, j]);
            }
        }

        mesh.RecomputeNormals();
        for (int i = 0; i < mesh.Normals.Count; i++) mesh.Normals[i] = -mesh.Normals[i];
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            if (mesh.NormalAt(i).LengthSq < 0.25)
            {
                var p = mesh.Position(i);
                var fallback = PlantMath.SafeNormal(new Vec3(p.X, p.Y, p.Z < 0.5 ? -1 : 1), Vec3.Up);
                mesh.Normals[i * 3] = (float)fallback.X;
                mesh.Normals[i * 3 + 1] = (float)fallback.Y;
                mesh.Normals[i * 3 + 2] = (float)fallback.Z;
            }
        }
        return mesh;
    }
}
