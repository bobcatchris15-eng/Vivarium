import os

file_path = "game/Render/CoverageRenderer.cs"

with open(file_path, "r", encoding="utf-8", newline="") as f:
    content = f.read()

crlf = "\r\n" in content
nl = "\r\n" if crlf else "\n"

# 1. Add _plasmodiumMaterial field
mat_marker = "    private ShaderMaterial _crustMaterial = null!;" + nl
new_mat = mat_marker + "    private ShaderMaterial _plasmodiumMaterial = null!;" + nl
assert mat_marker in content, "mat_marker not found"
content = content.replace(mat_marker, new_mat, 1)

# 2. Add _plasmodiumSpecies dictionary
dict_marker = "    private readonly Dictionary<byte, SpeciesRenderInfo> _crustSpecies = new();" + nl
new_dict = dict_marker + "    private readonly Dictionary<byte, SpeciesRenderInfo> _plasmodiumSpecies = new();" + nl
assert dict_marker in content, "dict_marker not found"
content = content.replace(dict_marker, new_dict, 1)

# 3. Add _plasmodiumMaterial in Build()
build_marker = "        _crustMaterial.SetShaderParameter(\"lichen_col\", GD.Load<Texture2D>(\"res://Textures/LichenThallus.png\"));" + nl
new_build = build_marker + \
"        _plasmodiumMaterial = Bridge.Shader(\"res://Shaders/flora.gdshader\");" + nl + \
"        _plasmodiumMaterial.SetShaderParameter(\"surface_mode\", 4);" + nl + \
"        _plasmodiumMaterial.SetShaderParameter(\"sway\", 0.0f);" + nl + \
"        _plasmodiumMaterial.SetShaderParameter(\"stiffness\", 3.0f);" + nl + \
"        Bridge.BindSurface(_plasmodiumMaterial, \"moss\", Bridge.Surfaces.Moss);" + nl
assert build_marker in content, "build_marker not found"
content = content.replace(build_marker, new_build, 1)

# 4. In InitSpecies(), populate _plasmodiumSpecies
init_marker = "        _matSpecies.Clear();" + nl + "        _crustSpecies.Clear();" + nl
new_init = init_marker + "        _plasmodiumSpecies.Clear();" + nl
assert init_marker in content, "init_marker not found"
content = content.replace(init_marker, new_init, 1)

# Inside the loop in InitSpecies()
sp_loop_end = "                _crustSpecies[lichenId] = new SpeciesRenderInfo" + nl + \
"                {" + nl + \
"                    SpeciesId = sp.Id," + nl + \
"                    Color1 = c1," + nl + \
"                    Color2 = c2," + nl + \
"                    HeightForm = MatHeightForm.Flat," + nl + \
"                    MaxHeightM = maxH," + nl + \
"                    DomeLength = 3.0," + nl + \
"                    FloraType = fType," + nl + \
"                };" + nl + \
"            }" + nl + \
"        }" + nl + \
"    }"

new_sp_loop = "                _crustSpecies[lichenId] = new SpeciesRenderInfo" + nl + \
"                {" + nl + \
"                    SpeciesId = sp.Id," + nl + \
"                    Color1 = c1," + nl + \
"                    Color2 = c2," + nl + \
"                    HeightForm = MatHeightForm.Flat," + nl + \
"                    MaxHeightM = maxH," + nl + \
"                    DomeLength = 3.0," + nl + \
"                    FloraType = fType," + nl + \
"                };" + nl + \
"            }" + nl + \
"            if (sp.Archetype == \"slime_mold\")" + nl + \
"            {" + nl + \
"                Color c1 = sp.Color != null && sp.Color.Length >= 3 ? Bridge.C(sp.Color) : new Color(0.96f, 0.65f, 0.08f);" + nl + \
"                Color c2 = sp.Color2 != null && sp.Color2.Length >= 3 ? Bridge.C(sp.Color2) : new Color(0.98f, 0.86f, 0.18f);" + nl + \
"                _plasmodiumSpecies[1] = new SpeciesRenderInfo" + nl + \
"                {" + nl + \
"                    SpeciesId = sp.Id," + nl + \
"                    Color1 = c1," + nl + \
"                    Color2 = c2," + nl + \
"                    HeightForm = MatHeightForm.Flat," + nl + \
"                    MaxHeightM = 0.006," + nl + \
"                    DomeLength = 2.0," + nl + \
"                    FloraType = CoverageFloraType.Generic," + nl + \
"                };" + nl + \
"            }" + nl + \
"        }" + nl + \
"    }"
assert sp_loop_end in content, "sp_loop_end not found"
content = content.replace(sp_loop_end, new_sp_loop, 1)

# 5. In GetSpecies(), support _plasmodiumSpecies
get_species_marker = "    private SpeciesRenderInfo GetSpecies(CoverageLayerId layerId, byte occ)" + nl + \
"    {" + nl + \
"        var dict = layerId == CoverageLayerId.Mat ? _matSpecies : _crustSpecies;" + nl + \
"        if (dict.TryGetValue(occ, out var info)) return info;" + nl + \
"        return _defaultSpecies;" + nl + \
"    }"

new_get_species = "    private SpeciesRenderInfo GetSpecies(CoverageLayerId layerId, byte occ)" + nl + \
"    {" + nl + \
"        var dict = layerId == CoverageLayerId.Mat ? _matSpecies" + nl + \
"            : layerId == CoverageLayerId.Crust ? _crustSpecies" + nl + \
"            : _plasmodiumSpecies;" + nl + \
"        if (dict.TryGetValue(occ, out var info)) return info;" + nl + \
"        return _defaultSpecies;" + nl + \
"    }"
assert get_species_marker in content, "get_species_marker not found"
content = content.replace(get_species_marker, new_get_species, 1)

