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
    private async Task CaptureLogFiniteProbe(string sceneName)
    {
        var byId = new Dictionary<ulong, ShaderMaterial>();
        void AddMaterial(Material? material)
        {
            if (material is not ShaderMaterial shaderMaterial || shaderMaterial.Shader == null) return;
            ulong id = shaderMaterial.GetInstanceId();
            byId.TryAdd(id, shaderMaterial);
        }

        void Visit(Node node)
        {
            if (node is GeometryInstance3D geometry) AddMaterial(geometry.MaterialOverride);
            Mesh? mesh = node is MeshInstance3D instance ? instance.Mesh
                : node is MultiMeshInstance3D multi ? multi.Multimesh?.Mesh : null;
            if (mesh != null)
                for (int surface = 0; surface < mesh.GetSurfaceCount(); surface++)
                    AddMaterial(mesh.SurfaceGetMaterial(surface));
            foreach (Node child in node.GetChildren()) Visit(child);
        }

        Visit(Session.Props);
        Visit(Session.Coverage);
        var targets = byId.Values
            .Where(m => m.Shader?.ResourcePath is "res://Shaders/log.gdshader" or "res://Shaders/coverage_mat.gdshader")
            .OrderBy(m => m.Shader!.ResourcePath, StringComparer.Ordinal)
            .ThenBy(m => m.GetInstanceId())
            .ToArray();
        if (targets.Length == 0)
            throw new InvalidOperationException("Log finite probe found no production log.gdshader or coverage_mat.gdshader materials on Props/Coverage.");

        var originals = targets.ToDictionary(m => m.GetInstanceId(), m => m.Shader!);
        var originalCodes = originals.Values.DistinctBy(s => s.GetInstanceId())
            .ToDictionary(s => s.GetInstanceId(), s => s.Code);
        var attr = Session.EnvRig.CameraAttributes;
        bool originalNear = attr.DofBlurNearEnabled;
        bool originalFar = attr.DofBlurFarEnabled;
        bool originalGlow = Session.EnvRig.Env.GlowEnabled;
        bool processing = Session.CameraRig.IsProcessing();
        Session.CameraRig.SetProcess(false);
        var records = new List<object>();

        void Apply(bool validityMarker)
        {
            foreach (ShaderMaterial material in targets)
            {
                Shader original = originals[material.GetInstanceId()];
                string code = originalCodes[original.GetInstanceId()];
                material.Shader = new Shader
                {
                    ResourceName = (validityMarker ? "log-finite-validity-marker." : "log-finite-guarded-inputs.") + material.GetInstanceId(),
                    Code = InstrumentLogFiniteShader(code, validityMarker),
                };
            }
        }

        async Task Capture(string mode)
        {
            await Frames(12);
            for (int frame = 0; frame < 12; frame++)
            {
                await Screenshot($"{sceneName}_log_finite_{mode}_{frame:D2}");
                if (frame < 11) await Frames(3);
            }
            records.Add(new
            {
                mode,
                frames = 12,
                dof_near = attr.DofBlurNearEnabled,
                dof_far = attr.DofBlurFarEnabled,
                glow = Session.EnvRig.Env.GlowEnabled,
                shaders = targets.Select(m => new
                {
                    instance_id = m.GetInstanceId(),
                    source = originals[m.GetInstanceId()].ResourcePath,
                    assigned = m.Shader?.ResourceName,
                }).ToArray(),
            });
        }

        try
        {
            Apply(validityMarker: true);
            attr.DofBlurNearEnabled = attr.DofBlurFarEnabled = false;
            Session.EnvRig.Env.GlowEnabled = false;
            await Capture("validity_marker");

            Apply(validityMarker: false);
            attr.DofBlurNearEnabled = originalNear;
            attr.DofBlurFarEnabled = originalFar;
            Session.EnvRig.Env.GlowEnabled = originalGlow;
            await Capture("guarded_inputs");

            RestoreShaders();
            attr.DofBlurNearEnabled = originalNear;
            attr.DofBlurFarEnabled = originalFar;
            Session.EnvRig.Env.GlowEnabled = originalGlow;
            await Capture("restored");
        }
        finally
        {
            RestoreShaders();
            attr.DofBlurNearEnabled = originalNear;
            attr.DofBlurFarEnabled = originalFar;
            Session.EnvRig.Env.GlowEnabled = originalGlow;
            Session.CameraRig.SetProcess(processing);
        }

        void RestoreShaders()
        {
            foreach (ShaderMaterial material in targets)
                if (GodotObject.IsInstanceValid(material))
                    material.Shader = originals[material.GetInstanceId()];
        }

        File.WriteAllText(Path.Combine(OutDir, sceneName + "_log_finite_probe.json"), JsonSerializer.Serialize(new
        {
            scene = sceneName,
            materials = targets.Select(m => new
            {
                instance_id = m.GetInstanceId(),
                shader = originals[m.GetInstanceId()].ResourcePath,
                resource_name = originals[m.GetInstanceId()].ResourceName,
            }).ToArray(),
            controls = records,
            scope = "In-memory shader clones instrument fragment-body normalize calls, negative COLOR, and final ALBEDO/NORMAL finiteness. No production shader resource is edited.",
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string InstrumentLogFiniteShader(string source, bool validityMarker)
    {
        const string helper = @"
vec3 log_finite_probe_unit(vec3 value, inout float bad) {
    float length_squared = dot(value, value);
    if (any(isnan(value)) || any(isinf(value)) || isnan(length_squared) || isinf(length_squared) || length_squared < 1e-12) {
        bad = 1.0;
        return vec3(0.0, 1.0, 0.0);
    }
    return value * inversesqrt(length_squared);
}
";
        int fragment = source.IndexOf("void fragment", StringComparison.Ordinal);
        if (fragment < 0) throw new InvalidOperationException("Finite probe shader has no fragment() function.");
        int open = source.IndexOf('{', fragment);
        if (open < 0) throw new InvalidOperationException("Finite probe could not find fragment() body.");
        int close = FindMatchingShaderBrace(source, open);
        string body = source.Substring(open + 1, close - open - 1);
        body = ReplaceNormalizeCalls(body);
        body = body.Replace("srgb_to_linear(COLOR.rgb)", "srgb_to_linear(max(COLOR.rgb, vec3(0.0)))", StringComparison.Ordinal);

        const string begin = @"
    float probe_bad = 0.0;
    if (any(isnan(COLOR.rgb)) || any(isinf(COLOR.rgb)) || any(lessThan(COLOR.rgb, vec3(0.0)))) probe_bad = 1.0;
";
        const string end = @"
    if (any(isnan(ALBEDO)) || any(isinf(ALBEDO)) || any(isnan(NORMAL)) || any(isinf(NORMAL))) probe_bad = 1.0;
";
        if (validityMarker)
            body += end + @"
    ALBEDO = vec3(0.0);
    EMISSION = probe_bad > 0.5 ? vec3(1.0, 0.12, 0.0) : vec3(0.0);
    NORMAL = vec3(0.0, 0.0, 1.0);
    ROUGHNESS = 1.0;
    SPECULAR = 0.0;
";
        else
            body += end;
        body = begin + body;

        string instrumented = source.Substring(0, open + 1) + body + source.Substring(close);
        int newFragment = instrumented.IndexOf("void fragment", StringComparison.Ordinal);
        instrumented = instrumented.Insert(newFragment, helper + "\n");
        if (validityMarker)
        {
            int mode = instrumented.IndexOf("render_mode", StringComparison.Ordinal);
            if (mode >= 0)
            {
                int modeEnd = instrumented.IndexOf(';', mode);
                if (modeEnd < 0) throw new InvalidOperationException("Finite probe could not parse render_mode statement.");
                instrumented = instrumented.Substring(0, mode) + "render_mode unshaded, cull_disabled;" + instrumented.Substring(modeEnd + 1);
            }
            else
            {
                instrumented = instrumented.Replace("shader_type spatial;", "shader_type spatial;\nrender_mode unshaded, cull_disabled;", StringComparison.Ordinal);
            }
        }
        return instrumented;
    }

    private static int FindMatchingShaderBrace(string code, int open)
    {
        int depth = 0;
        bool lineComment = false, blockComment = false;
        for (int i = open; i < code.Length; i++)
        {
            char c = code[i], next = i + 1 < code.Length ? code[i + 1] : '\0';
            if (lineComment)
            {
                if (c == '\n') lineComment = false;
                continue;
            }
            if (blockComment)
            {
                if (c == '*' && next == '/') { blockComment = false; i++; }
                continue;
            }
            if (c == '/' && next == '/') { lineComment = true; i++; continue; }
            if (c == '/' && next == '*') { blockComment = true; i++; continue; }
            if (c == '{') depth++;
            else if (c == '}' && --depth == 0) return i;
        }
        throw new InvalidOperationException("Finite probe found an unterminated fragment() body.");
    }

    private static string ReplaceNormalizeCalls(string body)
    {
        var output = new System.Text.StringBuilder(body.Length + 64);
        bool lineComment = false, blockComment = false;
        for (int i = 0; i < body.Length;)
        {
            char c = body[i], next = i + 1 < body.Length ? body[i + 1] : '\0';
            if (lineComment)
            {
                output.Append(c); i++;
                if (c == '\n') lineComment = false;
                continue;
            }
            if (blockComment)
            {
                output.Append(c); i++;
                if (c == '*' && next == '/') { output.Append('/'); i++; blockComment = false; }
                continue;
            }
            if (c == '/' && next == '/') { output.Append("//"); i += 2; lineComment = true; continue; }
            if (c == '/' && next == '*') { output.Append("/*"); i += 2; blockComment = true; continue; }
            if (IsIdentifierAt(body, i, "normalize"))
            {
                int paren = i + "normalize".Length;
                while (paren < body.Length && char.IsWhiteSpace(body[paren])) paren++;
                if (paren < body.Length && body[paren] == '(')
                {
                    int close = FindMatchingShaderParen(body, paren);
                    string argument = body.Substring(paren + 1, close - paren - 1);
                    output.Append("log_finite_probe_unit(").Append(argument).Append(", probe_bad)");
                    i = close + 1;
                    continue;
                }
            }
            output.Append(c); i++;
        }
        return output.ToString();
    }

    private static bool IsIdentifierAt(string text, int index, string identifier)
    {
        if (index + identifier.Length > text.Length || string.CompareOrdinal(text, index, identifier, 0, identifier.Length) != 0) return false;
        bool before = index == 0 || !(char.IsLetterOrDigit(text[index - 1]) || text[index - 1] == '_');
        int afterIndex = index + identifier.Length;
        bool after = afterIndex >= text.Length || !(char.IsLetterOrDigit(text[afterIndex]) || text[afterIndex] == '_');
        return before && after;
    }

    private static int FindMatchingShaderParen(string code, int open)
    {
        int depth = 0;
        for (int i = open; i < code.Length; i++)
        {
            if (code[i] == '(') depth++;
            else if (code[i] == ')' && --depth == 0) return i;
        }
        throw new InvalidOperationException("Finite probe found an unterminated normalize() call.");
    }
}
