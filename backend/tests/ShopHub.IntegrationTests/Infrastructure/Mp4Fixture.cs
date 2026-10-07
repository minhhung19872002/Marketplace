using System.Buffers.Binary;
using System.Text;

namespace ShopHub.IntegrationTests.Infrastructure;

/// <summary>
/// A tiny valid MP4 (ftyp + moov[mvhd 3 s, udta with a ©xyz GPS tag, trak[udta]] + mdat) — the shape a phone writes,
/// with the location of where it was filmed.
/// </summary>
public static class Mp4Fixture
{
    public const string Gps = "+21.0285+105.8542/";

    public static byte[] Build()
    {
        var mvhd = new byte[100];
        BinaryPrimitives.WriteUInt32BigEndian(mvhd.AsSpan(12), 1000);  // timescale
        BinaryPrimitives.WriteUInt32BigEndian(mvhd.AsSpan(16), 3000);  // duration: 3 s
        var xyz = Box([0xA9, (byte)'x', (byte)'y', (byte)'z'], [0x00, 0x12, 0x15, 0xC7, .. Encoding.ASCII.GetBytes(Gps)]);
        var moov = Box("moov", [.. Box("mvhd", mvhd), .. Box("udta", xyz), .. Box("trak", Box("udta", Box("titl"u8.ToArray(), "Nha rieng"u8.ToArray())))]);
        return [.. Box("ftyp", [.. "isom"u8, 0, 0, 2, 0, .. "isommp41"u8]), .. moov, .. Box("mdat", Enumerable.Repeat((byte)0x11, 64).ToArray())];
    }

    private static byte[] Box(string type, byte[] payload) => Box(Encoding.ASCII.GetBytes(type), payload);

    private static byte[] Box(byte[] type, byte[] payload)
    {
        var box = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        type.CopyTo(box, 4);
        payload.CopyTo(box, 8);
        return box;
    }
}
