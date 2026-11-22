using System.Buffers.Binary;
using MDGlint.Core.Services;

namespace MDGlint.Core.Tests;

public class ImageInfoTests
{
    /// <summary>A PNG's signature and header, which is all <see cref="ImageInfo"/> reads.</summary>
    public static byte[] Png(uint width, uint height)
    {
        var bytes = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), width);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20), height);
        return bytes;
    }

    private static byte[] Gif(string version, ushort width, ushort height)
    {
        var bytes = new byte[13];
        System.Text.Encoding.ASCII.GetBytes(version).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6), width);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), height);
        return bytes;
    }

    private static byte[] Bmp(uint headerSize, int width, int height)
    {
        var bytes = new byte[54];
        bytes[0] = (byte)'B';
        bytes[1] = (byte)'M';
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(14), headerSize);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18), width);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22), height);
        return bytes;
    }

    /// <summary>A JPEG's start, an APP0 segment, then whatever follows.</summary>
    private static byte[] Jpeg(params byte[] rest) => [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x04, 0x4A, 0x46, .. rest];

    private static byte[] Frame(byte marker, ushort width, ushort height) =>
        [0xFF, marker, 0x00, 0x11, 0x08, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 0x03];

    [Fact]
    public void ReadsAPng() => Assert.Equal(("image/png", 640, 480), ImageInfo.Read(Png(640, 480)));

    [Theory]
    [InlineData("GIF87a")]
    [InlineData("GIF89a")]
    public void ReadsAGif(string version) => Assert.Equal(("image/gif", 3, 2), ImageInfo.Read(Gif(version, 3, 2)));

    [Theory]
    [InlineData(10)]
    [InlineData(-10)] // Rows stored top-down.
    public void ReadsABitmap(int height) => Assert.Equal(("image/bmp", 20, 10), ImageInfo.Read(Bmp(40, 20, height)));

    [Fact]
    public void ReadsAJpegsFrame() =>
        Assert.Equal(("image/jpeg", 300, 200), ImageInfo.Read(Jpeg([.. Frame(0xC0, 300, 200)])));

    [Fact]
    public void ReadsAProgressiveJpegAfterFillBytesAndStandaloneMarkers() =>
        Assert.Equal(("image/jpeg", 7, 5), ImageInfo.Read(Jpeg([0xFF, 0xFF, 0xD0, 0xFF, 0xC4, 0x00, 0x02, .. Frame(0xC2, 7, 5)])));

    public static TheoryData<string, byte[]> Unreadable => new()
    {
        { "empty", [] },
        { "text", "hello, world, this is not an image"u8.ToArray() },
        { "short png", Png(1, 1)[..20] },
        { "other gif", Gif("GIF90a", 1, 1) },
        { "old bitmap header", Bmp(12, 1, 1) },
        { "short bitmap", Bmp(40, 1, 1)[..20] },
        { "bitmap of the most negative height", Bmp(40, 1, int.MinValue) },
        { "jpeg without a frame", Jpeg() },
        { "jpeg with a broken segment", Jpeg(0x00, 0x01, 0x02, 0x03) },
        { "jpeg with a too-short length", Jpeg(0xFF, 0xE1, 0x00, 0x01, 0x00) },
        { "jpeg with a cut-off frame", Jpeg(0xFF, 0xC0, 0x00, 0x11, 0x08, 0x00) },
        { "jpeg start only", [0xFF] },
        { "not a jpeg start", [0xFF, 0x00] },
        { "zero width", Png(0, 1) },
        { "zero height", Png(1, 0) },
        { "too wide", Png(ImageInfo.MaxSide + 1, 1) },
        { "too tall", Png(1, ImageInfo.MaxSide + 1) },
    };

    [Theory]
    [MemberData(nameof(Unreadable))]
    public void LeavesOutWhatItCantRead(string kind, byte[] bytes)
    {
        Assert.True(ImageInfo.Read(bytes) is null, kind);
    }
}