# 6. In SyncTiles, EnqueueTiles(_w.Coverage.Plasmodium)
sync_marker = "        EnqueueTiles(_w.Coverage.Mat);" + nl + \
"        EnqueueTiles(_w.Coverage.Crust);" + nl

new_sync = sync_marker + "        EnqueueTiles(_w.Coverage.Plasmodium);" + nl
assert sync_marker in content, "sync_marker not found"
content = content.replace(sync_marker, new_sync, 1)

# 7. In CreateTile & RebuildTile, select _plasmodiumMaterial
mat_select_marker = "        var mat = DebugMode ? (layer.Id == CoverageLayerId.Crust ? _debugCrustMaterial : _debugMatMaterial)" + nl + \
"            : (Material)(layer.Id == CoverageLayerId.Crust ? _crustMaterial : _matMaterial);"

new_mat_select = "        var mat = DebugMode ? (layer.Id == CoverageLayerId.Crust ? _debugCrustMaterial : _debugMatMaterial)" + nl + \
"            : layer.Id == CoverageLayerId.Plasmodium ? _plasmodiumMaterial" + nl + \
"            : (Material)(layer.Id == CoverageLayerId.Crust ? _crustMaterial : _matMaterial);"

assert content.count(mat_select_marker) == 2, f"mat_select_marker expected 2 occurrences, got {content.count(mat_select_marker)}"
content = content.replace(mat_select_marker, new_mat_select)

# 8. In ComputeThickness, handle CoverageLayerId.Plasmodium
comp_thick_marker = "        if (layerId == CoverageLayerId.Mat)" + nl
new_comp_thick = "        if (layerId == CoverageLayerId.Plasmodium)" + nl + \
"        {" + nl + \
"            float veinFactor = w / 255.0f;" + nl + \
"            float baseSheet = 0.0010f * bNorm;" + nl + \
"            float veinRidge = (float)(sp.MaxHeightM * bNorm * Math.Sqrt(veinFactor));" + nl + \
"            th = baseSheet + veinRidge;" + nl + \
"            if ((flags & (byte)CoverageFlags.Front) != 0) th = Math.Max(0.0006f, th * 0.7f);" + nl + \
"            if ((flags & (byte)CoverageFlags.Fruiting) != 0) th = 0.0004f;" + nl + \
"        }" + nl + \
"        else if (layerId == CoverageLayerId.Mat)" + nl
assert comp_thick_marker in content, "comp_thick_marker not found"
content = content.replace(comp_thick_marker, new_comp_thick, 1)

# 9. In SurfaceRelief, handle CoverageLayerId.Plasmodium
relief_marker = "    private static float SurfaceRelief(CoverageLayerId layer, double wx, double wz, float alpha, double maxHeight)" + nl + \
"    {" + nl + \
"        if (alpha <= 0) return 0;" + nl

new_relief = relief_marker + \
"        if (layer == CoverageLayerId.Plasmodium)" + nl + \
"        {" + nl + \
"            float wave = ValueNoise(wx + 0.015, wz + 0.015, 0.025);" + nl + \
"            return alpha * 0.0025f * wave;" + nl + \
"        }" + nl
assert relief_marker in content, "relief_marker not found"
content = content.replace(relief_marker, new_relief, 1)

# 10. In BuildTileMesh, baseOffset and cellColor for Plasmodium
base_offset_marker = "        float baseOffset = layer.Id == CoverageLayerId.Crust ? 0.002f : 0.0035f;" + nl
new_base_offset = "        float baseOffset = layer.Id == CoverageLayerId.Crust ? 0.002f : layer.Id == CoverageLayerId.Plasmodium ? 0.0038f : 0.0035f;" + nl
assert base_offset_marker in content, "base_offset_marker not found"
content = content.replace(base_offset_marker, new_base_offset, 1)

# CellColor logic in BuildTileMesh
color_start_marker = "            // Vertex colors derive from species Color and Color2 modulated by biomass, dormancy (browning), and health" + nl
new_color_start = "            if (layer.Id == CoverageLayerId.Plasmodium)" + nl + \
"            {" + nl + \
"                float veinFrac = Math.Clamp(w / 180f, 0f, 1f);" + nl + \
"                Color pCol = sp.Color2.Lerp(sp.Color1, veinFrac);" + nl + \
"                if ((flags & (byte)CoverageFlags.Front) != 0)" + nl + \
"                {" + nl + \
"                    pCol = sp.Color2 * 1.15f;" + nl + \
"                }" + nl + \
"                else if ((flags & (byte)CoverageFlags.Fruiting) != 0)" + nl + \
"                {" + nl + \
"                    pCol = new Color(0.48f, 0.44f, 0.36f);" + nl + \
"                }" + nl + \
"                else if ((flags & (byte)CoverageFlags.Sclerotium) != 0)" + nl + \
"                {" + nl + \
"                    pCol = new Color(0.82f, 0.42f, 0.10f);" + nl + \
"                }" + nl + \
"                pCol.A = 1f;" + nl + \
"                _cellColor[lx, lz] = pCol;" + nl + \
"                continue;" + nl + \
"            }" + nl + nl + color_start_marker

assert color_start_marker in content, "color_start_marker not found"
content = content.replace(color_start_marker, new_color_start, 1)

with open(file_path, "w", encoding="utf-8", newline="") as f:
    f.write(content)

print(f"Updated {file_path}")
