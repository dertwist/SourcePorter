using System.Collections.Concurrent;
using System.Diagnostics;
using SourcePorter.App.Theme;
using SourcePorter.Core.Domain;
using SourcePorter.Core.Toolchain;
using SourcePorter.Core.Validation;
using SourcePorter.Core.Vmap;

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
    private readonly CheckBox _compactLog = new() { Text = "Compact log" };
    private readonly CheckBox _unpackEmbedded = new() { Text = "Unpack embedded content" };
    private readonly CheckBox _collapsePrefabs = new() { Text = "Collapse prefabs" };
    private readonly CheckBox _skyboxTemplate = new() { Text = "Skybox template" };
    private ThemedGroupBox? _bspOptions; // BSP-only option group; shown only in BSP input mode

    private TableLayoutPanel? _inputGrid; // the field grid, kept so we can tone its labels
    private readonly Button _import = new() { Text = "Import" };
    private readonly Button _cancel = new() { Text = "Cancel", Enabled = false };
    private readonly ConsoleTextBox _console = new();
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
        Size = new Size(1366, 651);
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

        AppendConsole($"SourcePorter v{Application.ProductVersion.Split('+')[0]}", Themer.CurrentThemeColors.Accent);
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
        tools.DropDownItems.Add("&Import missing assets…", Themer.GetIcon("Recover", 16), async (_, _) => await RunImportMissingAsync());
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
        _useBsp.AutoSize = _noMerge.AutoSize = _skipDeps.AutoSize = _compileAssets.AutoSize = _compactLog.AutoSize = _unpackEmbedded.AutoSize = _collapsePrefabs.AutoSize = _skyboxTemplate.AutoSize = true;
        _useBsp.CheckedChanged += (_, _) => { if (_useBsp.Checked) _noMerge.Checked = false; };
        _noMerge.CheckedChanged += (_, _) => { if (_noMerge.Checked) _useBsp.Checked = false; };

        // The mode-specific BSP group is accented to mark it as the active mode's group.
        _bspOptions = MakeOptionGroup("BSP decompile", Themer.CurrentThemeColors.Accent, _unpackEmbedded);
        _bspOptions.Margin = new Padding(0, 4, 10, 0);

        var importOptions = MakeOptionGroup("Import options", Themer.CurrentThemeColors.Border, _useBsp, _noMerge, _skipDeps, _compileAssets, _compactLog, _collapsePrefabs, _skyboxTemplate);
        importOptions.Margin = new Padding(0, 4, 0, 0);

        var optionsRow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Margin = new Padding(0, 0, 0, 4) };
        optionsRow.Controls.Add(_bspOptions);
        optionsRow.Controls.Add(importOptions);
        // Span all three columns from column 0 so the option groups use the full width and
        // don't leave the label column blank to their left.
        form.Controls.Add(optionsRow, 0, 3);
        form.SetColumnSpan(optionsRow, 3);

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
        form.Controls.Add(actions, 0, 4);
        form.SetColumnSpan(actions, 3);

        // --- console ---
        _console.Dock = DockStyle.Fill;
        _console.ReadOnly = true;
        _console.BorderStyle = BorderStyle.None;
        _console.Font = new Font("Cascadia Mono", 9.5f);
        var consoleHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 8) };
        // Add order matters: docking resolves last-added-first. Console (Fill) first so it
        // takes the leftover space, then the line-number gutter (Left), then the header
        // (Top, added last) so it spans the full width above both.
        consoleHost.Controls.Add(_console);
        consoleHost.Controls.Add(new ConsoleLineGutter(_console));
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
        foreach (Control field in new Control[] { _cs2Dir, _sourceMap, _outputAddon, _inputMode })
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
        if (_outputAddon.Text.Trim().Length == 0)
        {
            AppendConsole("Enter an output addon name.", red);
            MessageBox.Show(this, "Enter an output addon name before importing.",
                "Output addon required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _outputAddon.Focus();
            return;
        }

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

            // A BSPSource-decompiled map is flat — every func_instance is inlined into the
            // world, so there are no instances to merge, and plain -usebsp can crash
            // source1import's merge pass on such maps. Prefer -usebsp_nomergeinstances for
            // decompiled input (the checkbox state is left as the user set it).
            var useBsp = _useBsp.Checked;
            var noMerge = _noMerge.Checked;
            if (bspMode && useBsp)
            {
                useBsp = false;
                noMerge = true;
                AppendConsole("Decompiled BSP: using -usebsp_nomergeinstances (a flat decompiled map has " +
                              "no instances to merge, and plain -usebsp can crash source1import).", muted);
            }

            var project = cs2.BuildProject(vmf, addon, new ImportOptions
            {
                UseBsp = useBsp,
                UseBspNoMergeInstances = noMerge,
                SkipDeps = _skipDeps.Checked,
                CompileAssets = _compileAssets.Checked,
                CompactLog = _compactLog.Checked,
                // MaxParallelism defaults to all logical processors minus one.
            });

            service = new MapImportService(cs2.Tools, runner, ResolveImportScriptsDir(cs2));
            service.OnLog += LogFromWorker;

            SetStatus($"Importing {project.MapName} → {addon}…");
            await Task.Run(() => service.ImportAsync(project, _cts.Token));

            AppendConsole($"Done. Imported {project.MapName} into addon '{addon}'.", Themer.CurrentThemeColors.Accent);
            SetStatus("Import complete.");

            // Opt-in post-import .vmap edits, before stats/validation so those reflect the final map.
            await RunPostImportVmapToolsAsync(cs2, addon, project.MapName, _cts.Token);

            ReportAddonStats(cs2, addon);

            // Validation checks compiled-resource RERL deps AND uncompiled model sources
            // (missing .dmx/.fbx/.vmdl), so it's worth running even on the fast no-compile
            // path — the source-dependency pass works without any _c files. Repairing the
            // missing materials/models is a separate, manual step (Tools → Import missing assets).
            await ValidateAddonAsync(cs2, addon, _cts.Token);
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
    /// Runs the opt-in post-import <c>.vmap</c> edits (Collapse prefabs, Skybox template) on the
    /// freshly-imported main map, before stats/validation so those reflect the final map. Shares
    /// the import's cancellation token; non-cancellation failures are reported without failing the
    /// (already successful) import. See <see cref="PostImportVmapTools"/>.
    /// </summary>
    private async Task RunPostImportVmapToolsAsync(Cs2Install cs2, string addon, string mapName, CancellationToken ct)
    {
        if (!_collapsePrefabs.Checked && !_skyboxTemplate.Checked)
            return;

        var red = Themer.CurrentThemeColors.ControlBoxHighlightCloseButton;
        try
        {
            if (_collapsePrefabs.Checked)
            {
                SetStatus("Collapsing prefabs…");
                await Task.Run(() => PostImportVmapTools.CollapsePrefabs(cs2, addon, mapName, LogFromWorker, ct), ct);
            }

            if (_skyboxTemplate.Checked)
            {
                SetStatus("Creating skybox template…");
                await Task.Run(() => PostImportVmapTools.CreateSkyboxTemplate(cs2, addon, mapName, LogFromWorker, ct), ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppendConsole($"Post-import vmap tools error: {ex.Message}", red);
        }
    }

    /// <summary>
    /// Tools → Import missing assets. Validates the already-imported addon and re-imports
    /// the materials/models the main import missed (a model's gib/breakpiece children, a
    /// skybox material from a lighting prefab, …), looping import→re-validate to a fixpoint
    /// via <see cref="MissingAssetImporter"/>. Runs against the current CS2 directory + output
    /// addon, independently of the source map — so it can repair any previously-imported addon.
    /// Shares the running-state and cancellation token with <see cref="RunImportAsync"/>, so
    /// only one runs at a time and Cancel aborts it.
    /// </summary>
    private async Task RunImportMissingAsync()
    {
        var red = Themer.CurrentThemeColors.ControlBoxHighlightCloseButton;
        var muted = Themer.CurrentThemeColors.ContrastSoft;

        if (_cts is not null)
        {
            AppendConsole("An import is already running.", muted);
            return;
        }

        var cs2 = new Cs2Install(_cs2Dir.Text.Trim());
        string? error = _cs2Dir.Text.Trim().Length == 0 ? "Set the CS2 directory."
            : !Directory.Exists(cs2.InstallRoot) ? $"CS2 directory not found: {cs2.InstallRoot}"
            : cs2.IsValid(out var v) ? null : v;
        if (error is not null) { AppendConsole(error, red); return; }

        var addon = _outputAddon.Text.Trim();
        if (addon.Length == 0) { AppendConsole("Enter an output addon name.", red); return; }
        if (!Directory.Exists(cs2.ContentAddonDir(addon)))
        {
            AppendConsole($"Addon content not found: {cs2.ContentAddonDir(addon)}. Import the map first.", red);
            return;
        }

        var runner = new ProcessRunner();
        var service = new MapImportService(cs2.Tools, runner, ResolveImportScriptsDir(cs2));
        service.OnLog += LogFromWorker;

        // Repair only needs the install-derived paths + addon (the missing assets are
        // re-imported by exact path); the source map isn't required, so build a minimal
        // project directly rather than via Cs2Install.BuildProject (which parses a .vmf).
        var project = new PortProject
        {
            S1GameInfoDir = cs2.S1GameInfoDir,
            S2GameInfoDir = cs2.S2GameInfoDir,
            AddonName = addon,
            MapName = addon,
            Import = new ImportOptions
            {
                CompileAssets = _compileAssets.Checked,
                CompactLog = _compactLog.Checked,
                // MaxParallelism defaults to all logical processors minus one.
            },
        };

        SetRunning(true);
        _cts = new CancellationTokenSource();
        try
        {
            var report = await ValidateAddonAsync(cs2, addon, _cts.Token);
            if (report is null)
                return; // validation error already reported

            if (report.MissingImportCount == 0)
            {
                AppendConsole("No un-imported materials/models found — nothing to import.", Themer.CurrentThemeColors.Accent);
                SetStatus("Nothing to import.");
                return;
            }

            await RepairMissingAssetsAsync(cs2, service, project, report, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            AppendConsole("Import missing cancelled.", muted);
            SetStatus("Cancelled.");
        }
        catch (Exception ex)
        {
            AppendConsole(ex.Message, red);
            SetStatus("Import missing failed.");
        }
        finally
        {
            service.OnLog -= LogFromWorker;
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
    private async Task<ValidationReport?> ValidateAddonAsync(Cs2Install cs2, string addon, CancellationToken ct)
    {
        var red = Themer.CurrentThemeColors.ControlBoxHighlightCloseButton;
        SetStatus($"Validating {addon}…");
        try
        {
            var report = await Task.Run(() => new AssetValidator(cs2, addon)
                .Validate(line => AppendConsole(line, Themer.CurrentThemeColors.ContrastSoft), ct));

            // The missing-import list can be hundreds of lines — cap it so the console
            // stays readable, but keep the full counts in the summary below.
            const int cap = 60;
            var shown = 0;
            foreach (var issue in report.Issues)
            {
                if (shown++ == cap)
                {
                    AppendConsole($"  …and {report.Issues.Count - cap} more (re-import with dependencies to resolve).", red);
                    break;
                }
                AppendConsole($"  [{issue.Kind}] {issue.Source}  →  {issue.Detail}", red);
            }

            if (report.HasIssues)
            {
                if (report.MissingImportCount > 0)
                    AppendConsole(
                        $"The content references {report.MissingImportMaterials} material(s) and {report.MissingImportModels} model(s) " +
                        "that are NOT imported and NOT in base CS2 — the map will load with them missing. " +
                        "Re-import with Skip-dependencies OFF (and Compile Assets ON for a shippable addon).", red);

                AppendConsole(
                    $"Validation found {report.MissingPrefabCount} missing prefab vmap(s), " +
                    $"{report.MissingImportCount} un-imported material/model(s), " +
                    $"{report.MissingSourceCount} missing mesh source(s), " +
                    $"{report.MissingReferenceCount} missing compiled dep(s), and " +
                    $"{report.ErrorCount} unreadable resource(s).", red);
                SetStatus("Validation found issues.");
            }
            else
            {
                AppendConsole(
                    $"Validation passed: {report.ContentFilesScanned} content file(s), " +
                    $"{report.ReferencesChecked} dependency(ies) checked — nothing missing.",
                    Themer.CurrentThemeColors.Accent);
                SetStatus("Validation passed.");
            }

            return report;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppendConsole($"Validation error: {ex.Message}", red);
            SetStatus("Validation failed.");
            return null;
        }
    }

    /// <summary>
    /// Re-imports the materials/models the validator reported as un-imported, then prints a
    /// summary. Looping and re-validation live in <see cref="MissingAssetImporter"/>; this
    /// just wires it to the console and reuses the import <paramref name="service"/> (same
    /// tools and working dir). Like validation, it shares the import's running-state and
    /// cancellation token, so Cancel aborts it too.
    /// </summary>
    private async Task RepairMissingAssetsAsync(
        Cs2Install cs2, MapImportService service, PortProject project, ValidationReport report, CancellationToken ct)
    {
        var muted = Themer.CurrentThemeColors.ContrastSoft;
        var red = Themer.CurrentThemeColors.ControlBoxHighlightCloseButton;

        var importer = new MissingAssetImporter(service, cs2);
        importer.OnLog += LogFromWorker;
        SetStatus("Importing missing assets…");
        try
        {
            AppendConsole(
                $"Re-importing {report.MissingImportCount} material/model(s) the importer missed…",
                muted);

            var result = await Task.Run(() => importer.RepairAsync(project, report, ct: ct));

            AppendConsole(
                $"Repair imported {result.ModelsImported} model(s) and {result.MaterialsImported} material(s) " +
                $"across {result.Rounds} round(s): resolved {result.Resolved} of {result.InitialMissing}.",
                result.StillMissing == 0 ? Themer.CurrentThemeColors.Accent : muted);

            if (result.StillMissing > 0)
                AppendConsole(
                    $"{result.StillMissing} material/model(s) still un-imported — no Source 1 source was found for them " +
                    "(they may be genuinely absent from this CS:GO install).", red);
            else
                SetStatus("Missing assets imported.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppendConsole($"Repair error: {ex.Message}", red);
        }
        finally
        {
            importer.OnLog -= LogFromWorker;
        }
    }

    /// <summary>Tallies and prints the imported addon's size + asset counts (see <see cref="AddonStats"/>).</summary>
    private void ReportAddonStats(Cs2Install cs2, string addon)
    {
        try
        {
            var gameDir = Path.Combine(cs2.GameDir, "csgo_addons", addon);
            var stats = AddonStats.Collect(cs2.ContentAddonDir(addon), gameDir);
            foreach (var line in stats.Format())
                AppendConsole(line, Themer.CurrentThemeColors.ContrastSoft);
        }
        catch (Exception ex)
        {
            AppendConsole($"Could not collect addon statistics: {ex.Message}", Themer.CurrentThemeColors.ContrastSoft);
        }
    }

    private void LogFromWorker(string line)
    {
        var color = Themer.CurrentThemeColors.Contrast;
        if (line.Contains("error", StringComparison.OrdinalIgnoreCase) || line.Contains("Aborting", StringComparison.OrdinalIgnoreCase))
            color = Themer.CurrentThemeColors.ControlBoxHighlightCloseButton;
        else if (line.StartsWith("▶", StringComparison.Ordinal))
            color = Themer.CurrentThemeColors.Accent;
        else if (line.StartsWith("  Ported ", StringComparison.Ordinal) || line.StartsWith("  Compiled ", StringComparison.Ordinal)
            || line.StartsWith("  Imported ", StringComparison.Ordinal) || line.StartsWith("  (repeated ", StringComparison.Ordinal))
            color = Themer.CurrentThemeColors.ContrastSoft;
        else if (line.StartsWith("---", StringComparison.Ordinal) || line.StartsWith("- Running", StringComparison.Ordinal))
            color = Themer.CurrentThemeColors.ContrastSoft;

        AppendConsole(line, color);
    }

    private void SetRunning(bool running)
    {
        _import.Enabled = !running;
        _cancel.Enabled = running;
        _cs2Dir.Enabled = _sourceMap.Enabled = _outputAddon.Enabled = !running;
        _inputMode.Enabled = !running;
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
        _compactLog.CheckedChanged += (_, _) => SaveSettings();
        _unpackEmbedded.CheckedChanged += (_, _) => SaveSettings();
        _collapsePrefabs.CheckedChanged += (_, _) => SaveSettings();
        _skyboxTemplate.CheckedChanged += (_, _) => SaveSettings();
        _inputMode.SelectedIndexChanged += (_, _) => { UpdateInputModeUi(); SaveSettings(); };
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
        _compactLog.Checked = _settings.CompactLog;
        _unpackEmbedded.Checked = _settings.UnpackEmbedded;
        _collapsePrefabs.Checked = _settings.CollapsePrefabs;
        _skyboxTemplate.Checked = _settings.CreateSkyboxTemplate;
        _inputMode.SelectedItem = _inputMode.Items.Contains(_settings.InputMode) ? _settings.InputMode : "VMF";
        UpdateInputModeUi();
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
        CompactLog = _compactLog.Checked,
        UnpackEmbedded = _unpackEmbedded.Checked,
        CollapsePrefabs = _collapsePrefabs.Checked,
        CreateSkyboxTemplate = _skyboxTemplate.Checked,
        InputMode = _inputMode.SelectedItem as string ?? "VMF",
    };
}
