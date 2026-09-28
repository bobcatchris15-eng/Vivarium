using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.Content;

namespace Vivarium.Game.Render;

/// <summary>Render-only tissue palettes. Geometry and ecological species definitions remain independent.</summary>
internal static class FloraSurfaceProfiles
{
    private readonly record struct LeafSurface(Vector3 Top, Vector3 Back, int Type, float Veins, float Sheen, float Transmission);

    public static void Bind(ShaderMaterial material, FloraSpeciesDef species, bool photographedLeaves = true)
    {
        var leaf = Leaf(species.Id);
        material.SetShaderParameter("leaf_top", leaf.Top);
        material.SetShaderParameter("leaf_back", leaf.Back);
        material.SetShaderParameter("leaf_type", leaf.Type);
        material.SetShaderParameter("vein_strength", leaf.Veins);
        material.SetShaderParameter("cuticle_sheen", leaf.Sheen);
        material.SetShaderParameter("leaf_transmission", leaf.Transmission);
        material.SetShaderParameter("leaf_pattern", LeafPattern(species.Id));
        if (photographedLeaves && Scan(species.Id) is { } scan)
        {
            material.SetShaderParameter("leaf_scan_col", GD.Load<Texture2D>($"res://Textures/Leaves/{scan.Asset}_2K-JPG_Color.jpg"));
            material.SetShaderParameter("leaf_scan_nrm", GD.Load<Texture2D>($"res://Textures/Leaves/{scan.Asset}_2K-JPG_NormalGL.jpg"));
            material.SetShaderParameter("leaf_scan_rgh", GD.Load<Texture2D>($"res://Textures/Leaves/{scan.Asset}_2K-JPG_Roughness.jpg"));
            material.SetShaderParameter("leaf_scan_rect", scan.Rect);
            material.SetShaderParameter("leaf_scan_swap", scan.Swap);
            material.SetShaderParameter("leaf_scan_gain", scan.Gain);
            material.SetShaderParameter("leaf_scan_blend", scan.Blend);
            material.SetShaderParameter("leaf_scan_normal", scan.Normal);
        }

        var (asset, repeats, weathering, tint) = Bark(species.Id);
        material.SetShaderParameter("bark_repeats_per_metre", repeats);
        material.SetShaderParameter("bark_weathering", weathering);
        material.SetShaderParameter("bark_tint", tint);
        if (species.Woody != null || species.Climber != null)
            Bridge.BindSurface(material, "bark", asset);

        material.SetShaderParameter("colony_relief", species.Archetype switch
        {
            "moss" => 0.7f,
            "lichen" => 0.38f,
            "fungus" => 0.22f,
            _ => 0f,
        });
    }

    // Type: 0 broadleaf, 1 fine needle, 2 fern, 3 waxy, 4 succulent, 5 grass/reed.
    // Values are linear-light reflectance targets, not the intentionally vivid simulation palette.
    private static LeafSurface Leaf(string id) => id switch
    {
        "ironlace" => new(new(.065f, .155f, .045f), new(.12f, .18f, .075f), 0, .76f, .28f, .25f),
        "umbraheart" => new(new(.035f, .13f, .035f), new(.14f, .18f, .075f), 0, .58f, .23f, .30f),
        "kiteleaf" => new(new(.075f, .19f, .08f), new(.16f, .21f, .16f), 0, .48f, .36f, .22f),
        "fenneedle" => new(new(.075f, .16f, .095f), new(.12f, .18f, .11f), 1, .14f, .18f, .18f),
        "embercrown" => new(new(.055f, .13f, .032f), new(.14f, .17f, .07f), 0, .42f, .40f, .24f),
        "lanternbrush" => new(new(.055f, .13f, .11f), new(.15f, .20f, .14f), 0, .54f, .31f, .25f),
        "shadebell" => new(new(.025f, .095f, .048f), new(.12f, .16f, .09f), 0, .55f, .32f, .22f),
        "clinglace" => new(new(.035f, .13f, .045f), new(.11f, .17f, .08f), 3, .42f, .37f, .23f),
        "spiralvine" => new(new(.055f, .16f, .052f), new(.17f, .21f, .085f), 3, .52f, .48f, .28f),
        "fenhook" => new(new(.065f, .16f, .11f), new(.13f, .20f, .13f), 4, .20f, .47f, .17f),
        "veilfern" => new(new(.035f, .15f, .055f), new(.10f, .18f, .075f), 2, .73f, .14f, .42f),
        "frosttussock" => new(new(.115f, .16f, .145f), new(.17f, .19f, .16f), 5, .22f, .24f, .22f),
        "glassrush" => new(new(.07f, .18f, .055f), new(.14f, .19f, .09f), 5, .10f, .38f, .12f),
        "ringreed" => new(new(.04f, .13f, .065f), new(.10f, .16f, .085f), 5, .12f, .25f, .13f),
        "brooklace" => new(new(.035f, .16f, .038f), new(.10f, .18f, .06f), 0, .62f, .42f, .28f),
        "fenbead" => new(new(.09f, .17f, .065f), new(.17f, .19f, .10f), 4, .18f, .55f, .16f),
        "mirrorleaf" => new(new(.045f, .12f, .042f), new(.13f, .16f, .07f), 3, .75f, .25f, .20f),
        "glassfinger" => new(new(.10f, .17f, .12f), new(.17f, .20f, .15f), 4, .10f, .58f, .14f),
        "sunstone_rosette" => new(new(.13f, .19f, .14f), new(.19f, .22f, .16f), 4, .12f, .51f, .16f),
        "mooncoin" => new(new(.11f, .16f, .11f), new(.22f, .23f, .18f), 3, .54f, .43f, .24f),
        "coinrunner" => new(new(.05f, .19f, .047f), new(.13f, .20f, .08f), 0, .61f, .36f, .28f),
        "trifold" => new(new(.07f, .17f, .052f), new(.15f, .20f, .085f), 0, .51f, .29f, .30f),
        "prismstar" => new(new(.055f, .17f, .055f), new(.13f, .20f, .09f), 0, .59f, .27f, .32f),
        "streamribbon" => new(new(.045f, .16f, .055f), new(.12f, .22f, .09f), 0, .45f, .38f, .48f),
        "fencomb" => new(new(.055f, .14f, .06f), new(.13f, .18f, .085f), 1, .30f, .32f, .42f),
        _ => new(new(.07f, .16f, .055f), new(.14f, .19f, .09f), 0, .45f, .30f, .25f),
    };

