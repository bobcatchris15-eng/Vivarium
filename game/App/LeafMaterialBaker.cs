using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Vivarium.Game.Render;

namespace Vivarium.Game.App;

/// <summary>Offline deterministic physical-map authoring. Color is never interpreted as geometric height.</summary>
public partial class LeafMaterialBaker : Node
{
    private const int Size = 2048;
    public override void _Ready()
    {
        try
        {
            if (DisplayServer.GetName() == "headless") throw new InvalidOperationException("Run --flora-bake with a rendering device; headless cannot serialize GPU texture arrays.");
            string[] ids = Main.ArgAfter("--bake-species")?.Split(',')
                ?? new[] { "glassfinger" };
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

    private static (float cellDist, float borderDist) VoronoiCell(float x, float y, int seed)
    {
        int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
        float minDist1 = 999f, minDist2 = 999f;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int cx = ix + dx, cy = iy + dy;
                unchecked
                {
                    uint h = (uint)(cx * 374761393 + cy * 668265263 + seed * 1447);
                    h = (h ^ (h >> 13)) * 1274126177;
                    float px = (h & 0xFFFF) / 65535f;
                    float py = ((h >> 16) & 0xFFFF) / 65535f;
                    float fx = cx + px * 0.7f + 0.15f - x;
                    float fy = cy + py * 0.7f + 0.15f - y;
                    float d = MathF.Sqrt(fx * fx + fy * fy);
                    if (d < minDist1) { minDist2 = minDist1; minDist1 = d; }
                    else if (d < minDist2) { minDist2 = d; }
                }
            }
        return (minDist1, minDist2 - minDist1);
    }

    private static void Bake(string id)
    {
        var profile = FloraVisualProfile.Load(id, includeInactive: true)!;
        string root = $"res://Textures/LeafPools/{id}";
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(root));
        var colors = new Godot.Collections.Array<Image>();
        var normals = new Godot.Collections.Array<Image>();
        var physical = new Godot.Collections.Array<Image>();
        for (int variant = 0; variant < 4; variant++)
        {
            using var master = Image.LoadFromFile(ProjectSettings.GlobalizePath($"res://Textures/LeafPools/Masters/{id}-{variant}.png"));
            if (master == null || master.IsEmpty()) throw new IOException($"Missing master {id}-{variant}");
            if (profile.RotateTissue) master.Rotate90(ClockDirection.Clockwise);
            master.Resize(Size, Size, Image.Interpolation.Lanczos);
            Vector3 mean = Vector3.Zero;
            for (int y = 0; y < Size; y += 8)
                for (int x = 0; x < Size; x += 8)
                { var c = master.GetPixel(x, y).SrgbToLinear(); mean += new Vector3(c.R, c.G, c.B); }
            mean /= (Size / 8) * (Size / 8);
            Color TissuePixel(int x, int y)
            {
                // Symmetric edge crossfade makes generated pigment continuous when repeated.
                float Weight(int p) { float t = Math.Clamp(Math.Min(p, Size - 1 - p) / 64f, 0, 1); return .5f * (1 - t * t * (3 - 2 * t)); }
                float wx = Weight(x), wy = Weight(y);
                if (wx == 0 && wy == 0) return master.GetPixel(x, y).SrgbToLinear();
                var a = master.GetPixel(x, y).SrgbToLinear().Lerp(master.GetPixel(Size - 1 - x, y).SrgbToLinear(), wx);
                if (wy == 0) return a;
                var b = master.GetPixel(x, Size - 1 - y).SrgbToLinear().Lerp(master.GetPixel(Size - 1 - x, Size - 1 - y).SrgbToLinear(), wx);
                return a.Lerp(b, wy);
            }
            var color = Image.CreateEmpty(Size, Size, false, Image.Format.Rgb8);
            var normal = Image.CreateEmpty(Size, Size, false, Image.Format.Rgb8);
            var packed = Image.CreateEmpty(Size, Size, false, Image.Format.Rgb8);
            var heights = new float[Size * Size];
            float areoleScale = 64f * Math.Max(0.5f, profile.MicroScale);
            for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
            {
                float u = x / (float)Size, v = y / (float)Size;
                // A restrained, periodic epidermal relief recipe, independent from generated pigment.
                int finePeriod = Math.Max(4, (int)(64 * profile.MicroScale));
                float fine = Noise(u * finePeriod, v * finePeriod, variant + 19, finePeriod);
                float coarse = Noise(u * 16, v * 16, variant + 71, 16);
                float striate = .5f + .5f * MathF.Sin(v * 128 * MathF.PI + Noise(u * 8, v * 8, variant + 127, 8) * .35f);

                // 4° Quaternary Areole Reticulation & Epidermal Cell Convex Normals
                var (cellD, borderD) = VoronoiCell(u * areoleScale, v * areoleScale, variant + 307);
                float cellDome = MathF.Max(0f, 1f - cellD * cellD * 3.2f) * profile.MicroRelief * 0.75f;
                float baseRelief = Mathf.Lerp(.62f * fine + .38f * coarse, striate, profile.Striation) * profile.MicroRelief;
                heights[y * Size + x] = baseRelief + cellDome;

                var src = TissuePixel(x, y);
                var ratio = new Vector3(src.R / Math.Max(mean.X, .001f), src.G / Math.Max(mean.Y, .001f), src.B / Math.Max(mean.Z, .001f));
                ratio = Vector3.One * .45f + ratio * .55f;
                var reflectance = new Color(Math.Clamp(ratio.X * .5f, .2f, .8f), Math.Clamp(ratio.Y * .5f, .2f, .8f), Math.Clamp(ratio.Z * .5f, .2f, .8f));
                color.SetPixel(x, y, reflectance.LinearToSrgb());

                // Bake optical thickness into tissue_physical.b (opaque sclerenchyma at cell borders, translucent mesophyll in areoles)
                float opticalThickness = Mathf.Lerp(0.32f, 0.78f, Math.Clamp(1.0f - borderD * 3.5f, 0f, 1f));
                packed.SetPixel(x, y, new Color(.5f + (coarse - .5f) * .5f, .80f + (fine - .5f) * .15f, opticalThickness));
            }
            WriteNormals(normal, heights, periodic: true, strength: 1f);
            colors.Add(color); normals.Add(normal); physical.Add(packed);
        }
        SavePool(root + "/color.res", colors); SavePool(root + "/normal.res", normals); SavePool(root + "/physical.res", physical);
        BakeVeins(root, profile.Veins);
        GD.Print("FLORA_BAKED " + id);
    }

