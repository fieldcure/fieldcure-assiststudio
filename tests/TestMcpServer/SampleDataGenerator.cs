using System.IO.Compression;

namespace TestMcpServer;

/// <summary>
/// Generates minimal binary media samples for testing without external file dependencies.
/// </summary>
public static class SampleDataGenerator
{
    /// <summary>
    /// Generates a minimal valid PNG image (solid color rectangle).
    /// Constructs PNG manually: signature + IHDR + IDAT (zlib-compressed raw scanlines) + IEND.
    /// </summary>
    public static byte[] GeneratePng(int width = 16, int height = 16, byte r = 0x33, byte g = 0x99, byte b = 0xFF)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // PNG signature
        bw.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        // IHDR chunk (13 bytes data)
        var ihdrData = new byte[13];
        WriteBigEndianInt32(ihdrData, 0, width);
        WriteBigEndianInt32(ihdrData, 4, height);
        ihdrData[8] = 8;  // bit depth
        ihdrData[9] = 2;  // color type: RGB
        ihdrData[10] = 0; // compression
        ihdrData[11] = 0; // filter
        ihdrData[12] = 0; // interlace
        WriteChunk(bw, "IHDR", ihdrData);

        // Build raw scanlines: filter byte (0) + RGB per pixel per row
        var rawSize = height * (1 + width * 3);
        var raw = new byte[rawSize];
        var idx = 0;
        for (var y = 0; y < height; y++)
        {
            raw[idx++] = 0; // filter: none
            for (var x = 0; x < width; x++)
            {
                raw[idx++] = r;
                raw[idx++] = g;
                raw[idx++] = b;
            }
        }

        // Compress with zlib (DeflateStream wrapped in zlib header/checksum)
        var compressed = ZlibCompress(raw);
        WriteChunk(bw, "IDAT", compressed);

        // IEND chunk
        WriteChunk(bw, "IEND", []);

