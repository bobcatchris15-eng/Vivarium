using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Vivarium.Sim.Persistence;

namespace Vivarium.Game.App;

public partial class SmokeRunner
{
    private async Task AuditPerformanceControlsAsync()
    {
        bool wasPaused = W.Clock.Paused, uiVisible = Session.Ui.Visible;
        bool sessionProcessing = Session.IsProcessing();
        W.Clock.Paused = true;
        Session.SetProcess(false);
        W.Scheduler.CompletePendingTick(); W.Liquid.CompletePending();
        string digest = WorldSerializer.Digest(W);
        var s = Session.Settings;
        string settingsBefore = JsonSerializer.Serialize(s);
        async Task Toggle(string name, Func<bool> applied)
        {
            var check = Session.Ui.Windows.FindChild(name, true, false) as CheckBox
                ?? throw new InvalidOperationException("Missing rendering control " + name);
            bool original = check.ButtonPressed;
            try
            {
                check.ButtonPressed = !original;
                await Frames(2);
                Check(name + " applies", applied() == !original);
            }
            finally { check.ButtonPressed = original; }
            await Frames(2);
            Check(name + " restores", applied() == original);
        }
        try
        {
            var originalEye = Session.CameraRig.GlobalPosition;
            var originalTarget = originalEye - Session.CameraRig.GlobalBasis.Z * 3;
            var triangleAudit = new System.Collections.Generic.List<object>();
            foreach (var pose in BuildReferenceScenes().Where(p => p.Name is "overview" or "ground_vegetation" or "water_margin" or "dense_colony"))
            {
                Session.CameraRig.LookAtPoint(pose.Eye, pose.Target);
                await Frames(2);
                Session.Flora.Rebuild(resetDetailHistory: true);
                long managed = Session.Flora.TrianglesDrawn, native = Session.Flora.AuditPublishedTriangles();
                Check("flora published triangle count matches native metadata: " + pose.Name, managed >= 0 && managed == native);
                triangleAudit.Add(new { scene = pose.Name, managed_triangles = managed, native_triangles = native });
            }
            File.WriteAllText(Path.Combine(OutDir, "flora-triangle-audit.json"), JsonSerializer.Serialize(triangleAudit));
            Session.CameraRig.LookAtPoint(originalEye, originalTarget);
            await Frames(2); Session.Flora.Rebuild(resetDetailHistory: true);
            var waterAudit = Session.WaterFluid.AuditPublishedTextures();
            Check("water texture payload and tile halos match authoritative snapshot", waterAudit.Tiles > 0 && waterAudit.Mismatches == 0);
            var mesh = new Vivarium.Sim.Geometry.MeshData();
            for (int z = 0; z <= 48; z++)
            for (int x = 0; x <= 64; x++)
                mesh.AddVertex(new(x * .01, Math.Sin(x * .1) * .001, z * .01), new(0, 1, 0), .2, .4, .1, 1, x / 64.0, z / 48.0);
            for (int z = 0; z < 48; z++)
            for (int x = 0; x < 64; x++)
            {
                int a = z * 65 + x;
                mesh.Indices.AddRange(new[] { a, a + 65, a + 1, a + 1, a + 65, a + 66 });
            }
            var expected = Bridge.PackMesh(mesh);
            Bridge.PackedMesh actual = null!;
            int packYields = 0;
            foreach (bool _ in Bridge.PackMeshBatched(mesh, result => actual = result)) { packYields++; await Frames(1); }
            Check("batched mesh packing exactly matches reference", expected.Vertices.SequenceEqual(actual.Vertices) && expected.Normals.SequenceEqual(actual.Normals) && expected.Colors.SequenceEqual(actual.Colors) && expected.UV.SequenceEqual(actual.UV) && expected.UV2.SequenceEqual(actual.UV2) && packYields > 1);
            var expectedTangents = Bridge.GenerateTangents(expected.Vertices, expected.Normals, expected.UV, mesh.Indices, mesh.VertexCount);
            float[] actualTangents = null!;
            int tangentYields = 0;
            foreach (bool _ in Bridge.GenerateTangentsBatched(actual.Vertices, actual.Normals, actual.UV, mesh.Indices, mesh.VertexCount, result => actualTangents = result)) { tangentYields++; await Frames(1); }
            Check("batched tangents exactly match reference", expectedTangents.SequenceEqual(actualTangents) && tangentYields > 1);
            using (var native = Bridge.CreateNativeArrays(actual, actualTangents, mesh.Indices))
            using (var actualMesh = new ArrayMesh())
            using (var expectedMesh = Bridge.ToArrayMesh(mesh))
            {
                actualMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, native);
                using var expectedArrays = expectedMesh.SurfaceGetArrays(0);
                using var actualArrays = actualMesh.SurfaceGetArrays(0);
                Check("prepared native mesh arrays exactly match reference", GD.VarToBytes(expectedArrays).SequenceEqual(GD.VarToBytes(actualArrays)));
            }
            await Toggle("DynamicShadowsCheck", () => Session.EnvRig.Sun.ShadowEnabled);
            await Toggle("GlobalIlluminationCheck", () => Session.EnvRig.Env.SdfgiEnabled);
            await Toggle("FineIndirectLightingCheck", () => Session.EnvRig.Env.SdfgiCascades == 4
                && Mathf.IsEqualApprox(Session.EnvRig.Env.SdfgiMinCellSize, .025f));
            Check("indirect lighting detail keeps the same reach", Mathf.IsEqualApprox(Session.EnvRig.Env.SdfgiMaxDistance, 25.6f));
            Check("indirect lighting layout follows restored preference", Session.EnvRig.Env.SdfgiCascades == (s.FineIndirectLighting ? 4 : 3)
                && Mathf.IsEqualApprox(Session.EnvRig.Env.SdfgiMinCellSize, s.FineIndirectLighting ? .025f : .05f));
            Check("missing detail preference uses efficient GI", !JsonSerializer.Deserialize<UserSettings>("{}")!.FineIndirectLighting);
            await Toggle("AmbientOcclusionCheck", () => Session.EnvRig.Env.SsaoEnabled);
            await Toggle("VolumetricMistCheck", () => Session.EnvRig.Env.VolumetricFogEnabled);
            await Toggle("ReducedDistantPlantsCheck", () => Session.Flora.ReducedDistantPlants);
            await Toggle("DistanceBlurCheck", () => Session.EnvRig.CameraAttributes.DofBlurFarEnabled);
            await Toggle("SimpleFoliageLightingCheck", () => s.SimpleFoliageLighting);
            var scale = Session.Ui.Windows.FindChild("RenderScalePicker", true, false) as OptionButton
                ?? throw new InvalidOperationException("Missing resolution control");
            int originalIndex = scale.Selected;
            try
            {
                scale.Selected = 1; scale.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
                Check("95 percent resolution UI applies", Math.Abs(GetViewport().Scaling3DScale - .95) < .001);
                scale.Selected = 2; scale.EmitSignal(OptionButton.SignalName.ItemSelected, 2L);
                Check("resolution UI applies", Math.Abs(GetViewport().Scaling3DScale - .75) < .001);
            }
            finally { scale.Selected = originalIndex; scale.EmitSignal(OptionButton.SignalName.ItemSelected, (long)originalIndex); }
            Check("render controls preserve settings", settingsBefore == JsonSerializer.Serialize(s));
            var roundTrip = JsonSerializer.Deserialize<UserSettings>(JsonSerializer.Serialize(s))!;
            Check("render settings round trip", JsonSerializer.Serialize(roundTrip) == settingsBefore);
            Check("render controls preserve ecosystem", digest == WorldSerializer.Digest(W));
            Session.Ui.Visible = true; Session.Ui.Windows.Open("Settings");
            await Frames(2); await Screenshot("performance_settings");
            Session.Ui.Windows.Close("Settings");
            File.WriteAllText(Path.Combine(OutDir, "performance-controls-audit.json"), JsonSerializer.Serialize(new { settings = s, digest_before = digest, digest_after = WorldSerializer.Digest(W) }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { W.Clock.Paused = wasPaused; Session.Ui.Visible = uiVisible; Session.ResetFrameClock(); Session.SetProcess(sessionProcessing); }
    }
}
