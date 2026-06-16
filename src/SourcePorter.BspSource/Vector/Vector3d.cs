namespace SourcePorter.BspSource.Vector;

/// <summary>
/// Immutable 3D double vector, ported from BSPSource's <c>Vector3d</c>. The
/// decompiler does its geometry (windings, clipping) in double precision; this is
/// the type used throughout the geometry pipeline.
/// </summary>
public readonly record struct Vector3d(double X, double Y, double Z)
{
    public static readonly Vector3d Null = new(0, 0, 0);
    // NOTE: BSPSource deliberately seeds these with Float.MAX_VALUE, not Double.MAX_VALUE.
    public static readonly Vector3d MaxValue = new(float.MaxValue, float.MaxValue, float.MaxValue);
    public static readonly Vector3d MinValue = new(-(double)float.MaxValue, -(double)float.MaxValue, -(double)float.MaxValue);

    public static readonly Vector3d BaseVectorX = new(1, 0, 0);
    public static readonly Vector3d BaseVectorY = new(0, 1, 0);
    public static readonly Vector3d BaseVectorZ = new(0, 0, 1);

    public static Vector3d FromFloat(Vector3f vector) => vector.ToDouble();

    public double Get(int index) => index switch
    {
        0 => X,
        1 => Y,
        2 => Z,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    public Vector3d WithX(double value) => this with { X = value };
    public Vector3d WithY(double value) => this with { Y = value };
    public Vector3d WithZ(double value) => this with { Z = value };

    public Vector3d Add(double v) => new(X + v, Y + v, Z + v);
    public Vector3d Add(Vector3d o) => new(X + o.X, Y + o.Y, Z + o.Z);
    public Vector3d Sub(double v) => new(X - v, Y - v, Z - v);
    public Vector3d Sub(Vector3d o) => new(X - o.X, Y - o.Y, Z - o.Z);

    public double Dot(Vector3d o)
    {
        double sum = 0.0;
        sum += X * o.X;
        sum += Y * o.Y;
        sum += Z * o.Z;
        return sum;
    }

    public Vector3d Scalar(double v) => new(X * v, Y * v, Z * v);
    public Vector3d Scalar(Vector3d o) => new(X * o.X, Y * o.Y, Z * o.Z);

    public Vector3d Min(double v) => new(Math.Min(X, v), Math.Min(Y, v), Math.Min(Z, v));
    public Vector3d Min(Vector3d o) => new(Math.Min(X, o.X), Math.Min(Y, o.Y), Math.Min(Z, o.Z));
    public Vector3d Max(double v) => new(Math.Max(X, v), Math.Max(Y, v), Math.Max(Z, v));
    public Vector3d Max(Vector3d o) => new(Math.Max(X, o.X), Math.Max(Y, o.Y), Math.Max(Z, o.Z));

    public Vector3d Normalize()
    {
        var length = Length();
        return new Vector3d(X / length, Y / length, Z / length);
    }

    public double Length() => Math.Sqrt(X * X + Y * Y + Z * Z);

    public bool IsNaN() => double.IsNaN(X) || double.IsNaN(Y) || double.IsNaN(Z);
    public bool IsInfinite() => double.IsInfinity(X) || double.IsInfinity(Y) || double.IsInfinity(Z);
    public bool IsValid() => !IsNaN() && !IsInfinite();

    public Vector3d Cross(Vector3d that)
    {
        var rx = Y * that.Z - Z * that.Y;
        var ry = Z * that.X - X * that.Z;
        var rz = X * that.Y - Y * that.X;
        return new Vector3d(rx, ry, rz);
    }

    /// <summary>Extrinsic (X-Y-Z) rotation in a right-handed system; angles in degrees.</summary>
    public Vector3d Rotate(Vector3d angles)
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

        return new Vector3d(rvx, rvy, rvz);
    }

    public override string ToString() =>
        FormattableString.Invariant($"({X}, {Y}, {Z})");
}
