using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Vivarium.Sim.Content;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.Repr.Plant;

var lib=ContentLoader.Load(new DirectoryContentSource(Path.GetFullPath("game/content")));
var result=new List<object>();
void Mesh(BinaryWriter w, MeshData m) { void F(List<float> a) { w.Write(a.Count); foreach(var v in a) w.Write(v); } F(m.Positions); F(m.Normals); F(m.Colors); F(m.UV); F(m.UV2); w.Write(m.Indices.Count); foreach(var v in m.Indices) w.Write(v); }
foreach(var def in lib.Flora) {
 var gen=PlantGraphCatalog.Create(def); var d=gen.Create(8675309,101); gen.Grow(d,1,.5,null);
 for(int lod=0;lod<3;lod++) {
  var a=PlantCompiler.Compile(d,lod); using var ms=new MemoryStream(); using var w=new BinaryWriter(ms);
  Mesh(w,a.Wood); w.Write(a.Instances.Length); foreach(var i in a.Instances) { w.Write(i.ShapeId); w.Write(i.M00); w.Write(i.M01); w.Write(i.M02); w.Write(i.M03); w.Write(i.M10); w.Write(i.M11); w.Write(i.M12); w.Write(i.M13); w.Write(i.M20); w.Write(i.M21); w.Write(i.M22); w.Write(i.M23); w.Write(i.Seed); w.Write(i.Maturity); w.Write(i.Health); } w.Write(d.Shapes.Length); foreach(var s in d.Shapes) Mesh(w,PlantCompiler.BuildShapeMesh(s,lod)); w.Flush();
  result.Add(new { id=def.Id,lod,organs=d.Organs.Length,vertices=a.Wood.VertexCount,triangles=a.WoodTriangles,instances=a.Instances.Length,shapes=d.Shapes.Length,hash=Convert.ToHexString(SHA256.HashData(ms.ToArray())) });
 }
}
File.WriteAllText(args[0],JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
foreach(var id in new[]{"fenforge","moonbark","mirespire","shadewrap","bitterbine","trumpetbind"}) { var def=lib.Flora.First(x=>x.Id==id); var gen=PlantGraphCatalog.Create(def); var d=gen.Create(8675309,101); gen.Grow(d,1,.5,null); for(int k=0;k<3;k++) PlantCompiler.Compile(d,0); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); long start=GC.GetAllocatedBytesForCurrentThread(); var sw=Stopwatch.StartNew(); const int n=10; for(int k=0;k<n;k++) PlantCompiler.Compile(d,0); sw.Stop(); Console.WriteLine($"{id} organs={d.Organs.Length} ms={sw.Elapsed.TotalMilliseconds/n:F3} bytes={(GC.GetAllocatedBytesForCurrentThread()-start)/n}"); }
Console.WriteLine($"entries={result.Count}");
