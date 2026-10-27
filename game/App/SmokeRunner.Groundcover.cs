using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;

namespace Vivarium.Game.App;

public partial class SmokeRunner
{
    // Disposable texture controls on the actual production materials. The ecology
    // and terrain's own habitat texture are never modified by this review.
    private async Task CaptureGroundcoverResponse(RefScene scene)
    {
        Session.CameraRig.LookAtPoint(scene.Eye, scene.Target);
        Session.CameraRig.Cam.Transform = Transform3D.Identity;
        Session.CameraRig.Cam.LookAt(scene.Target, Vector3.Up);
        Session.AmbientGround.Invalidate();
        Session.AmbientGround._Process(1.1);
        Session.Flora._Process(.5);
        await Frames(24);
        await SettleSurfaceDetailsAsync();
        var nodes = new List<MeshInstance3D>();
        var materials = new Dictionary<ulong, ShaderMaterial>();
        void Visit(Node node)
        {
            Mesh? mesh = node is MultiMeshInstance3D mm ? mm.Multimesh?.Mesh : (node as MeshInstance3D)?.Mesh;
            if (node is MeshInstance3D mi && mesh != null) nodes.Add(mi);
            if (mesh != null)
                for (int i = 0; i < mesh.GetSurfaceCount(); i++)
                    if (mesh.SurfaceGetMaterial(i) is ShaderMaterial mat)
                        materials[mat.GetInstanceId()] = mat;
            foreach (Node child in node.GetChildren()) Visit(child);
        }
        Visit(Session.AmbientGround);
        var mats = materials.Values.ToArray();
        var originals = mats.Select(m => (Mat:m, Texture:m.GetShaderParameter("field_tex"), Rect:m.GetShaderParameter("field_rect"))).ToArray();
        ulong Id(Variant v) => v.AsGodotObject()?.GetInstanceId() ?? 0;
        bool bound = originals.Length == 2 && originals.All(x => Id(x.Texture) != 0 && x.Rect.AsVector4().Z > 0)
            && originals.Select(x => Id(x.Texture)).Distinct().Count() == 1;
        Check("turf and bank materials share the production live habitat texture", bound);
        Check("turf and bank use matte cuticle settings", mats.Length == 2 && mats.All(m =>
            m.GetShaderParameter("tissue_specular_stress").AsDouble() <= .121
            && m.GetShaderParameter("tissue_material_f0").AsDouble() <= .021));
        string Geometry() => string.Join(";", Session.AmbientGround.GetChildren().OfType<MultiMeshInstance3D>()
            .Select(n => $"{n.Multimesh.GetInstanceId()}:{n.Multimesh.InstanceCount}"))
            + string.Join(";", nodes.Select(n => n.Mesh.GetInstanceId().ToString()));
        string geometry = Geometry();
        int refresh = Session.AmbientGround.RefreshCount, bankBuilds = Session.AmbientGround.BankPatchBuildCount;
        using var img = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        img.Fill(new Color(.1f, 0, 0, .1f));
        using var tex = ImageTexture.CreateFromImage(img);
        await Screenshot("cover_production_before");
        var controls = new[] {
            (Name:"cover_dry_shade", Moisture:.08f, Light:.10f),
            (Name:"cover_dry_sun", Moisture:.08f, Light:.85f),
            (Name:"cover_wet_sun", Moisture:.85f, Light:.85f),
            (Name:"cover_wet_shade", Moisture:.85f, Light:.10f),
        };
        try
        {
            foreach (var mat in mats) mat.SetShaderParameter("field_tex", tex);
            foreach (var control in controls)
            {
                img.Fill(new Color(control.Moisture, 0, 0, control.Light));
                tex.Update(img); // Same bound texture; no mesh/instance rebuild.
                await Frames(8);
                Check(control.Name + " uses the updated shared control image", mats.All(m => Id(m.GetShaderParameter("field_tex")) == tex.GetInstanceId()));
                await Screenshot(control.Name);
            }
        }
        finally
        {
            foreach (var x in originals)
            {
                x.Mat.SetShaderParameter("field_tex", x.Texture);
                x.Mat.SetShaderParameter("field_rect", x.Rect);
            }
        }
        await Frames(8);
        await Screenshot("cover_production_restored");
        bool unchanged = geometry == Geometry() && refresh == Session.AmbientGround.RefreshCount
            && bankBuilds == Session.AmbientGround.BankPatchBuildCount;
        Check("live cover colour controls require no geometry rebuild", unchanged);
        Check("production cover habitat bindings restored", originals.All(x => Id(x.Mat.GetShaderParameter("field_tex")) == Id(x.Texture)));
        var proof = new { production_binding_valid=bound, geometry_unchanged=unchanged,
            materials=mats.Length, refresh_count=refresh, bank_patch_builds=bankBuilds,
            camera_eye=new[]{scene.Eye.X,scene.Eye.Y,scene.Eye.Z},
            controls=controls.Select(c => new {name=c.Name,moisture=c.Moisture,light=c.Light}),
            scope="Actual production foliage, controlled shared image updates; photographic/performance acceptance not claimed" };
        _facts["groundcover_response"] = proof;
        File.WriteAllText(Path.Combine(OutDir,"groundcover_response.json"), JsonSerializer.Serialize(proof, new JsonSerializerOptions{WriteIndented=true}));
    }
}
