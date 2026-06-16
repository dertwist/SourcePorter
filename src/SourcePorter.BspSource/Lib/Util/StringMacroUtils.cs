using System.Text;

namespace SourcePorter.BspSource.Lib.Util;

/// <summary>
/// Port of BSPSource's <c>StringMacroUtils</c> — simulates the Source SDK
/// <c>MAKEID</c> macro that packs a 4-character string into an int (used for
/// fourCC lump/game-lump identifiers).
/// </summary>
public static class StringMacroUtils
{
    /// <summary>Packs a 4-character string into a little-endian fourCC int.</summary>
    public static int MakeId(string id)
    {
        if (id.Length != 4)
            throw new ArgumentException("String must be exactly 4 characters long", nameof(id));

        var b = Encoding.ASCII.GetBytes(id);
        return (b[3] << 24) | (b[2] << 16) | (b[1] << 8) | b[0];
    }

    /// <summary>Unpacks a fourCC int into its 4-character string.</summary>
    public static string UnmakeId(int id)
    {
        var bytes = new[]
        {
            (byte)id,
            (byte)((uint)id >> 8),
            (byte)((uint)id >> 16),
            (byte)((uint)id >> 24),
        };
        return Encoding.Latin1.GetString(bytes);
    }
}
