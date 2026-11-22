using System.Buffers.Binary;

namespace MDGlint.Core.Services;

/// <summary>
/// The type and pixel size of an image Word can show (PNG, JPEG, GIF or BMP), read from the
/// file's own header rather than the type it was served or named with. Anything else, or a header
/// that doesn't hold together, is null.
/// </summary>
public static class ImageInfo
{
    /// <summary>Larger sides are treated as broken headers.</summary>
    public const int MaxSide = 65535;

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    // JPEG start-of-frame markers, which hold the size; C4, C8 and CC are other segments.
    private static readonly HashSet<byte> FrameMarkers =
        [0xC0, 0xC1, 0xC2, 0xC3, 0xC5, 0xC6, 0xC7, 0xC9, 0xCA, 0xCB, 0xCD, 0xCE, 0xCF];

    // JPEG markers that stand alone, with no length after them.
    private static readonly HashSet<byte> StandaloneMarkers = [0x01, 0xD0, 0xD1, 0xD2, 0xD3, 0xD4, 0xD5, 0xD6, 0xD7];

    public static (string ContentType, int Width, int Height)? Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 24 && bytes[..8].SequenceEqual(PngSignature))
        {
            return Valid("image/png", BinaryPrimitives.ReadUInt32BigEndian(bytes[16..]), BinaryPrimitives.ReadUInt32BigEndian(bytes[20..]));
        }

        if (bytes.Length >= 10 && (bytes[..6].SequenceEqual("GIF87a"u8) || bytes[..6].SequenceEqual("GIF89a"u8)))
        {
            return Valid("image/gif", BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]), BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..]));
        }

        // Only the Windows bitmap header (40 bytes or more), whose height is negative for top-down rows.
        if (bytes.Length >= 26 && bytes[..2].SequenceEqual("BM"u8) && BinaryPrimitives.ReadUInt32LittleEndian(bytes[14..]) >= 40)
        {
            return Valid("image/bmp", BinaryPrimitives.ReadInt32LittleEndian(bytes[18..]),
                Math.Abs((long)BinaryPrimitives.ReadInt32LittleEndian(bytes[22..])));
        }

        return bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xD8 ? Jpeg(bytes) : null;
    }

    private static (string, int, int)? Jpeg(ReadOnlySpan<byte> bytes)
    {
        var i = 2;
        while (i + 3 < bytes.Length)
        {
            if (bytes[i] != 0xFF)
            {
                return null;
            }

            var marker = bytes[i + 1];
            if (marker == 0xFF)
            {
                i++; // A fill byte.
                continue;
            }

            if (StandaloneMarkers.Contains(marker))
            {
                i += 2;
                continue;
            }

            if (FrameMarkers.Contains(marker))
            {
                // Length, precision, then the height and the width.
                return i + 8 < bytes.Length
                    ? Valid("image/jpeg", BinaryPrimitives.ReadUInt16BigEndian(bytes[(i + 7)..]), BinaryPrimitives.ReadUInt16BigEndian(bytes[(i + 5)..]))
                    : null;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes[(i + 2)..]);
            if (length < 2)
            {
                return null;
            }

            i += 2 + length;
        }

        return null;
    }

    private static (string, int, int)? Valid(string contentType, long width, long height) =>
        width is > 0 and <= MaxSide && height is > 0 and <= MaxSide ? (contentType, (int)width, (int)height) : null;
}