    private static float Noise(float x, float y, int seed, int period)
    {
        int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
        float tx = x - ix, ty = y - iy; tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
        float Hash(int a, int b)
        { unchecked { uint h = (uint)(a * 374761393 + b * 668265263 + seed * 1447); h = (h ^ (h >> 13)) * 1274126177; return (h ^ (h >> 16)) / (float)uint.MaxValue; } }
        // Power-of-two periods allow seamless reuse at cell and pore scales.
        float a = Hash(ix % period, iy % period), b = Hash((ix + 1) % period, iy % period), c = Hash(ix % period, (iy + 1) % period), d = Hash((ix + 1) % period, (iy + 1) % period);
        return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
    }

    private static void WriteNormals(Image image, float[] height, bool periodic, float strength)
    {
        int At(int x, int y) => periodic ? ((y + Size) % Size) * Size + (x + Size) % Size : Math.Clamp(y, 0, Size - 1) * Size + Math.Clamp(x, 0, Size - 1);
        for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
        {
            float dx = (height[At(x + 1, y)] - height[At(x - 1, y)]) * Size * .5f * strength;
            float dy = (height[At(x, y + 1)] - height[At(x, y - 1)]) * Size * .5f * strength;
            var n = new Vector3(-dx, -dy, 1).Normalized();
            image.SetPixel(x, y, new Color(n.X * .5f + .5f, n.Y * .5f + .5f, n.Z * .5f + .5f));
        }
    }

