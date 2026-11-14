// Headless runner for Repr self tests: dotnet run --project tools/ReprSelfTest [-- name-substring]
using System.Diagnostics;
using Vivarium.Sim.Repr;

string? filter = args.Length > 0 ? args[0] : null;
var tests = typeof(IReprSelfTest).Assembly.GetTypes()
    .Where(t => !t.IsAbstract && typeof(IReprSelfTest).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) != null)
    .Select(t => (IReprSelfTest)Activator.CreateInstance(t)!)
    .Where(t => filter == null || t.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
    .OrderBy(t => t.Name).ToList();
if (tests.Count == 0)
{
    Console.WriteLine($"REPR_SELFTEST_NO_MATCH filter={filter ?? "<none>"}");
    return 2;
}
int failed = 0;
foreach (var t in tests)
{
    var sw = Stopwatch.StartNew();
    string? r;
    try { r = t.Run(); } catch (Exception e) { r = e.GetType().Name + ": " + e.Message; }
    if (r != null) failed++;
    Console.WriteLine($"{(r == null ? "PASS" : "FAIL")} {t.Name} ({sw.ElapsedMilliseconds} ms){(r == null ? "" : " :: " + r)}");
}
Console.WriteLine($"REPR_SELFTEST tests={tests.Count} failed={failed}");
return failed == 0 ? 0 : 1;
