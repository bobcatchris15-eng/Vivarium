using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.Repr.Plant;
using Vivarium.Sim.Repr;

var root = args.Length > 0 ? args[0] : Path.GetFullPath("game/content");
var output = args.Length > 1 ? args[1] : Path.GetFullPath("experiments/plant_catalog_test/results.json");
var library = ContentLoader.Load(new DirectoryContentSource(root));
var errors = new List<string>();
void Check(bool ok, string msg) { if (!ok) errors.Add(msg); }
bool Fin(double v) => double.IsFinite(v);
bool Vector(Vec3 v) => Fin(v.X) && Fin(v.Y) && Fin(v.Z);
var contentIds = library.Flora.Select(x => x.Id).ToArray();
var catalogIds = PlantGraphCatalog.Species.Select(x => x.Id).ToArray();
Check(contentIds.Length == 125, $"content count {contentIds.Length}, expected 125");
Check(contentIds.Distinct(StringComparer.Ordinal).Count() == contentIds.Length, "duplicate content IDs");
Check(catalogIds.Distinct(StringComparer.Ordinal).Count() == catalogIds.Length, "duplicate catalog IDs");
foreach(var id in contentIds.Except(catalogIds,StringComparer.Ordinal)) errors.Add("missing catalog: "+id);
foreach(var id in catalogIds.Except(contentIds,StringComparer.Ordinal)) errors.Add("extra catalog: "+id);
var results = new List<object>();
var fingerprints = new Dictionary<string,List<string>>();
string Hash(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
string Q(double x) => Math.Round(x,5).ToString("G17",System.Globalization.CultureInfo.InvariantCulture);
void Mesh(MeshData mesh, string label, bool requireGeometry)
{
    Check(mesh.Positions.Count%3==0 && mesh.Normals.Count==mesh.Positions.Count, label+" vertex/normal lengths");
    Check(mesh.Indices.Count%3==0 && mesh.Indices.All(x=>x>=0 && x<mesh.VertexCount),label+" indices");
    Check(mesh.Positions.All(float.IsFinite) && mesh.Normals.All(float.IsFinite),label+" nonfinite vertex/normal");
    for(int i=0;i<mesh.VertexCount;i++) Check(mesh.NormalAt(i).LengthSq is > .25 and < 2.25,label+" nonunit normal");
    if(requireGeometry) Check(mesh.VertexCount>0 && mesh.Indices.Count>0,label+" empty mesh");
    if(mesh.VertexCount>0) { var (min,max)=mesh.Bounds(); Check(Vector(min)&&Vector(max)&&min.X<=max.X&&min.Y<=max.Y&&min.Z<=max.Z,label+" bounds"); }
}
foreach(var def in library.Flora)
{
    try
    {
        var generator=PlantGraphCatalog.Create(def);
        var info=PlantGraphCatalog.Info(def.Id);
        Check(!string.IsNullOrWhiteSpace(info.Silhouette)&&!string.IsNullOrWhiteSpace(info.GrowthHabit),def.Id+" missing authored habit");
        var instances=new List<object>();
        foreach(ulong id in new ulong[]{101,202,303})
        {
            var plan=generator.PlanFor(8675309,id);
            var d=generator.Create(8675309,id);
            int previous=0;
            Organ[] prior=Array.Empty<Organ>();
            var counts=new List<int>();
            foreach(double age in new[]{.15,.4,.7,1.0})
            {
                generator.Grow(d,age,.5,null);
                int n=d.Organs.Length; counts.Add(n);
                Check(n>=previous,$"{def.Id}/{id}/{age} organ count regressed"); previous=n;
                Check(n>0,$"{def.Id}/{id}/{age} empty");
                for(int i=0;i<Math.Min(prior.Length,n);i++)
                {
                    var before=prior[i]; var after=d.Organs[i];
                    Check(before.Parent==after.Parent && before.Type==after.Type && before.ShapeId==after.ShapeId && before.Seed==after.Seed,$"{def.Id}/{id}/{age}/{i} organ identity changed");
                    Check(after.Maturity+1e-6>=before.Maturity,$"{def.Id}/{id}/{age}/{i} maturity regressed");
                    Check((after.Start-before.Start).Length<1e-7,$"{def.Id}/{id}/{age}/{i} attachment moved");
                }
                prior=(Organ[])d.Organs.Clone();
                for(int i=0;i<n;i++)
                {
                    var o=d.Organs[i];
                    Check(o.Parent<i && o.Parent>=-1,$"{def.Id}/{id}/{age}/{i} parent order");
                    Check(Vector(o.Start)&&Vector(o.End)&&Vector(o.StartTangent)&&Vector(o.EndTangent)&&Vector(o.Up),$"{def.Id}/{id}/{age}/{i} nonfinite organ vector");
                    Check(Fin(o.RadiusStart)&&Fin(o.RadiusEnd)&&o.RadiusStart>=0&&o.RadiusEnd>=0&&Fin(o.Maturity)&&o.Maturity>=0&&o.Maturity<=1,$"{def.Id}/{id}/{age}/{i} invalid radius/maturity");
                    if(PlantCompiler.IsFlat(o.Type)) Check(o.ShapeId<d.Shapes.Length,$"{def.Id}/{id}/{age}/{i} shape index");
                }
                Check(Fin(d.Height)&&Fin(d.CrownRadius)&&d.Height>=0&&d.CrownRadius>=0,$"{def.Id}/{id}/{age} bounds");
            }
            Check(d.Organs.All(o=>o.Maturity>=.9999f),$"{def.Id}/{id} organs not mature at age 1");
            Check(d.Organs.Length==plan.Items.Count,$"{def.Id}/{id} mature count {d.Organs.Length} != plan {plan.Items.Count}");
            // These dense multistem crowns intentionally carry individually readable
            // foliage. Keep a finite bound without thinning them to fit an old template.
            int organBudget=def.Id is "fenforge" or "moonbark" or "mirespire" or "fencomb" or "deepquill" ? 10000 : 4000;
            Check(d.Organs.Length<organBudget,$"{def.Id}/{id} organ budget {d.Organs.Length} >= {organBudget}");
            var lods=new List<object>();
            for(int lod=0;lod<3;lod++)
            {
                var shapes=d.Shapes.Select(x=>PlantCompiler.BuildShapeMesh(x,lod)).ToArray();
                for(int j=0;j<shapes.Length;j++) Mesh(shapes[j],$"{def.Id}/{id}/lod{lod}/shape{j}",true);
                var assembly=PlantCompiler.Compile(d,lod);
                Mesh(assembly.Wood,$"{def.Id}/{id}/lod{lod}/wood",false);
                Check(assembly.Instances.All(x=>x.ShapeId<shapes.Length),$"{def.Id}/{id}/lod{lod} instance shape");
                var flat=PlantCompiler.Flatten(d,assembly,shapes);
                Mesh(flat,$"{def.Id}/{id}/lod{lod}/flatten",true);
                var bounds=flat.Bounds();
                lods.Add(new { lod, vertices=flat.VertexCount, triangles=flat.TriangleCount, boundsMin=new[]{bounds.Min.X,bounds.Min.Y,bounds.Min.Z}, boundsMax=new[]{bounds.Max.X,bounds.Max.Y,bounds.Max.Z} });
            }
            // Excludes identity and color; preserves topology, geometry, shape forms, growth timings.
            var signature=new StringBuilder();
            foreach(var shape in plan.Shapes) signature.Append((int)shape.SurfaceForm).Append(',').Append(shape.LobeCount).Append(',').Append(Q(shape.LobeDepth)).Append(',').Append(string.Join(',',shape.WidthProfile.Select(x=>Q(x)))).Append(';');
            foreach(var o in plan.Items) signature.Append((int)o.Type).Append(',').Append(o.Parent).Append(',').Append(Q(o.AttachT)).Append(',').Append(Q(o.P0.X)).Append(',').Append(Q(o.P0.Y)).Append(',').Append(Q(o.P0.Z)).Append(',').Append(Q(o.P1.X)).Append(',').Append(Q(o.P1.Y)).Append(',').Append(Q(o.P1.Z)).Append(',').Append(Q(o.R0)).Append(',').Append(Q(o.R1)).Append(',').Append(Q(o.Emerge)).Append(',').Append(Q(o.Dur)).Append(';');
            var fingerprint=Hash(signature.ToString());
            if(id==101) { if(!fingerprints.TryGetValue(fingerprint,out var list)) fingerprints[fingerprint]=list=new(); list.Add(def.Id); }
            instances.Add(new { id, counts, mature=plan.Items.Count, fingerprint, lods });
        }
        results.Add(new { id=def.Id, family=info.Family, silhouette=info.Silhouette, growthHabit=info.GrowthHabit, attached=info.AttachedSurface, instances });
    }
    catch(Exception ex) { errors.Add($"{def.Id}: {ex}"); }
}
var duplicates=fingerprints.Where(x=>x.Value.Count>1).Select(x=>new { fingerprint=x.Key, species=x.Value }).ToArray();
var report=new { contentRoot=root, contentDigest=library.ContentDigest, catalogCount=catalogIds.Length, contentCount=contentIds.Length, errors, duplicateFingerprints=duplicates, species=results };
File.WriteAllText(output,JsonSerializer.Serialize(report,new JsonSerializerOptions { WriteIndented=true }));
Console.WriteLine($"Catalog {catalogIds.Length}, content {contentIds.Length}, completed {results.Count}, errors {errors.Count}, duplicate fingerprints {duplicates.Length}. Report: {output}");
foreach(var error in errors.Take(40)) Console.WriteLine(error);
return errors.Count == 0 ? 0 : 1;
