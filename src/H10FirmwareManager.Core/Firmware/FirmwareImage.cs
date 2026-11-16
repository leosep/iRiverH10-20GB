using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace H10FirmwareManager.Core.Firmware;

/// <summary>
/// An iRiver H10 firmware file. Handles both the original mi4 format and the
/// Rockbox bootloader (a renamed mi4 whose first 4 bytes and 0x1F8 signature differ).
/// </summary>
public sealed class FirmwareImage
{
    private FirmwareImage(string path, byte[] data)
    {
        Path = path;
        Data = data;
        FileName = System.IO.Path.GetFileName(path);
        Size = data.LongLength;
    }

    public string Path { get; }
    public string FileName { get; }
    public long Size { get; }
    public byte[] Data { get; }
    public Mi4Header? Header { get; private init; }
    public bool IsMi4 => Header is { MagicValid: true };
    public bool IsRockbox { get; private init; }
    public uint? ComputedCrc32 { get; private init; }
    public bool CrcValid { get; private init; }

    /// <summary>True when the CRC was actually calculated. Header-only scans cannot know it.</summary>
    public bool CrcChecked { get; private init; }
    public TeaKey? Key { get; private init; }
    public long? MagicOffset { get; private init; }
    public string? KeySearchNote { get; private init; }
    public string Sha256 { get; private init; } = string.Empty;
    public string Md5 { get; private init; } = string.Empty;

    public string VersionText => Header?.VersionText ?? "—";

    public string KindText => IsRockbox
        ? "Rockbox bootloader"
        : IsMi4 ? "iRiver mi4 firmware" : "Unknown file";

    public string StatusText => IsRockbox
        ? "Rockbox bootloader detected"
        : IsMi4 && CrcValid ? "Valid iRiver firmware"
        : IsMi4 && !CrcChecked ? "iRiver firmware (CRC not checked)"
        : IsMi4 ? "Invalid firmware CRC"
        : "Not an mi4 firmware";

    public static FirmwareImage Load(string path, bool computeHashes = true, bool detectKey = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        byte[] data = File.ReadAllBytes(path);
        return FromBytes(path, data, computeHashes, detectKey);
    }

    public static FirmwareImage FromBytes(string path, byte[] data, bool computeHashes = true, bool detectKey = true)
    {
        var header = Mi4Header.TryParse(data);
        bool isRockbox = data.Length >= 0x1FC &&
                         data[0x1F8] == (byte)'R' && data[0x1F9] == (byte)'B' &&
                         data[0x1FA] == (byte)'B' && data[0x1FB] == (byte)'L';

        uint? crc = null;
        bool crcValid = false;
        bool crcChecked = false;
        if (header is { MagicValid: true } && data.Length > Mi4Header.HeaderSize)
        {
            crc = Crc32.Compute(data.AsSpan(Mi4Header.HeaderSize));
            crcValid = crc.Value == header.StoredCrc32;
            crcChecked = true;
        }

        TeaKey? key = null;
        long? magicOffset = null;
        string? note = null;
        if (detectKey && header is { MagicValid: true, CryptoType: 2 } && data.Length > 0x400)
        {
            key = TryFindKey(data, header, out long foundOffset, out note);
            if (key is not null)
            {
                magicOffset = foundOffset;
            }
        }

        string sha256 = string.Empty;
        string md5 = string.Empty;
        if (computeHashes)
        {
            sha256 = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
            md5 = Convert.ToHexString(MD5.HashData(data)).ToLowerInvariant();
        }

        return new FirmwareImage(path, data)
        {
            Header = header,
            IsRockbox = isRockbox,
            ComputedCrc32 = crc,
            CrcValid = crcValid,
            CrcChecked = crcChecked,
            Key = key,
            MagicOffset = magicOffset,
            KeySearchNote = note,
            Sha256 = sha256,
            Md5 = md5
        };
    }

    /// <summary>Reads only the header and Rockbox signature; cheap enough for polling a device.</summary>
    public static FirmwareImage LoadHeaderOnly(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        int length = (int)Math.Min(fs.Length, Mi4Header.HeaderSize);
        byte[] headerBytes = new byte[Mi4Header.HeaderSize];
        fs.ReadExactly(headerBytes, 0, length);
        var header = Mi4Header.TryParse(headerBytes);
        bool isRockbox = length >= 0x1FC &&
                         headerBytes[0x1F8] == (byte)'R' && headerBytes[0x1F9] == (byte)'B' &&
                         headerBytes[0x1FA] == (byte)'B' && headerBytes[0x1FB] == (byte)'L';

        return new FirmwareImage(path, headerBytes)
        {
            Header = header,
            IsRockbox = isRockbox,
            Sha256 = string.Empty,
            Md5 = string.Empty
        };
    }

