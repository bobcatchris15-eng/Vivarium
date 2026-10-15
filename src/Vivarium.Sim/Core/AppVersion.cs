namespace Vivarium.Sim.Core;

/// <summary>
/// The single canonical source of the application and save-schema versions.
/// scripts/export.ps1 reads these constants and stamps project.godot / export_presets.cfg,
/// and a test asserts the Godot project metadata agrees with them.
/// </summary>
public static class AppVersion
{
    public const string ProductName = "Vivarium";
    public const string Application = "0.1.2";

    /// <summary>Schema 4 replaces mobile water with conservative fine cells/airborne state; prior worlds migrate in staging.</summary>
    public const int SaveSchema = 4;

    public static string Describe() => $"{ProductName} {Application} (save schema {SaveSchema})";
}