    private static void BakeVeins(string root, string style)
    {
        bool succulent = style == "succulent";
        var heights = new float[Size * Size];
        var masks = new float[Size * Size];
        var optical = new float[Size * Size];

        void Segment(Vector2 a, Vector2 b, float width, float lift, float optThickness)
        {
            int minX = Math.Clamp((int)((Math.Min(a.X, b.X) - width * 3) * Size), 0, Size - 1);
            int maxX = Math.Clamp((int)((Math.Max(a.X, b.X) + width * 3) * Size), 0, Size - 1);
            int minY = Math.Clamp((int)((Math.Min(a.Y, b.Y) - width * 3) * Size), 0, Size - 1);
            int maxY = Math.Clamp((int)((Math.Max(a.Y, b.Y) + width * 3) * Size), 0, Size - 1);
            var d = b - a;
            float lenSq = Math.Max(d.LengthSquared(), 1e-8f);
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    var p = new Vector2(x / (float)Size, y / (float)Size);
                    float t = Math.Clamp((p - a).Dot(d) / lenSq, 0, 1);
                    float dist = (p - (a + d * t)).Length();
                    float ridge = MathF.Exp(-dist * dist / (width * width));
                    int i = y * Size + x;
                    masks[i] = Math.Max(masks[i], ridge);
                    heights[i] = Math.Max(heights[i], ridge * lift);
                    optical[i] = Math.Max(optical[i], ridge * optThickness);
                }
        }

        void Bezier(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float w0, float w1, float l0, float l1, float opt, int steps = 16)
        {
            Vector2 prev = p0;
            for (int s = 1; s <= steps; s++)
            {
                float t = s / (float)steps;
                float it = 1f - t;
                Vector2 curr = it * it * it * p0 + 3f * it * it * t * p1 + 3f * it * t * t * p2 + t * t * t * p3;
                float w = Mathf.Lerp(w0, w1, t);
                float l = Mathf.Lerp(l0, l1, t);
                Segment(prev, curr, w, l, opt);
                prev = curr;
            }
        }

        // 1° Primary Midrib (Costa): tapered central rib from petiole (u=0, v=0.5) to apex (u=1, v=0.5)
        int midribSteps = 32;
        Vector2 prevMid = new(0f, .5f);
        for (int k = 1; k <= midribSteps; k++)
        {
            float u = k / (float)midribSteps;
            float width = Mathf.Lerp(.014f, .0018f, u);
            float lift = Mathf.Lerp(succulent ? .0005f : .0028f, succulent ? .00008f : .00035f, u);
            Vector2 currMid = new(u, .5f);
            Segment(prevMid, currMid, width, lift, 1.0f);
            prevMid = currMid;
        }

        // 2° Secondary Lateral Veins: curved Bézier arches (6-15 pairs) branching at species-specific angles (42° - 72°)
        // forming brochidodromous loops near margins
        int pairs = style switch
        {
            "needle" => 0,
            "striate" or "slender" => 6,
            "leathery" => 9,
            "pitcher" => 8,
            "peltate" => 8,
            "cordate" => 14,
            "compound" => 13,
            "pinnate" => 15,
            "fenestrata" => 15,
            _ => 12
        };
        float branchAngleDeg = style switch
        {
            "cordate" => 68f,
            "peltate" => 72f,
            "needle" => 42f,
            "striate" or "slender" => 46f,
            "leathery" => 50f,
            "pitcher" => 48f,
            "pinnate" => 54f,
            "fenestrata" => 62f,
            _ => 58f
        };
        float angleRad = branchAngleDeg * MathF.PI / 180f;

        List<(Vector2[] pts, int side)> secondaryVeins = new();

