using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Vivarium.Game.Render;

namespace Vivarium.Game.App;

/// <summary>Offline deterministic physical-map authoring. Color is never interpreted as geometric height.</summary>
public partial class LeafMaterialBaker : Node
{
    private const int Size = 1024;
    public override void _Ready()
    {
        try
        {
            if (DisplayServer.GetName() == "headless") throw new InvalidOperationException("Run --flora-bake with a rendering device; headless cannot serialize GPU texture arrays.");
            string[] ids = Main.ArgAfter("--bake-species")?.Split(',')
                ?? new[] { "umbraheart", "kiteleaf", "glassfinger" };
            foreach (string id in ids) Bake(id);
            GD.Print("FLORA_BAKE_OK"); GetTree().Quit();
        }
        catch (Exception e) { GD.PrintErr("FLORA_BAKE_FAILED " + e); GetTree().Quit(1); }
    }

    private static void SavePool(string path, Godot.Collections.Array<Image> images)
    {
        foreach (var image in images)
        {
            image.GenerateMipmaps();
            var error = image.Compress(Image.CompressMode.Bptc);
            if (error != Error.Ok) throw new IOException($"Texture compression: {error}");
        }
        using var pool = new Texture2DArray();
        var result = pool.CreateFromImages(images);
        if (result != Error.Ok) throw new IOException($"Texture array: {result}");
        result = ResourceSaver.Save(pool, path);
        if (result != Error.Ok) throw new IOException($"Save {path}: {result}");
        foreach (var image in images) image.Dispose();
        images.Clear();
    }

