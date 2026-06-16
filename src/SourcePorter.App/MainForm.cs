using SourcePorter.App.Theme;
using SourcePorter.Core.Domain;
using SourcePorter.Core.Toolchain;
using SourcePorter.Core.Validation;

namespace SourcePorter.App;

/// <summary>
/// SourcePorter shell — a self-contained importer that drives Valve's
/// source1import toolchain the same way <c>import_scripts</c> does. Inputs: a CS2
/// install directory, a source <c>.vmf</c>, and an output addon. Output streams to
/// the console. The menu opens the Reference and Configs-editor windows.
/// </summary>
public sealed class MainForm : Form
{
    private readonly AppSettings _settings = AppSettings.Load();

    private readonly TextBox _cs2Dir = new();
    private readonly TextBox _sourceMap = new();
    private readonly TextBox _outputAddon = new();
    private readonly CheckBox _useBsp = new() { Text = "Use BSP" };
    private readonly CheckBox _noMerge = new() { Text = "Don't merge instances" };
    private readonly CheckBox _skipDeps = new() { Text = "Skip dependencies" };
    private readonly Button _import = new() { Text = "Import" };
    private readonly Button _cancel = new() { Text = "Cancel", Enabled = false };
    private readonly RichTextBox _console = new();
    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _statusLabel = new();

    private CancellationTokenSource? _cts;

    public MainForm()
    {
        Text = "SourcePorter — Source 1 → Source 2 Map Porter";
        MinimumSize = new Size(820, 560);
        Size = new Size(1040, 700);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);

        BuildLayout();
        ApplySettingsToUi();
        WireSettingsPersistence();
        Themer.ApplyTheme(this);
        FormClosing += (_, _) => SaveSettings();