        for (int k = 0; k < pairs; k++)
        {
            float uBase = .04f + .88f * (k + 0.35f) / pairs;
            float uNext = .04f + .88f * (k + 1.35f) / pairs;
            for (int side = -1; side <= 1; side += 2)
            {
                Vector2 p0 = new(uBase, .5f);
                float forwardReach = MathF.Cos(angleRad) * .22f;
                float lateralReach = MathF.Sin(angleRad) * .26f * side;
                Vector2 p1 = p0 + new Vector2(forwardReach * 0.45f, lateralReach * 0.45f);
                Vector2 p2 = p0 + new Vector2(forwardReach * 0.95f, lateralReach * 0.88f);
                // Brochidodromous loop: arches and joins the next distal lateral rib near margin
                Vector2 p3 = new(Math.Min(.98f, uNext + .05f), .5f + side * .43f);

                float w0 = .0045f, w1 = .0018f;
                float l0 = succulent ? .00018f : .0011f, l1 = succulent ? .00006f : .00038f;
                Bezier(p0, p1, p2, p3, w0, w1, l0, l1, 0.92f, 18);

                // Sample points along arch for 3° tertiary cross-veins
                int archSamples = 8;
                var pts = new Vector2[archSamples];
                for (int s = 0; s < archSamples; s++)
                {
                    float t = s / (float)(archSamples - 1);
                    float it = 1f - t;
                    pts[s] = it * it * it * p0 + 3f * it * it * t * p1 + 3f * it * t * t * p2 + t * t * t * p3;
                }
                secondaryVeins.Add((pts, side));
            }
        }

        // 3° Tertiary Percurrent Cross-Veins: ladder-like cross bridges connecting adjacent secondary veins
        if (!succulent && style != "needle" && secondaryVeins.Count >= 4)
        {
            for (int v = 0; v < secondaryVeins.Count - 2; v += 2)
            {
                for (int sideIdx = 0; sideIdx < 2; sideIdx++)
                {
                    int v0 = v + sideIdx;
                    int v1 = v + 2 + sideIdx;
                    if (v1 >= secondaryVeins.Count) continue;
                    var pts0 = secondaryVeins[v0].pts;
                    var pts1 = secondaryVeins[v1].pts;
                    int bridges = 5;
                    for (int b = 1; b <= bridges; b++)
                    {
                        float tb = b / (float)(bridges + 1);
                        int idx0 = Math.Clamp((int)(tb * pts0.Length), 0, pts0.Length - 1);
                        int idx1 = Math.Clamp((int)(tb * pts1.Length), 0, pts1.Length - 1);
                        Vector2 a = pts0[idx0];
                        Vector2 c = pts1[idx1];
                        Vector2 mid = a.Lerp(c, 0.5f) + new Vector2(0.012f, (secondaryVeins[v0].side) * 0.008f);
                        Segment(a, mid, .0013f, .00022f, 0.72f);
                        Segment(mid, c, .0012f, .00020f, 0.70f);
                    }
                }
            }
        }

