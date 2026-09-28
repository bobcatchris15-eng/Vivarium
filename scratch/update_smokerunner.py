file_path = "game/App/SmokeRunner.cs"

with open(file_path, "r", encoding="utf-8", newline="") as f:
    content = f.read()

crlf = "\r\n" in content
nl = "\r\n" if crlf else "\n"

old_block = """            var slime = W.Flora.Items.Where(x => x.SpeciesId == "ambervein").ToList();
            if (slime.Count > 0)
            {
                var hub = slime.OrderByDescending(x => slime.Count(o => Vec2.Distance(o.Position, x.Position) < 0.5)).ThenBy(x => x.Id.Value).First();
                var hp = new Vector3((float)hub.X, (float)W.GroundHeight(hub.Position), (float)hub.Z);
                cam.LookAtPoint(hp + new Vector3(0.55f, 0.6f, 0.55f), hp);
                await Frames(8);
                await Screenshot("slime_network");
                _facts["slime_patches"] = slime.Count;
            }"""

new_block = """            var slime = W.Flora.Items.Where(x => x.SpeciesId == "ambervein").ToList();
            if (slime.Count > 0)
            {
                var hub = slime.OrderByDescending(x => slime.Count(o => Vec2.Distance(o.Position, x.Position) < 0.5)).ThenBy(x => x.Id.Value).First();
                var hp = new Vector3((float)hub.X, (float)W.GroundHeight(hub.Position), (float)hub.Z);
                cam.LookAtPoint(hp + new Vector3(0.55f, 0.6f, 0.55f), hp);
                await Frames(8);
                await Screenshot("slime_network");
                _facts["slime_patches"] = slime.Count;
            }
            else if (W.Coverage.Plasmodium.Tiles.Any(t => !t.IsEmpty()))
            {
                var firstTile = W.Coverage.Plasmodium.Tiles.First(t => !t.IsEmpty());
                int firstLi = 0;
                for (int i = 0; i < CoverageTile.N; i++) if (firstTile.Occ[i] != 0) { firstLi = i; break; }
                int gx = firstTile.Ti * CoverageSpec.TileEdge + (firstLi % CoverageSpec.TileEdge);
                int gz = firstTile.Tj * CoverageSpec.TileEdge + (firstLi / CoverageSpec.TileEdge);
                var p = new Vec2((gx + 0.5) * CoverageSpec.CellSize, (gz + 0.5) * CoverageSpec.CellSize);
                var hp = new Vector3((float)p.X, (float)W.GroundHeight(p), (float)p.Z);
                cam.LookAtPoint(hp + new Vector3(0.55f, 0.6f, 0.55f), hp);
                await Frames(8);
                await Screenshot("slime_network");
                _facts["slime_patches"] = 1;
            }"""

old_block_norm = old_block.replace("\r\n", "\n").replace("\n", nl)
new_block_norm = new_block.replace("\r\n", "\n").replace("\n", nl)

assert old_block_norm in content, "old_block not found"
content = content.replace(old_block_norm, new_block_norm, 1)

with open(file_path, "w", encoding="utf-8", newline="") as f:
    f.write(content)

print(f"Updated {file_path}")
