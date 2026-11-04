namespace Vivarium.Sim.Core;

/// <summary>
/// The single canonical source of the application and save-schema versions.
/// scripts/export.ps1 reads these constants and stamps project.godot / export_presets.cfg,
/// and a test asserts the Godot project metadata agrees with them.
/// </summary>
public static class AppVersion
{
    public const string ProductName = "Vivarium";
    public const string Application = "0.1.4";

    /// <summary>Schema 5 stores the tiled fluid solver's established-surface state. Older saves are unsupported.</summary>
    public const int SaveSchema = 5;

    public static string Describe() => $"{ProductName} {Application} (save schema {SaveSchema})";
}
