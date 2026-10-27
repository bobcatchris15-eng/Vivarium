using System.Collections.Generic;
using Godot;

namespace Vivarium.Game.Render;

/// <summary>
/// Starts the guardian model loading on a worker thread while the rest of a
/// world is prepared. The guardian asset is by far the largest thing the game
/// loads - a single scene with its meshes, skeleton and textures - and the
/// world start knows which guardian it needs before the renderer runs, so the
/// wait can be spent building the island instead of streaming the model.
/// </summary>
public static class PreloadGuardians
{
    private static readonly Dictionary<string, PackedScene> _ready = new();
    private static readonly HashSet<string> _pending = new();

    /// <summary>Begins loading the guardian scene for an archetype, if it is not already loaded.</summary>
    public static void Begin(string? archetype)
    {
        if (string.IsNullOrEmpty(archetype)) return;
        string path = $"res://assets/guardians/{archetype}/guardian.glb";
        if (_ready.ContainsKey(path) || _pending.Contains(path)) return;
        if (!ResourceLoader.Exists(path)) return;
        if (ResourceLoader.HasCached(path)) return;
        var error = ResourceLoader.LoadThreadedRequest(path, "PackedScene", true);
        if (error == Error.Ok) _pending.Add(path);
    }

    /// <summary>Returns the scene if the threaded load has finished, otherwise null.</summary>
    public static PackedScene? TryTake(string path)
    {
        if (_ready.TryGetValue(path, out var scene)) { _ready.Remove(path); return scene; }
        if (!_pending.Contains(path)) return null;
        var status = ResourceLoader.LoadThreadedGetStatus(path);
        if (status == ResourceLoader.ThreadLoadStatus.Loaded)
        {
            _pending.Remove(path);
            var loaded = ResourceLoader.LoadThreadedGet(path) as PackedScene;
            if (loaded != null) _ready[path] = loaded;
            return loaded;
        }
        if (status == ResourceLoader.ThreadLoadStatus.Failed || status == ResourceLoader.ThreadLoadStatus.InvalidResource)
        {
            _pending.Remove(path);
            return null;
        }
        return null;
    }
}
