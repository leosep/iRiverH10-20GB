namespace H10FirmwareManager.Core.Firmware;

/// <summary>A PortalPlayer mi4 TEA key. The name matches the public mi4code key table.</summary>
public sealed record TeaKey(string Name, uint K0, uint K1, uint K2, uint K3)
{
    public uint[] ToArray() => [K0, K1, K2, K3];

    public string Hex => $"0x{K0:X8} 0x{K1:X8} 0x{K2:X8} 0x{K3:X8}";
}

/// <summary>
/// PortalPlayer mi4 encryption. This is the 8-round TEA variant used by mi4code and
/// Rockbox's mi4-loader: the key is incremented for every 8-byte block.
/// </summary>
public static class Mi4Crypto
{
    public const uint YInit = 0xF1BBCDC8u;
    public const uint YStep = 0x61C88647u;
    public const uint VerifyMagic = 0xAA55AA55u;

    public static readonly IReadOnlyList<TeaKey> KnownKeys =
    [
        new("sansa",     0xE494E96E, 0x3EE32966, 0x6F48512B, 0xA93FBB42),
        new("sansa_gh",  0xD7B10538, 0xC662945B, 0x1B3FCE68, 0xF389C0E6),
        new("sansa_103", 0x1D29DDC0, 0x2579C2CD, 0xCE339E1A, 0x75465DFE),
        new("20gc_eng",  0x0240772C, 0x6F3329B5, 0x3EC9A6C5, 0xB0C9E493),
        new("20gc_fre",  0xBEDE8817, 0xB23BFE4F, 0x80AA682D, 0xD13F598C),
        new("c200",      0xBF2D06FA, 0xF0E23D59, 0x29738132, 0xE2D04CA7),
        new("c200_003",  0x2A7968DE, 0x15127979, 0x142E60A7, 0xE49C1893),
        new("c200_106",  0xA913D139, 0xF842F398, 0x3E03F1A6, 0x060EE012)
    ];

    public static TeaKey? FindByName(string name) =>
        KnownKeys.FirstOrDefault(k => string.Equals(k.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Adds a 32-bit block index to the 128-bit little-endian key, as mi4code does.</summary>
    public static uint[] AddToKey(uint[] key, int add)
    {
        var result = new uint[4];
        uint carry = (uint)add;
        for (int i = 0; i < 4; i++)
        {
            uint sum = key[i] + carry;
            carry = sum < key[i] ? 1u : 0u;
            result[i] = sum;
        }
        return result;
    }

    public static void DecryptBlock(Span<byte> data, uint[] key)
    {
        if (data.Length < 8)
        {
            throw new ArgumentException("A TEA block is 8 bytes.", nameof(data));
        }

        uint d1 = BitConverter.ToUInt32(data[..4]);
        uint d2 = BitConverter.ToUInt32(data.Slice(4, 4));
        uint y = YInit;

        for (int i = 0; i < 8; i++)
        {
            uint x = key[2] + (d1 << 4);
            x ^= d1 + y;
            x ^= key[3] + (d1 >> 5);
            d2 -= x;

            x = key[0] + (d2 << 4);
            x ^= d2 + y;
            x ^= key[1] + (d2 >> 5);
            d1 -= x;

            y += YStep;
        }

        BitConverter.TryWriteBytes(data[..4], d1);
        BitConverter.TryWriteBytes(data.Slice(4, 4), d2);
    }

    public static void EncryptBlock(Span<byte> data, uint[] key)
    {
        if (data.Length < 8)
        {
            throw new ArgumentException("A TEA block is 8 bytes.", nameof(data));
        }

        uint d1 = BitConverter.ToUInt32(data[..4]);
        uint d2 = BitConverter.ToUInt32(data.Slice(4, 4));
        uint y = unchecked(YInit + 7u * YStep);

        for (int i = 0; i < 8; i++)
        {
            uint x = key[0] + (d2 << 4);
            x ^= d2 + y;
            x ^= key[1] + (d2 >> 5);
            d1 += x;

            x = key[2] + (d1 << 4);
            x ^= d1 + y;
            x ^= key[3] + (d1 >> 5);
            d2 += x;

            y -= YStep;
        }

        BitConverter.TryWriteBytes(data[..4], d1);
        BitConverter.TryWriteBytes(data.Slice(4, 4), d2);
    }

    /// <summary>Decrypts a whole payload, starting at block index 0 of that payload.</summary>
    public static byte[] Decrypt(ReadOnlySpan<byte> encrypted, TeaKey key, int startBlockIndex = 0)
    {
        var result = encrypted.ToArray();
        var words = key.ToArray();
        for (int offset = 0, block = startBlockIndex; offset + 8 <= result.Length; offset += 8, block++)
        {
            var blockKey = AddToKey(words, block);
            DecryptBlock(result.AsSpan(offset, 8), blockKey);
        }
        return result;
    }

    public static byte[] Encrypt(ReadOnlySpan<byte> plain, TeaKey key, int startBlockIndex = 0)
    {
        var result = plain.ToArray();
        var words = key.ToArray();
        for (int offset = 0, block = startBlockIndex; offset + 8 <= result.Length; offset += 8, block++)
        {
            var blockKey = AddToKey(words, block);
            EncryptBlock(result.AsSpan(offset, 8), blockKey);
        }
        return result;
    }
}
