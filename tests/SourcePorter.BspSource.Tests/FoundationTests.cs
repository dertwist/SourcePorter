using SourcePorter.BspSource.Io;
using SourcePorter.BspSource.Lib.Lump;
using SourcePorter.BspSource.Lib.Util;
using SourcePorter.BspSource.Vector;

namespace SourcePorter.BspSource.Tests;

/// <summary>
/// Phase 1 foundation tests: the IO/vector/lump contracts every later phase
/// depends on. These pin endianness handling, the signed-byte semantics, and the
/// vector math (cross/dot/rotate) that the geometry pipeline is built on.
/// </summary>
public class FoundationTests
{
    [Fact]
    public void ByteBuffer_LittleEndian_RoundTripsIntAndFloat()
    {
        var buf = ByteBuffer.Allocate(8).SetOrder(ByteOrder.LittleEndian);
        buf.PutInt(0x01020304);
        buf.PutFloat(1.5f);
        buf.Rewind();

        Assert.Equal(0x01020304, buf.GetInt());
        Assert.Equal(1.5f, buf.GetFloat());
    }

    [Fact]
    public void ByteBuffer_Endianness_AffectsByteLayout()
    {
        var le = ByteBuffer.Allocate(4).SetOrder(ByteOrder.LittleEndian);
        le.PutInt(0x04030201);
        Assert.Equal((byte)0x01, le.Get(0)); // least-significant byte first

        var be = ByteBuffer.Allocate(4).SetOrder(ByteOrder.BigEndian);
        be.PutInt(0x04030201);
        Assert.Equal((byte)0x04, be.Get(0)); // most-significant byte first
    }

    [Fact]
    public void ByteBuffer_AbsoluteSlice_IsIndependentView()
    {
        var buf = ByteBuffer.Allocate(16).SetOrder(ByteOrder.LittleEndian);
        for (int i = 0; i < 16; i++) buf.Put((byte)i);
        var slice = buf.Slice(4, 4).SetOrder(ByteOrder.LittleEndian);

        Assert.Equal(4, slice.Limit);
        Assert.Equal(0x07060504, slice.GetInt());
    }

    [Fact]
    public void DataReader_ReadsPrimitivesAndStrings()
    {
        var buf = ByteBuffer.Allocate(64).SetOrder(ByteOrder.LittleEndian);
        buf.PutInt(-5);
        buf.PutShort(unchecked((short)0xFFFE)); // -2 signed / 65534 unsigned
        buf.Put((byte)0xFF);                     // -1 signed / 255 unsigned
        // fixed string "abc\0" in 4 bytes, then null-terminated "hi\0"
        buf.Put("abc"u8.ToArray());
        buf.Put((byte)0);
        buf.Put("hi"u8.ToArray());
        buf.Put((byte)0);
        buf.Rewind();

        var dr = DataReaders.ForByteBuffer(buf);
        Assert.Equal(-5, dr.ReadInt());
        Assert.Equal(65534, dr.ReadUnsignedShort());
        Assert.Equal(255, dr.ReadUnsignedByte());
        Assert.Equal("abc", dr.ReadStringFixed(4));
        Assert.Equal("hi", dr.ReadStringNull());
    }

    [Fact]
    public void Vector3f_CrossAndDot_MatchKnownValues()
    {
        var x = Vector3f.BaseVectorX;
        var y = Vector3f.BaseVectorY;
        Assert.Equal(Vector3f.BaseVectorZ, x.Cross(y));
        Assert.Equal(0f, x.Dot(y));
        Assert.Equal(1f, x.Dot(x));
    }

    [Fact]
    public void Vector3d_Rotate90AboutZ_MapsXAxisToYAxis()
    {
        // Extrinsic XYZ, right-handed: +90° about Z sends (1,0,0) -> (0,1,0).
        var rotated = new Vector3d(1, 0, 0).Rotate(new Vector3d(0, 0, 90));
        Assert.Equal(0, rotated.X, 6);
        Assert.Equal(1, rotated.Y, 6);
        Assert.Equal(0, rotated.Z, 6);
    }

    [Fact]
    public void Vector3f_NormalizeAndLength()
    {
        var v = new Vector3f(3, 4, 0);
        Assert.Equal(5f, v.Length(), 5);
        var n = v.Normalize();
        Assert.Equal(1f, n.Length(), 5);
    }

    [Fact]
    public void Vectors_HaveValueEquality()
    {
        Assert.Equal(new Vector3f(1, 2, 3), new Vector3f(1, 2, 3));
        Assert.NotEqual(new Vector3f(1, 2, 3), new Vector3f(1, 2, 4));
    }

    [Fact]
    public void StringMacroUtils_MakeAndUnmakeId_RoundTrip()
    {
        int id = StringMacroUtils.MakeId("sprp");
        Assert.Equal("sprp", StringMacroUtils.UnmakeId(id));
    }

    [Fact]
    public void LumpType_LookupRespectsDeclarationOrderForReusedIndices()
    {
        // index 22 is PROPCOLLISION@v21, UNUSED0@v20, PORTALS@v19 — version selects.
        Assert.Equal(LumpType.LUMP_PORTALS, LumpType.Get(22, 19));
        Assert.Equal(LumpType.LUMP_PROPCOLLISION, LumpType.Get(22, 21));
        Assert.Equal(LumpType.LUMP_ENTITIES, LumpType.Get(0, 21));
    }
}
