using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace SourcePorter.App.Theme;

/// <summary>
/// A fully dark-themed <see cref="ComboBox"/>. The stock WinForms combo ignores the
/// dark colour mode (light dropdown button, system border), so this owner-draws the
/// closed value + list items and overlays a dark dropdown button, a subtle chevron,
/// and a single-colour border — matching the Source 2 Viewer look. The Input Method
/// picker uses an accent border, the Threads picker a soft one.
/// </summary>
public sealed class BorderedComboBox : ComboBox
{
    private const int WM_PAINT = 0x000F;

    /// <summary>Colour of the 1px outer border.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BorderColor { get; set; } = Themer.CurrentThemeColors.Border;

    public BorderedComboBox()
    {
        FlatStyle = FlatStyle.Flat;
        DropDownStyle = ComboBoxStyle.DropDownList;
        DrawMode = DrawMode.OwnerDrawFixed;
    }

    // Draws both the closed value (the "edit" area) and each dropdown-list row.
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0)
            return;

        var isEdit = (e.State & DrawItemState.ComboBoxEdit) == DrawItemState.ComboBoxEdit;
        var back = !isEdit && (e.State & DrawItemState.Selected) != 0
            ? Themer.CurrentThemeColors.HoverAccent
            : BackColor;

        using (var b = new SolidBrush(back))
            e.Graphics.FillRectangle(b, e.Bounds);

        var text = Items[e.Index]?.ToString() ?? string.Empty;
        var bounds = e.Bounds with { X = e.Bounds.X + 3 };
        TextRenderer.DrawText(e.Graphics, text, Font, bounds,
            ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);

        // After the flat combo has painted, overlay a dark dropdown button (the
        // system paints a light one), a chevron, and the themed border.
        if (m.Msg == WM_PAINT)
        {
            using var g = Graphics.FromHwnd(Handle);
            var btnW = SystemInformation.VerticalScrollBarWidth;
            var button = new Rectangle(Width - btnW - 1, 1, btnW, Height - 2);

            using (var bg = new SolidBrush(BackColor))
                g.FillRectangle(bg, button);

            DrawChevron(g, button);

            using var pen = new Pen(BorderColor);
            g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }

    private static void DrawChevron(Graphics g, Rectangle area)
    {
        var prev = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var cx = area.Left + area.Width / 2f;
        var cy = area.Top + area.Height / 2f;
        const float s = 3.5f;
        using var brush = new SolidBrush(Themer.CurrentThemeColors.ContrastSoft);
        g.FillPolygon(brush,
        [
            new PointF(cx - s, cy - s / 2),
            new PointF(cx + s, cy - s / 2),
            new PointF(cx, cy + s / 2 + 1),
        ]);
        g.SmoothingMode = prev;
    }
}

/// <summary>
/// A <see cref="GroupBox"/> that owner-draws its border in <see cref="BorderColor"/>.
/// The standard GroupBox border colour is not themeable, so this lets the option
/// groups use the soft border while the mode-specific BSP group uses the accent
/// colour — matching the redesign.
/// </summary>
public sealed class ThemedGroupBox : GroupBox
{
    /// <summary>Colour of the group's frame.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BorderColor { get; set; } = Themer.CurrentThemeColors.Border;

    public ThemedGroupBox()
    {
        SetStyle(
            ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw,
            true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);

        var titleSize = TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.NoPadding);
        var top = titleSize.Height / 2;
        var frame = new Rectangle(0, top, Width - 1, Height - top - 1);

        using (var pen = new Pen(BorderColor))
            g.DrawRectangle(pen, frame);

        // Punch a gap in the top edge for the caption, then draw the caption over it.
        const int titleX = 10;
        var gap = new Rectangle(titleX - 3, 0, titleSize.Width + 6, titleSize.Height);
        using (var bg = new SolidBrush(BackColor))
            g.FillRectangle(bg, gap);
        TextRenderer.DrawText(g, Text, Font, new Point(titleX, 0), ForeColor, TextFormatFlags.NoPadding);
    }
}
