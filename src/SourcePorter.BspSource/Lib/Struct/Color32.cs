namespace SourcePorter.BspSource.Lib.Struct;

/// <summary>A simple RGBA value, ported from BSPSource's <c>Color32</c>.</summary>
public sealed class Color32
{
    public readonly int R;
    public readonly int G;
    public readonly int B;
    public readonly int A;
    public readonly int Rgba;

    /// <summary>Creates an RGBA value from individual 0–255 components.</summary>
    public Color32(int red, int green, int blue, int alpha)
    {
        R = red & 0xFF;
        G = green & 0xFF;
        B = blue & 0xFF;
        A = alpha & 0xFF;
        Rgba = (((((A << 8) + B) << 8) + G) << 8) + R;
    }

    /// <summary>Creates an RGBA value from a packed integer.</summary>
    public Color32(int value)
    {
        R = value & 0xFF;
        G = (value >> 8) & 0xFF;
        B = (value >> 16) & 0xFF;
        A = (int)((uint)value >> 24) & 0xFF;
        Rgba = value;
    }
}
