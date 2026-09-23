using System;
using System.Linq;
using System.Text;
using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Fauna;
using Vivarium.Sim.Flora;
using Vivarium.Sim.Genetics;
using Vivarium.Sim.Tools;
using Vivarium.Sim.World;

namespace Vivarium.Game.UI;

/// <summary>
/// Selection inspector. Stores only the selection's kind + id and re-reads authoritative state on every
/// refresh, so it can never show stale data or keep a dead entity alive.
/// </summary>
public partial class Inspector : PanelContainer
{
    public GameSession Session { get; set; } = null!;
    private WorldHit _sel = WorldHit.None;
    private Label _title = null!;
    private RichTextLabel _body = null!;
    private HBoxContainer _tabs = null!, _actions = null!;
    private string _tab = "Details";
    private Button _focus = null!, _move = null!, _remove = null!;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        CustomMinimumSize = new Vector2(330, 0);
        _title = UiKit.Label("Nothing selected", 17, UiKit.Accent);
        _title.Name = "InspectorTitle";
        _tabs = UiKit.Row(
            UiKit.Button("Tab_Details", "Details", () => SetTab("Details")),
            UiKit.Button("Tab_Genome", "Genome", () => SetTab("Genome")),
            UiKit.Button("Tab_Lineage", "Lineage", () => SetTab("Lineage")));
        _body = UiKit.Rich("InspectorBody");
        _focus = UiKit.Button("InspectorFocus", "Focus (F)", () => Session.Tools.FocusSelected());
        _move = UiKit.Button("InspectorMoveProp", "Move", () => Session.Tools.BeginMoveSelectedProp());
        _remove = UiKit.Button("InspectorRemoveProp", "Remove", () => Session.Tools.RemoveSelectedProp());
        _actions = UiKit.Row(_focus, _move, _remove);
        AddChild(UiKit.Column(_title, _tabs, _body, _actions));
        Show(WorldHit.None);
    }

    private void SetTab(string t) { _tab = t; Refresh(); }

    public void Show(WorldHit hit)
    {
        _sel = hit;
        if (hit.Kind != HitKind.Fauna) _tab = "Details";
        Refresh();
    }

    public WorldHit Current => _sel;
    public string BodyText => _body?.GetParsedText() ?? "";

    public void Refresh()
    {
        if (_body == null) return;
        var w = Session.World;
        Visible = _sel.IsHit;
        if (w == null || !_sel.IsHit) return;
        _tabs.Visible = _sel.Kind == HitKind.Fauna;
        bool isProp = _sel.Kind is HitKind.Rock or HitKind.Log or HitKind.Gravel;
        _move.Visible = _remove.Visible = isProp;
        _focus.Visible = true;
        string? text = _sel.Kind switch
        {
            HitKind.Fauna => FaunaText(w),
            HitKind.Flora => FloraText(w),
            HitKind.Rock or HitKind.Log or HitKind.Gravel => PropText(w),
            HitKind.Terrain or HitKind.Water => TerrainText(w),
            _ => null,
        };
        if (text == null)
        {
            // the subject no longer exists: clear safely instead of holding a stale reference
            _title.Text = "Selection gone";
            _body.Text = "[color=#9ab]It is no longer part of the vivarium (it died, was removed or eaten).[/color]";
            _sel = WorldHit.None;
            _actions.Visible = false;
            return;
        }
        _actions.Visible = true;
        _body.Text = text;
    }

    private string? FaunaText(VivariumWorld w)
    {
        var f = w.Fauna.Get(_sel.Id);
        if (f == null) return null;
        var sp = w.Content.FaunaOrThrow(f.SpeciesId);
        var g = w.Genomes.Get(f.GenomeId);
        var ph = w.FaunaSystem.PhenotypeOf(f);
        _title.Text = $"{sp.Name}  #{f.Id.Serial}";
        var sb = new StringBuilder();
        if (_tab == "Details")
        {
            var s = w.FaunaSystem.Suitability(sp, f.PositionXZ);
            sb.AppendLine($"[color=#9ab]{sp.Role}[/color]");
            sb.AppendLine($"Stage [b]{f.Stage}[/b] · age {f.Age / SimUnits.Day:0.0} of ~{sp.Lifespan * f.LifespanFactor / SimUnits.Day:0} days");
            sb.AppendLine($"Energy {f.Energy:0.00}  {UiKit.Bar(f.Energy / sp.MaxEnergy)}");
            sb.AppendLine($"Habitat {(s.HardRefused ? "[color=#f88]" + s.RefusalReason + "[/color]" : $"{s.Score:0.00} {UiKit.Bar(s.Score)}")}");
            sb.AppendLine($"Medium {sp.Medium} · body {ph.BodySize * 1000:0.0} mm");
            sb.AppendLine($"Offspring {f.Offspring} · generation {g?.Generation ?? 0}");
            sb.AppendLine($"Diet: {string.Join(", ", sp.Diet.Select(d => d.Resource))}");
            if (w.Clock.SimSeconds < f.DisturbedUntil) sb.AppendLine("[color=#fd8]Startled![/color]");
            if (f.Grabbed) sb.AppendLine("[color=#fd8]Being held[/color]");
            if (w.FaunaSystem.CanReproduce(f, sp, w.Clock.SimSeconds, out var why)) sb.AppendLine("Ready to breed"); else sb.AppendLine($"Breeding: {why}");
        }
        else if (_tab == "Genome")
        {
            if (g == null) return sb.Append("no genome").ToString();
            sb.AppendLine("[b]Genotype[/b] (inherited, 0–1) → [b]phenotype[/b] (what you see)");
            for (int i = 0; i < sp.Traits.Count; i++)
            {
                var def = w.Content.Genetics.Get(sp.Traits[i])!;
                string pheno = sp.Traits[i] switch
                {
                    "size" => $"{ph.BodySize * 1000:0.00} mm body",
                    "hue_shift" => $"{ph.HueShift * 360:+0;-0}° hue",
                    "ornament_density" => $"{ph.OrnamentDensity * 100:0}% markings",
                    "pattern_strength" => $"{ph.PatternStrength * 100:0}% contrast",
                    "appendage_length" => $"×{ph.AppendageScale:0.00} appendages",
                    "metabolic_efficiency" => $"×{ph.MetabolicFactor:0.00} basal energy use",
                    _ => "",
                };
                string mark = g.MutatedTrait == i ? " [color=#fd8](mutated)[/color]" : "";
                sb.AppendLine($"{def.Name,-20} {g.Traits[i]:0.000} {UiKit.Bar(g.Traits[i], 8)} → {pheno}{mark}");
            }
            sb.AppendLine($"Generation {g.Generation}");
        }
        else
        {
            var rec = w.Lineage.Get(f.Id);
            if (rec == null) return sb.Append("No lineage record.").ToString();
            sb.AppendLine(rec.ParentA.IsNone ? "Founder (introduced)" : "[b]Parents[/b]");
            foreach (var p in w.Lineage.Parents(f.Id)) sb.AppendLine("  " + Describe(w, p));
            var anc = w.Lineage.Ancestors(f.Id, 4).Where(a => a.Depth >= 2).ToList();
            if (anc.Count > 0)
            {
                sb.AppendLine("[b]Earlier ancestors[/b]");
                foreach (var (a, depth) in anc.Take(8)) sb.AppendLine($"  {new string('·', depth)} {Describe(w, a)}");
            }
            var kids = w.Lineage.Children(f.Id);
            sb.AppendLine($"[b]Offspring[/b] ({kids.Count})");
            foreach (var k in kids.Take(10)) sb.AppendLine("  " + Describe(w, k));
        }
        return sb.ToString();
    }

    private static string Describe(VivariumWorld w, LineageRecord r)
    {
        // dead ancestors are shown from their lineage record only (no entity is resurrected)
        string status = r.Alive ? "[color=#8e8]alive[/color]" : $"[color=#aaa]died day {r.DeathTick * 10 / SimUnits.Day:0.0} ({r.DeathCause})[/color]";
        var g = w.Genomes.Get(r.GenomeId);
        string size = "";
        if (g != null && w.Content.FaunaById(r.SpeciesId) is { } sp) size = $" · {Phenotype.From(sp, g).BodySize * 1000:0.0} mm";
        return $"#{r.Id.Serial} gen {r.Generation}{size} · {status}";
    }

    private string? FloraText(VivariumWorld w)
    {
        var f = w.Flora.Get(_sel.Id);
        if (f == null) return null;
        var sp = w.Content.FloraOrThrow(f.SpeciesId);
        _title.Text = $"{sp.Name}  #{f.Id.Serial}";
        var s = w.FloraSystem.Suitability(sp, f.Position, f.Id);
        var sb = new StringBuilder();
        sb.AppendLine($"[color=#9ab]{sp.Archetype} · {sp.Role}[/color]");
        sb.AppendLine($"Stage [b]{f.Stage(sp)}[/b] · age {f.Age / SimUnits.Day:0.0} days");
        sb.AppendLine($"Biomass {f.Biomass:0.000} ({f.BiomassFraction(sp) * 100:0}% of max) {UiKit.Bar(f.BiomassFraction(sp))}");
        sb.AppendLine($"Health {f.Health:0.00} · spread {f.Radius(sp) * 100:0} cm");
        if (s.HardRefused) sb.AppendLine($"[color=#f88]Habitat refused: {s.RefusalReason}[/color]");
        else
        {
            sb.AppendLine($"[b]Suitability {s.Score:0.00}[/b] {UiKit.Bar(s.Score)}");
            sb.AppendLine($"  substrate {SubstrateIds.Id(s.Substrate)} → {s.SubstrateFactor:0.00}");
            sb.AppendLine($"  moisture {s.Moisture:0.00} → {s.MoistureFactor:0.00}");
            sb.AppendLine($"  light {s.Light:0.00} → {s.LightFactor:0.00}");
            sb.AppendLine($"  nutrients {s.Nutrients:0.00} → {s.NutrientFactor:0.00}");
            if (s.ProximityBonus > 0) sb.AppendLine($"  neighbour bonus +{s.ProximityBonus:0.00}");
        }
        sb.AppendLine($"[color=#9ab]{sp.Description}[/color]");
        return sb.ToString();
    }

    private string? PropText(VivariumWorld w)
    {
        var p = w.Props.Find(_sel.Id);
        switch (p)
        {
            case Rock r:
                _title.Text = $"Rock #{r.Id.Serial}";
                return $"Size {r.SizeX * 200:0} × {r.SizeZ * 200:0} × {r.SizeY * 200:0} cm\nSubstrate: rock (lichens, cushion moss)\nVariant {r.VariantSeed % 10000}";
            case LogProp l:
                _title.Text = $"Fallen log #{l.Id.Serial}";
                string[] decay = { "fresh bark", "weathered bark", "softening wood", "rotting wood" };
                return $"Length {l.Length:0.00} m · Ø {l.Radius * 200:0} cm\nDecay: {decay[Math.Clamp(l.DecayClass, 0, 3)]}\nHabitat tags: {string.Join(", ", l.HabitatTags)}";
            case GravelPatch g:
                _title.Text = $"Gravel patch #{g.Id.Serial}";
                return $"Radius {g.Radius:0.00} m\nSubstrate: gravel (drains fast, suits lichens)";
            default: return null;
        }
    }

    private string TerrainText(VivariumWorld w)
    {
        var p = _sel.Point.XZ;
        _title.Text = _sel.Kind == HitKind.Water ? "Water" : "Ground";
        var layer = w.Strata.Layers[0];
        return $"Position ({p.X:0.00}, {p.Z:0.00}) · elevation {w.SurfaceHeight(p):0.00} m\nSubstrate {SubstrateIds.Id(w.SubstrateAt(p))}\nSurface stratum: {layer.Name}\nWater depth {w.Water.DepthAt(p) * 100:0.0} cm";
    }
}
