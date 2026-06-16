using SharpCompress.Compressors.LZMA;
using SourcePorter.BspSource.Common;
using SourcePorter.BspSource.Lib.Util;

namespace SourcePorter.BspSource.Io;

/// <summary>
/// LZMA decoding helper, ported from BSPSource's <c>LzmaUtil</c>. Source compresses
/// individual BSP lumps with a custom 17-byte header
/// (<c>"LZMA"</c> id + uncompressed size + compressed size + 1 props byte + dict
/// size) followed by raw LZMA data. The Java tool uses org.tukaani:xz; we use
/// SharpCompress's <see cref="LzmaStream"/>, which takes the standard 5-byte
/// LZMA properties (props byte + little-endian dict size).
/// </summary>
public static class LzmaUtil
{
    private static readonly Logger L = LogManager.GetLogger();

    public static readonly int LzmaId = StringMacroUtils.MakeId("LZMA");
    public const int HeaderSize = 17;

    public static bool IsCompressed(ByteBuffer buffer)
    {
        var bb = buffer.Duplicate().SetOrder(ByteOrder.LittleEndian);
        bb.Rewind();
        return bb.Remaining >= HeaderSize && bb.GetInt() == LzmaId;
    }

    public static ByteBuffer Uncompress(ByteBuffer buffer)
    {
        var bo = buffer.Order;
        var bbc = buffer.Duplicate().SetOrder(ByteOrder.LittleEndian);
        bbc.Rewind();

        if (bbc.Remaining < HeaderSize || bbc.GetInt() != LzmaId)
            throw new IOException("Buffer is not compressed");

        int actualSize = bbc.GetInt();
        int lzmaSize = bbc.GetInt();
        byte probByte = bbc.Get();
        int dictSize = bbc.GetInt();

        int lzmaSizeBuf = bbc.Limit - HeaderSize;
        if (lzmaSizeBuf != lzmaSize)
            L.Warn("Difference in LZMA data length: found {} bytes, expected {}", lzmaSizeBuf, lzmaSize);

        // remaining bytes after the header are the raw LZMA stream
        var compressed = bbc.Slice().ToArray();

        // SharpCompress LZMA properties: [propByte, dictSize as 4-byte LE]
        var properties = new byte[5];
        properties[0] = probByte;
        BitConverter.GetBytes(dictSize).CopyTo(properties, 1); // BitConverter is LE on supported platforms

        using var input = new MemoryStream(compressed, writable: false);
        using var lzma = LzmaStream.Create(properties, input, lzmaSizeBuf, actualSize);

        var output = new byte[actualSize];
        int read = 0;
        while (read < actualSize)
        {
            int n = lzma.Read(output, read, actualSize - read);
            if (n <= 0) break;
            read += n;
        }
        if (read != actualSize)
            L.Warn("LZMA produced {} bytes, expected {}", read, actualSize);

        return ByteBuffer.Wrap(output).SetOrder(bo);
    }

    /// <summary>BSP saving is out of scope for the decompiler.</summary>
    public static ByteBuffer Compress(ByteBuffer buffer) =>
        throw new NotSupportedException("BSP lump compression is not supported (decompiler is read-only).");
}