    private static void Bake(string id)
    {
        var profile = FloraVisualProfile.Load(id,includeInactive:true)!;
        string root = $"res://Textures/LeafPools/{id}";
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(root));
        var colors = new Godot.Collections.Array<Image>();
        var normals = new Godot.Collections.Array<Image>();
        var physical = new Godot.Collections.Array<Image>();
        for (int variant = 0; variant < 4; variant++)
        {
            using var master = Image.LoadFromFile(ProjectSettings.GlobalizePath($"res://Textures/LeafPools/Masters/{id}-{variant}.png"));
            if (master == null || master.IsEmpty()) throw new IOException($"Missing master {id}-{variant}");
            if(profile.RotateTissue) master.Rotate90(ClockDirection.Clockwise);
            master.Resize(Size, Size, Image.Interpolation.Lanczos);
            Vector3 mean = Vector3.Zero;
            for (int y = 0; y < Size; y += 8)
                for (int x = 0; x < Size; x += 8)
                { var c = master.GetPixel(x,y).SrgbToLinear(); mean += new Vector3(c.R,c.G,c.B); }
            mean /= (Size / 8) * (Size / 8);
            Color TissuePixel(int x,int y)
            {
                // Symmetric edge crossfade makes generated pigment continuous when repeated.
                float Weight(int p) { float t=Math.Clamp(Math.Min(p,Size-1-p)/64f,0,1);return .5f*(1-t*t*(3-2*t)); }
                float wx=Weight(x),wy=Weight(y);
                if(wx==0 && wy==0) return master.GetPixel(x,y).SrgbToLinear();
                var a=master.GetPixel(x,y).SrgbToLinear().Lerp(master.GetPixel(Size-1-x,y).SrgbToLinear(),wx);
                if(wy==0) return a;
                var b=master.GetPixel(x,Size-1-y).SrgbToLinear().Lerp(master.GetPixel(Size-1-x,Size-1-y).SrgbToLinear(),wx);
                return a.Lerp(b,wy);
            }
            var color = Image.CreateEmpty(Size,Size,false,Image.Format.Rgb8);
            var normal = Image.CreateEmpty(Size,Size,false,Image.Format.Rgb8);
            var packed = Image.CreateEmpty(Size,Size,false,Image.Format.Rgb8);
            var heights = new float[Size*Size];
            for (int y=0;y<Size;y++) for (int x=0;x<Size;x++)
            {
                float u=x/(float)Size,v=y/(float)Size;
                // A restrained, periodic epidermal relief recipe, independent from generated pigment.
                int finePeriod=Math.Max(4,(int)(64*profile.MicroScale));
                float fine=Noise(u*finePeriod,v*finePeriod,variant+19,finePeriod),coarse=Noise(u*16,v*16,variant+71,16);
                float striate=.5f+.5f*MathF.Sin(v*128*MathF.PI+Noise(u*8,v*8,variant+127,8)*.35f);
                heights[y*Size+x]=Mathf.Lerp(.62f*fine+.38f*coarse,striate,profile.Striation)*profile.MicroRelief;
                var src=TissuePixel(x,y);
                var ratio=new Vector3(src.R/Math.Max(mean.X,.001f),src.G/Math.Max(mean.Y,.001f),src.B/Math.Max(mean.Z,.001f));
                ratio=Vector3.One*.45f+ratio*.55f;
                var reflectance=new Color(Math.Clamp(ratio.X*.5f,.2f,.8f),Math.Clamp(ratio.Y*.5f,.2f,.8f),Math.Clamp(ratio.Z*.5f,.2f,.8f));
                color.SetPixel(x,y,reflectance.LinearToSrgb());
                packed.SetPixel(x,y,new Color(.5f+(coarse-.5f)*.5f,.80f+(fine-.5f)*.15f,.5f+(coarse-.5f)*.12f));
            }
            WriteNormals(normal,heights,periodic:true,strength:1f);
            colors.Add(color); normals.Add(normal); physical.Add(packed);
        }
        SavePool(root+"/color.res",colors); SavePool(root+"/normal.res",normals); SavePool(root+"/physical.res",physical);
        BakeVeins(root,profile.Veins);
        GD.Print("FLORA_BAKED "+id);
    }

    private static float Noise(float x,float y,int seed,int period)
    {
        int ix=(int)MathF.Floor(x),iy=(int)MathF.Floor(y);
        float tx=x-ix,ty=y-iy; tx=tx*tx*(3-2*tx);ty=ty*ty*(3-2*ty);
        float Hash(int a,int b)
        { unchecked { uint h=(uint)(a*374761393+b*668265263+seed*1447);h=(h^(h>>13))*1274126177;return (h^(h>>16))/(float)uint.MaxValue; } }
        // Power-of-two periods allow seamless reuse at cell and pore scales.
        float a=Hash(ix%period,iy%period),b=Hash((ix+1)%period,iy%period),c=Hash(ix%period,(iy+1)%period),d=Hash((ix+1)%period,(iy+1)%period);
        return Mathf.Lerp(Mathf.Lerp(a,b,tx),Mathf.Lerp(c,d,tx),ty);
    }

    private static void WriteNormals(Image image,float[] height,bool periodic,float strength)
    {
        int At(int x,int y)=>periodic?((y+Size)%Size)*Size+(x+Size)%Size:Math.Clamp(y,0,Size-1)*Size+Math.Clamp(x,0,Size-1);
        for(int y=0;y<Size;y++)for(int x=0;x<Size;x++)
        {
            float dx=(height[At(x+1,y)]-height[At(x-1,y)])*Size*.5f*strength;
            float dy=(height[At(x,y+1)]-height[At(x,y-1)])*Size*.5f*strength;
            var n=new Vector3(-dx,-dy,1).Normalized();
            image.SetPixel(x,y,new Color(n.X*.5f+.5f,n.Y*.5f+.5f,n.Z*.5f+.5f));
        }
    }

    private static void BakeVeins(string root,string style)
    {
        bool succulent=style=="succulent";
        var heights=new float[Size*Size]; var masks=new float[Size*Size];
        // Rooted midrib, tapering secondary branches, tertiary forks, and fine cross-links.
        void Segment(Vector2 a,Vector2 b,float width,float lift)
        {
            int minX=Math.Clamp((int)((Math.Min(a.X,b.X)-width*3)*Size),0,Size-1),maxX=Math.Clamp((int)((Math.Max(a.X,b.X)+width*3)*Size),0,Size-1);
            int minY=Math.Clamp((int)((Math.Min(a.Y,b.Y)-width*3)*Size),0,Size-1),maxY=Math.Clamp((int)((Math.Max(a.Y,b.Y)+width*3)*Size),0,Size-1);
            var d=b-a;
            for(int y=minY;y<=maxY;y++)for(int x=minX;x<=maxX;x++)
            {
                var p=new Vector2(x/(float)Size,y/(float)Size);
                float t=Math.Clamp((p-a).Dot(d)/Math.Max(d.LengthSquared(),1e-8f),0,1);
                float dist=(p-(a+d*t)).Length();
                float ridge=MathF.Exp(-dist*dist/(width*width)); int i=y*Size+x;
                masks[i]=Math.Max(masks[i],ridge); heights[i]=Math.Max(heights[i],ridge*lift);
            }
        }
        for(int k=0;k<12;k++)
        {
            float u=k/12f;
            Segment(new(u,.5f),new((k+1)/12f,.5f),Mathf.Lerp(.008f,.002f,u),succulent?.0003f:.0016f);
        }
        int branches=style switch { "needle"=>0,"leathery"=>5,"slender"=>6,_=>8 };
        for(int k=1;k<=branches;k++)for(int side=-1;side<=1;side+=2)
        {
            float u=.065f+k*(style is "pinnate" or "cordate" or "succulent" ? .095f : .76f/(branches+1));var a=new Vector2(u,.5f);
            var b=new Vector2(Math.Min(.99f,u+.13f),.5f+side*.23f);
            var c=new Vector2(Math.Min(.995f,u+.22f),.5f+side*.46f);
            Segment(a,b,.0035f,succulent?.0001f:.0007f);Segment(b,c,.002f,succulent?.00008f:.00045f);
            if(succulent || style is "leathery" or "slender")continue;
            for(int j=1;j<4;j++)
            {
                var node=a.Lerp(b,j/4f);var end=node+new Vector2(.10f,side*.065f);
                Segment(node,end,.0014f,.00024f);
                if(k<8)Segment(end,end+new Vector2(.055f,-side*.045f),.0009f,.00015f);
            }
        }
        if(style=="needle")for(int side=-1;side<=1;side+=2)
            Segment(new(.03f,.5f+side*.22f),new(.99f,.5f+side*.22f),.002f,.00015f);
        if(style=="cordate")for(int side=-1;side<=1;side+=2)
        {
            // Basal ribs radiate into a cordate blade's broad lower lobes.
            Segment(new(.04f,.5f),new(.16f,.5f+side*.27f),.004f,.0009f);
            Segment(new(.16f,.5f+side*.27f),new(.27f,.5f+side*.47f),.0025f,.0005f);
        }
        using var normal=Image.CreateEmpty(Size,Size,false,Image.Format.Rgb8);
        using var mask=Image.CreateEmpty(Size,Size,false,Image.Format.Rgb8);
        WriteNormals(normal,heights,false,1f);
        for(int y=0;y<Size;y++)for(int x=0;x<Size;x++) {float v=masks[y*Size+x];mask.SetPixel(x,y,new Color(v,v,.5f));}
        normal.SavePng(ProjectSettings.GlobalizePath(root+"/vein_normal.png"));
        mask.SavePng(ProjectSettings.GlobalizePath(root+"/vein_mask.png"));
    }
}
