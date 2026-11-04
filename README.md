# Vivarium

Vivarium 0.1.4 is a desktop ecosystem on a hexagonal island.

## Launch

On Windows, double-click **launch.cmd**. It builds the C# game, imports assets on
first launch, and opens the current application. Use **launch-editor.cmd** to open
the same project in Godot for editing.

Requirements:

- Godot **4.7.1 .NET / Mono**, using the Forward+ renderer.
- **.NET 10 SDK** for Godot's editor/import tools. The game targets .NET 8.
- A Vulkan-capable GPU and Windows 10/11 x64 for the supplied launchers.

The launchers use the local engine in `tools/godot/` and local SDK in
`tools/dotnet10/` when present. For a fresh checkout, install the SDK and set
`VIVARIUM_GODOT` to your Godot .NET executable, or put the engine in the local
folder expected by the launcher. Tool downloads and generated caches are ignored
by Git.

The first build can need internet access to restore build packages. Running the
prepared game does not require a network connection.

## Build manually

```powershell
dotnet build Vivarium.sln
```

Import the assets with your Godot .NET executable:

```powershell
Godot.exe --headless --editor --path game --import
Godot.exe --path game
```

## Project contents

- `game/`: application, UI, rendering, shaders, scenes, content, and runtime assets.
- `src/Vivarium.Sim/`: ecosystem simulation and save/load implementation.
- `tools/`: local Godot and SDK installations when available.
- `Vivarium.sln`: the game and simulation projects.

Development review tools, tests, research, source-art history, scratch files, and
old build outputs have been archived outside this checkout.

## Saves and controls

On Windows, saves, settings, and logs live in `%APPDATA%\Vivarium\`.
Version 0.1.4 uses **save schema 5**; older save schemas are unsupported.
Press **F1** in the application for controls. Create a new world to use revised
terrain presets; existing worlds retain their saved terrain descriptors.

## Notices

See `THIRD_PARTY_NOTICES.md`. Godot's own license files are included with the local
engine and portable release bundles.
