namespace SourcePorter.BspSource.Lib.Lump;

/// <summary>Default lump stored in a BSP file's lump directory. Ported from BSPSource's <c>Lump</c>.</summary>
public sealed class Lump : AbstractLump
{
    public LumpType Type { get; }
    public int Index { get; }
    public string? ParentFile { get; set; }

    public Lump(int index, LumpType type)
    {
        Index = index;
        Type = type;
    }

    public Lump(LumpType type) : this(type.Index, type) { }

    public override string GetName() => Type.Name;

    public override void SetCompressed(bool compressed)
    {
        base.SetCompressed(compressed);
        FourCC = compressed ? Length : 0;
    }
}
