using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Vivarium.Sim.Content;

namespace Vivarium.Game.App;

public partial class SpeciesIsolationReview : Node3D
{
    public ContentLibrary Content { get; set; } = null!;
    public string OutDir { get; set; } = "";
    public override void _Ready() => _=RunAsync();
    private async Task RunAsync()
    {
        var results=new List<object>(); int failures=0;
        try
        {
            Directory.CreateDirectory(OutDir);
            string? requested=Main.ArgAfter("--isolation-species");
            var species=Content.Flora.Where(s=>requested==null || requested.Split(',').Contains(s.Id)).ToArray();
            if(species.Length==0) throw new ArgumentException("No isolated species selected.");
            foreach(var sp in species.OrderBy(s=>s.Id=="blue_sundew"?0:1).ThenBy(s=>s.Id))
            {
                GD.Print("ISOLATED_SPECIES_BEGIN "+sp.Id);
                var preview=new SpecimenPreview { Content=Content, SpeciesId=sp.Id,
                    OutDir=Path.Combine(OutDir,sp.Id), BatchMode=true, CoverageProductionPath=sp.IsCoverageSpecies, GameTier=true };
                AddChild(preview);
                bool ok=await preview.Finished.Task;
                results.Add(new { species=sp.Id, coverage=sp.IsCoverageSpecies, ok });
                if(!ok) failures++;
                preview.QueueFree();
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            }
            File.WriteAllText(Path.Combine(OutDir,"isolation_report.json"),JsonSerializer.Serialize(new {
                total=results.Count,failures,results, species_scope="All Content.Flora entries, including coverage species; fauna/pilot-tree catalogue excluded", performance_acceptance=false
            },new JsonSerializerOptions{WriteIndented=true}));
            GD.Print($"ISOLATION_REVIEW_DONE species={results.Count} failures={failures}");
            GetTree().Quit(failures==0?0:1);
        }
        catch(Exception ex) { GD.PrintErr("ISOLATION_REVIEW_FAILED "+ex); GetTree().Quit(1); }
    }
}
