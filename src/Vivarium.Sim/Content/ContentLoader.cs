using System.Text.Json;
using Vivarium.Sim.Core;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Content;

/// <summary>Abstracts where content text comes from (filesystem in tests, res:// pck in the game).</summary>
public interface IContentSource
{
    string Describe { get; }
    bool Exists(string relativePath);
    string ReadText(string relativePath);
}

public sealed class DirectoryContentSource : IContentSource
{
    public string Root { get; }
    public DirectoryContentSource(string root) { Root = Path.GetFullPath(root); }
    public string Describe => Root;
    public bool Exists(string p) => File.Exists(Path.Combine(Root, p));
    public string ReadText(string p) => File.ReadAllText(Path.Combine(Root, p));
}

/// <summary>In-memory source for tests (e.g. intentionally broken fixtures layered over real content).</summary>
public sealed class OverlayContentSource : IContentSource
{
    private readonly IContentSource _base;
    private readonly Dictionary<string, string?> _over = new(StringComparer.Ordinal);
    public OverlayContentSource(IContentSource baseSource) { _base = baseSource; }
    public string Describe => "overlay:" + _base.Describe;
    public OverlayContentSource Set(string path, string? text) { _over[path] = text; return this; }
    public bool Exists(string p) => _over.TryGetValue(p, out var t) ? t != null : _base.Exists(p);
    public string ReadText(string p) => _over.TryGetValue(p, out var t) ? (t ?? throw new FileNotFoundException(p)) : _base.ReadText(p);
}

public sealed class ContentLibrary
{
    public IReadOnlyDictionary<Substrate, SubstrateDef> Substrates { get; init; } = new Dictionary<Substrate, SubstrateDef>();
    public IReadOnlyList<StratumDef> Strata { get; init; } = Array.Empty<StratumDef>();
    public GeneticsConfig Genetics { get; init; } = new();
    public EcologyConfig Ecology { get; init; } = new();
    public ToolConfig Tools { get; init; } = new();
    public FloraInteractionMatrix FloraInteractions { get; init; } = new();
    /// <summary>Id-ordered (ordinal) for deterministic iteration.</summary>
    public IReadOnlyList<FloraSpeciesDef> Flora { get; init; } = Array.Empty<FloraSpeciesDef>();
    public IReadOnlyList<FaunaSpeciesDef> Fauna { get; init; } = Array.Empty<FaunaSpeciesDef>();
    public IReadOnlyDictionary<string, WorldDescriptor> Presets { get; init; } = new Dictionary<string, WorldDescriptor>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public string ContentDigest { get; init; } = "";

    private Dictionary<string, FloraSpeciesDef>? _floraById;
    private Dictionary<string, FaunaSpeciesDef>? _faunaById;

    public FloraSpeciesDef? FloraById(string id) => (_floraById ??= Flora.ToDictionary(f => f.Id)).GetValueOrDefault(id);
    public FaunaSpeciesDef? FaunaById(string id) => (_faunaById ??= Fauna.ToDictionary(f => f.Id)).GetValueOrDefault(id);
    public FloraSpeciesDef FloraOrThrow(string id) => FloraById(id) ?? throw new KeyNotFoundException($"Unknown flora species '{id}'.");
    public FaunaSpeciesDef FaunaOrThrow(string id) => FaunaById(id) ?? throw new KeyNotFoundException($"Unknown fauna species '{id}'.");
    public WorldDescriptor PresetOrThrow(string id) => Presets.TryGetValue(id, out var p) ? p.Clone() : throw new KeyNotFoundException($"Unknown world preset '{id}'.");
}

/// <summary>
/// Loads and validates every data definition listed in content/index.json. Invalid content fails with
/// a ContentValidationException naming each file and JSON path. Loading is deterministic: output order
/// depends only on file contents.
/// </summary>
public static class ContentLoader
{
    public const string IndexFile = "index.json";

