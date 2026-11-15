using Vivarium.Sim.Core;

namespace Vivarium.Sim.World;

public sealed partial class Heightfield
{
    private int _soilVersion;
    private ulong _soilSeed;
    private double _soilAmplitude;
    // Frozen version-one default. Change the model version rather than this constant.
    private const double VersionOneAmplitude = .008;
    private SoilSurface? _soilSurface;
    private SoilSurface? _previousSoilSurface;
    public int SoilSurfaceVersion => _soilVersion;
    public SoilSurface SoilSurface
    {
        get
        {
            if (_soilVersion == 0) throw new InvalidOperationException("Legacy terrain has no granular lattice");
            if (_soilSurface != null) return _soilSurface;
            var parameters = new SoilSurfaceParameters(_soilVersion, _soilSeed, _soilAmplitude == 0 ? VersionOneAmplitude : _soilAmplitude);
            _soilSurface = _previousSoilSurface is { } previous && previous.Parameters == parameters
                ? previous.Rebase(this) : new SoilSurface(this, parameters);
            _previousSoilSurface = null;
            return _soilSurface;
        }
    }
    private void ConfigureSoil(int version, ulong seed, double amplitude)
    { _soilVersion = version; _soilSeed = seed; _soilAmplitude = amplitude; _soilSurface = null; }
    internal void UpgradeSoil(double amplitude)
    {
        if (!double.IsFinite(amplitude) || amplitude <= 0 || amplitude > .05) throw new ArgumentOutOfRangeException(nameof(amplitude));
        _soilVersion = global::Vivarium.Sim.World.SoilSurface.CurrentVersion; _soilAmplitude = amplitude;
        _soilSurface = null; _previousSoilSurface = null; Version++;
    }
}