        return ms.ToArray();
    }

    /// <summary>
    /// Generates a valid WAV file with a sine wave tone.
    /// Constructs RIFF/WAVE header (44 bytes) + PCM 16-bit mono data.
    /// </summary>
    public static byte[] GenerateWav(double durationSeconds = 1.0, int sampleRate = 44100, double frequency = 440.0)
    {
        var numSamples = (int)(sampleRate * durationSeconds);
        var dataSize = numSamples * 2; // 16-bit mono = 2 bytes per sample
        var fileSize = 44 + dataSize;

        var wav = new byte[fileSize];
        using var ms = new MemoryStream(wav);
        using var bw = new BinaryWriter(ms);

        // RIFF header
        bw.Write("RIFF"u8);
        bw.Write(fileSize - 8);    // file size - 8
        bw.Write("WAVE"u8);

        // fmt sub-chunk
        bw.Write("fmt "u8);
        bw.Write(16);              // sub-chunk size
        bw.Write((short)1);        // PCM format
        bw.Write((short)1);        // mono
        bw.Write(sampleRate);      // sample rate
        bw.Write(sampleRate * 2);  // byte rate (sampleRate * channels * bitsPerSample/8)
        bw.Write((short)2);        // block align (channels * bitsPerSample/8)
        bw.Write((short)16);       // bits per sample

        // data sub-chunk
        bw.Write("data"u8);
        bw.Write(dataSize);

        // Generate sine wave PCM samples
        for (var i = 0; i < numSamples; i++)
        {
            var sample = (short)(Math.Sin(2.0 * Math.PI * frequency * i / sampleRate) * 16000);
            bw.Write(sample);
        }

        return wav;
    }

    /// <summary>
    /// Generates a WAV file of a specified size in megabytes for large media testing.
    /// Uses zero-filled PCM data for efficiency.
    /// </summary>
    public static byte[] GenerateLargeWav(int sizeMb)
    {
        var dataSize = sizeMb * 1024 * 1024;
        var fileSize = 44 + dataSize;
        var wav = new byte[fileSize];
        using var ms = new MemoryStream(wav);
        using var bw = new BinaryWriter(ms);

        var sampleRate = 44100;

        // RIFF header
        bw.Write("RIFF"u8);
        bw.Write(fileSize - 8);
        bw.Write("WAVE"u8);

        // fmt sub-chunk
        bw.Write("fmt "u8);
        bw.Write(16);
        bw.Write((short)1);        // PCM
        bw.Write((short)1);        // mono
        bw.Write(sampleRate);
        bw.Write(sampleRate * 2);
        bw.Write((short)2);
        bw.Write((short)16);

        // data sub-chunk header (data is zero-filled by default)
        bw.Write("data"u8);
        bw.Write(dataSize);
        // Remaining bytes are already zero (silence)

        return wav;
    }

    /// <summary>
    /// Generates arbitrary bytes of a given size for download/fallback testing.
    /// </summary>
    public static byte[] GenerateBytes(int sizeBytes)
    {
        var data = new byte[sizeBytes];
        // Fill with a recognizable pattern
        for (var i = 0; i < sizeBytes; i++)
            data[i] = (byte)(i % 256);
        return data;
    }

    /// <summary>
    /// Writes a PNG chunk (length + type + data + CRC32).
    /// </summary>
    private static void WriteChunk(BinaryWriter bw, string type, byte[] data)
    {
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        WriteBigEndianInt32(bw, data.Length);
        bw.Write(typeBytes);
        bw.Write(data);

        // CRC32 over type + data
        var crc = Crc32(typeBytes, data);
        WriteBigEndianInt32(bw, (int)crc);
    }

    /// <summary>
    /// Writes a big-endian 32-bit integer to a byte array at the given offset.
    /// </summary>
    private static void WriteBigEndianInt32(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    /// <summary>
    /// Writes a big-endian 32-bit integer to a BinaryWriter.
    /// </summary>
    private static void WriteBigEndianInt32(BinaryWriter bw, int value)
    {
        bw.Write((byte)(value >> 24));
        bw.Write((byte)(value >> 16));
        bw.Write((byte)(value >> 8));
        bw.Write((byte)value);
    }

    /// <summary>
    /// Compresses raw data with zlib wrapping (2-byte header + deflate + 4-byte Adler32).
    /// </summary>
    private static byte[] ZlibCompress(byte[] raw)
    {
        using var output = new MemoryStream();

        // Zlib header: CMF=0x78 (deflate, window=32768), FLG=0x01 (no dict, check bits)
        output.WriteByte(0x78);
        output.WriteByte(0x01);

        using (var deflate = new DeflateStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            deflate.Write(raw, 0, raw.Length);
        }

        // Adler32 checksum (big-endian)
        var adler = Adler32(raw);
        output.WriteByte((byte)(adler >> 24));
        output.WriteByte((byte)(adler >> 16));
        output.WriteByte((byte)(adler >> 8));
        output.WriteByte((byte)adler);

        return output.ToArray();
    }

    /// <summary>
    /// Computes Adler-32 checksum of the given data.
    /// </summary>
    private static uint Adler32(byte[] data)
    {
        uint a = 1, b = 0;
        foreach (var t in data)
        {
            a = (a + t) % 65521;
            b = (b + a) % 65521;
        }
        return (b << 16) | a;
    }

    /// <summary>
    /// Standard CRC-32 used by PNG chunks.
    /// </summary>
    private static uint Crc32(byte[] typeBytes, byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in typeBytes) crc = Crc32Update(crc, b);
        foreach (var b in data) crc = Crc32Update(crc, b);
        return crc ^ 0xFFFFFFFF;
    }

    /// <summary>
    /// Updates CRC-32 with a single byte.
    /// </summary>
    private static uint Crc32Update(uint crc, byte b)
    {
        crc ^= b;
        for (var i = 0; i < 8; i++)
            crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        return crc;
    }
}