    private static TeaKey? TryFindKey(byte[] data, Mi4Header header, out long magicOffset, out string? note)
    {
        note = null;
        long payloadStart = header.PayloadOffset;
        long labeledMagicOffset = -1;

        // The plaintext boot stub carries a label at 0x2E0. When its magic is 0x100,
        // the pointer at 0x2E8 points to the end of the encrypted payload.
        if (header.PlaintextLength >= 0xEC && data.Length >= 0x2EC)
        {
            uint label = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0x2E0, 4));
            if (label == 0x100)
            {
                uint pointer = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0x2E8, 4));
                long candidate = Mi4Header.HeaderSize + pointer - 4;
                if (candidate >= payloadStart && candidate + 4 <= data.Length)
                {
                    labeledMagicOffset = candidate;
                }
            }
        }

        long[] candidates = labeledMagicOffset >= 0
            ? [labeledMagicOffset, data.Length - 4]
            : [data.Length - 4];

        foreach (long candidate in candidates.Distinct())
        {
            if (candidate < payloadStart || candidate + 4 > data.Length)
            {
                continue;
            }

            long relative = candidate - payloadStart;
            long blockStart = payloadStart + (relative / 8) * 8;
            int offsetInBlock = (int)(candidate - blockStart);
            int blockIndex = (int)((blockStart - payloadStart) / 8);

            foreach (var key in Mi4Crypto.KnownKeys)
            {
                var block = data.AsSpan((int)blockStart, 8).ToArray();
                Mi4Crypto.DecryptBlock(block, Mi4Crypto.AddToKey(key.ToArray(), blockIndex));
                uint magic = BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(offsetInBlock, 4));
                if (magic == Mi4Crypto.VerifyMagic)
                {
                    magicOffset = candidate;
                    return key;
                }
            }
        }

        magicOffset = labeledMagicOffset >= 0 ? labeledMagicOffset : data.Length - 4;
        note = "No known TEA key matched the verification magic.";
        return null;
    }

    /// <summary>
    /// Returns the decrypted image: 512-byte header + plaintext boot stub + decrypted payload.
    /// Returns the original bytes when no key is available.
    /// </summary>
    public byte[] Decrypt()
    {
        if (!IsMi4 || Header is null || Header.CryptoType != 2 || Key is null)
        {
            return Data.ToArray();
        }

        var result = Data.ToArray();
        int payloadStart = (int)Header.PayloadOffset;
        int length = result.Length - payloadStart;
        if (length <= 0)
        {
            return result;
        }

        byte[] decrypted = Mi4Crypto.Decrypt(result.AsSpan(payloadStart, length), Key);
        Buffer.BlockCopy(decrypted, 0, result, payloadStart, decrypted.Length);
        return result;
    }

    public bool DecryptedStartsWithPortalPlayer()
    {
        if (Data.Length <= 0x400)
        {
            return false;
        }
        byte[] clear = Decrypt();
        var tail = clear.AsSpan(0x400);
        int index = tail.IndexOf("portalplayer"u8);
        return index is >= 0 and < 0x2000;
    }

    public IReadOnlyList<string> ExtractStrings(int minLength = 5, int maxCount = 400)
    {
        byte[] source = IsMi4 && Key is not null ? Decrypt() : Data;
        var results = new List<string>(maxCount);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var current = new StringBuilder();
        foreach (byte b in source)
        {
            if (b >= 32 && b < 127)
            {
                current.Append((char)b);
            }
            else
            {
                AddIfUseful(current.ToString());
                current.Clear();
            }
        }
        AddIfUseful(current.ToString());

        // UTF-16LE strings (the firmware embeds XML metadata this way).
        current.Clear();
        for (int i = 0; i + 1 < source.Length; i += 2)
        {
            char c = (char)BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(i, 2));
            if (c >= 32 && c < 127)
            {
                current.Append(c);
            }
            else
            {
                AddIfUseful(current.ToString());
                current.Clear();
            }
        }
        AddIfUseful(current.ToString());

        return results;

        void AddIfUseful(string value)
        {
            if (value.Length < minLength || results.Count >= maxCount)
            {
                return;
            }
            value = value.Trim();
            if (value.Length >= minLength && seen.Add(value))
            {
                results.Add(value);
            }
        }
    }

    public string ToReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"File:             {Path}");
        sb.AppendLine($"Size:             {Size:N0} bytes");
        sb.AppendLine($"Kind:             {KindText}");
        sb.AppendLine($"SHA-256:          {(Sha256.Length > 0 ? Sha256 : "—")}");
        sb.AppendLine($"MD5:              {(Md5.Length > 0 ? Md5 : "—")}");
        if (Header is not null)
        {
            sb.AppendLine($"Magic:            {Header.Magic} ({(Header.MagicValid ? "valid" : "invalid")})");
            sb.AppendLine($"Version:          {Header.VersionText} (0x{Header.Version:X8})");
            sb.AppendLine($"Crypto:           {Header.CryptoText}");
            sb.AppendLine($"Total length:     {Header.TotalLength:N0} bytes");
            sb.AppendLine($"Plain length:     {Header.PlaintextLength:N0} bytes");
            sb.AppendLine($"Data length:      {Header.DataLength:N0} bytes");
            sb.AppendLine($"Stored CRC-32:    0x{Header.StoredCrc32:X8}");
            sb.AppendLine($"Computed CRC-32:  0x{ComputedCrc32.GetValueOrDefault():X8} ({(CrcValid ? "valid" : "INVALID")})");
            sb.AppendLine($"TEA key:          {(Key is null ? "not identified" : $"{Key.Name} [{Key.Hex}]")}");
            sb.AppendLine($"Magic offset:     {(MagicOffset is null ? "—" : $"0x{MagicOffset:X}")}");
            if (!string.IsNullOrEmpty(KeySearchNote))
            {
                sb.AppendLine($"Key note:         {KeySearchNote}");
            }
        }
        sb.AppendLine($"Rockbox:          {(IsRockbox ? "yes (RBBL signature at 0x1F8)" : "no")}");
        return sb.ToString();
    }
}
