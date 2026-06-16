using System.Diagnostics;
using SourcePorter.App.Theme;

namespace SourcePorter.App;

/// <summary>
/// A reference window: what each field means (carried over from Valve's importer
/// GUI tooltips) plus the useful links/tools from the S2ZE porting guide.
/// </summary>
public sealed class ReferenceForm : Form
{
    public ReferenceForm()
    {
        Text = "SourcePorter — Reference";
        Size = new Size(620, 640);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9f);

        var text = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            DetectUrls = true,
            Font = new Font("Segoe UI", 9.5f),
        };
        text.LinkClicked += (_, e) =>
        {
            if (e.LinkText is { } url)
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        };

        text.Text = Content;
        Controls.Add(text);
        Controls.Add(new Label
        {
            Text = "  Fields & tools reference",
            Dock = DockStyle.Top,
            Height = 28,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Themer.CurrentThemeColors.ContrastSoft,
        });

        Themer.ApplyTheme(this);
    }

    private const string Content =
"""
FIELDS

CS2 Directory
    The Counter-Strike 2 install root (…\Counter-Strike Global Offensive).
    It contains both csgo\ (the S1 gameinfo.txt) and game\csgo\ (the S2
    gameinfo.gi). Everything else is derived from this.

Source Map
    Full path to the Source 1 .vmf to port. It must live inside a 'maps'
    folder; the map name is taken relative to that folder. A decompiled .vmf
    must be saved once in S1 Hammer before it will import.

Output Addon
    The target CS2 Workshop addon name. The map and its assets are imported
    into game\csgo_addons\<addon> (and content\csgo_addons\<addon>).

IMPORT OPTIONS

Use BSP (recommended)
    Runs the map through vbsp to generate clean geometry from brushes,
    removing hidden faces. Merges all func_instances and triangulates geo.

Use BSP, don't merge instances
    Like Use BSP, but keeps the func_instance hierarchy of the S1 map.
    Mutually exclusive with the option above.

Skip dependencies
    Only produce the .vmap file(s); skip importing/compiling content. A fast
    iteration mode when you only changed entities.

USEFUL LINKS & TOOLS

  S2ZE Discord            https://discord.gg/s2ze
  S2ZE GitHub             https://github.com/Source2ZE
  Source 2 Viewer         https://s2v.app/
  ValveResourceFormat     https://github.com/ValveResourceFormat/ValveResourceFormat
  BSPSource (.bsp decompiler)  https://github.com/ata4/bspsrc
  Crowbar (.mdl decompiler)    https://github.com/ZeqMacaw/Crowbar
""";
}
