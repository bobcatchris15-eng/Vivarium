using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using Vivarium.Game.Render;
using Vivarium.Sim.Guardians;
using Vivarium.Sim.World;

namespace Vivarium.Game.App;

public partial class GuardianReview
{
    // Runs without a GPU. Exercise the real New World picker and production
    // renderer, not a second test-only list of UI labels or model definitions.
    private void RunSelectionCheck()
    {
        var session = new GameSession(); AddChild(session);
        session.Initialize(Content, new UserSettings { Transient = true, AutosaveEnabled = false, ShowHelpOnStart = false });
        var picker = session.Ui.FindChild("GuardianPicker", true, false) as OptionButton
            ?? throw new InvalidOperationException("New World guardian picker was not created");
        if (GuardianCatalog.All.Count != 10 || picker.ItemCount != 12)
            throw new InvalidOperationException($"Expected ten guardians + Random + None, got {picker.ItemCount} picker entries");
        var labels = Enumerable.Range(0, picker.ItemCount).Select(picker.GetItemText).ToArray();
        var treePicker = session.Ui.FindChild("PilotTreePicker", true, false) as OptionButton
            ?? throw new InvalidOperationException("Pilot tree picker was not created");
        treePicker.Select(1); // None, independently of guardian choice.
        for (int index = 0; index < GuardianCatalog.All.Count; index++)
        {
            picker.Select(index + 2);
            if (picker.GetItemText(picker.Selected) != GuardianCatalog.All[index].Name || picker.IsItemDisabled(index + 2))
                throw new InvalidOperationException("Guardian missing or disabled: " + GuardianCatalog.All[index].Id);
            var selected = session.Ui.Windows.DescriptorFromUi();
            if (selected.GuardianId != GuardianCatalog.All[index].Id || selected.PilotTreeId != "none")
                throw new InvalidOperationException("Picker selection did not reach the world descriptor");
        }
        var records = new List<object>();
        foreach (var id in new[] { GuardianCatalog.Fernback, GuardianCatalog.Dewhorn, GuardianCatalog.Hushbill, GuardianCatalog.Ruffleback })
        {
            picker.Select(GuardianCatalog.All.ToList().FindIndex(def => def.Id == id) + 2);
            var descriptor = session.Ui.Windows.DescriptorFromUi();
            var world = VivariumWorld.Create(Content, descriptor, warmupSurfaceWater: false);
            var renderer = new GuardianRenderer(); AddChild(renderer); renderer.Build(world);
            var meshes = DescendantMeshes(renderer).ToArray();
            if (world.Guardian?.ArchetypeId != id || meshes.Length < 10 || renderer.FindChild("GuardianFallback", true, false) != null)
                throw new InvalidOperationException("Guardian failed to spawn with a distinct model: " + id);
            foreach (var mesh in meshes)
            {
                var bounds = mesh.Mesh.GetAabb();
                if (!bounds.Position.IsFinite() || !bounds.Size.IsFinite())
                    throw new InvalidOperationException("Non-finite guardian geometry: " + id);
            }
            foreach (var activity in new[] { GuardianActivity.Rest, GuardianActivity.Explore, GuardianActivity.Feed })
            {
                world.Guardian!.Activity = activity;
                renderer._Process(1.0 / 30);
            }
            records.Add(new { id, meshes = meshes.Length, selected = world.Descriptor.GuardianId });
            renderer.Free();
        }
        File.WriteAllText(Path.Combine(OutDir, "guardian-selection.json"), JsonSerializer.Serialize(new
        { passed = true, labels, guardians = records, visual_acceptance = false }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("GUARDIAN_SELECTION_OK count=10 picker=12 new_models=4");
    }
}