        AppendConsole($"SourcePorter v{Application.ProductVersion}", Themer.CurrentThemeColors.Accent);
        AppendConsole("Set the CS2 directory and a source .vmf, then press Import.", Themer.CurrentThemeColors.ContrastSoft);
    }

    private void BuildLayout()
    {
        // --- menu ---
        var menu = new MenuStrip { Renderer = new DarkToolStripRenderer(new CustomColorTable()) };
        var tools = new ToolStripMenuItem("&Tools");
        tools.DropDownItems.Add("&Validate Addon…", Themer.GetIcon("Recover", 16), async (_, _) => await RunValidateAsync());
        tools.DropDownItems.Add(new ToolStripSeparator());
        tools.DropDownItems.Add("&Reference…", Themer.GetIcon("Find", 16), (_, _) => new ReferenceForm().Show(this));
        tools.DropDownItems.Add("&Configs Editor…", Themer.GetIcon("Settings", 16), (_, _) => new ConfigsEditorForm().Show(this));
        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.Add("E&xit", null, (_, _) => Close());
        menu.Items.Add(file);
        menu.Items.Add(tools);

        // --- input form ---
        var form = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            Padding = new Padding(10, 8, 10, 4),
        };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));

        AddField(form, 0, "CS2 Directory", _cs2Dir, "Browse…", BrowseCs2Dir);
        AddField(form, 1, "Source Map", _sourceMap, "Browse…", BrowseSourceMap);
        AddField(form, 2, "Output Addon", _outputAddon, null, null);

        var options = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 4, 0, 0) };
        _useBsp.AutoSize = _noMerge.AutoSize = _skipDeps.AutoSize = true;
        _useBsp.CheckedChanged += (_, _) => { if (_useBsp.Checked) _noMerge.Checked = false; };
        _noMerge.CheckedChanged += (_, _) => { if (_noMerge.Checked) _useBsp.Checked = false; };
        options.Controls.Add(_useBsp);
        options.Controls.Add(_noMerge);
        options.Controls.Add(_skipDeps);
        form.Controls.Add(options, 1, 3);

        var actions = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 4, 0, 0) };
        _import.Width = 120;
        _import.Height = 30;
        _import.Image = Themer.GetIcon("Decompile", 16);
        _import.ImageAlign = ContentAlignment.MiddleLeft;
        _import.TextAlign = ContentAlignment.MiddleRight;
        _import.TextImageRelation = TextImageRelation.ImageBeforeText;
        _import.Click += async (_, _) => await RunImportAsync();
        _cancel.Width = 90;
        _cancel.Height = 30;
        _cancel.Click += (_, _) => _cts?.Cancel();
        actions.Controls.Add(_import);
        actions.Controls.Add(_cancel);
        form.Controls.Add(actions, 1, 4);

        // --- console ---
        _console.Dock = DockStyle.Fill;
        _console.ReadOnly = true;
        _console.BorderStyle = BorderStyle.None;
        _console.Font = new Font("Cascadia Mono", 9f);
        var consoleHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 8) };
        consoleHost.Controls.Add(_console);
        consoleHost.Controls.Add(BuildConsoleHeader());

        _statusLabel.Spring = true;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _status.Items.Add(_statusLabel);
        _status.SizingGrip = false;

        Controls.Add(consoleHost);
        Controls.Add(form);
        Controls.Add(_status);
        Controls.Add(menu);
        MainMenuStrip = menu;
    }

    private static void AddField(TableLayoutPanel form, int row, string label, TextBox box, string? buttonText, Action? onButton)
    {
        form.Controls.Add(new Label
        {
            Text = label,
            Anchor = AnchorStyles.Left,
            AutoSize = true,
            Margin = new Padding(3, 7, 3, 3),
        }, 0, row);

        box.Dock = DockStyle.Fill;
        box.Margin = new Padding(3, 4, 3, 4);
        form.Controls.Add(box, 1, row);

        if (buttonText is not null && onButton is not null)
        {
            var btn = new Button { Text = buttonText, Dock = DockStyle.Fill, Margin = new Padding(3, 4, 3, 4) };
            btn.Click += (_, _) => onButton();
            form.Controls.Add(btn, 2, row);
        }
    }

    private Panel BuildConsoleHeader()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 24 };

        var logIcon = new PictureBox
        {
            Dock = DockStyle.Left,
            Width = 22,
            SizeMode = PictureBoxSizeMode.CenterImage,
            Image = Themer.GetIcon("Log", 16),
        };
        var title = new Label
        {
            Text = "CONSOLE",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Themer.CurrentThemeColors.ContrastSoft,
        };
        var clear = new Button
        {
            Dock = DockStyle.Right,
            Width = 30,
            Image = Themer.GetIcon("ClearLog", 16),
            FlatStyle = FlatStyle.Flat,
        };
        clear.Click += (_, _) => _console.Clear();
        new ToolTip().SetToolTip(clear, "Clear console");

        header.Controls.Add(title);
        header.Controls.Add(logIcon);
        header.Controls.Add(clear);
        return header;
    }

    private void BrowseCs2Dir()
    {
        using var dlg = new FolderBrowserDialog { Description = "Select the Counter-Strike 2 install directory" };
        if (Directory.Exists(_cs2Dir.Text))
            dlg.SelectedPath = _cs2Dir.Text;
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _cs2Dir.Text = dlg.SelectedPath;
            SaveSettings();
        }
    }

    private void BrowseSourceMap()
    {
        using var dlg = new OpenFileDialog { Filter = "Source 1 map (*.vmf)|*.vmf|All files (*.*)|*.*", Title = "Select the source .vmf" };
        if (File.Exists(_sourceMap.Text))
            dlg.FileName = _sourceMap.Text;
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _sourceMap.Text = dlg.FileName;
            if (_outputAddon.Text.Length == 0)
                _outputAddon.Text = Path.GetFileNameWithoutExtension(dlg.FileName);
            SaveSettings();
        }
    }

    private async Task RunImportAsync()
    {
        var cs2 = new Cs2Install(_cs2Dir.Text.Trim());
        string? error = _cs2Dir.Text.Trim().Length == 0 ? "Set the CS2 directory."
            : !Directory.Exists(cs2.InstallRoot) ? $"CS2 directory not found: {cs2.InstallRoot}"
            : cs2.IsValid(out var v) ? null : v;
        if (error is not null)
        {
            AppendConsole(error, Themer.CurrentThemeColors.ControlBoxHighlightCloseButton);
            return;
        }
        if (!File.Exists(_sourceMap.Text) || !Cs2Install.TryParseSourceMap(_sourceMap.Text, out _, out _))
        {
            AppendConsole("Source map must be an existing .vmf inside a 'maps' folder.", Themer.CurrentThemeColors.ControlBoxHighlightCloseButton);
            return;
        }
        if (_outputAddon.Text.Trim().Length == 0)
        {
            AppendConsole("Enter an output addon name.", Themer.CurrentThemeColors.ControlBoxHighlightCloseButton);
            return;
        }

        SaveSettings(); // persist the inputs before a long-running import

        var project = cs2.BuildProject(_sourceMap.Text.Trim(), _outputAddon.Text.Trim(), new ImportOptions
        {
            UseBsp = _useBsp.Checked,
            UseBspNoMergeInstances = _noMerge.Checked,
            SkipDeps = _skipDeps.Checked,
        });

        var runner = new ProcessRunner();
        var service = new MapImportService(cs2.Tools, runner, AppPaths.ImportScriptsDir);
        service.OnLog += LogFromWorker;

        SetRunning(true);
        SetStatus($"Importing {project.MapName} → {project.AddonName}…");
        _cts = new CancellationTokenSource();
        try
        {
            await Task.Run(() => service.ImportAsync(project, _cts.Token));
            AppendConsole($"Done. Imported {project.MapName} into addon '{project.AddonName}'.", Themer.CurrentThemeColors.Accent);
            SetStatus("Import complete.");
        }
        catch (OperationCanceledException)
        {
            AppendConsole("Import cancelled.", Themer.CurrentThemeColors.ContrastSoft);
            SetStatus("Cancelled.");
        }
        catch (Exception ex)
        {
            AppendConsole(ex.Message, Themer.CurrentThemeColors.ControlBoxHighlightCloseButton);
            SetStatus("Import failed.");
        }
        finally
        {
            service.OnLog -= LogFromWorker;
            _cts.Dispose();
            _cts = null;
            SetRunning(false);
        }
    }

    private async Task RunValidateAsync()
    {
        var cs2 = new Cs2Install(_cs2Dir.Text.Trim());
        string? error = _cs2Dir.Text.Trim().Length == 0 ? "Set the CS2 directory."
            : !Directory.Exists(cs2.InstallRoot) ? $"CS2 directory not found: {cs2.InstallRoot}"
            : cs2.IsValid(out var v) ? null : v;
        if (error is not null)
        {
            AppendConsole(error, Themer.CurrentThemeColors.ControlBoxHighlightCloseButton);
            return;
        }
        var addon = _outputAddon.Text.Trim();
        if (addon.Length == 0)
        {
            AppendConsole("Enter the output addon to validate.", Themer.CurrentThemeColors.ControlBoxHighlightCloseButton);
            return;
        }

        var red = Themer.CurrentThemeColors.ControlBoxHighlightCloseButton;
        SetRunning(true);
        SetStatus($"Validating {addon}…");
        _cts = new CancellationTokenSource();
        try
        {
            var report = await Task.Run(() => new AssetValidator(cs2, addon)
                .Validate(line => AppendConsole(line, Themer.CurrentThemeColors.ContrastSoft), _cts.Token));

            foreach (var issue in report.Issues)
                AppendConsole($"  [{issue.Kind}] {issue.Source}  →  {issue.Detail}", red);

            if (report.HasIssues)
            {
                AppendConsole($"Validation found {report.MissingCount} missing file(s) and {report.ErrorCount} unreadable resource(s).", red);
                SetStatus("Validation found issues.");
            }
            else
            {
                AppendConsole($"Validation passed: {report.ResourcesScanned} resources, {report.ReferencesChecked} references, no missing files.", Themer.CurrentThemeColors.Accent);
                SetStatus("Validation passed.");
            }
        }
        catch (OperationCanceledException)
        {
            AppendConsole("Validation cancelled.", Themer.CurrentThemeColors.ContrastSoft);
            SetStatus("Cancelled.");
        }
        catch (Exception ex)
        {
            AppendConsole(ex.Message, red);
            SetStatus("Validation failed.");
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            SetRunning(false);
        }
    }

    private void LogFromWorker(string line)
    {
        var color = Themer.CurrentThemeColors.Contrast;
        if (line.Contains("error", StringComparison.OrdinalIgnoreCase) || line.Contains("Aborting", StringComparison.OrdinalIgnoreCase))
            color = Themer.CurrentThemeColors.ControlBoxHighlightCloseButton;
        else if (line.StartsWith("---", StringComparison.Ordinal) || line.StartsWith("- Running", StringComparison.Ordinal))
            color = Themer.CurrentThemeColors.ContrastSoft;

        AppendConsole(line, color);
    }

    private void SetRunning(bool running)
    {
        _import.Enabled = !running;
        _cancel.Enabled = running;
        _cs2Dir.Enabled = _sourceMap.Enabled = _outputAddon.Enabled = !running;
    }

    private void AppendConsole(string text, Color color)
    {
        if (_console.InvokeRequired)
        {
            _console.BeginInvoke(() => AppendConsole(text, color));
            return;
        }

        _console.SelectionStart = _console.TextLength;
        _console.SelectionColor = color;
        _console.AppendText(text + Environment.NewLine);
        _console.SelectionColor = _console.ForeColor;
        _console.ScrollToCaret();
    }

    private void SetStatus(string text)
    {
        if (_status.InvokeRequired)
            _status.BeginInvoke(() => _statusLabel.Text = text);
        else
            _statusLabel.Text = text;
    }

    // Persist user config whenever it changes — not just on close — so it
    // survives even if the process is killed (FormClosing wouldn't fire).
    private void WireSettingsPersistence()
    {
        _cs2Dir.Leave += (_, _) => SaveSettings();
        _sourceMap.Leave += (_, _) => SaveSettings();
        _outputAddon.Leave += (_, _) => SaveSettings();
        _useBsp.CheckedChanged += (_, _) => SaveSettings();
        _noMerge.CheckedChanged += (_, _) => SaveSettings();
        _skipDeps.CheckedChanged += (_, _) => SaveSettings();
    }

    private void SaveSettings() => CaptureSettings().Save();

    private void ApplySettingsToUi()
    {
        _cs2Dir.Text = _settings.Cs2Directory;
        _sourceMap.Text = _settings.SourceMap;
        _outputAddon.Text = _settings.OutputAddon;
        _useBsp.Checked = _settings.UseBsp;
        _noMerge.Checked = _settings.UseBspNoMergeInstances;
        _skipDeps.Checked = _settings.SkipDeps;
    }

    private AppSettings CaptureSettings() => new()
    {
        Cs2Directory = _cs2Dir.Text,
        SourceMap = _sourceMap.Text,
        OutputAddon = _outputAddon.Text,
        UseBsp = _useBsp.Checked,
        UseBspNoMergeInstances = _noMerge.Checked,
        SkipDeps = _skipDeps.Checked,
    };
}
