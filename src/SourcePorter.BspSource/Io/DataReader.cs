using System.Text;

namespace SourcePorter.BspSource.Io;

/// <summary>
/// Byte-order-aware binary reader over a <see cref="ByteBuffer"/>. Reimplements the
/// subset of <c>info.ata4.io.DataReader</c> (external <c>ioutils</c> library) that
/// BSPSource's structs and lump readers use.
///
/// Signed/unsigned note: Java's <c>byte</c> is signed, so <see cref="ReadByte"/>
/// returns <see cref="sbyte"/> (which sign-extends to int implicitly, matching the
/// Java port one-to-one). Use <see cref="ReadUnsignedByte"/> where the Java code
/// masks with <c>&amp; 0xFF</c>.
/// </summary>
public sealed class DataReader
{
    private readonly ByteBuffer _buf;

    /// <summary>Latin-1 maps each byte to one char losslessly; BSP strings are ASCII.</summary>
    private static readonly Encoding StringEncoding = Encoding.Latin1;

    internal DataReader(ByteBuffer buf) => _buf = buf;

    public ByteBuffer Buffer => _buf;

    public ByteOrder Order
    {
        get => _buf.Order;
        set => _buf.SetOrder(value);
    }

    /// <summary>Current position, in bytes (used by <c>DataReaderUtil.readDStruct</c> size checks).</summary>
    public long Position => _buf.Position;

    public void Seek(long pos) => _buf.SetPosition((int)pos);

    /// <summary>Relative/absolute seek, mirroring <c>info.ata4.io.Seekable.seek(offset, Origin)</c>.</summary>
    public void Seek(long offset, SeekOrigin origin) => _buf.SetPosition(origin switch
    {
        SeekOrigin.Begin => (int)offset,
        SeekOrigin.Current => (int)(_buf.Position + offset),
        SeekOrigin.End => (int)(_buf.Limit + offset),
        _ => throw new ArgumentOutOfRangeException(nameof(origin)),
    });

    public long Remaining => _buf.Remaining;

    public sbyte ReadByte() => unchecked((sbyte)_buf.Get());
    public int ReadUnsignedByte() => _buf.Get() & 0xFF;
    public bool ReadBoolean() => _buf.Get() != 0;

    public short ReadShort() => _buf.GetShort();
    public int ReadUnsignedShort() => _buf.GetShort() & 0xFFFF;

    public int ReadInt() => _buf.GetInt();
    public long ReadUnsignedInt() => _buf.GetInt() & 0xFFFFFFFFL;

    public float ReadFloat() => _buf.GetFloat();
    public double ReadDouble() => _buf.GetDouble();

    /// <summary>Fills <paramref name="dst"/> completely.</summary>
    public void ReadBytes(byte[] dst) => _buf.Get(dst);

    /// <summary>Reads <paramref name="length"/> bytes; returns the string up to the first NUL.</summary>
    public string ReadStringFixed(int length)
    {
        var raw = new byte[length];
        _buf.Get(raw);
        int n = Array.IndexOf(raw, (byte)0);
        if (n < 0) n = length;
        return StringEncoding.GetString(raw, 0, n);
    }

    /// <summary>Reads bytes up to and including a NUL terminator; returns the string without the NUL.</summary>
    public string ReadStringNull()
    {
        var sb = new StringBuilder();
        while (_buf.HasRemaining)
        {
            byte b = _buf.Get();
            if (b == 0) break;
            sb.Append((char)b);
        }
        return sb.ToString();
    }
}

/// <summary>Factory methods mirroring <c>info.ata4.io.DataReaders</c>.</summary>
public static class DataReaders
{
    /// <summary>Mirrors <c>DataReaders.forByteBuffer</c>: reads from the buffer's current position honouring its order.</summary>
    public static DataReader ForByteBuffer(ByteBuffer buffer) => new(buffer);
}
