using System.Buffers.Binary;
using System.Text;

namespace H10FirmwareManager.Core.Firmware;

/// <summary>Header of a PortalPlayer mi4 image (512 bytes).</summary>
public sealed record Mi4Header(
    string Magic,
    uint Version,
    uint DataLength,
    uint StoredCrc32,
    uint CryptoType,
    uint TotalLength,
    uint PlaintextLength)
{
    public const int HeaderSize = 0x200;

    public bool MagicValid => Magic == "PPOS";

    public uint Major => (Version >> 16) & 0xFF;
    public uint Minor => (Version >> 8) & 0xFF;
    public uint Patch => Version & 0xFF;

    /// <summary>iRiver prints this value as "1.03.01".</summary>
    public string VersionText => $"{Major}.{Minor:D2}.{Patch:D2}";

    public string CryptoText => CryptoType switch
    {
        0 => "None",
        2 => "TEA",
        _ => $"Unknown ({CryptoType})"
    };

    public long PayloadOffset => HeaderSize + PlaintextLength;

    public static Mi4Header? TryParse(ReadOnlySpan<byte> header)
    {
        if (header.Length < HeaderSize)
        {
            return null;
        }

        string magic = Encoding.ASCII.GetString(header[..4]);
        return new Mi4Header(
            magic,
            BinaryPrimitives.ReadUInt32LittleEndian(header[4..]),
            BinaryPrimitives.ReadUInt32LittleEndian(header[8..]),
            BinaryPrimitives.ReadUInt32LittleEndian(header[0x0C..]),
            BinaryPrimitives.ReadUInt32LittleEndian(header[0x10..]),
            BinaryPrimitives.ReadUInt32LittleEndian(header[0x14..]),
            BinaryPrimitives.ReadUInt32LittleEndian(header[0x18..]));
    }
}
