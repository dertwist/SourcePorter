using System.Text;

namespace SourcePorter.BspSource.Io;

/// <summary>
/// Byte-order-aware binary writer over a <see cref="ByteBuffer"/>, mirroring the
/// subset of <c>info.ata4.io.DataWriter</c> that BSPSource's <c>DStruct.write</c>
/// methods call. BSPSource saving (writing a .bsp back) is out of scope for the
/// decompiler, but the writer must exist so the ported structs compile, and it is
/// kept correct for completeness / round-trip tests.
/// </summary>
public sealed class DataWriter
{
    private readonly ByteBuffer _buf;
    private static readonly Encoding StringEncoding = Encoding.Latin1;

    internal DataWriter(ByteBuffer buf) => _buf = buf;

    public ByteBuffer Buffer => _buf;

    public ByteOrder Order
    {
        get => _buf.Order;
        set => _buf.SetOrder(value);
    }

    public long Position => _buf.Position;
    public void Seek(long pos) => _buf.SetPosition((int)pos);

    public void WriteByte(int value) => _buf.Put((byte)value);
    public void WriteUnsignedByte(int value) => _buf.Put((byte)(value & 0xFF));
    public void WriteBoolean(bool value) => _buf.Put((byte)(value ? 1 : 0));

    public void WriteShort(int value) => _buf.PutShort((short)value);
    public void WriteUnsignedShort(int value) => _buf.PutShort((short)(value & 0xFFFF));

    public void WriteInt(int value) => _buf.PutInt(value);
    public void WriteUnsignedInt(long value) => _buf.PutInt((int)(value & 0xFFFFFFFFL));

    public void WriteFloat(float value) => _buf.PutFloat(value);
    public void WriteDouble(double value) => _buf.PutDouble(value);

    public void WriteBytes(byte[] src) => _buf.Put(src);
    public void WriteBytes(byte[] src, int offset, int length) => _buf.Put(src, offset, length);

    /// <summary>Writes the string as bytes, padded with NULs or truncated to <paramref name="length"/>.</summary>
    public void WriteStringFixed(string value, int length)
    {
        var raw = new byte[length];
        var encoded = StringEncoding.GetBytes(value);
        Array.Copy(encoded, raw, Math.Min(encoded.Length, length));
        _buf.Put(raw);
    }

    public void WriteStringNull(string value)
    {
        _buf.Put(StringEncoding.GetBytes(value));
        _buf.Put((byte)0);
    }
}

/// <summary>Factory methods mirroring <c>info.ata4.io.DataWriters</c>.</summary>
public static class DataWriters
{
    public static DataWriter ForByteBuffer(ByteBuffer buffer) => new(buffer);
}
