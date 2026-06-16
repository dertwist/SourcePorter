using SourcePorter.BspSource.Common;
using SourcePorter.BspSource.Io;

namespace SourcePorter.BspSource.Lib.Lump;

/// <summary>
/// Generic lump base shared by the normal <see cref="Lump"/> and the
/// <see cref="GameLump"/>, ported from BSPSource's <c>AbstractLump</c>. Holds the
/// lump's byte buffer and transparently tracks/applies LZMA compression.
/// </summary>
public abstract class AbstractLump
{
    private static readonly Logger L = LogManager.GetLogger();

    private ByteBuffer _buffer = ByteBuffer.Allocate(0);
    private bool _compressed;

    public int Offset { get; set; }
    public int Version { get; set; }
    public int FourCC { get; set; }

    /// <summary>Uncompressed length of the lump (buffer limit).</summary>
    public int Length => _buffer.Limit;

    /// <summary>A view of the lump buffer; changes are reflected in the lump.</summary>
    public ByteBuffer GetBuffer() => _buffer.Duplicate().SetOrder(_buffer.Order);

    public void SetBuffer(ByteBuffer buf)
    {
        _buffer = buf.Duplicate().SetOrder(buf.Order);
        SetCompressed(LzmaUtil.IsCompressed(_buffer));
    }

    /// <summary>Reads the lump bytes (between position and limit) as a stream.</summary>
    public Stream GetInputStream() => new MemoryStream(GetBuffer().ToArray(), writable: false);

    public bool IsCompressed => _compressed;

    public void Compress()
    {
        if (_compressed) return;
        try { _buffer = LzmaUtil.Compress(_buffer); }
        catch (IOException ex) { L.Error("Couldn't compress lump " + this, ex); }
        SetCompressed(true);
    }

    public void Uncompress()
    {
        if (!_compressed) return;
        try { _buffer = LzmaUtil.Uncompress(_buffer); }
        catch (IOException ex) { L.Error("Couldn't uncompress lump " + this, ex); }
        SetCompressed(false);
    }

    public virtual void SetCompressed(bool compressed) => _compressed = compressed;

    public abstract string GetName();

    public override string ToString() => GetName();
}
