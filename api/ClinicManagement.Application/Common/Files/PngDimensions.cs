using System.Buffers.Binary;

namespace ClinicManagement.Application.Common.Files;

/// <summary>
/// A PNG's pixel size, read from its <c>IHDR</c> chunk — no decode, no image library.
/// <para>
/// The upload validator checks a file's magic bytes and never its dimensions; a letterhead band is the first upload
/// whose acceptance depends on them, since a 600-pixel-wide band prints blurred across 210 mm.
/// </para>
/// </summary>
public static class PngDimensions
{
    private static ReadOnlySpan<byte> Signature => new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    /// <summary>Width and height in pixels, or null when the bytes do not open with a well-formed IHDR.</summary>
    public static (int Width, int Height)? Read(ReadOnlySpan<byte> png)
    {
        // Signature (8) · chunk length (4) · "IHDR" (4) · width (4) · height (4).
        if (png.Length < 24 || !png[..8].SequenceEqual(Signature) || !png.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            return null;
        }

        var width = BinaryPrimitives.ReadInt32BigEndian(png.Slice(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(png.Slice(20, 4));
        return width > 0 && height > 0 ? (width, height) : null;
    }
}
