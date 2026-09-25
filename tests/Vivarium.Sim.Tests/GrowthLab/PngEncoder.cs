using System.IO.Compression;

namespace Vivarium.Sim.Tests.GrowthLab;

/// <summary>
/// Minimal, dependency-free PNG encoder (8-bit RGB, no filtering, no interlace) via
/// <see cref="ZLibStream"/> for the growth-lab timelapse frames (docs/overhaul/growth_models.md §12).
/// </summary>
public static class PngEncoder
{
    public static void WriteRgb(string path, int width, int height, byte[] rgb)
    {
        if (rgb.Length != width * height * 3) throw new ArgumentException("rgb buffer size does not match width*height*3");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        WriteSignature(fs);
        WriteChunk(fs, "IHDR", Ihdr(width, height));
        WriteChunk(fs, "IDAT", Deflate(Scanlines(width, height, rgb)));
        WriteChunk(fs, "IEND", Array.Empty<byte>());
    }

    private static byte[] Scanlines(int width, int height, byte[] rgb)
    {
        int stride = width * 3;
        var raw = new byte[(stride + 1) * height];
        for (int y = 0; y < height; y++)
        {
            raw[y * (stride + 1)] = 0; // filter type: None
            Buffer.BlockCopy(rgb, y * stride, raw, y * (stride + 1) + 1, stride);
        }
        return raw;
    }

    private static byte[] Deflate(byte[] raw)
    {
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Fastest, leaveOpen: true)) z.Write(raw, 0, raw.Length);
        return ms.ToArray();
    }

    private static byte[] Ihdr(int width, int height)
    {
        var b = new byte[13];
        WriteBE(b, 0, width);
        WriteBE(b, 4, height);
        b[8] = 8;  // bit depth
        b[9] = 2;  // colour type: truecolor (RGB)
        b[10] = 0; // compression
        b[11] = 0; // filter
        b[12] = 0; // interlace
        return b;
    }

    private static void WriteBE(byte[] buf, int offset, int value)
    {
        buf[offset] = (byte)(value >> 24);
        buf[offset + 1] = (byte)(value >> 16);
        buf[offset + 2] = (byte)(value >> 8);
        buf[offset + 3] = (byte)value;
    }

    private static void WriteSignature(Stream s) => s.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        var lenBytes = new byte[4];
        WriteBE(lenBytes, 0, data.Length);
        s.Write(lenBytes);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        uint crc = Crc32(typeBytes, data);
        var crcBytes = new byte[4];
        WriteBE(crcBytes, 0, unchecked((int)crc));
        s.Write(crcBytes);
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xedb88320 ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    private static uint Crc32(byte[] type, byte[] data)
    {
        uint crc = 0xffffffff;
        foreach (var b in type) crc = CrcTable[(crc ^ b) & 0xff] ^ (crc >> 8);
        foreach (var b in data) crc = CrcTable[(crc ^ b) & 0xff] ^ (crc >> 8);
        return crc ^ 0xffffffff;
    }
}
