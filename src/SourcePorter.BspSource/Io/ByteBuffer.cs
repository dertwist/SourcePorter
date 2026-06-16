using System.Buffers.Binary;

namespace SourcePorter.BspSource.Io;

/// <summary>
/// A faithful, minimal port of <c>java.nio.ByteBuffer</c>. BSPSource passes lump
/// data around as <c>ByteBuffer</c>s and reads structs through a byte-order-aware
/// cursor, so reproducing the position/limit/mark/order model here keeps the lump
/// and struct port mechanical rather than a rewrite.
///
/// Only the operations BSPSource actually uses are implemented. Backed by a shared
/// <see cref="byte"/> array with an offset so <see cref="Duplicate"/> and
/// <see cref="Slice"/> are views, exactly like the Java original.
/// </summary>
public sealed class ByteBuffer
{
    private readonly byte[] _hb;   // backing array (shared between views)
    private readonly int _baseOffset; // index in _hb that this view's index 0 maps to
    private int _position;
    private int _limit;
    private readonly int _capacity;
    private int _mark = -1;

    /// <summary>Default order is big-endian, matching Java. Callers set little-endian explicitly.</summary>
    public ByteOrder Order { get; private set; } = ByteOrder.BigEndian;

    private ByteBuffer(byte[] hb, int baseOffset, int position, int limit, int capacity)
    {
        _hb = hb;
        _baseOffset = baseOffset;
        _position = position;
        _limit = limit;
        _capacity = capacity;
    }

    public static ByteBuffer Allocate(int capacity)
    {
        if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        return new ByteBuffer(new byte[capacity], 0, 0, capacity, capacity);
    }

    /// <summary>Direct vs heap allocation is irrelevant in managed memory; treated as <see cref="Allocate"/>.</summary>
    public static ByteBuffer AllocateDirect(int capacity) => Allocate(capacity);

    public static ByteBuffer Wrap(byte[] array)
    {
        ArgumentNullException.ThrowIfNull(array);
        return new ByteBuffer(array, 0, 0, array.Length, array.Length);
    }

    public static ByteBuffer Wrap(byte[] array, int offset, int length)
    {
        ArgumentNullException.ThrowIfNull(array);
        var b = new ByteBuffer(array, 0, offset, offset + length, array.Length);
        return b;
    }

    /// <summary>Sets the byte order and returns this buffer (Java fluent style).</summary>
    public ByteBuffer SetOrder(ByteOrder order)
    {
        Order = order;
        return this;
    }

    public int Position => _position;
    public int Limit => _limit;
    public int Capacity => _capacity;
    public int Remaining => _limit - _position;
    public bool HasRemaining => _position < _limit;

    public ByteBuffer SetPosition(int newPosition)
    {
        if (newPosition < 0 || newPosition > _limit)
            throw new ArgumentOutOfRangeException(nameof(newPosition));
        _position = newPosition;
        if (_mark > _position) _mark = -1;
        return this;
    }

    public ByteBuffer SetLimit(int newLimit)
    {
        if (newLimit < 0 || newLimit > _capacity)
            throw new ArgumentOutOfRangeException(nameof(newLimit));
        _limit = newLimit;
        if (_position > _limit) _position = _limit;
        if (_mark > _limit) _mark = -1;
        return this;
    }

    public ByteBuffer Rewind()
    {
        _position = 0;
        _mark = -1;
        return this;
    }

    public ByteBuffer Clear()
    {
        _position = 0;
        _limit = _capacity;
        _mark = -1;
        return this;
    }

    public ByteBuffer Flip()
    {
        _limit = _position;
        _position = 0;
        _mark = -1;
        return this;
    }

    public ByteBuffer Mark()
    {
        _mark = _position;
        return this;
    }

    public ByteBuffer Reset()
    {
        if (_mark < 0) throw new InvalidOperationException("Mark not set");
        _position = _mark;
        return this;
    }

    /// <summary>Shares content; independent position/limit/mark; order resets to big-endian (as in Java).</summary>
    public ByteBuffer Duplicate() =>
        new(_hb, _baseOffset, _position, _limit, _capacity);

    /// <summary>View of the bytes between position and limit; index 0 = current position.</summary>
    public ByteBuffer Slice()
    {
        int rem = Remaining;
        return new ByteBuffer(_hb, _baseOffset + _position, 0, rem, rem);
    }

    /// <summary>Absolute slice (Java 13+ <c>slice(index, length)</c>): a view of <paramref name="length"/> bytes starting at <paramref name="index"/> from this buffer's origin.</summary>
    public ByteBuffer Slice(int index, int length)
    {
        if (index < 0 || length < 0 || index + length > _capacity)
            throw new ArgumentOutOfRangeException(nameof(index));
        return new ByteBuffer(_hb, _baseOffset + index, 0, length, length);
    }

