using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace ClinicManagement.UnitTests.Common;

/// <summary>
/// A real, decodable white PNG of any size — the letterhead rules turn on a band's pixel dimensions, and the
/// renderer must be able to draw it.
/// </summary>
internal static class TestPng
{
    public static byte[] White(int width, int height)
    {
        using var png = new MemoryStream();
        png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), height);
        header[8] = 8; // bit depth
        header[9] = 0; // greyscale
        WriteChunk(png, "IHDR", header);

        using var pixels = new MemoryStream();
        using (var zlib = new ZLibStream(pixels, CompressionLevel.Fastest, leaveOpen: true))
        {
            var row = new byte[width + 1]; // filter byte 0, then one byte per pixel
            Array.Fill(row, (byte)0xFF, 1, width);
            for (var y = 0; y < height; y++)
            {
                zlib.Write(row);
            }
        }

        WriteChunk(png, "IDAT", pixels.ToArray());
        WriteChunk(png, "IEND", Array.Empty<byte>());
        return png.ToArray();
    }

    private static void WriteChunk(Stream png, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        png.Write(length);

        var typeAndData = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        png.Write(typeAndData);

        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(typeAndData));
        png.Write(crc);
    }

    private static uint Crc32(byte[] bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }
}
