# Third-party notices — Vivarium

Vivarium's own code, content and procedural assets are original to this project. The release bundle
also contains the following third-party software and textures:

| Component | Version | License | Where |
|---|---|---|---|
| Godot Engine (export template, GodotSharp) | 4.7.1-stable (mono) | MIT | `Vivarium.exe`, `data_Vivarium_windows_x86_64/GodotSharp*.dll` |
| Components bundled inside Godot (FreeType, HarfBuzz, ICU, Vulkan loader, zstd, …) | see `ENGINE_LICENSES.txt` | various (MIT, BSD, Zlib, Apache-2.0, ICU, …) | `Vivarium.exe` |
| .NET 8 runtime + base class libraries | 8.0.x | MIT | `data_Vivarium_windows_x86_64/` |
| Photo-scanned surface textures from ambientCG (Ground048, Ground037, Moss002, ScatteredLeaves007, Rock058, Gravel022, Bark014; 1K colour, normal and roughness maps) | downloaded 2026-09-23 | CC0 1.0 (public domain) | `Vivarium.pck` (source: `game/Textures/`) |

`ENGINE_LICENSES.txt` is generated at export time from the engine's own license inventory
(`Engine.GetCopyrightInfo()` / `Engine.GetLicenseInfo()`), so it matches exactly what is bundled.

Apart from the CC0 textures above, no third-party art, audio, fonts beyond Godot's built-in default font, or network
services are used. The textures are public domain and need no attribution; they are credited to ambientCG.com anyway.

.NET runtime license: https://github.com/dotnet/runtime/blob/main/LICENSE.TXT (MIT, Copyright (c) .NET Foundation and Contributors).
