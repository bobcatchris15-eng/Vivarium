using System.Security.Cryptography;
using System.Text;

namespace Vivarium.Sim.Core;

public static class Digest
{
    public static string Sha256Hex(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    public static string Sha256Hex(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}

/// <summary>Incremental canonical hasher for ad-hoc digests (e.g. mesh geometry, field arrays).</summary>
public sealed class DigestBuilder : IDisposable
{
    private readonly IncrementalHash _h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private readonly byte[] _buf = new byte[8];

    public DigestBuilder Add(double v) { BitConverter.TryWriteBytes(_buf, BitConverter.DoubleToInt64Bits(v)); _h.AppendData(_buf, 0, 8); return this; }
    public DigestBuilder Add(float v) { BitConverter.TryWriteBytes(_buf, BitConverter.SingleToInt32Bits(v)); _h.AppendData(_buf, 0, 4); return this; }
    public DigestBuilder Add(long v) { BitConverter.TryWriteBytes(_buf, v); _h.AppendData(_buf, 0, 8); return this; }
    public DigestBuilder Add(ulong v) { BitConverter.TryWriteBytes(_buf, v); _h.AppendData(_buf, 0, 8); return this; }
    public DigestBuilder Add(int v) { BitConverter.TryWriteBytes(_buf, v); _h.AppendData(_buf, 0, 4); return this; }
    public DigestBuilder Add(string s) { var b = Encoding.UTF8.GetBytes(s); Add(b.Length); _h.AppendData(b); return this; }
    public DigestBuilder Add(ReadOnlySpan<double> vs) { foreach (var v in vs) Add(v); return this; }
    public DigestBuilder Add(ReadOnlySpan<byte> bs) { Add(bs.Length); _h.AppendData(bs); return this; }
    public string Hex() => Convert.ToHexString(_h.GetHashAndReset()).ToLowerInvariant();
    public void Dispose() => _h.Dispose();
}
