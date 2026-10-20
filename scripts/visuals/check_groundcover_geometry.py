"""Run the renderer's exact pure mesh builders against Godot's clockwise winding contract."""
import argparse
import subprocess
from pathlib import Path

repo = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser()
parser.add_argument('--source', type=Path, default=repo / 'game/Render/AmbientGroundCoverRenderer.cs')
parser.add_argument('--output', type=Path, default=repo / 'build/forest-reference-20261015/geometry-probe')
args = parser.parse_args()
source = args.source.read_text(encoding='utf-8-sig')

def method(name):
    start = source.index('private static MeshData ' + name + '(')
    opening = source.index('{', start)
    depth = 1
    end = opening + 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end]

checks = r'''
    public static void Main() { BUILD_CHECKS }
    private static void Check(string name, MeshData mesh)
    {
        foreach (float x in mesh.Positions.Concat(mesh.Normals).Concat(mesh.UV).Concat(mesh.UV2).Concat(mesh.Colors))
            if (!float.IsFinite(x)) throw new Exception(name + ": nonfinite attribute");
        if (mesh.VertexCount == 0 || mesh.TriangleCount == 0) throw new Exception(name + ": empty mesh");
        if (mesh.UV.Count != mesh.VertexCount * 2) throw new Exception(name + ": missing UVs");
        if (mesh.UV2.Count != mesh.VertexCount * 2) throw new Exception(name + ": missing crown occlusion");
        for (int i=0; i<mesh.VertexCount; i++)
        {
            if (Math.Abs(mesh.NormalAt(i).Length - 1) > 1e-5) throw new Exception(name + ": invalid normal");
            if (mesh.UV2[i*2] < .15f-1e-6 || mesh.UV2[i*2] > 1f+1e-6)
                throw new Exception(name + ": crown occlusion outside shader contract");
            if (name == "arching")
            {
                var p = mesh.Position(i);
                if (Math.Sqrt(p.X*p.X+p.Z*p.Z) > .15 || p.Y < 0 || p.Y > .20)
                    throw new Exception(name + ": rosette exceeds placement clearance envelope");
            }
        }
        for (int i=0; i<mesh.Indices.Count; i+=3)
        {
            int a=mesh.Indices[i], b=mesh.Indices[i+1], c=mesh.Indices[i+2];
            var face=(mesh.Position(b)-mesh.Position(a)).Cross(mesh.Position(c)-mesh.Position(a));
            if (face.LengthSq < 1e-18) throw new Exception(name + ": degenerate face");
            if (face.Dot(mesh.NormalAt(a)+mesh.NormalAt(b)+mesh.NormalAt(c)) >= 0)
                throw new Exception(name + ": clockwise Godot front face disagrees with outward normals");
        }
        Console.WriteLine($"PASS {name}: {mesh.VertexCount} vertices, {mesh.TriangleCount} triangles, finite attributes, unit normals, clockwise front faces");
    }
'''
names = ['BuildBladeGeometry', 'BuildBroadleafGeometry']
calls = 'Check("grass", BuildBladeGeometry()); Check("broadleaf", BuildBroadleafGeometry());'
if 'BuildBroadleafGeometry(bool arching' in source:
    calls += ' Check("arching", BuildBroadleafGeometry(true));'
if 'private static MeshData BuildRunnerGeometry(' in source:
    names.append('BuildRunnerGeometry')
    calls += ' Check("runners", BuildRunnerGeometry());'
checks = checks.replace('BUILD_CHECKS', calls)
if 'private static MeshData FinishCoverGeometry(' in source:
    names.append('FinishCoverGeometry')
args.output.mkdir(parents=True, exist_ok=True)
(args.output / 'Program.cs').write_text('using System;\nusing System.Linq;\nusing Vivarium.Sim.Core;\nusing Vivarium.Sim.Geometry;\nclass Probe {\n' + checks + '\n'.join(method(name) for name in names) + '\n}', encoding='utf-8')
reference = (repo / 'src/Vivarium.Sim/Vivarium.Sim.csproj').as_posix()
(args.output / 'GeometryProbe.csproj').write_text(f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework></PropertyGroup><ItemGroup><ProjectReference Include="{reference}" /></ItemGroup></Project>', encoding='utf-8')
raise SystemExit(subprocess.call(['dotnet', 'run', '--project', str(args.output / 'GeometryProbe.csproj'), '--verbosity', 'quiet'], cwd=repo))