    // Broad pigment and cuticle patterns are separate from the shared leaf silhouette.
    // 0 plain, 1 soft catalpa, 2 glaucous two-tone, 3 glossy ivy,
    // 4 fleshy bloom, 5 parallel-striate, 6 finely veined fern.
    private static int LeafPattern(string id) => id switch
    {
        "umbraheart" => 1,
        "kiteleaf" => 2,
        "clinglace" or "spiralvine" => 3,
        "fenhook" or "glassfinger" or "sunstone_rosette" or "mooncoin" => 4,
        "fenneedle" or "glassrush" or "ringreed" or "frosttussock" or "streamribbon" => 5,
        "veilfern" => 6,
        _ => 0,
    };

    private readonly record struct LeafScan(string Asset, Vector4 Rect, bool Swap, Vector3 Gain, float Blend, float Normal);

    // Rectangles select clear tissue within each CC0 photographed leaf atlas.
    // UV.x follows petiole-to-tip and UV.y runs from one blade edge to the other.
    private static LeafScan? Scan(string id) => id switch
    {
        "umbraheart" => new("UmbraheartLeaf", new(.44f, .70f, .22f, -.20f), true,
            new(.55f, .85f, 1.10f), .78f, .70f),
        "kiteleaf" => new("KiteleafLeaf", new(.17f, .065f, -.09f, .045f), false,
            new(.85f, 1.10f, 1.25f), .78f, .48f),
        "glassfinger" => new("GlassfingerLeaf", new(.36f, .12f, -.15f, .06f), false,
            new(1.12f, 1.55f, 1.55f), .82f, .58f),
        _ => null,
    };

    private static (string Asset, float Repeats, float Weathering, Vector3 Tint) Bark(string id) => id switch
    {
        "kiteleaf" => ("Bark013", 1.55f, .30f, new(.58f, .59f, .56f)),
        "ironlace" => ("Bark001", 1.85f, .46f, new(.48f, .46f, .42f)),
        "umbraheart" => ("Bark014", 1.25f, .58f, new(.72f, .69f, .61f)),
        "fenneedle" => ("Bark014", 2.65f, .22f, new(.68f, .70f, .63f)),
        "embercrown" => ("Bark001", 2.8f, .34f, new(.47f, .38f, .33f)),
        "lanternbrush" => ("Bark014", 3.1f, .18f, new(.38f, .29f, .27f)),
        "shadebell" => ("Bark013", 2.85f, .25f, new(.35f, .34f, .32f)),
        "clinglace" => ("Bark013", 3.0f, .14f, new(.39f, .40f, .33f)),
        "spiralvine" => ("Bark014", 2.7f, .34f, new(.52f, .45f, .35f)),
        _ => (Bridge.Surfaces.Bark, 2.2f, .3f, new(.70f, .67f, .61f)),
    };
}
