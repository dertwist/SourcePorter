using SourcePorter.BspSource.Io;

namespace SourcePorter.BspSource.Vector;

/// <summary>Immutable 4D float vector, ported from BSPSource's <c>Vector4f</c>.</summary>
public readonly record struct Vector4f(float X, float Y, float Z, float W)
{
    public static Vector4f Read(DataReader reader)
    {
        float x = reader.ReadFloat();
        float y = reader.ReadFloat();
        float z = reader.ReadFloat();
        float w = reader.ReadFloat();
        return new Vector4f(x, y, z, w);
    }

    public static void Write(DataWriter writer, Vector4f vec)
    {
        writer.WriteFloat(vec.X);
        writer.WriteFloat(vec.Y);
        writer.WriteFloat(vec.Z);
        writer.WriteFloat(vec.W);
    }

    public static readonly Vector4f Null = new(0, 0, 0, 0);

    public float Get(int index) => index switch
    {
        0 => X,
        1 => Y,
        2 => Z,
        3 => W,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    public override string ToString() =>
        FormattableString.Invariant($"({X}, {Y}, {Z}, {W})");
}
