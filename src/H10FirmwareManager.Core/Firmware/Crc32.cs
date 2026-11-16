using System.Buffers;

namespace H10FirmwareManager.Core.Firmware;

/// <summary>
/// CRC-32 as used by iRiver firmware images: reflected polynomial 0xEDB88320,
/// initial value 0, no final XOR. The checksum covers the file from offset 0x200 to EOF.
/// </summary>
public static class Crc32
{
    private const uint Polynomial = 0xEDB88320u;
    private static readonly uint[] Table = CreateTable();

    private static uint[] CreateTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint value = i;
            for (int bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? (value >> 1) ^ Polynomial : value >> 1;
            }
            table[i] = value;
        }
        return table;
    }

    public static uint Compute(ReadOnlySpan<byte> data, uint initial = 0)
    {
        uint crc = initial;
        foreach (byte b in data)
        {
            crc = (crc >> 8) ^ Table[(crc ^ b) & 0xFF];
        }
        return crc;
    }

    public static uint ComputeStream(Stream stream, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        uint crc = 0;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);
        long total = 0;
        try
        {
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                crc = Compute(buffer.AsSpan(0, read), crc);
                total += read;
                progress?.Report(total);
            }
            return crc;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static uint ComputeFile(string path, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
        return ComputeStream(stream, progress, cancellationToken);
    }
}
