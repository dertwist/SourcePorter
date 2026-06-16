using System.Collections.Concurrent;
using System.Diagnostics;
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
    private readonly BorderedComboBox _inputMode = new();
    private readonly CheckBox _useBsp = new() { Text = "Use BSP" };
    private readonly CheckBox _noMerge = new() { Text = "Don't merge instances" };
    private readonly CheckBox _skipDeps = new() { Text = "Skip dependencies" };
    private readonly CheckBox _compileAssets = new() { Text = "Compile Assets" };
    private readonly CheckBox _compileMap = new() { Text = "Compile map" };
    private readonly CheckBox _unpackEmbedded = new() { Text = "Unpack embedded content" };
    private readonly BorderedComboBox _threads = new();
    private ThemedGroupBox? _bspOptions; // BSP-only option group; shown only in BSP input mode

    private int ThreadsValue => int.TryParse(_threads.SelectedItem as string, out var n) ? n : 4;
    private TableLayoutPanel? _inputGrid; // the field grid, kept so we can tone its labels
    private readonly Button _import = new() { Text = "Import" };
    private readonly Button _cancel = new() { Text = "Cancel", Enabled = false };
    private readonly RichTextBox _console = new();
    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _statusLabel = new();

    private CancellationTokenSource? _cts;

    // Console output is queued here and flushed in batches by _logFlushTimer on the
    // UI thread, so a chatty import can't flood the message pump and freeze the window.
    private readonly ConcurrentQueue<(string Text, Color Color)> _pendingLog = new();
    private readonly System.Windows.Forms.Timer _logFlushTimer = new() { Interval = 75 };

    public MainForm()
    {
        Text = "Source Porter";
        Icon = LoadAppIcon();
        MinimumSize = new Size(820, 560);
        Size = new Size(1040, 700);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10f);

        BuildLayout();
        ApplySettingsToUi();
        WireSettingsPersistence();
        Themer.ApplyTheme(this);
        RefineAppearance();
        FormClosing += (_, _) => SaveSettings();

        _logFlushTimer.Tick += (_, _) => FlushConsole();
        _logFlushTimer.Start();
        FormClosing += (_, _) => _logFlushTimer.Stop();

        AppendConsole($"SourcePorter v{Application.ProductVersion}", Themer.CurrentThemeColors.Accent);
        AppendConsole("Set the CS2 directory and a source .vmf, then press Import.", Themer.CurrentThemeColors.ContrastSoft);

        TryAutoDetectCs2Directory();
    }

    /// <summary>
    /// On first run (no saved CS2 directory) locate the install via the Windows
    /// registry + Steam library folders, so the user rarely has to browse for it.
    /// </summary>
    private void TryAutoDetectCs2Directory()
    {
        if (!string.IsNullOrWhiteSpace(_cs2Dir.Text))
            return;

        var detected = Cs2InstallLocator.TryLocate();
        if (detected is null)
            return;

        _cs2Dir.Text = detected;
        AppendConsole($"Detected CS2 install: {detected}", Themer.CurrentThemeColors.ContrastSoft);
        SaveSettings();
    }

    /// <summary>Loads the embedded multi-resolution app icon (see app.svg / app.ico).</summary>
    private static Icon? LoadAppIcon()
    {
        using var stream = typeof(MainForm).Assembly.GetManifestResourceStream("SourcePorter.App.app.ico");
        return stream is null ? null : new Icon(stream);
    }

    private void BuildLayout()
    {
        // --- menu ---
        var menu = new MenuStrip { Renderer = new DarkToolStripRenderer(new CustomColorTable()) };
        var tools = new ToolStripMenuItem("&Tools");
        tools.DropDownItems.Add("&Configs Editor…", Themer.GetIcon("Settings", 16), (_, _) => new ConfigsEditorForm().Show(this));
        var help = new ToolStripMenuItem("&Help");
        help.DropDownItems.Add("&Reference…", Themer.GetIcon("Find", 16), (_, _) => new ReferenceForm().Show(this));
        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.Add("E&xit", null, (_, _) => Close());
        menu.Items.Add(file);
        menu.Items.Add(tools);
        menu.Items.Add(help);

        // --- input form ---
        var form = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            Padding = new Padding(12, 10, 12, 6),
        };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        _inputGrid = form;

        AddField(form, 0, "CS2 Directory", _cs2Dir, "Browse…", BrowseCs2Dir);

        // Source Map row: the Input Method picker (VMF/BSP) sits inline ahead of the
        // path — it selects the source type and the Browse filter, and toggles the
        // BSP-only option group below.
        _inputMode.Items.AddRange(["VMF", "BSP"]);
        _inputMode.Width = 96;
        _inputMode.Anchor = AnchorStyles.Left;
        _inputMode.Margin = new Padding(0, 4, 8, 4);
        _inputMode.BorderColor = Themer.CurrentThemeColors.Accent;
        _sourceMap.Dock = DockStyle.Fill;
        _sourceMap.Margin = new Padding(0, 4, 3, 4);

        var sourceCell = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
        sourceCell.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        sourceCell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        sourceCell.Controls.Add(_inputMode, 0, 0);
        sourceCell.Controls.Add(_sourceMap, 1, 0);

        form.Controls.Add(new Label { Text = "Source Map", Anchor = AnchorStyles.Left, AutoSize = true, Margin = new Padding(3, 7, 3, 3) }, 0, 1);
        form.Controls.Add(sourceCell, 1, 1);
        var browseSource = new Button { Text = "Browse…", Dock = DockStyle.Fill, Margin = new Padding(3, 4, 3, 4) };
        browseSource.Click += (_, _) => BrowseSourceMap();
        form.Controls.Add(browseSource, 2, 1);

        AddField(form, 2, "Output Addon", _outputAddon, "Open", OpenOutputAddonFolder);

        // --- options, grouped by stage; the BSP group shows only in BSP mode ---
        _useBsp.AutoSize = _noMerge.AutoSize = _skipDeps.AutoSize = _compileAssets.AutoSize = _compileMap.AutoSize = _unpackEmbedded.AutoSize = true;
        _useBsp.CheckedChanged += (_, _) => { if (_useBsp.Checked) _noMerge.Checked = false; };
        _noMerge.CheckedChanged += (_, _) => { if (_noMerge.Checked) _useBsp.Checked = false; };

        // The mode-specific BSP group is accented to mark it as the active mode's group.
        _bspOptions = MakeOptionGroup("BSP decompile", Themer.CurrentThemeColors.Accent, _unpackEmbedded);
        _bspOptions.Margin = new Padding(0, 4, 10, 0);

        for (var i = 1; i <= 16; i++)
            _threads.Items.Add(i.ToString());
        _threads.Width = 56;
        _threads.Margin = new Padding(0, 3, 0, 3);
        _threads.BorderColor = Themer.CurrentThemeColors.Border;

        var threads = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = Padding.Empty };
        threads.Controls.Add(new Label { Text = "Threads:", AutoSize = true, Margin = new Padding(10, 6, 4, 3) });
        threads.Controls.Add(_threads);
        var importOptions = MakeOptionGroup("Import options", Themer.CurrentThemeColors.Border, _useBsp, _noMerge, _skipDeps, _compileAssets, _compileMap, threads);
        importOptions.Margin = new Padding(0, 4, 0, 0);

        var optionsRow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Margin = new Padding(0, 0, 0, 4) };
        optionsRow.Controls.Add(_bspOptions);
        optionsRow.Controls.Add(importOptions);
        form.Controls.Add(optionsRow, 1, 3);
        form.SetColumnSpan(optionsRow, 2);

        var actions = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 6, 0, 2) };
        _import.Width = 120;
        _import.Height = 30;
        _import.Image = Themer.GetIcon("Decompile", 16);
        _import.ImageAlign = ContentAlignment.MiddleLeft;
        _import.TextAlign = ContentAlignment.MiddleRight;
        _import.TextImageRelation = TextImageRelation.ImageBeforeText;
        _import.Click += async (_, _) => await RunImportAsync();
        _cancel.Width = 90;
        _cancel.Height = 30;
        _cancel.Click += (_, _) => CancelImport();
        actions.Controls.Add(_import);
        actions.Controls.Add(_cancel);
        form.Controls.Add(actions, 1, 4);
        form.SetColumnSpan(actions, 2);

        // --- console ---
        _console.Dock = DockStyle.Fill;
        _console.ReadOnly = true;
        _console.BorderStyle = BorderStyle.None;
        _console.Font = new Font("Cascadia Mono", 9.5f);
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

    // A themed GroupBox wrapping option controls in a single auto-sizing row.
    private static ThemedGroupBox MakeOptionGroup(string title, Color borderColor, params Control[] controls)
    {
        var inner = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
        };
        inner.Controls.AddRange(controls);

        var group = new ThemedGroupBox
        {
            Text = title,
            BorderColor = borderColor,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(10, 4, 10, 6),
        };
        group.Controls.Add(inner);
        return group;
    }

    /// <summary>Shows the BSP-only option group only when the input method is BSP.</summary>
    private void UpdateInputModeUi()
    {
        if (_bspOptions is not null)
            _bspOptions.Visible =
                string.Equals(_inputMode.SelectedItem as string, "BSP", StringComparison.OrdinalIgnoreCase);
    }

    // Post-theme polish. The base Themer paints every label high-contrast white, which
    // reads flat; tone the field captions and group titles to the soft tone so the
    // textbox values are the emphasis (the label/value hierarchy from the design), and
    // give the primary Import button a subtle accent border. Runs after ApplyTheme.
    private void RefineAppearance()
    {
        var muted = Themer.CurrentThemeColors.ContrastSoft;

        if (_inputGrid is not null)
            foreach (Control c in _inputGrid.Controls)
                if (c is Label label)
                    label.ForeColor = muted;

        // Input fields read as "wells": one step lighter than the window (App), so
        // the value text is the emphasis — matches the reference. The Themer paints
        // them with the window colour, so re-assert it here after ApplyTheme.
        var well = Themer.CurrentThemeColors.AppMiddle;
        foreach (Control field in new Control[] { _cs2Dir, _sourceMap, _outputAddon, _inputMode, _threads })
            field.BackColor = well;

        MuteGroupTitles(this, muted);
        _import.FlatAppearance.BorderColor = Themer.CurrentThemeColors.Accent;
    }

    private static void MuteGroupTitles(Control parent, Color color)
    {
        foreach (Control child in parent.Controls)
        {
            if (child is GroupBox group)
                group.ForeColor = color;
            MuteGroupTitles(child, color);
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
        clear.Click += (_, _) => { while (_pendingLog.TryDequeue(out _)) { } _console.Clear(); };
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
        var bsp = string.Equals(_inputMode.SelectedItem as string, "BSP", StringComparison.OrdinalIgnoreCase);
        using var dlg = new OpenFileDialog
        {
            Filter = bsp
                ? "Source 1 BSP (*.bsp)|*.bsp|All files (*.*)|*.*"
                : "Source 1 map (*.vmf)|*.vmf|All files (*.*)|*.*",
            Title = bsp ? "Select the source .bsp" : "Select the source .vmf",
        };
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

    /// <summary>
    /// Opens the output addon's content folder in Explorer. Falls back to the
    /// <c>content\csgo_addons</c> parent when the addon hasn't been imported yet
    /// (or no name is set), so the button always lands somewhere useful.
    /// </summary>
    private void OpenOutputAddonFolder()
    {
        var red = Themer.CurrentThemeColors.ControlBoxHighlightCloseButton;

        var root = _cs2Dir.Text.Trim();
        if (root.Length == 0 || !Directory.Exists(root))
        {
            AppendConsole("Set a valid CS2 directory first.", red);
            return;
        }

        var cs2 = new Cs2Install(root);
        var addon = _outputAddon.Text.Trim();
        var addonDir = cs2.ContentAddonDir(addon);

        var target = addon.Length > 0 && Directory.Exists(addonDir) ? addonDir
            : Directory.Exists(cs2.ContentAddonsDir) ? cs2.ContentAddonsDir
            : null;

        if (target is null)
        {
            AppendConsole($"Folder not found: {cs2.ContentAddonsDir}", red);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppendConsole($"Couldn't open folder: {ex.Message}", red);
        }
    }

    private async Task RunImportAsync()
    {
        var red = Themer.CurrentThemeColors.ControlBoxHighlightCloseButton;
        var muted = Themer.CurrentThemeColors.ContrastSoft;

        var cs2 = new Cs2Install(_cs2Dir.Text.Trim());
        string? error = _cs2Dir.Text.Trim().Length == 0 ? "Set the CS2 directory."
            : !Directory.Exists(cs2.InstallRoot) ? $"CS2 directory not found: {cs2.InstallRoot}"
            : cs2.IsValid(out var v) ? null : v;
        if (error is not null) { AppendConsole(error, red); return; }

        var source = _sourceMap.Text.Trim();
        if (source.Length == 0 || !File.Exists(source)) { AppendConsole("Source map file not found.", red); return; }
        if (_outputAddon.Text.Trim().Length == 0) { AppendConsole("Enter an output addon name.", red); return; }

        SaveSettings(); // persist the inputs before a long-running import

        var bspMode = string.Equals(_inputMode.SelectedItem as string, "BSP", StringComparison.OrdinalIgnoreCase);
        var addon = _outputAddon.Text.Trim();
        var runner = new ProcessRunner();
        MapImportService? service = null;

        SetRunning(true);
        _cts = new CancellationTokenSource();
        try
        {
            // Stage the source into a temp content root that satisfies source1import's
            // "<contentdir>\maps\<name>.vmf" layout. For a .bsp this decompiles it first
            // (and, when enabled, unpacks its embedded materials/models alongside).
            string vmf;
            if (bspMode)
            {
                var decompiler = new BspDecompiler(runner);
                decompiler.OnLog += LogFromWorker;
                try
                {
                    vmf = await Task.Run(() =>
                        MapStaging.StageBspAsync(decompiler, source, _unpackEmbedded.Checked, _cts.Token));
                }
                finally { decompiler.OnLog -= LogFromWorker; }
            }
            else
            {
                vmf = await Task.Run(() => MapStaging.StageVmf(source, LogFromWorker));
            }
            AppendConsole($"Source staged → {vmf}", muted);

            var project = cs2.BuildProject(vmf, addon, new ImportOptions
            {
                UseBsp = _useBsp.Checked,
                UseBspNoMergeInstances = _noMerge.Checked,
                SkipDeps = _skipDeps.Checked,
                CompileAssets = _compileAssets.Checked,
                MaxParallelism = ThreadsValue,
            });

            service = new MapImportService(cs2.Tools, runner, ResolveImportScriptsDir(cs2));
            service.OnLog += LogFromWorker;

            SetStatus($"Importing {project.MapName} → {addon}…");
            await Task.Run(() => service.ImportAsync(project, _cts.Token));

            if (_compileMap.Checked)
            {
                AppendConsole($"Compiling {project.MapName} → .vmap_c…", muted);
                await Task.Run(() => service.CompileMapAsync(project, _cts.Token));
            }

            AppendConsole($"Done. Imported {project.MapName} into addon '{addon}'.", Themer.CurrentThemeColors.Accent);
            SetStatus("Import complete.");

            // Validation reads compiled _c resources (RERL), so it only makes sense when
            // something was compiled. With Compile Assets/Compile map off (the fast path)
            // there are no _c files to check — skip with a note rather than report "0 resources".
            if (_compileAssets.Checked || _compileMap.Checked)
                await ValidateAddonAsync(cs2, addon, _cts.Token);
            else
                AppendConsole("Validation skipped — no assets were compiled (enable 'Compile Assets' to validate).", muted);
        }
        catch (OperationCanceledException)
        {
            AppendConsole("Import cancelled.", muted);
            SetStatus("Cancelled.");
        }
        catch (Exception ex)
        {
            AppendConsole(ex.Message, red);
            SetStatus("Import failed.");
        }
        finally
        {
            if (service is not null) service.OnLog -= LogFromWorker;
            _cts.Dispose();
            _cts = null;
            SetRunning(false);
        }
    }

    /// <summary>
    /// Requests cancellation without blocking the UI thread. <see cref="CancellationTokenSource.Cancel()"/>
    /// runs its registered callbacks synchronously on the caller — and those callbacks kill the tool
    /// process trees (<c>Process.Kill(entireProcessTree: true)</c>), which can take a while and would
    /// freeze the window. <see cref="CancellationTokenSource.CancelAsync"/> runs them on the thread pool
    /// instead, so the UI stays responsive. The button is disabled to give feedback and block re-clicks.
    /// </summary>
    private void CancelImport()
    {
        if (_cts is null)
            return;

        _cancel.Enabled = false;
        SetStatus("Cancelling…");
        _ = _cts.CancelAsync();
    }

    // source1import needs its real home dir as cwd (config lists + ./bin/vbsp.exe).
    private static string ResolveImportScriptsDir(Cs2Install cs2) =>
        Directory.Exists(cs2.ImportScriptsDir) ? cs2.ImportScriptsDir : AppPaths.ImportScriptsDir;

    /// <summary>
    /// Runs the asset validator over the addon and reports findings to the console.
    /// Called automatically at the end of a successful import — it does not own the
    /// running-state or cancellation token; the caller (RunImportAsync) does, so the
    /// Cancel button aborts validation too. Lets <see cref="OperationCanceledException"/>
    /// propagate to the import's handler; other failures are reported without marking
    /// the (already successful) import as failed.
    /// </summary>
    private async Task ValidateAddonAsync(Cs2Install cs2, string addon, CancellationToken ct)
    {
        var red = Themer.CurrentThemeColors.ControlBoxHighlightCloseButton;
        SetStatus($"Validating {addon}…");
        try
        {
            var report = await Task.Run(() => new AssetValidator(cs2, addon)
                .Validate(line => AppendConsole(line, Themer.CurrentThemeColors.ContrastSoft), ct));

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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppendConsole($"Validation error: {ex.Message}", red);
            SetStatus("Validation failed.");
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
        _inputMode.Enabled = _threads.Enabled = !running;
    }

    // Thread-safe and cheap: the worker threads just enqueue. The actual RichTextBox
    // writes happen in FlushConsole on the UI thread, batched, so input stays live.
    private void AppendConsole(string text, Color color) => _pendingLog.Enqueue((text, color));

    // Drains queued lines on the UI thread (called by _logFlushTimer). Bounded per
    // tick so even a huge burst of output can't stall Cancel/menu/redraw handling.
    private void FlushConsole()
    {
        if (_pendingLog.IsEmpty)
            return;

        var appended = 0;
        while (appended < 400 && _pendingLog.TryDequeue(out var entry))
        {
            _console.SelectionStart = _console.TextLength;
            _console.SelectionColor = entry.Color;
            _console.AppendText(entry.Text + Environment.NewLine);
            appended++;
        }
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
        _sourceMap.TextChanged += (_, _) => UpdateTitle();
        _sourceMap.Leave += (_, _) => SaveSettings();
        _outputAddon.Leave += (_, _) => SaveSettings();
        _useBsp.CheckedChanged += (_, _) => SaveSettings();
        _noMerge.CheckedChanged += (_, _) => SaveSettings();
        _skipDeps.CheckedChanged += (_, _) => SaveSettings();
        _compileAssets.CheckedChanged += (_, _) => SaveSettings();
        _compileMap.CheckedChanged += (_, _) => SaveSettings();
        _unpackEmbedded.CheckedChanged += (_, _) => SaveSettings();
        _inputMode.SelectedIndexChanged += (_, _) => { UpdateInputModeUi(); SaveSettings(); };
        _threads.SelectedIndexChanged += (_, _) => SaveSettings();
    }

    private void SaveSettings() => CaptureSettings().Save();

    private void UpdateTitle()
    {
        var path = _sourceMap.Text.Trim();
        Text = path.Length > 0
            ? $"Source Porter [{Path.GetFileName(path)}]"
            : "Source Porter";
    }

    private void ApplySettingsToUi()
    {
        _cs2Dir.Text = _settings.Cs2Directory;
        _sourceMap.Text = _settings.SourceMap;
        _outputAddon.Text = _settings.OutputAddon;
        _useBsp.Checked = _settings.UseBsp;
        _noMerge.Checked = _settings.UseBspNoMergeInstances;
        _skipDeps.Checked = _settings.SkipDeps;
        _compileAssets.Checked = _settings.CompileAssets;
        _compileMap.Checked = _settings.CompileMap;
        _unpackEmbedded.Checked = _settings.UnpackEmbedded;
        _inputMode.SelectedItem = _inputMode.Items.Contains(_settings.InputMode) ? _settings.InputMode : "VMF";
        UpdateInputModeUi();
        _threads.SelectedItem = Math.Clamp(_settings.Threads, 1, 16).ToString();
        UpdateTitle();
    }

    private AppSettings CaptureSettings() => new()
    {
        Cs2Directory = _cs2Dir.Text,
        SourceMap = _sourceMap.Text,
        OutputAddon = _outputAddon.Text,
        UseBsp = _useBsp.Checked,
        UseBspNoMergeInstances = _noMerge.Checked,
        SkipDeps = _skipDeps.Checked,
        CompileAssets = _compileAssets.Checked,
        CompileMap = _compileMap.Checked,
        UnpackEmbedded = _unpackEmbedded.Checked,
        InputMode = _inputMode.SelectedItem as string ?? "VMF",
        Threads = ThreadsValue,
    };
}