        // Morphological family venation: needle, cordate, peltate, striate, pitcher, pinnate
        if (style == "needle")
        {
            for (int side = -1; side <= 1; side += 2)
                for (int rib = 1; rib <= 3; rib++)
                {
                    float vOffset = side * rib * .12f;
                    Segment(new(.02f, .5f + vOffset), new(.98f, .5f + vOffset * 0.6f), .0022f, .00028f, 0.85f);
                }
        }
        if (style == "cordate")
        {
            for (int side = -1; side <= 1; side += 2)
            {
                Bezier(new(.04f, .5f), new(.08f, .5f + side * .25f), new(.18f, .5f + side * .45f), new(.32f, .5f + side * .48f),
                    .0042f, .0022f, .00095f, .00045f, 0.94f, 16);
            }
        }
        if (style == "peltate")
        {
            Vector2 origin = new(0.14f, 0.5f);
            for (int ray = -4; ray <= 4; ray++)
            {
                if (ray == 0) continue;
                float rayAngle = ray * 22f * MathF.PI / 180f;
                Vector2 target = origin + new Vector2(MathF.Cos(rayAngle) * 0.82f, MathF.Sin(rayAngle) * 0.44f);
                target = new(Math.Clamp(target.X, 0.02f, 0.98f), Math.Clamp(target.Y, 0.05f, 0.95f));
                Vector2 c1 = origin + new Vector2(MathF.Cos(rayAngle) * 0.28f, MathF.Sin(rayAngle) * 0.15f);
                Vector2 c2 = origin + new Vector2(MathF.Cos(rayAngle) * 0.55f, MathF.Sin(rayAngle) * 0.30f);
                Bezier(origin, c1, c2, target, .0042f, .0016f, .00095f, .00030f, 0.92f, 16);
            }
        }
        if (style is "striate" or "slender")
        {
            for (int side = -1; side <= 1; side += 2)
            {
                for (int rib = 1; rib <= 5; rib++)
                {
                    float vOffset = side * rib * .085f;
                    Segment(new(.01f, .5f + vOffset), new(.99f, .5f + vOffset * 0.85f), .0020f, .00045f, 0.88f);
                    for (int bridge = 1; bridge <= 10; bridge++)
                    {
                        float ub = bridge / 11f;
                        Vector2 pA = new(ub, .5f + vOffset);
                        Vector2 pB = new(ub + 0.015f, .5f + (vOffset - side * .085f));
                        Segment(pA, pB, .0009f, .00015f, 0.65f);
                    }
                }
            }
        }
        if (style == "pitcher")
        {
            for (int rib = 1; rib <= 4; rib++)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    float vMax = .5f + side * rib * .11f;
                    Vector2 p0 = new(.02f, .5f);
                    Vector2 p1 = new(.25f, vMax);
                    Vector2 p2 = new(.75f, vMax * 0.95f);
                    Vector2 p3 = new(.98f, .5f + side * .15f);
                    Bezier(p0, p1, p2, p3, .0035f, .0014f, .00070f, .00025f, 0.88f, 16);
                }
            }
        }
        if (style == "pinnate")
        {
            for (int k = 0; k < 16; k++)
            {
                float uBase = .04f + .90f * (k + 0.25f) / 16f;
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector2 p0 = new(uBase, .5f);
                    Vector2 pEnd = new(Math.Min(.98f, uBase + .14f), .5f + side * .44f);
                    Vector2 pMid = p0.Lerp(pEnd, 0.5f) + new Vector2(0.02f, side * 0.04f);
                    Segment(p0, pMid, .0030f, .00075f, 0.90f);
                    Vector2 forkA = pEnd + new Vector2(0.03f, -side * 0.04f);
                    Vector2 forkB = pEnd + new Vector2(0.04f, side * 0.02f);
                    Segment(pMid, forkA, .0016f, .00035f, 0.75f);
                    Segment(pMid, forkB, .0016f, .00035f, 0.75f);
                }
            }
        }

        // 4° Quaternary Areole Reticulation: multi-scale Voronoi islets creating epidermal cell normals and optical thickness
        float areoleFreq = 54f;
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float u = x / (float)Size, v = y / (float)Size;
                var (cellD, borderD) = VoronoiCell(u * areoleFreq, v * areoleFreq, 839);
                float qVein = MathF.Exp(-borderD * borderD / (0.042f * 0.042f));
                int i = y * Size + x;
                masks[i] = Math.Max(masks[i], qVein * 0.32f);
                heights[i] = Math.Max(heights[i], qVein * (succulent ? 0.00003f : 0.00010f));
                float areoleOptical = Mathf.Lerp(succulent ? 0.22f : 0.30f, 0.68f, qVein);
                optical[i] = Math.Max(optical[i], areoleOptical);
            }

        using var normal = Image.CreateEmpty(Size, Size, false, Image.Format.Rgb8);
        using var mask = Image.CreateEmpty(Size, Size, false, Image.Format.Rgb8);
        WriteNormals(normal, heights, false, 1f);
        for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
        {
            int idx = y * Size + x;
            mask.SetPixel(x, y, new Color(masks[idx], masks[idx], optical[idx]));
        }
        normal.SavePng(ProjectSettings.GlobalizePath(root + "/vein_normal.png"));
        mask.SavePng(ProjectSettings.GlobalizePath(root + "/vein_mask.png"));
    }
}
