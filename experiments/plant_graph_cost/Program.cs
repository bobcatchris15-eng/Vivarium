using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.IO;
using Vivarium.Sim.Repr.Plant;
using Vivarium.Sim.Core;

var asm = typeof(PlantPlan).Assembly;
Console.WriteLine($"assembly={asm.Location} mvid={asm.ManifestModule.ModuleVersionId}");
var sw = Stopwatch.StartNew(); long before = GC.GetAllocatedBytesForCurrentThread();
using var stream = new MemoryStream(); using var w = new BinaryWriter(stream);
foreach (var g in new PlanGenerator[] { new GlowbriarGenerator(), new MoonfernGenerator(), new ShadebellGenerator() }) {
 var start=sw.Elapsed; long alloc=GC.GetAllocatedBytesForCurrentThread(); int count=0; long planAlloc=0; double planMs=0;
 for (ulong id=1; id<=60; id++) { long pa=GC.GetAllocatedBytesForCurrentThread(); long ticks=Stopwatch.GetTimestamp(); var p=g.PlanFor(0x123456789abcdef0, id); planMs += Stopwatch.GetElapsedTime(ticks).TotalMilliseconds; planAlloc += GC.GetAllocatedBytesForCurrentThread()-pa; count += p.Items.Count; foreach(var o in p.Items) { w.Write(o.Gen);w.Write(o.Parent);w.Write((int)o.Type);w.Write((int)o.Tissue);w.Write((int)o.Flags);w.Write(o.ShapeId);w.Write(o.Order);w.Write(o.AttachT); Write(o.P0);Write(o.T0);Write(o.P1);Write(o.T1);Write(o.Up);w.Write(o.R0);w.Write(o.R1);w.Write(o.Emerge);w.Write(o.Dur);w.Write(o.Seed); } }
 Console.WriteLine($"{g.SpeciesId}: ms={(sw.Elapsed-start).TotalMilliseconds:F3} allocated={GC.GetAllocatedBytesForCurrentThread()-alloc} plan_ms={planMs:F3} plan_allocated={planAlloc} organs={count}");
}
Console.WriteLine($"total_ms={sw.Elapsed.TotalMilliseconds:F3} allocated={GC.GetAllocatedBytesForCurrentThread()-before} sha256={Convert.ToHexString(SHA256.HashData(stream.ToArray()))}");
void Write(Vec3 v) { w.Write(v.X);w.Write(v.Y);w.Write(v.Z); }
