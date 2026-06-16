using System.Globalization;
using SourcePorter.BspSource.Io;

namespace SourcePorter.BspSource.Vector;

/// <summary>
/// Immutable 3D float vector, ported from BSPSource's <c>Vector3f</c> (which
/// extends the generic <c>VectorXf</c>). Value-equality matches Java's
/// array-based <c>equals</c>. All mutating-looking methods return a new value.
/// </summary>
public readonly record struct Vector3f(float X, float Y, float Z)
{
    public static Vector3f Read(DataReader reader)
    {
        float x = reader.ReadFloat();
        float y = reader.ReadFloat();
        float z = reader.ReadFloat();
        return new Vector3f(x, y, z);
    }

    public static void Write(DataWriter writer, Vector3f vec)
    {
        writer.WriteFloat(vec.X);
        writer.WriteFloat(vec.Y);
        writer.WriteFloat(vec.Z);
    }

    public static readonly Vector3f Null = new(0, 0, 0);
    public static readonly Vector3f MaxValue = new(float.MaxValue, float.MaxValue, float.MaxValue);
    public static readonly Vector3f MinValue = new(-float.MaxValue, -float.MaxValue, -float.MaxValue);

    public static readonly Vector3f BaseVectorX = new(1, 0, 0);
    public static readonly Vector3f BaseVectorY = new(0, 1, 0);
    public static readonly Vector3f BaseVectorZ = new(0, 0, 1);

    public float Get(int index) => index switch
    {
        0 => X,
        1 => Y,
        2 => Z,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    public Vector3f WithX(float value) => this with { X = value };
    public Vector3f WithY(float value) => this with { Y = value };
    public Vector3f WithZ(float value) => this with { Z = value };

    public Vector3f Add(float v) => new(X + v, Y + v, Z + v);
    public Vector3f Add(Vector3f o) => new(X + o.X, Y + o.Y, Z + o.Z);
    public Vector3f Sub(float v) => new(X - v, Y - v, Z - v);
    public Vector3f Sub(Vector3f o) => new(X - o.X, Y - o.Y, Z - o.Z);

    public float Dot(Vector3f o)
    {
        float sum = 0f;
        sum += X * o.X;
        sum += Y * o.Y;
        sum += Z * o.Z;
        return sum;
    }

    public Vector3f Scalar(float v) => new(X * v, Y * v, Z * v);
    public Vector3f Scalar(Vector3f o) => new(X * o.X, Y * o.Y, Z * o.Z);

    public Vector3f Min(float v) => new(MathF.Min(X, v), MathF.Min(Y, v), MathF.Min(Z, v));
    public Vector3f Min(Vector3f o) => new(MathF.Min(X, o.X), MathF.Min(Y, o.Y), MathF.Min(Z, o.Z));
    public Vector3f Max(float v) => new(MathF.Max(X, v), MathF.Max(Y, v), MathF.Max(Z, v));
    public Vector3f Max(Vector3f o) => new(MathF.Max(X, o.X), MathF.Max(Y, o.Y), MathF.Max(Z, o.Z));

    public Vector3f Normalize()
    {
        var length = Length();
        return new Vector3f(X / length, Y / length, Z / length);
    }

    public float Length()
    {
        double sum = (double)X * X + (double)Y * Y + (double)Z * Z;
        return (float)Math.Sqrt(sum);
    }

    public bool IsNaN() => float.IsNaN(X) || float.IsNaN(Y) || float.IsNaN(Z);
    public bool IsInfinite() => float.IsInfinity(X) || float.IsInfinity(Y) || float.IsInfinity(Z);
    public bool IsValid() => !IsNaN() && !IsInfinite();

    /// <summary>Cross product.</summary>
    public Vector3f Cross(Vector3f that)
    {
        var rx = Y * that.Z - Z * that.Y;
        var ry = Z * that.X - X * that.Z;
        var rz = X * that.Y - Y * that.X;
        return new Vector3f(rx, ry, rz);
    }

    /// <summary>Extrinsic (X-Y-Z) rotation in a right-handed system; angles in degrees.</summary>
    public Vector3f Rotate(Vector3f angles)
    {
        if (angles.X == 0 && angles.Y == 0 && angles.Z == 0)
            return this;

        double vx = X, vy = Y, vz = Z;

        var phiX = double.DegreesToRadians(angles.X);
        var phiY = double.DegreesToRadians(angles.Y);
        var phiZ = double.DegreesToRadians(angles.Z);

        var cx = Math.Cos(phiX);
        var sx = Math.Sin(phiX);
        var cy = Math.Cos(phiY);
        var sy = Math.Sin(phiY);
        var cz = Math.Cos(phiZ);
        var sz = Math.Sin(phiZ);

        var r00 = cz * cy;
        var r01 = cz * sy * sx - sz * cx;
        var r02 = cz * sy * cx + sz * sx;
        var r10 = sz * cy;
        var r11 = sz * sy * sx + cz * cx;
        var r12 = sz * sy * cx - cz * sx;
        var r20 = -sy;
        var r21 = cy * sx;
        var r22 = cy * cx;

        var rvx = r00 * vx + r01 * vy + r02 * vz;
        var rvy = r10 * vx + r11 * vy + r12 * vz;
        var rvz = r20 * vx + r21 * vy + r22 * vz;

        return new Vector3f((float)rvx, (float)rvy, (float)rvz);
    }

    /// <summary>Projects this point onto the 2D plane defined by an origin and two orthonormal axes.</summary>
    public Vector2f ProjectOnPlane(Vector3f origin, Vector3f axis1, Vector3f axis2)
    {
        return new Vector2f(
            axis1.Dot(Sub(origin)),
            axis2.Dot(Sub(origin)));
    }

    public Vector3d ToDouble() => new(X, Y, Z);

    public override string ToString() =>
        FormattableString.Invariant($"({X}, {Y}, {Z})");
}
