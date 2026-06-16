using System.Diagnostics;
using SourcePorter.App.Theme;

namespace SourcePorter.App;

/// <summary>
/// A reference window: what each field/option means (carried over from Valve's
/// importer GUI tooltips, kept in sync with the current UI) plus the useful
/// links/tools from the S2ZE porting guide.
/// </summary>
public sealed class ReferenceForm : Form
{
    private static readonly (string Label, string Url)[] Links =
    [
        ("S2ZE Discord", "https://discord.gg/s2ze"),
        ("S2ZE GitHub", "https://github.com/Source2ZE"),
        ("Source 2 Viewer", "https://s2v.app/"),
        ("ValveResourceFormat", "https://github.com/ValveResourceFormat/ValveResourceFormat"),
        ("BSPSource — .bsp decompiler", "https://github.com/ata4/bspsrc"),
        ("Crowbar — .mdl decompiler", "https://github.com/ZeqMacaw/Crowbar"),
    ];

    // (heading, field, description) — heading is non-null only on the first field of a section.
    private static readonly (string? Heading, string Field, string Description)[] Entries =
    [
        ("Fields", "CS2 Directory",
            "The Counter-Strike 2 install root (…\\Counter-Strike Global Offensive). It holds both " +
            "csgo\\ (the Source 1 gameinfo) and game\\csgo\\ (the Source 2 gameinfo.gi). Everything " +
            "else is derived from it — usually auto-detected on first run."),
        (null, "Input Method",
            "VMF imports a Source 1 map file directly. BSP first decompiles a compiled .bsp to a .vmf " +
            "with the bundled BSPSource, then imports that. The picker also sets the Browse filter."),
        (null, "Source Map",
            "Full path to the source map. It must live inside a 'maps' folder; the map name is taken " +
            "relative to that folder. Decompiled maps import as-is — the old 'open and re-save in " +
            "Hammer' step is no longer needed."),
        (null, "Output Addon",
            "The target CS2 addon name. The map and its assets are imported into " +
            "game\\csgo_addons\\<addon> and content\\csgo_addons\\<addon>."),

        ("Import options", "Use BSP (recommended)",
            "Runs the map through vbsp to build clean geometry from the brushes, removing hidden " +
            "faces. Merges func_instances and triangulates geometry."),
        (null, "Don't merge instances",
            "Like Use BSP, but keeps the map's func_instance hierarchy. Mutually exclusive with Use BSP."),
        (null, "Skip dependencies",
            "Produce only the .vmap; skip importing and compiling models/materials. Fast iteration when " +
            "you only changed entities."),
        (null, "Compile Assets",
            "After importing the model/material sources, compile them to their _c files with " +
            "resourcecompiler. Off (the default) is faster — you get a fully populated content tree to " +
            "finish in Hammer; tick it to produce a shippable, validated addon."),
        (null, "Compile map",
            "After import, compile the .vmap to .vmap_c. Runs the lighting bake, so it is slow."),
        (null, "Threads",
            "Max parallel tool processes during the dependency phase (model import + material compile). " +
            "Set 1 for sequential."),

        ("BSP decompile (BSP input only)", "Unpack embedded content",
            "When decompiling a .bsp, extract its packed materials and models so the imported addon is " +
            "self-contained."),
    ];

    public ReferenceForm()
    {
        Text = "SourcePorter — Reference";
        Size = new Size(640, 680);
        MinimumSize = new Size(480, 420);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9f);

        var docs = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            DetectUrls = false,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            Font = new Font("Segoe UI", 9.5f),
            TabStop = false,
        };
        var docsHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 6, 14, 6) };
        docsHost.Controls.Add(docs);

        Controls.Add(docsHost);
        Controls.Add(BuildHeader());
        Controls.Add(BuildLinks());

        Themer.ApplyTheme(this);

        // Selection-based formatting needs the handle, so build once it exists.
        Load += (_, _) => BuildDocs(docs);
    }

    private static Panel BuildHeader()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(12, 0, 8, 0) };
        header.Controls.Add(new Label
        {
            Text = "REFERENCE",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Themer.CurrentThemeColors.ContrastSoft,
        });
        header.Controls.Add(new PictureBox
        {
            Dock = DockStyle.Left,
            Width = 24,
            SizeMode = PictureBoxSizeMode.CenterImage,
            Image = Themer.GetIcon("Find", 16),
        });
        return header;
    }

    private static TableLayoutPanel BuildLinks()
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Padding = new Padding(14, 10, 14, 12),
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var heading = new Label
        {
            Text = "USEFUL LINKS & TOOLS",
            AutoSize = true,
            ForeColor = Themer.CurrentThemeColors.Accent,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 8),
        };
        table.Controls.Add(heading, 0, 0);
        table.SetColumnSpan(heading, 2);

        var row = 1;
        foreach (var (label, url) in Links)
        {
            table.Controls.Add(new Label
            {
                Text = label,
                AutoSize = true,
                ForeColor = Themer.CurrentThemeColors.Contrast,
                Margin = new Padding(0, 3, 10, 3),
            }, 0, row);

            var link = new LinkLabel
            {
                Text = url,
                AutoSize = true,
                Margin = new Padding(0, 3, 0, 3),
                LinkColor = Themer.CurrentThemeColors.Accent,
                ActiveLinkColor = Themer.CurrentThemeColors.Contrast,
                VisitedLinkColor = Themer.CurrentThemeColors.Accent,
                LinkBehavior = LinkBehavior.HoverUnderline,
            };
            var target = url;
            link.LinkClicked += (_, _) => OpenUrl(target);
            table.Controls.Add(link, 1, row);
            row++;
        }

        return table;
    }

    private static void BuildDocs(RichTextBox docs)
    {
        var colors = Themer.CurrentThemeColors;
        var headingFont = new Font("Segoe UI", 11f, FontStyle.Bold);
        var fieldFont = new Font("Segoe UI", 9.75f, FontStyle.Bold);
        var bodyFont = new Font("Segoe UI", 9.5f);
        var gapFont = new Font("Segoe UI", 4f);

        docs.Clear();
        var first = true;

        foreach (var (heading, field, description) in Entries)
        {
            if (heading is not null)
            {
                if (!first)
                    Append(docs, "\n", gapFont, colors.ContrastSoft, 0);
                Append(docs, heading + "\n", headingFont, colors.Accent, 0);
                Append(docs, "\n", gapFont, colors.ContrastSoft, 0);
            }
            first = false;

            Append(docs, field + "\n", fieldFont, colors.Contrast, 16);
            Append(docs, description + "\n", bodyFont, colors.ContrastSoft, 32);
            Append(docs, "\n", gapFont, colors.ContrastSoft, 0);
        }

        docs.Select(0, 0);
        docs.ScrollToCaret();
    }

    private static void Append(RichTextBox docs, string text, Font font, Color color, int indent)
    {
        docs.SelectionStart = docs.TextLength;
        docs.SelectionLength = 0;
        docs.SelectionFont = font;
        docs.SelectionColor = color;
        docs.SelectionIndent = indent;
        docs.SelectionHangingIndent = 0;
        docs.AppendText(text);
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Best-effort: a missing browser association shouldn't crash the window.
        }
    }
}