    public static ContentLibrary Load(IContentSource source)
    {
        var errors = new ContentErrors();
        var warnings = new List<string>();
        var digestParts = new List<string>();

        JNode? Open(string file)
        {
            if (!source.Exists(file)) { errors.Add(file, "$", "file listed in content index does not exist"); return null; }
            string text;
            try { text = source.ReadText(file); }
            catch (Exception ex) { errors.Add(file, "$", $"could not read: {ex.Message}"); return null; }
            digestParts.Add(file + "\n" + text);
            try
            {
                var doc = JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                return new JNode(doc.RootElement.Clone(), file, "$", errors);
            }
            catch (JsonException ex) { errors.Add(file, $"$ (line {ex.LineNumber + 1})", $"invalid JSON: {ex.Message}"); return null; }
        }

        var index = Open(IndexFile);
        if (index == null) throw new ContentValidationException(errors.All);
        var ix = index.Value;
        string fSub = ix.Str("substrates"), fStrata = ix.Str("strata"), fGen = ix.Str("genetics"),
               fEco = ix.Str("ecology"), fTools = ix.Str("tools"), fInter = ix.Str("floraInteractions");
        var floraFiles = ix.StrList("flora", required: true);
        var faunaFiles = ix.StrList("fauna", required: true);
        var presetFiles = ix.StrList("presets", required: true);

        var substrates = Open(fSub) is { } ns ? ParseSubstrates(ns) : new();
        var strata = Open(fStrata) is { } nst ? ParseStrata(nst) : new();
        var genetics = Open(fGen) is { } ng ? ParseGenetics(ng) : new GeneticsConfig();
        var ecology = Open(fEco) is { } ne ? ParseEcology(ne) : new EcologyConfig();
        var tools = Open(fTools) is { } nt ? ParseTools(nt) : new ToolConfig();

        var flora = new List<FloraSpeciesDef>();
        foreach (var f in floraFiles) if (Open(f) is { } n) flora.Add(ParseFlora(n));
        var fauna = new List<FaunaSpeciesDef>();
        foreach (var f in faunaFiles) if (Open(f) is { } n) fauna.Add(ParseFauna(n));
        flora.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        fauna.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));

        var interactions = Open(fInter) is { } ni ? ParseInteractions(ni) : new FloraInteractionMatrix();

        var presets = new Dictionary<string, WorldDescriptor>(StringComparer.Ordinal);
        foreach (var f in presetFiles)
            if (Open(f) is { } n)
            {
                var p = ParsePreset(n);
                if (presets.ContainsKey(p.PresetId)) n.Error($"duplicate preset id '{p.PresetId}'");
                presets[p.PresetId] = p;
            }

        CrossValidate(errors, warnings, substrates, genetics, ecology, flora, fauna, interactions, presets, fInter);

        if (errors.Any) throw new ContentValidationException(errors.All);

        digestParts.Sort(StringComparer.Ordinal);
        var digest = Digest.Sha256Hex(string.Join("\n\u0001", digestParts));
        Log.Info(LogCategory.Content, $"Loaded content from {source.Describe}: {flora.Count} flora, {fauna.Count} fauna, {presets.Count} preset(s), digest {digest[..12]}.");
        foreach (var w in warnings) Log.Warn(LogCategory.Content, w);

        return new ContentLibrary
        {
            Substrates = substrates, Strata = strata, Genetics = genetics, Ecology = ecology, Tools = tools,
            FloraInteractions = interactions, Flora = flora, Fauna = fauna, Presets = presets,
            Warnings = warnings, ContentDigest = digest,
        };
    }

    // ------------------------------------------------------------------ parsers

    private static Dictionary<Substrate, SubstrateDef> ParseSubstrates(JNode n)
    {
        var map = new Dictionary<Substrate, SubstrateDef>();
        foreach (var s in n.Items("substrates"))
        {
            s.RejectUnknown("id", "name", "color", "description");
            string id = s.Str("id");
            if (!SubstrateIds.TryParse(id, out var sub)) { s["id"].Error($"unknown substrate '{id}' (expected {string.Join(", ", SubstrateIds.All.Select(SubstrateIds.Id))})"); continue; }
            if (map.ContainsKey(sub)) s["id"].Error($"duplicate substrate '{id}'");
            map[sub] = new SubstrateDef { Substrate = sub, Name = s.Str("name"), Color = s.Color("color"), Description = s.Str("description", "") };
        }
        foreach (var sub in SubstrateIds.All)
            if (!map.ContainsKey(sub)) n.Error($"substrate '{SubstrateIds.Id(sub)}' must be defined");
        return map;
    }

    private static List<StratumDef> ParseStrata(JNode n)
    {
        var list = new List<StratumDef>();
        foreach (var s in n.Items("layers"))
        {
            s.RejectUnknown("id", "name", "thickness", "color", "color2", "grain", "banding");
            var c = s.Color("color");
            list.Add(new StratumDef
            {
                Id = s.Str("id"), Name = s.Str("name"), Thickness = s.Num("thickness", min: 0.02, max: 10),
                Color = c, Color2 = s.Color("color2", c), Grain = s.Num("grain", 0.3, 0, 1), Banding = s.Num("banding", 0.2, 0, 1),
            });
        }
        if (list.Count < 3) n.Error("at least three strata (topsoil, subsoil, base material) are required");
        if (list.Select(l => l.Id).Distinct().Count() != list.Count) n.Error("stratum ids must be unique");
        return list;
    }

    private static GeneticsConfig ParseGenetics(JNode n)
    {
        n.RejectUnknown("mutationProbability", "traits");
        var traits = new List<TraitDef>();
        foreach (var t in n.Items("traits"))
        {
            t.RejectUnknown("id", "name", "default", "min", "max", "description", "simulationRelevant");
            double min = t.Num("min", 0, 0, 1), max = t.Num("max", 1, 0, 1);
            double def = t.Num("default", 0.5, 0, 1);
            if (max <= min) t["max"].Error("max must exceed min");
            if (def < min || def > max) t["default"].Error("default must lie within [min, max]");
            traits.Add(new TraitDef { Id = t.Str("id"), Name = t.Str("name"), Min = min, Max = max, Default = def, Description = t.Str("description", ""), SimulationRelevant = t.Bool("simulationRelevant", false) });
        }
        if (traits.Select(t => t.Id).Distinct().Count() != traits.Count) n.Error("trait ids must be unique");
        return new GeneticsConfig { MutationProbability = n.Num("mutationProbability", 0.10, 0, 1), Traits = traits };
    }

    private static EcologyConfig ParseEcology(JNode n)
    {
        var res = new List<ResourceDef>();
        foreach (var r in n.Items("resources"))
        {
            string medium = r.Str("medium", "any");
            if (medium is not ("any" or "aquatic" or "terrestrial")) r["medium"].Error("expected any | aquatic | terrestrial");
            res.Add(new ResourceDef { Id = r.Str("id"), Name = r.Str("name"), Medium = medium });
        }
        var nu = n.Req("nutrients"); var mo = n.Req("moisture"); var de = n.Req("detritus"); var bi = n.Req("biofilm"); var pl = n.Req("plankton");
        const double D = SimUnits.Day;
        return new EcologyConfig
        {
            Resources = res,
            NutrientBaseline = nu.Num("baseline", min: 0, max: 10), NutrientMax = nu.Num("max", min: 0.1, max: 100),
            NutrientRelax = nu.Num("relaxPerDay", min: 0, max: 5) / D, NutrientDiffusion = nu.Num("diffusionPerDay", min: 0, max: 20) / D,
            MoistureDrying = mo.Num("dryingPerDay", min: 0, max: 20) / D, MoistureWetting = mo.Num("wettingPerDay", min: 0, max: 200) / D,
            MoistureDiffusion = mo.Num("diffusionPerDay", min: 0, max: 20) / D, MoistureWaterTableRange = mo.Num("waterTableRange", min: 0.01, max: 5),
            MoistureDryBaseline = mo.Num("dryBaseline", min: 0, max: 1),
            DetritusDecay = de.Num("decayPerDay", min: 0, max: 5) / D, DetritusNutrientYield = de.Num("nutrientYield", min: 0, max: 1), DetritusMax = de.Num("max", min: 0.01, max: 1000),
            BiofilmGrowth = bi.Num("growthPerDay", min: 0, max: 20) / D, BiofilmCapacity = bi.Num("capacity", min: 0, max: 100), BiofilmNutrientUse = bi.Num("nutrientUse", min: 0, max: 10),
            PlanktonGrowth = pl.Num("growthPerDay", min: 0, max: 20) / D, PlanktonCapacity = pl.Num("capacity", min: 0, max: 100), PlanktonNutrientUse = pl.Num("nutrientUse", min: 0, max: 10),
        };
    }

    private static ToolConfig ParseTools(JNode n)
    {
        var nu = n.Req("nutrients"); var po = n.Req("poke"); var gr = n.Req("grab"); var ro = n.Req("rock"); var lo = n.Req("log"); var gv = n.Req("gravel"); var intro = n.Req("introduce"); var te = n.Req("terrain"); var wa = n.Req("water");
        var t = new ToolConfig
        {
            NutrientAmount = nu.Num("amount", min: 0.001, max: 10), NutrientRadius = nu.Num("radius", min: 0.05, max: 5),
            NutrientMinRadius = nu.Num("minRadius", min: 0.05, max: 5), NutrientMaxRadius = nu.Num("maxRadius", min: 0.05, max: 5),
            PokeRadius = po.Num("radius", min: 0.01, max: 3), PokeStrength = po.Num("strength", min: 0, max: 10), PokeDisturbSeconds = po.Num("disturbMinutes", min: 0.1, max: 600) * 60,
            GrabHoldHeight = gr.Num("holdHeight", min: 0, max: 3),
            RockMinScale = ro.Num("minScale", min: 0.05, max: 3), RockMaxScale = ro.Num("maxScale", min: 0.05, max: 3), PropMinSpacing = ro.Num("minSpacing", min: 0, max: 3),
            LogMinLength = lo.Num("minLength", min: 0.2, max: 6), LogMaxLength = lo.Num("maxLength", min: 0.2, max: 6), LogMinRadius = lo.Num("minRadius", min: 0.03, max: 1), LogMaxRadius = lo.Num("maxRadius", min: 0.03, max: 1),
            GravelMinRadius = gv.Num("minRadius", min: 0.1, max: 4), GravelMaxRadius = gv.Num("maxRadius", min: 0.1, max: 4),
            IntroduceFaunaCount = intro.Int("faunaCount", min: 1, max: 20),
            SculptRate = te.Num("ratePerSecond", min: 0.001, max: 2), SculptMinRadius = te.Num("minRadius", min: 0.05, max: 5), SculptMaxRadius = te.Num("maxRadius", min: 0.05, max: 5),
            PourRate = wa.Num("pourPerSecond", min: 0, max: 1), DrainRate = wa.Num("drainPerSecond", min: 0, max: 1),
            WaterMinRadius = wa.Num("minRadius", min: 0.05, max: 5), WaterMaxRadius = wa.Num("maxRadius", min: 0.05, max: 5),
            SpringDischarge = wa.Num("springLitresPerHour", min: 0, max: 1000) / 1000 / 3600, MaxSprings = wa.Int("maxSprings", min: 0, max: 32),
        };
        if (t.NutrientMinRadius > t.NutrientMaxRadius) nu["minRadius"].Error("minRadius exceeds maxRadius");
        if (t.RockMinScale > t.RockMaxScale) ro["minScale"].Error("minScale exceeds maxScale");
        if (t.LogMinLength > t.LogMaxLength) lo["minLength"].Error("minLength exceeds maxLength");
        if (t.SculptMinRadius > t.SculptMaxRadius) te["minRadius"].Error("minRadius exceeds maxRadius");
        if (t.WaterMinRadius > t.WaterMaxRadius) wa["minRadius"].Error("minRadius exceeds maxRadius");
        return t;
    }

    private static Dictionary<Substrate, double> ParseAffinity(JNode n, string field)
    {
        var raw = n.NumMap(field, 0, 1, required: true);
        var map = new Dictionary<Substrate, double>();
        foreach (var (k, v) in raw)
        {
            if (!SubstrateIds.TryParse(k, out var s)) { n[field][k].Error($"unknown substrate '{k}'"); continue; }
            map[s] = v;
        }
        return map;
    }

    private static HashSet<Substrate> ParseSubstrateSet(JNode n, string field)
    {
        var set = new HashSet<Substrate>();
        foreach (var id in n.StrList(field))
        {
            if (SubstrateIds.TryParse(id, out var s)) set.Add(s);
            else n[field].Error($"unknown substrate '{id}'");
        }
        return set;
    }

    private static Pref ParsePref(JNode n, string field)
    {
        var p = n.Req(field);
        p.RejectUnknown("optimum", "tolerance");
        return new Pref(p.Num("optimum", min: 0, max: 1), p.Num("tolerance", min: 0.01, max: 5));
    }

    private static FloraSpeciesDef ParseFlora(JNode n)
    {
        n.RejectUnknown("id", "name", "archetype", "role", "description", "habitat", "growth", "spread", "competition", "proximity", "litterFraction", "sheddingPerDay", "grazingValue", "visual", "tags");
        const double D = SimUnits.Day;
        string arch = n.Str("archetype");
        if (arch is not ("moss" or "lichen" or "plant")) n["archetype"].Error("expected moss | lichen | plant");
        var h = n.Req("habitat");
        h.RejectUnknown("substrates", "refuseSubstrates", "refuseTags", "moisture", "light", "nutrients", "maxWaterDepth", "hardMinMoisture", "minSuitability");
        var g = n.Req("growth");
        g.RejectUnknown("ratePerDay", "maxBiomass", "initialBiomass", "declinePerDay", "maturityDays", "lifespanDays", "nutrientPerBiomass", "radiusAtMax", "minRadius");
        var s = n.Req("spread");
        s.RejectUnknown("intervalDays", "radius", "propagules", "minBiomassFraction");
        var c = n.Req("competition");
        c.RejectUnknown("radius", "crowdingLimit", "sensitivity");
        var v = n.Req("visual");
        v.RejectUnknown("shape", "color", "color2", "colorVariance", "height");
        var prox = new List<ProximityRule>();
        foreach (var p in n.Items("proximity", required: false))
        {
            p.RejectUnknown("target", "radius", "bonus");
            prox.Add(new ProximityRule { Target = p.Str("target"), Radius = p.Num("radius", min: 0.01, max: 5), Bonus = p.Num("bonus", min: 0, max: 1) });
        }
        var col = v.Color("color");
        var def = new FloraSpeciesDef
        {
            Id = n.Str("id"), Name = n.Str("name"), Archetype = arch, Role = n.Str("role", ""), Description = n.Str("description", ""), SourceFile = n.File,
            SubstrateAffinity = ParseAffinity(h, "substrates"), RefuseSubstrates = ParseSubstrateSet(h, "refuseSubstrates"),
            RefuseTags = new HashSet<string>(h.StrList("refuseTags"), StringComparer.Ordinal),
            Moisture = ParsePref(h, "moisture"), Light = ParsePref(h, "light"), Nutrients = ParsePref(h, "nutrients"),
            MaxWaterDepth = h.Num("maxWaterDepth", 0, 0, 1), HardMinMoisture = h.Num("hardMinMoisture", 0, 0, 1), MinSuitability = h.Num("minSuitability", 0.15, 0, 1),
            GrowthRate = g.Num("ratePerDay", min: 0.001, max: 20) / D, MaxBiomass = g.Num("maxBiomass", min: 0.001, max: 100), InitialBiomass = g.Num("initialBiomass", min: 0.0001, max: 100),
            DeclineRate = g.Num("declinePerDay", min: 0, max: 20) / D, MaturityAge = g.Num("maturityDays", min: 0.1, max: 3650) * D, Lifespan = g.Num("lifespanDays", min: 1, max: 36500) * D,
            NutrientPerBiomass = g.Num("nutrientPerBiomass", min: 0, max: 10), RadiusAtMax = g.Num("radiusAtMax", min: 0.01, max: 3), MinRadius = g.Num("minRadius", 0.02, 0.005, 3),
            SpreadInterval = s.Num("intervalDays", min: 0.05, max: 365) * D, SpreadRadius = s.Num("radius", min: 0.01, max: 5), Propagules = s.Int("propagules", min: 0, max: 20),
            SpreadMinBiomassFraction = s.Num("minBiomassFraction", 0.5, 0, 1),
            CompetitionRadius = c.Num("radius", min: 0.01, max: 3), CrowdingLimit = c.Num("crowdingLimit", min: 0.01, max: 100), CompetitionSensitivity = c.Num("sensitivity", 1, 0, 10),
            Proximity = prox, LitterFraction = n.Num("litterFraction", 0.8, 0, 1), SheddingRate = n.Num("sheddingPerDay", 0.02, 0, 0.5) / D, GrazingValue = n.Num("grazingValue", 0, 0, 1),
            Shape = v.Str("shape"), Color = col, Color2 = v.Color("color2", col), ColorVariance = v.Num("colorVariance", 0.05, 0, 0.5), Height = v.Num("height", min: 0.001, max: 2),
            Tags = n.StrList("tags"),
        };
        if (def.InitialBiomass > def.MaxBiomass) g["initialBiomass"].Error("initialBiomass exceeds maxBiomass");
        if (def.MaturityAge >= def.Lifespan) g["maturityDays"].Error("maturityDays must be shorter than lifespanDays");
        if (def.SubstrateAffinity.Count == 0) h["substrates"].Error("at least one substrate affinity is required");
        foreach (var r in def.RefuseSubstrates)
            if (def.SubstrateAffinity.TryGetValue(r, out var a) && a > 0) h["refuseSubstrates"].Error($"substrate '{SubstrateIds.Id(r)}' is both refused and given positive affinity");
        return def;
    }

    private static FaunaSpeciesDef ParseFauna(JNode n)
    {
        n.RejectUnknown("id", "name", "medium", "role", "description", "habitat", "movement", "diet", "metabolism", "lifecycle", "reproduction", "body", "genetics", "visual", "behaviors", "schooling");
        const double D = SimUnits.Day, H = SimUnits.Hour;
        string medium = n.Str("medium");
        if (medium is not ("terrestrial" or "aquatic")) n["medium"].Error("expected terrestrial | aquatic");
        var h = n.Req("habitat"); h.RejectUnknown("moisture", "substrates", "minWaterDepth", "maxWaterDepth", "minSuitability");
        var m = n.Req("movement"); m.RejectUnknown("speedPerHour", "turnRatePerMinute", "wander", "habitatSeek", "senseRadius");
        var me = n.Req("metabolism"); me.RejectUnknown("basalPerHour", "maxEnergy", "initialEnergy", "hungerThreshold", "wasteFraction");
        var l = n.Req("lifecycle"); l.RejectUnknown("maturityDays", "lifespanDays", "lifespanVariance", "dailyMortality");
        var r = n.Req("reproduction"); r.RejectUnknown("mode", "minEnergy", "cost", "clutchMin", "clutchMax", "cooldownDays", "mateRadius", "offspringEnergy", "maxLocalDensity", "populationCap");
        var b = n.Req("body"); b.RejectUnknown("sizeMin", "sizeMax", "visualScale", "massAtMid", "detritusOnDeath");
        var ge = n.Req("genetics"); ge.RejectUnknown("traits", "mutationMagnitude", "initialVariance");
        var v = n.Req("visual"); v.RejectUnknown("model", "baseColor", "ornamentColor");
        var diet = new List<DietEntry>();
        foreach (var d in n.Items("diet"))
        {
            d.RejectUnknown("resource", "ratePerHour", "efficiency");
            diet.Add(new DietEntry { Resource = d.Str("resource"), RatePerSecond = d.Num("ratePerHour", min: 0, max: 100) / H, Efficiency = d.Num("efficiency", min: 0, max: 50) });
        }
        if (diet.Count == 0) n["diet"].Error("at least one diet entry is required");
        string mode = r.Str("mode");
        if (mode is not ("sexual" or "asexual")) r["mode"].Error("expected sexual | asexual");
        SchoolingParams? school = null;
        var behaviors = n.StrList("behaviors");
        foreach (var bh in behaviors)
            if (bh is not ("schooling" or "conglobate")) n["behaviors"].Error($"unknown behavior component '{bh}'");
        if (n.Has("schooling"))
        {
            var sc = n["schooling"]; sc.RejectUnknown("radius", "cohesion", "alignment", "separation", "separationDistance");
            school = new SchoolingParams { Radius = sc.Num("radius", min: 0.01, max: 3), Cohesion = sc.Num("cohesion", min: 0, max: 10), Alignment = sc.Num("alignment", min: 0, max: 10), Separation = sc.Num("separation", min: 0, max: 10), SeparationDistance = sc.Num("separationDistance", min: 0.001, max: 1) };
        }
        var def = new FaunaSpeciesDef
        {
            Id = n.Str("id"), Name = n.Str("name"), Medium = medium == "aquatic" ? Medium.Aquatic : Medium.Terrestrial,
            Role = n.Str("role", ""), Description = n.Str("description", ""), SourceFile = n.File,
            Moisture = ParsePref(h, "moisture"), SubstrateAffinity = ParseAffinity(h, "substrates"),
            MinWaterDepth = h.Num("minWaterDepth", 0, 0, 2), MaxWaterDepth = h.Num("maxWaterDepth", min: 0, max: 5), MinSuitability = h.Num("minSuitability", 0.1, 0, 1),
            Speed = m.Num("speedPerHour", min: 0.001, max: 100) / H, TurnRate = m.Num("turnRatePerMinute", min: 0.01, max: 100) / 60.0,
            Wander = m.Num("wander", min: 0, max: 1), HabitatSeek = m.Num("habitatSeek", min: 0, max: 1), SenseRadius = m.Num("senseRadius", min: 0.01, max: 3),
            Diet = diet,
            BasalRate = me.Num("basalPerHour", min: 0, max: 1) / H, MaxEnergy = me.Num("maxEnergy", 1, 0.1, 10), InitialEnergy = me.Num("initialEnergy", min: 0.01, max: 10),
            HungerThreshold = me.Num("hungerThreshold", min: 0, max: 1), WasteFraction = me.Num("wasteFraction", min: 0, max: 0.9),
            MaturityAge = l.Num("maturityDays", min: 0.1, max: 3650) * D, Lifespan = l.Num("lifespanDays", min: 0.5, max: 36500) * D,
            LifespanVariance = l.Num("lifespanVariance", 0.15, 0, 0.9), DailyMortality = l.Num("dailyMortality", 0, 0, 0.5),
            Sexual = mode == "sexual", ReproMinEnergy = r.Num("minEnergy", min: 0, max: 10), ReproCost = r.Num("cost", min: 0, max: 10),
            ClutchMin = r.Int("clutchMin", min: 1, max: 50), ClutchMax = r.Int("clutchMax", min: 1, max: 50), ReproCooldown = r.Num("cooldownDays", min: 0.01, max: 365) * D,
            MateRadius = r.Num("mateRadius", 0.3, 0, 5), OffspringEnergy = r.Num("offspringEnergy", min: 0.01, max: 10),
            MaxLocalDensity = r.Int("maxLocalDensity", 12, 1, 1000), PopulationCap = r.Int("populationCap", 400, 1, 20000),
            SizeMin = b.Num("sizeMin", min: 0.0001, max: 1), SizeMax = b.Num("sizeMax", min: 0.0001, max: 1), VisualScale = b.Num("visualScale", 1, 1, 100),
            MassAtMid = b.Num("massAtMid", min: 0.00001, max: 10), DetritusOnDeath = b.Num("detritusOnDeath", 1, 0, 10),
            Traits = ge.StrList("traits", required: true), MutationMagnitude = ge.Num("mutationMagnitude", min: 0, max: 1), InitialVariance = ge.Num("initialVariance", 0.08, 0, 0.5),
            Model = v.Str("model"), BaseColor = v.Color("baseColor"), OrnamentColor = v.Color("ornamentColor"),
            Behaviors = behaviors, Schooling = school,
        };
        if (def.SizeMin >= def.SizeMax) b["sizeMin"].Error("sizeMin must be smaller than sizeMax");
        if (def.ClutchMin > def.ClutchMax) r["clutchMin"].Error("clutchMin exceeds clutchMax");
        if (def.MaturityAge >= def.Lifespan) l["maturityDays"].Error("maturityDays must be shorter than lifespanDays");
        if (def.MinWaterDepth > def.MaxWaterDepth) h["minWaterDepth"].Error("minWaterDepth exceeds maxWaterDepth");
        if (def.InitialEnergy > def.MaxEnergy) me["initialEnergy"].Error("initialEnergy exceeds maxEnergy");
        if (def.Medium == Medium.Aquatic && def.MinWaterDepth <= 0) h["minWaterDepth"].Error("aquatic species need minWaterDepth > 0");
        if (def.Behaviors.Contains("schooling") && def.Schooling == null) n["schooling"].Error("behavior 'schooling' requires a schooling block");
        return def;
    }

    private static FloraInteractionMatrix ParseInteractions(JNode n)
    {
        n.RejectUnknown("neutralCompetition", "intraspecificCompetition", "relations");
        var rels = new List<FloraRelation>();
        foreach (var r in n.Items("relations"))
        {
            r.RejectUnknown("a", "b", "type", "strength", "radius", "bonus", "reason");
            string type = r.Str("type");
            var t = type switch
            {
                "compete" => FloraRelationType.Compete, "benefit" => FloraRelationType.Benefit,
                "refuse" => FloraRelationType.Refuse, "neutral" => FloraRelationType.Neutral,
                _ => FloraRelationType.Neutral,
            };
            if (type is not ("compete" or "benefit" or "refuse" or "neutral")) r["type"].Error("expected compete | benefit | refuse | neutral");
            var rel = new FloraRelation
            {
                A = r.Str("a"), B = r.Str("b"), Type = t,
                Strength = r.Num("strength", t == FloraRelationType.Compete ? null : 0, 0, 10),
                Radius = r.Num("radius", t is FloraRelationType.Benefit or FloraRelationType.Refuse ? null : 0, 0, 5),
                Bonus = r.Num("bonus", t == FloraRelationType.Benefit ? null : 0, 0, 1),
                Reason = r.Str("reason", t == FloraRelationType.Neutral ? "" : null),
            };
            rels.Add(rel);
        }
        return new FloraInteractionMatrix
        {
            NeutralCompetition = n.Num("neutralCompetition", 0.5, 0, 10),
            IntraspecificCompetition = n.Num("intraspecificCompetition", 1.0, 0, 10),
            Relations = rels,
        };
    }

    private static WorldDescriptor ParsePreset(JNode n)
    {
        n.RejectUnknown("id", "name", "seed", "diameter", "cellSize", "terrain", "water", "placement", "starterFlora", "starterFauna");
        var t = n.Req("terrain");
        t.RejectUnknown("baseHeight", "relief", "noiseScale", "octaves", "minHeight", "maxHeight", "bottom", "rockExposure", "features");
        var w = n.Req("water");
        w.RejectUnknown("waterTable", "springs", "flowRate", "evaporationPerDay", "infiltrationPerDay", "wetDepth", "boundaryDrop", "subSteps");
        var p = n.Req("placement");
        p.RejectUnknown("rocks", "rockMinScale", "rockMaxScale", "logs", "logMinLength", "logMaxLength", "logMinRadius", "logMaxRadius", "gravelPatches", "gravelMinRadius", "gravelMaxRadius", "spacing");
        var d = new WorldDescriptor
        {
            PresetId = n.Str("id"), Name = n.Str("name"),
            Seed = (ulong)n.Num("seed", min: 0, max: 9.0e15),
            Diameter = n.Num("diameter", min: WorldDescriptor.MinDiameter, max: WorldDescriptor.MaxDiameter),
            CellSize = n.Num("cellSize", 0.25, 0.1, 1.0),
            Terrain = new TerrainProfile
            {
                BaseHeight = t.Num("baseHeight", min: -2, max: 3), Relief = t.Num("relief", min: 0, max: 3), NoiseScale = t.Num("noiseScale", min: 0.01, max: 2),
                Octaves = t.Int("octaves", min: 1, max: 8), MinHeight = t.Num("minHeight", min: -3, max: 3), MaxHeight = t.Num("maxHeight", min: -2, max: 5),
                Bottom = t.Num("bottom", min: -10, max: 0), RockExposure = t.Num("rockExposure", 0.12, 0, 1),
                Features = t.Items("features", required: false).Select(f =>
                {
                    f.RejectUnknown("type", "x", "z", "toX", "toZ", "radius", "amount", "width");
                    string type = f.Str("type");
                    if (type is not ("basin" or "hill" or "channel" or "ridge")) f["type"].Error("expected basin | hill | channel | ridge");
                    return new TerrainFeature
                    {
                        Type = type, X = f.Num("x", min: -10, max: 10), Z = f.Num("z", min: -10, max: 10), ToX = f.Num("toX", 0, -10, 10), ToZ = f.Num("toZ", 0, -10, 10),
                        Radius = f.Num("radius", 2, 0.1, 10), Amount = f.Num("amount", min: -3, max: 3), Width = f.Num("width", 0.6, 0.05, 5),
                    };
                }).ToList(),
            },
            Water = new WaterConfig
            {
                WaterTable = w.Num("waterTable", min: -3, max: 3),
                FlowRate = w.Num("flowRate", 0.2, 0.01, 0.24), Evaporation = w.Num("evaporationPerDay", 0.004, 0, 1), Infiltration = w.Num("infiltrationPerDay", 0.01, 0, 1),
                WetDepth = w.Num("wetDepth", 0.008, 0.0005, 0.2), BoundaryDrop = w.Num("boundaryDrop", 0.3, 0.01, 5), SubSteps = w.Int("subSteps", 4, 1, 32),
                Springs = w.Items("springs", required: false).Select(s =>
                {
                    s.RejectUnknown("x", "z", "dischargePerHour");
                    return new SpringConfig { X = s.Num("x", min: -10, max: 10), Z = s.Num("z", min: -10, max: 10), Discharge = s.Num("dischargePerHour", min: 0, max: 10) };
                }).ToList(),
            },
            Placement = new PlacementProfile
            {
                Rocks = p.Int("rocks", min: 0, max: 200), RockMinScale = p.Num("rockMinScale", 0.2, 0.05, 3), RockMaxScale = p.Num("rockMaxScale", 0.7, 0.05, 3),
                Logs = p.Int("logs", min: 0, max: 50), LogMinLength = p.Num("logMinLength", 1.2, 0.2, 6), LogMaxLength = p.Num("logMaxLength", 2.6, 0.2, 6),
                LogMinRadius = p.Num("logMinRadius", 0.12, 0.03, 1), LogMaxRadius = p.Num("logMaxRadius", 0.25, 0.03, 1),
                GravelPatches = p.Int("gravelPatches", min: 0, max: 100), GravelMinRadius = p.Num("gravelMinRadius", 0.5, 0.1, 4), GravelMaxRadius = p.Num("gravelMaxRadius", 1.1, 0.1, 4),
                Spacing = p.Num("spacing", 0.4, 0, 3),
            },
            StarterFlora = n.Items("starterFlora", required: false).Select(s => new StarterEntry { Species = s.Str("species"), Count = s.Int("count", min: 0, max: 1000) }).ToList(),
            StarterFauna = n.Items("starterFauna", required: false).Select(s => new StarterEntry { Species = s.Str("species"), Count = s.Int("count", min: 0, max: 1000) }).ToList(),
        };
        foreach (var e in d.Validate()) n.Error(e);
        return d;
    }

    // ------------------------------------------------------------------ cross-references

    private static void CrossValidate(ContentErrors errors, List<string> warnings,
        Dictionary<Substrate, SubstrateDef> substrates, GeneticsConfig genetics, EcologyConfig ecology,
        List<FloraSpeciesDef> flora, List<FaunaSpeciesDef> fauna, FloraInteractionMatrix inter,
        Dictionary<string, WorldDescriptor> presets, string interFile)
    {
        var floraIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in flora)
            if (!floraIds.Add(f.Id)) errors.Add(f.SourceFile, "$.id", $"duplicate flora species id '{f.Id}'");
        var faunaIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in fauna)
            if (!faunaIds.Add(f.Id) || floraIds.Contains(f.Id)) errors.Add(f.SourceFile, "$.id", $"duplicate species id '{f.Id}'");

        var features = new[] { "log", "rock", "water", "gravel" };
        foreach (var f in flora)
            for (int i = 0; i < f.Proximity.Count; i++)
            {
                var p = f.Proximity[i];
                if (p.IsFeature ? Array.IndexOf(features, p.Feature) < 0 : !floraIds.Contains(p.Target))
                    errors.Add(f.SourceFile, $"$.proximity[{i}].target", $"unknown proximity target '{p.Target}' (feature:log|rock|water|gravel or a flora species id)");
            }

        var archetypes = flora.Select(f => f.Archetype).ToHashSet();
        foreach (var f in fauna)
        {
            for (int i = 0; i < f.Diet.Count; i++)
            {
                var d = f.Diet[i];
                bool ok;
                if (d.Resource.StartsWith("flora:", StringComparison.Ordinal)) ok = archetypes.Contains(d.Resource[6..]);
                else
                {
                    var res = ecology.Resources.FirstOrDefault(r => r.Id == d.Resource);
                    ok = res != null;
                    if (res != null && res.Medium == "aquatic" && f.Medium == Medium.Terrestrial)
                        errors.Add(f.SourceFile, $"$.diet[{i}].resource", $"terrestrial species cannot eat aquatic-only resource '{d.Resource}'");
                    if (res != null && res.Medium == "terrestrial" && f.Medium == Medium.Aquatic)
                        errors.Add(f.SourceFile, $"$.diet[{i}].resource", $"aquatic species cannot eat terrestrial-only resource '{d.Resource}'");
                }
                if (!ok) errors.Add(f.SourceFile, $"$.diet[{i}].resource", $"unknown diet resource '{d.Resource}' (known: {string.Join(", ", ecology.Resources.Select(r => r.Id))}, flora:<{string.Join("|", archetypes)}>)");
            }
            for (int i = 0; i < f.Traits.Count; i++)
                if (genetics.Get(f.Traits[i]) == null) errors.Add(f.SourceFile, $"$.genetics.traits[{i}]", $"unknown trait '{f.Traits[i]}'");
            if (!f.Traits.Contains("size")) errors.Add(f.SourceFile, "$.genetics.traits", "every fauna species must enable the 'size' trait");
            if (!f.Traits.Any(t => t is "ornament_density" or "hue_shift" or "pattern_strength" or "appendage_length"))
                errors.Add(f.SourceFile, "$.genetics.traits", "at least one ornamentation trait is required");
            if (f.Model is not ("springtail" or "shrimp" or "triops" or "minnow" or "isopod"))
                errors.Add(f.SourceFile, "$.visual.model", $"unknown visual model '{f.Model}' (springtail | shrimp | triops | minnow | isopod)");
        }

        for (int i = 0; i < inter.Relations.Count; i++)
        {
            var r = inter.Relations[i];
            if (!floraIds.Contains(r.A)) errors.Add(interFile, $"$.relations[{i}].a", $"unknown flora species '{r.A}'");
            if (!floraIds.Contains(r.B)) errors.Add(interFile, $"$.relations[{i}].b", $"unknown flora species '{r.B}'");
            if (r.Type != FloraRelationType.Neutral && string.IsNullOrWhiteSpace(r.Reason)) errors.Add(interFile, $"$.relations[{i}].reason", "non-neutral relations must explain themselves");
        }
        var dup = inter.Relations.GroupBy(r => (r.A, r.B)).FirstOrDefault(g => g.Count() > 1);
        if (dup != null) errors.Add(interFile, "$.relations", $"duplicate relation {dup.Key.A} -> {dup.Key.B}");

        foreach (var (id, p) in presets)
        {
            foreach (var s in p.StarterFlora) if (!floraIds.Contains(s.Species)) errors.Add($"presets/{id}", "$.starterFlora", $"unknown flora species '{s.Species}'");
            foreach (var s in p.StarterFauna) if (!faunaIds.Contains(s.Species)) errors.Add($"presets/{id}", "$.starterFauna", $"unknown fauna species '{s.Species}'");
        }
        if (!presets.ContainsKey("default")) errors.Add("index.json", "$.presets", "a preset with id 'default' is required");
        if (genetics.Get("size") == null) errors.Add("genetics", "$.traits", "trait 'size' must be defined");
    }
}
