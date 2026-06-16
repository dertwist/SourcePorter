using SourcePorter.App.Theme;

namespace SourcePorter.App;

/// <summary>
/// Editor for Valve's <c>source1import_*.txt</c> config files (exclusion lists,
/// material substitution lists, …) bundled under the app's
/// <c>import_scripts\</c> working directory. Edits take effect on the next import.
/// </summary>
public sealed class ConfigsEditorForm : Form
{
    private readonly ListBox _files = new();
    private readonly TextBox _editor = new();
    private readonly Button _save = new();
    private readonly Label _status = new();
    private string? _current;

    public ConfigsEditorForm()
    {
        Text = "SourcePorter — Configs Editor";
        Size = new Size(900, 640);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9f);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 260,
            FixedPanel = FixedPanel.Panel1,
        };

        _files.Dock = DockStyle.Fill;
        _files.BorderStyle = BorderStyle.None;
        _files.IntegralHeight = false;
        _files.SelectedIndexChanged += (_, _) => LoadSelected();
        split.Panel1.Controls.Add(_files);

        _editor.Dock = DockStyle.Fill;
        _editor.Multiline = true;
        _editor.ScrollBars = ScrollBars.Both;
        _editor.WordWrap = false;
        _editor.AcceptsTab = true;
        _editor.BorderStyle = BorderStyle.None;
        _editor.Font = new Font("Cascadia Mono", 9.5f);

        var bar = new Panel { Dock = DockStyle.Bottom, Height = 36, Padding = new Padding(6) };
        _save.Text = "Save";
        _save.Dock = DockStyle.Right;
        _save.Width = 90;
        _save.Click += (_, _) => SaveCurrent();
        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.ForeColor = Themer.CurrentThemeColors.ContrastSoft;
        bar.Controls.Add(_status);
        bar.Controls.Add(_save);

        split.Panel2.Controls.Add(_editor);
        split.Panel2.Controls.Add(bar);

        Controls.Add(split);
        Controls.Add(new Label
        {
            Text = "  source1import configs  (" + AppPaths.ImportScriptsDir + ")",
            Dock = DockStyle.Top,
            Height = 26,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Themer.CurrentThemeColors.ContrastSoft,
        });

        Themer.ApplyTheme(this);
        PopulateFileList();
    }

    private void PopulateFileList()
    {
        _files.Items.Clear();
        if (!Directory.Exists(AppPaths.ImportScriptsDir))
        {
            _status.Text = "Working configs folder not found.";
            return;
        }

        foreach (var path in Directory.EnumerateFiles(AppPaths.ImportScriptsDir, "*.txt").OrderBy(p => p))
            _files.Items.Add(Path.GetFileName(path));

        if (_files.Items.Count > 0)
            _files.SelectedIndex = 0;
    }

    private void LoadSelected()
    {
        if (_files.SelectedItem is not string name)
            return;

        _current = Path.Combine(AppPaths.ImportScriptsDir, name);
        try
        {
            _editor.Text = File.ReadAllText(_current);
            _status.Text = name;
        }
        catch (Exception ex)
        {
            _editor.Text = "";
            _status.Text = $"Failed to open {name}: {ex.Message}";
        }
    }

    private void SaveCurrent()
    {
        if (_current is null)
            return;

        try
        {
            File.WriteAllText(_current, _editor.Text);
            _status.Text = $"Saved {Path.GetFileName(_current)}.";
        }
        catch (Exception ex)
        {
            _status.Text = $"Save failed: {ex.Message}";
        }
    }
}
