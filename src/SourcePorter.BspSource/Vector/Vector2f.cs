using SourcePorter.BspSource.Io;

namespace SourcePorter.BspSource.Vector;

/// <summary>Immutable 2D float vector, ported from BSPSource's <c>Vector2f</c>.</summary>
public readonly record struct Vector2f(float X, float Y)
{
    public static Vector2f Read(DataReader reader)
    {
        float x = reader.ReadFloat();
        float y = reader.ReadFloat();
        return new Vector2f(x, y);
    }

    public static void Write(DataWriter writer, Vector2f vec)
    {
        writer.WriteFloat(vec.X);
        writer.WriteFloat(vec.Y);
    }

    public static readonly Vector2f Null = new(0, 0);

    public float Get(int index) => index switch
    {
        0 => X,
        1 => Y,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    public Vector2f Add(Vector2f o) => new(X + o.X, Y + o.Y);
    public Vector2f Sub(Vector2f o) => new(X - o.X, Y - o.Y);
    public float Dot(Vector2f o) => X * o.X + Y * o.Y;
    public Vector2f Scalar(float v) => new(X * v, Y * v);

    public Vector2f Normalize()
    {
        var length = Length();
        return new Vector2f(X / length, Y / length);
    }

    public float Length() => (float)Math.Sqrt((double)X * X + (double)Y * Y);

    public bool IsNaN() => float.IsNaN(X) || float.IsNaN(Y);
    public bool IsInfinite() => float.IsInfinity(X) || float.IsInfinity(Y);
    public bool IsValid() => !IsNaN() && !IsInfinite();

    public override string ToString() =>
        FormattableString.Invariant($"({X}, {Y})");
}
