using System.Buffers.Binary;
using SourcePorter.BspSource.Lib.Util;

namespace SourcePorter.BspSource.Lib.Lump;

/// <summary>
/// Lump stored inside <c>LUMP_GAME_LUMP</c>, identified by a fourCC, ported from
/// BSPSource's <c>GameLump</c>.
/// </summary>
public sealed class GameLump : AbstractLump
{
    public int Flags { get; set; }

    public override string GetName() =>
        StringMacroUtils.UnmakeId(BinaryPrimitives.ReverseEndianness(FourCC));

    public override void SetCompressed(bool compressed)
    {
        base.SetCompressed(compressed);
        Flags = compressed ? 1 : 0;
    }
}
