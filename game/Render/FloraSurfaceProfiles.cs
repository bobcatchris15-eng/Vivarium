using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.Content;

namespace Vivarium.Game.Render;

/// <summary>Render-only tissue palettes. Geometry and ecological species definitions remain independent.</summary>
internal static class FloraSurfaceProfiles
{
    internal readonly record struct LeafSurface(Vector3 Top, Vector3 Back, int Type, float Veins, float Sheen, float Transmission);

    public static void Bind(ShaderMaterial material, FloraSpeciesDef species, bool photographedLeaves = true)
    {
        if (species.Id == "plumegrass")
            material.SetShaderParameter("pampas_plume_color", new Vector3(0.84f, 0.76f, 0.59f));
        material.SetShaderParameter("ghost_membrane", species.Id == "ghostbonnet" ? 1.0f : 0.0f);
        material.SetShaderParameter("dewbonnet_gills", species.Id == "dewbonnet");
        if (species.Id == "dewbonnet")
            material.SetShaderParameter("dewbonnet_gill_normal", GD.Load<Texture2D>("res://Textures/Fungi/dewbonnet_gills_normal.png"));
        var leaf = Leaf(species.Id);
        material.SetShaderParameter("leaf_top", leaf.Top);
        material.SetShaderParameter("top_pigment", leaf.Top);
        material.SetShaderParameter("leaf_back", leaf.Back);
        material.SetShaderParameter("back_pigment", leaf.Back);
        material.SetShaderParameter("leaf_type", leaf.Type);
        // Stem epidermis families: 0 herb, 1 reed/grass, 2 fleshy, 3 woody shoot.
        int stemFamily = leaf.Type == 5 ? 1 : leaf.Type == 4 ? 2 :
            species.Woody != null || species.Climber != null ? 3 : 0;
        material.SetShaderParameter("stem_family", stemFamily);
        material.SetShaderParameter("vein_strength", leaf.Veins);
        material.SetShaderParameter("cuticle_sheen", leaf.Sheen);
        material.SetShaderParameter("leaf_roughness", Mathf.Clamp(0.70f - leaf.Sheen * 0.18f, 0.20f, 0.85f));
        material.SetShaderParameter("back_roughness", 0.82f);
        material.SetShaderParameter("leaf_transmission", leaf.Transmission);
        material.SetShaderParameter("transmission", leaf.Transmission);
        material.SetShaderParameter("leaf_pattern", LeafPattern(species.Id));
        material.SetShaderParameter("leaf_variegation", Variegation(species.Id));
        material.SetShaderParameter("pitcher_tissue", species.Id == "pitcher_plant");
        material.SetShaderParameter("single_leaf_sheets", species.Id is "pitcher_plant" or "blue_sundew"
            or "snaptrap" or "veilblade" or "hookthicket" or "kinkcane"
            or "veilfern" or "streamribbon" or "ringreed" or "frosttussock"
            or "glassrush" or "coinrunner" or "trifold" or "prismstar" or "fencomb"
            or "plumegrass"
            or "miremyrtle" or "nutglade" or "thornmantle" or "greywatch" or "silverfelt"
            or "lanternbrush" or "moonbark" or "shiverleaf" or "fenforge" or "banksorrow"
            or "mirespire" or "witchberry");
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
    internal static LeafSurface Leaf(string id) => id switch
    {
        "embercrown" => new(new(.055f, .15f, .032f), new(.09f, .19f, .055f), 0, .42f, .40f, .24f),
        "lanternbrush" => new(new(.055f, .13f, .11f), new(.09f, .20f, .12f), 0, .54f, .31f, .25f),
        "shadebell" => new(new(.025f, .095f, .048f), new(.075f, .175f, .09f), 0, .55f, .32f, .22f),
        "clinglace" => new(new(.035f, .13f, .045f), new(.11f, .17f, .08f), 3, .42f, .37f, .23f),
        "spiralvine" => new(new(.055f, .16f, .052f), new(.17f, .21f, .085f), 3, .52f, .48f, .28f),
        "fenhook" => new(new(.065f, .16f, .11f), new(.13f, .20f, .13f), 4, .20f, .47f, .17f),
        "veilfern" => new(new(.035f, .15f, .055f), new(.10f, .18f, .075f), 2, .73f, .14f, .42f),
        "frosttussock" => new(new(.115f, .16f, .145f), new(.17f, .19f, .16f), 5, .22f, .24f, .22f),
        "glassrush" => new(new(.065f, .20f, .05f), new(.09f, .21f, .065f), 5, .10f, .38f, .20f),
        "ringreed" => new(new(.045f, .17f, .06f), new(.075f, .19f, .07f), 5, .12f, .27f, .22f),
        "brooklace" => new(new(.035f, .17f, .038f), new(.07f, .20f, .05f), 0, .62f, .42f, .28f),
        "fenbead" => new(new(.09f, .17f, .065f), new(.17f, .19f, .10f), 4, .18f, .55f, .16f),
        "mirrorleaf" => new(new(.045f, .12f, .042f), new(.13f, .16f, .07f), 3, .75f, .25f, .20f),
        "glassfinger" => new(new(.112f, .178f, .138f), new(.185f, .215f, .178f), 4, .08f, .58f, .26f),
        "sunstone_rosette" => new(new(.13f, .19f, .14f), new(.19f, .22f, .16f), 4, .12f, .51f, .16f),
        // A glaucous, farina-coated blade barely transmits light: the shared chlorophyll
        // transmission tint turns a silver-sage leaf lime green, so Mooncoin keeps almost none of
        // it. It also keeps the broadleaf roughness family, because a wax-bloomed leaf is matte,
        // not wet plastic.
        "mooncoin" => new(new(.105f, .128f, .112f), new(.20f, .21f, .185f), 0, .34f, .18f, .04f),
        "coinrunner" => new(new(.05f, .19f, .047f), new(.13f, .20f, .08f), 0, .40f, .16f, .18f),
        "trifold" => new(new(.075f, .155f, .058f), new(.135f, .185f, .082f), 0, .30f, .20f, .32f),
        "prismstar" => new(new(.055f, .18f, .05f), new(.085f, .21f, .065f), 0, .59f, .27f, .32f),
        "streamribbon" => new(new(.045f, .17f, .05f), new(.08f, .22f, .065f), 0, .45f, .38f, .48f),
        "fencomb" => new(new(.055f, .14f, .06f), new(.13f, .18f, .085f), 1, .30f, .32f, .42f),
        "veilblade" => new(new(.05f, .20f, .055f), new(.075f, .22f, .06f), 5, .18f, .30f, .34f),
        "kinkcane" => new(new(.055f, .21f, .04f), new(.08f, .23f, .055f), 5, .20f, .30f, .32f),
        "hookthicket" => new(new(.026f, .088f, .052f), new(.092f, .120f, .115f), 0, .48f, .16f, .34f),
        "blue_sundew" => new(new(.055f, .20f, .23f), new(.10f, .23f, .27f), 0, .22f, .48f, .28f),
        "snaptrap" => new(new(.06f, .20f, .042f), new(.09f, .22f, .06f), 0, .35f, .32f, .22f),
        // Plumegrass is a grass: parallel striations, a glaucous blade, and enough
        // transmission to read as a thin sunlit blade rather than a painted card.
        "plumegrass" => new(new(.082f, .112f, .066f), new(.132f, .144f, .090f), 5, .13f, .08f, .28f),
        // A cycad pinna is firm and leathery, not a soft broad leaf: a hard cuticle, a
        // single strong midrib and almost no translucency.
        "vaultfern" => new(new(.030f, .058f, .044f), new(.056f, .080f, .058f), 0, .20f, .26f, .07f),
        // These species previously shared the generic green despite distinct authored identities.
        // Reflectance values are linear, matching the other leaf targets above.
        "moonfern" => new(new(.032f, .093f, .105f), new(.075f, .145f, .155f), 2, .62f, .16f, .32f),
        "glowbriar" => new(new(.027f, .085f, .082f), new(.063f, .135f, .123f), 0, .42f, .28f, .22f),
        "rainshield" => new(new(.025f, .105f, .080f), new(.065f, .155f, .112f), 3, .48f, .37f, .24f),
        "cragparasol" => new(new(.095f, .115f, .035f), new(.145f, .16f, .065f), 3, .35f, .22f, .12f),
        "hollowcrown" => new(new(.027f, .058f, .027f), new(.065f, .105f, .046f), 0, .38f, .24f, .18f),
        "lacemantle" => new(new(.034f, .100f, .060f), new(.075f, .145f, .083f), 0, .50f, .25f, .26f),
        "splitcrown" => new(new(.035f, .092f, .035f), new(.085f, .142f, .065f), 0, .46f, .23f, .25f),
        "blackneedle" => new(new(.035f, .075f, .035f), new(.20f, .22f, .18f), 1, .28f, .12f, .12f),
        "coalspike" => new(new(.035f, .095f, .045f), new(.075f, .13f, .075f), 5, .16f, .22f, .18f),
        "lanternsedge" => new(new(.038f, .105f, .062f), new(.085f, .15f, .105f), 5, .22f, .24f, .22f),
        "silverweed" => new(new(.025f, .078f, .058f), new(.10f, .15f, .13f), 2, .38f, .28f, .38f),
        "glasslily" => new(new(.025f, .075f, .064f), new(.10f, .13f, .09f), 3, .40f, .48f, .17f),
        "tankbloom" => new(new(.038f, .095f, .048f), new(.095f, .135f, .075f), 3, .32f, .37f, .18f),
        "tidebell" => new(new(.038f, .095f, .080f), new(.09f, .15f, .12f), 4, .20f, .42f, .20f),
        "pitcher_plant" => new(new(.075f, .14f, .042f), new(.13f, .18f, .07f), 3, .35f, .38f, .20f),
        // --- New catalog expansion (2026-11-06): linear conversions of each species'
        // sRGB visual colour (pow 2.2, scaled into the reflectance targets above). ---
        // Ferns
        "swanfrond" => new(new(.053f, .14f, .027f), new(.127f, .20f, .089f), 2, .70f, .16f, .42f),
        "sceptrefern" => new(new(.034f, .12f, .042f), new(.115f, .19f, .087f), 2, .66f, .14f, .38f),
        "emberbracken" => new(new(.13f, .13f, .02f), new(.188f, .19f, .054f), 2, .72f, .18f, .36f),
        "mirrorstrap_fern" => new(new(.042f, .10f, .034f), new(.092f, .17f, .069f), 3, .55f, .28f, .20f),
        // Graminoids
        "quillsedge" => new(new(.067f, .13f, .038f), new(.141f, .19f, .069f), 5, .16f, .28f, .30f),
        "cloudtuft" => new(new(.107f, .14f, .055f), new(.091f, .119f, .047f), 5, .12f, .22f, .26f),
        "duskbristle" => new(new(.062f, .10f, .036f), new(.123f, .16f, .083f), 5, .18f, .24f, .24f),
        "bannerreed" => new(new(.057f, .12f, .034f), new(.133f, .19f, .069f), 5, .14f, .30f, .32f),
        "ashwire" => new(new(.122f, .16f, .204f), new(.181f, .21f, .20f), 5, .10f, .20f, .22f),
        "sootband" => new(new(.053f, .10f, .038f), new(.111f, .17f, .075f), 5, .20f, .26f, .28f),
        // Forbs
        "starveil" => new(new(.026f, .09f, .024f), new(.022f, .077f, .02f), 0, .44f, .18f, .33f),
        "butterglint" => new(new(.046f, .12f, .026f), new(.039f, .102f, .022f), 0, .40f, .34f, .28f),
        "cinderberry" => new(new(.028f, .10f, .025f), new(.024f, .085f, .021f), 0, .46f, .16f, .34f),
        "mistviolet" => new(new(.029f, .10f, .038f), new(.025f, .085f, .032f), 0, .42f, .28f, .24f),
        "hushclover" => new(new(.034f, .09f, .03f), new(.029f, .077f, .026f), 0, .45f, .20f, .36f),
        "hallowsting" => new(new(.021f, .09f, .021f), new(.105f, .20f, .075f), 0, .48f, .26f, .24f),
        "burlmantle" => new(new(.047f, .11f, .039f), new(.04f, .094f, .033f), 0, .38f, .24f, .22f),
        "heddlelace" => new(new(.046f, .12f, .04f), new(.039f, .102f, .034f), 0, .50f, .30f, .27f),
        "fenfoam" => new(new(.054f, .11f, .03f), new(.046f, .094f, .026f), 0, .44f, .32f, .26f),
        "marshkettle" => new(new(.028f, .10f, .028f), new(.024f, .085f, .024f), 0, .40f, .36f, .29f),
        "widowspire" => new(new(.054f, .11f, .03f), new(.046f, .094f, .026f), 0, .46f, .28f, .25f),
        // Succulents
        "brightknot" => new(new(.054f, .14f, .036f), new(.046f, .119f, .031f), 4, .10f, .54f, .18f),
        "cragset" => new(new(.108f, .14f, .108f), new(.092f, .119f, .092f), 4, .12f, .48f, .16f),
        // Aquatics
        "deepquill" => new(new(.021f, .09f, .051f), new(.061f, .16f, .092f), 4, .42f, .34f, .40f),
        "mirepocket" => new(new(.04f, .12f, .026f), new(.034f, .102f, .022f), 4, .44f, .38f, .42f),
        "pondstars" => new(new(.046f, .12f, .04f), new(.138f, .20f, .112f), 4, .38f, .32f, .36f),
        "bogfringe" => new(new(.046f, .12f, .04f), new(.039f, .102f, .034f), 0, .40f, .16f, .35f),
        "burrflag" => new(new(.054f, .11f, .03f), new(.137f, .16f, .049f), 5, .36f, .25f, .36f),
        "paddlerib" => new(new(.046f, .12f, .046f), new(.039f, .102f, .039f), 3, .44f, .36f, .33f),
        "barbarrow" => new(new(.046f, .12f, .04f), new(.039f, .102f, .034f), 3, .42f, .40f, .34f),
        // Climbers
        "shadewrap" => new(new(.028f, .08f, .028f), new(.089f, .14f, .059f), 3, .52f, .44f, .24f),
        "bitterbine" => new(new(.08f, .14f, .023f), new(.068f, .119f, .02f), 3, .48f, .40f, .25f),
        "trumpetbind" => new(new(.046f, .12f, .04f), new(.039f, .102f, .034f), 0, .50f, .38f, .26f),
        // Shrubs
        "miremyrtle" => new(new(.082f, .195f, .105f), new(.118f, .235f, .132f), 0, .46f, .30f, .34f),
        "nutglade" => new(new(.088f, .240f, .065f), new(.135f, .275f, .095f), 0, .54f, .28f, .36f),
        "thornmantle" => new(new(.068f, .195f, .058f), new(.112f, .230f, .085f), 0, .50f, .38f, .32f),
        "greywatch" => new(new(.078f, .185f, .195f), new(.115f, .225f, .220f), 1, .28f, .24f, .28f),
        "silverfelt" => new(new(.185f, .245f, .195f), new(.215f, .270f, .225f), 0, .38f, .18f, .36f),
        // Trees
        "moonbark" => new(new(.095f, .230f, .075f), new(.138f, .260f, .105f), 0, .46f, .30f, .35f),
        "shiverleaf" => new(new(.092f, .235f, .082f), new(.142f, .265f, .112f), 0, .48f, .34f, .36f),
        "fenforge" => new(new(.058f, .170f, .058f), new(.102f, .215f, .088f), 0, .52f, .26f, .32f),
        "banksorrow" => new(new(.088f, .210f, .108f), new(.130f, .240f, .138f), 0, .44f, .28f, .38f),
        "mirespire" => new(new(.065f, .165f, .055f), new(.110f, .205f, .085f), 1, .26f, .22f, .30f),
        "witchberry" => new(new(.068f, .180f, .068f), new(.110f, .225f, .098f), 0, .50f, .32f, .32f),
        _ => new(new(.06f, .17f, .05f), new(.09f, .20f, .06f), 0, .45f, .30f, .28f),
    };

    // Broad pigment and cuticle patterns are separate from the shared leaf silhouette.
    // 0 plain, 3 glossy ivy (1, 2 retired),
    // 4 fleshy bloom, 5 parallel-striate, 6 finely veined fern, 7 glaucous farina.
    private static int LeafPattern(string id) => id switch
    {
        "clinglace" or "spiralvine" => 3,
        "fenhook" or "glassfinger" or "sunstone_rosette" or "brightknot" or "cragset" => 4,
        "mooncoin" => 7,
        "glassrush" or "ringreed" or "frosttussock" or "streamribbon" or "veilblade" or "kinkcane"
            or "plumegrass" or "quillsedge" or "cloudtuft" or "duskbristle" or "bannerreed"
            or "ashwire" or "sootband" or "deepquill" or "mirepocket" => 5,
        "veilfern" or "moonfern" or "silverweed"
            or "swanfrond" or "sceptrefern" or "emberbracken" or "mirrorstrap_fern" => 6,
        _ => 0,
    };

    // Leaf colour patterning, matched to each species' description: 0 none, 1 pale veins,
    // 2 silver-patterned, 3 red-purple underside, 4 clover chevron watermark,
    // 5 azure crystal frost. Most species stay plain green.
    public static int Variegation(string id) => id switch
    {
        "mirrorleaf" => 1,
        "trifold" => 4,
        // Mooncoin's silvering is carried by its own vertex pigment (farina bloom, pale palmate
        // veins, bleached sector) rather than the shared silver wash, which reads as camouflage
        // on a round blade.
        "mooncoin" => 0,
        "glassfinger" => 5,
        "lanternbrush" or "snaptrap" => 3,
        _ => 0,
    };

    private readonly record struct LeafScan(string Asset, Vector4 Rect, bool Swap, Vector3 Gain, float Blend, float Normal);

    // Rectangles select clear tissue within each CC0 photographed leaf atlas.
    // UV.x follows petiole-to-tip and UV.y runs from one blade edge to the other.
    private static LeafScan? Scan(string id) => id switch
    {
        "glassfinger" => new("GlassfingerLeaf", new(.36f, .12f, -.15f, .06f), false,
            new(1.12f, 1.55f, 1.55f), .82f, .58f),
        _ => null,
    };

    internal static (string Asset, float Repeats, float Weathering, Vector3 Tint) Bark(string id) => id switch
    {
        "embercrown" => ("Bark001", 2.8f, .34f, new(.47f, .38f, .33f)),
        "lanternbrush" => ("Bark014", 3.1f, .18f, new(.48f, .38f, .35f)),
        "shadebell" => ("Bark013", 2.85f, .25f, new(.35f, .34f, .32f)),
        "clinglace" => ("Bark013", 3.0f, .14f, new(.39f, .40f, .33f)),
        "spiralvine" => ("Bark014", 2.7f, .34f, new(.52f, .45f, .35f)),
        "hookthicket" => ("Bark013", 3.2f, .22f, new(.42f, .36f, .28f)),
        "cragparasol" => ("Bark001", 2.6f, .42f, new(.40f, .34f, .30f)),
        "glowbriar" => ("Bark001", 3.0f, .28f, new(.44f, .38f, .32f)),
        "splitcrown" => ("Bark013", 2.5f, .35f, new(.62f, .64f, .56f)),
        "vaultfern" => ("Bark013", 3.2f, .22f, new(.235f, .265f, .262f)),
        "lacemantle" => ("Bark001", 2.8f, .32f, new(.36f, .32f, .29f)),
        "rainshield" => ("Bark014", 2.4f, .25f, new(.48f, .46f, .38f)),
        // --- New woody/climber species (2026-11-06 expansion) ---
        "shadewrap" => ("Bark013", 3.0f, .28f, new(.40f, .42f, .36f)),
        "bitterbine" => ("Bark014", 3.2f, .20f, new(.46f, .44f, .36f)),
        "trumpetbind" => ("Bark001", 3.4f, .30f, new(.50f, .45f, .40f)),
        "miremyrtle" => ("Bark013", 2.8f, .28f, new(.32f, .22f, .18f)),
        "nutglade" => ("Bark001", 2.8f, .24f, new(.32f, .27f, .20f)),
        "thornmantle" => ("Bark001", 3.2f, .36f, new(.22f, .19f, .17f)),
        "greywatch" => ("Bark014", 2.9f, .38f, new(.30f, .22f, .18f)),
        "silverfelt" => ("Bark013", 2.6f, .30f, new(.24f, .20f, .16f)),
        "moonbark" => ("Bark013", 2.4f, .16f, new(.42f, .39f, .36f)),
        "shiverleaf" => ("Bark013", 2.2f, .18f, new(.36f, .37f, .32f)),
        "fenforge" => ("Bark001", 2.7f, .38f, new(.13f, .12f, .11f)),
        "banksorrow" => ("Bark014", 2.5f, .32f, new(.20f, .17f, .11f)),
        "mirespire" => ("Bark001", 2.3f, .40f, new(.22f, .13f, .09f)),
        "witchberry" => ("Bark013", 2.6f, .24f, new(.18f, .15f, .16f)),
        _ => (Bridge.Surfaces.Bark, 2.5f, .30f, new(.45f, .40f, .35f)),
    };
}