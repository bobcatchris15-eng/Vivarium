using System;
using System.Diagnostics;

class Probe
{
    static void Main()
    {
        var v3 = new Godot.Vector3(1, 2, 3);
        var t3 = new Godot.Transform3D(new Godot.Basis(), v3);
        var sw = Stopwatch.StartNew();
        const int n = 2000000;
        for (int i = 0; i < n; i++) { var r = t3 * t3; var p = r * v3; if (p.X == 12345f) Console.WriteLine("x"); }
        double perOp = sw.Elapsed.TotalMilliseconds * 1e6 / n;
        Console.WriteLine($"Godot Transform3D*Transform3D + *Vector3: {perOp:0.0} ns per iteration (2 operators)");

        // plain float arithmetic for reference
        sw.Restart();
        double acc = 0;
        for (int i = 0; i < n; i++)
        {
            float ax = i * 1.0001f, ay = i * 0.9999f, az = i * 1.0002f;
            float bx = 1.5f, by = 2.5f, bz = 3.5f;
            acc += ax * bx + ay * by + az * bz;
        }
        Console.WriteLine($"plain float dot: {sw.Elapsed.TotalMilliseconds * 1e6 / n:0.0} ns per iteration ({acc})");

        // Godot Vector3 add
        sw.Restart();
        var accv = Godot.Vector3.Zero;
        for (int i = 0; i < n; i++) { accv += v3; }
        Console.WriteLine($"Godot Vector3 add: {sw.Elapsed.TotalMilliseconds * 1e6 / n:0.0} ns per iteration");

        // Godot Vector3 length
        sw.Restart();
        float len = 0;
        for (int i = 0; i < n; i++) { len += v3.Length(); }
        Console.WriteLine($"Godot Vector3 Length: {sw.Elapsed.TotalMilliseconds * 1e6 / n:0.0} ns per iteration ({len})");
    }
}