    // ---- relative get ----

    public byte Get()
    {
        if (_position >= _limit) throw new InvalidOperationException("BufferUnderflow");
        return _hb[_baseOffset + _position++];
    }

    public ByteBuffer Get(byte[] dst)
    {
        Get(dst, 0, dst.Length);
        return this;
    }

    public ByteBuffer Get(byte[] dst, int offset, int length)
    {
        if (length > Remaining) throw new InvalidOperationException("BufferUnderflow");
        Array.Copy(_hb, _baseOffset + _position, dst, offset, length);
        _position += length;
        return this;
    }

    public short GetShort()
    {
        var v = PeekShort(_position);
        _position += 2;
        return v;
    }

    public int GetInt()
    {
        var v = PeekInt(_position);
        _position += 4;
        return v;
    }

    public long GetLong()
    {
        var v = PeekLong(_position);
        _position += 8;
        return v;
    }

    public float GetFloat() => BitConverter.Int32BitsToSingle(GetInt());
    public double GetDouble() => BitConverter.Int64BitsToDouble(GetLong());

    // ---- absolute get ----

    public byte Get(int index) => _hb[_baseOffset + index];
    public short GetShort(int index) => PeekShort(index);
    public int GetInt(int index) => PeekInt(index);

    // ---- relative put ----

    public ByteBuffer Put(byte b)
    {
        if (_position >= _limit) throw new InvalidOperationException("BufferOverflow");
        _hb[_baseOffset + _position++] = b;
        return this;
    }

    public ByteBuffer Put(byte[] src)
    {
        Put(src, 0, src.Length);
        return this;
    }

    public ByteBuffer Put(byte[] src, int offset, int length)
    {
        if (length > Remaining) throw new InvalidOperationException("BufferOverflow");
        Array.Copy(src, offset, _hb, _baseOffset + _position, length);
        _position += length;
        return this;
    }

    public ByteBuffer PutShort(short value)
    {
        PokeShort(_position, value);
        _position += 2;
        return this;
    }

    public ByteBuffer PutInt(int value)
    {
        PokeInt(_position, value);
        _position += 4;
        return this;
    }

    public ByteBuffer PutLong(long value)
    {
        PokeLong(_position, value);
        _position += 8;
        return this;
    }

    public ByteBuffer PutFloat(float value) => PutInt(BitConverter.SingleToInt32Bits(value));
    public ByteBuffer PutDouble(double value) => PutLong(BitConverter.DoubleToInt64Bits(value));

    /// <summary>Copies the remaining bytes (position..limit) into a fresh array.</summary>
    public byte[] ToArray()
    {
        var result = new byte[Remaining];
        Array.Copy(_hb, _baseOffset + _position, result, 0, result.Length);
        return result;
    }

    private short PeekShort(int index)
    {
        var span = _hb.AsSpan(_baseOffset + index, 2);
        return Order == ByteOrder.LittleEndian
            ? BinaryPrimitives.ReadInt16LittleEndian(span)
            : BinaryPrimitives.ReadInt16BigEndian(span);
    }

    private int PeekInt(int index)
    {
        var span = _hb.AsSpan(_baseOffset + index, 4);
        return Order == ByteOrder.LittleEndian
            ? BinaryPrimitives.ReadInt32LittleEndian(span)
            : BinaryPrimitives.ReadInt32BigEndian(span);
    }

    private long PeekLong(int index)
    {
        var span = _hb.AsSpan(_baseOffset + index, 8);
        return Order == ByteOrder.LittleEndian
            ? BinaryPrimitives.ReadInt64LittleEndian(span)
            : BinaryPrimitives.ReadInt64BigEndian(span);
    }

    private void PokeShort(int index, short value)
    {
        var span = _hb.AsSpan(_baseOffset + index, 2);
        if (Order == ByteOrder.LittleEndian) BinaryPrimitives.WriteInt16LittleEndian(span, value);
        else BinaryPrimitives.WriteInt16BigEndian(span, value);
    }

    private void PokeInt(int index, int value)
    {
        var span = _hb.AsSpan(_baseOffset + index, 4);
        if (Order == ByteOrder.LittleEndian) BinaryPrimitives.WriteInt32LittleEndian(span, value);
        else BinaryPrimitives.WriteInt32BigEndian(span, value);
    }

    private void PokeLong(int index, long value)
    {
        var span = _hb.AsSpan(_baseOffset + index, 8);
        if (Order == ByteOrder.LittleEndian) BinaryPrimitives.WriteInt64LittleEndian(span, value);
        else BinaryPrimitives.WriteInt64BigEndian(span, value);
    }
}
