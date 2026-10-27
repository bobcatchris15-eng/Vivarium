# Flower and leaf material routing — 2026-10-25

User reported flowers sharing leaf textures, including Glass Lily and Blackneedle. No subagents used.

Cause: FloraVisualCompiler.Split used leaf-span ownership alone. Those spans also include petals and marked organs for LOD, so the separate leaf shader ignored the authored UV2 organ treatment. The legacy flora shader also calculated leaf textures/normals before overriding organ color.

Change: UV2 organ band (x > 1.20 and x < 1.50, y > .5) stays on the authored body material even inside a leaf span. The flora shader handles this band before foliage shading, retaining vertex pigment, prismatic treatment and geometric normals without foliage texture sampling. This also covers marked organs on Marrowbloom and Tankbloom. Geometry, simulation and content unchanged.

Validation: eight focused tests passed (four existing split contracts and four organ-routing cases, each new case covering detail levels 0/1/2). Solution build passed with zero warnings/errors. Game build passed with six existing warnings, output directed to build/flower-material-check/game. Initial broader FloraVisualTests run also failed ThinBladesHaveOnlyOneAuthoredSide for Shadebell; that assertion was not changed or resolved. New test initially counted unused source vertices and assumed every species retained unmarked foliage; corrected to check indexed organ preservation and require foliage on Glass Lily.

No native capture or shader-runtime acceptance performed. Existing Godot processes preserved. In-game appearance remains unverified; a new game process must load rebuilt C# to use the splitter change. No commits.
