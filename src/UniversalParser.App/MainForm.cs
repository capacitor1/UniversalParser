using System.Collections.Immutable;
using System.Globalization;
using UniversalParser.App.Controls;
using UniversalParser.Core.Binary;
using UniversalParser.Core.Export;
using UniversalParser.Core.Formatting;
using UniversalParser.Core.Model;
using UniversalParser.Core.Parsing;

namespace UniversalParser.App;

/// <summary>
/// Workbench window. Layout, fonts and control positions follow the legacy Form1; the parser,
/// file access, tree population and export paths were rebuilt on the immutable core so the UI never
/// blocks and never shows stale results.
/// </summary>
internal sealed partial class MainForm : Form
{
    private static readonly ParserEngine Engine = new(ParserRegistry.CreateDefault());

    private static readonly int[] TopChartSliceOptions = [3, 5, 8, 10];

    private readonly LoadingOverlay _loadingOverlay;
    private readonly ToolStripMenuItem _statisticsMenuItem;
    private readonly Dictionary<FormatNode, NodeParseResult> _nodeCache = new();
    private readonly Queue<FormatNode> _cacheOrder = new();

    private ParseSession? _session;
    private CancellationTokenSource? _parseCts;
    private CancellationTokenSource? _nodeCts;
    private CancellationTokenSource? _expandCts;
    private CancellationTokenSource? _exportCts;
    private TreeNode? _selectedTreeNode;
    private string? _startupPath;
    private int _loadGeneration;
    private int _selectionGeneration;
    private int _nodeOverlayGeneration;
    private bool _closing;

    public MainForm(string? startupPath = null)
    {
        _startupPath = startupPath;
        InitializeComponent();

        MainStage.Visible = false;

        _loadingOverlay = new LoadingOverlay(
            this,
            LoadingPanel,
            LoadingProgressBar,
            LoadingTitle,
            LoadingInfo,
            LoadingCancel);

        VirtualFieldListView.Initialize(ParseResultList);

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add(new ToolStripMenuItem("Export Raw", null, ExportRaw_Click));
        contextMenu.Items.Add(new ToolStripMenuItem("Export Analysis", null, ExportAnalysis_Click));
        DataStructureTreeV.ContextMenuStrip = contextMenu;
        components?.Add(contextMenu);

        viewVToolStripMenuItem.DropDownItems.Add(new ToolStripSeparator());
        _statisticsMenuItem = new ToolStripMenuItem("Statistics(&S)", null, Statistics_Click)
        {
            Name = "Statistics",
            ShortcutKeys = Keys.Control | Keys.Shift | Keys.S,
            Enabled = false,
        };
        viewVToolStripMenuItem.DropDownItems.Add(_statisticsMenuItem);
        BuildSettingsMenu();

        TipStage.Visible = true;
        TipStage.BringToFront();
        DropArea.AllowDrop = true;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _ = ParseLimitsConfiguration.Current;
        if (ParseLimitsConfiguration.CurrentError is { Length: > 0 } limitsError)
        {
            SetStatus($"Limits configuration ignored: {limitsError}");
        }

        if (!string.IsNullOrWhiteSpace(_startupPath) && File.Exists(_startupPath))
        {
            var path = _startupPath;
            _startupPath = null;
            _ = OpenFileAsync(path);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _closing = true;
        CancelAllWork();
        _session?.Dispose();
        _session = null;
        _loadingOverlay.Hide();
        _loadingOverlay.SetCancelHandler(null);
        base.OnFormClosing(e);
    }

    private void Menu_Open_Click(object? sender, EventArgs e) => ShowOpenDialog();

    private void M_Open_Click(object? sender, EventArgs e) => ShowOpenDialog();

    private void Menu_Close_Click(object? sender, EventArgs e) => CloseCurrentFile();

    private void Statistics_Click(object? sender, EventArgs e)
    {
        var session = _session;
        if (session is null)
        {
            return;
        }

        var target = StatisticsForm.ShowFor(this, session.Document);
        if (target is not null)
        {
            SelectNode(target);
        }
    }

    private void BuildSettingsMenu()
    {
        var decimals = new ToolStripMenuItem("Percentage decimals(&P)");
        for (var value = ValueFormat.MinPercentageDecimals; value <= ValueFormat.MaxPercentageDecimals; value++)
        {
            var option = new ToolStripMenuItem(value.ToString(CultureInfo.InvariantCulture))
            {
                Tag = value,
                Checked = value == ValueFormat.PercentageDecimals,
            };
            option.Click += PercentageDecimals_Click;
            decimals.DropDownItems.Add(option);
        }

        settingsToolStripMenuItem.DropDownItems.Add(decimals);

        var topNames = new ToolStripMenuItem("Top node names(&T)");
        foreach (var value in TopChartSliceOptions)
        {
            var option = new ToolStripMenuItem(value.ToString(CultureInfo.InvariantCulture))
            {
                Tag = value,
                Checked = value == AppSettings.TopChartSlices,
            };
            option.Click += TopChartSlices_Click;
            topNames.DropDownItems.Add(option);
        }

        settingsToolStripMenuItem.DropDownItems.Add(topNames);
    }

    private static void PercentageDecimals_Click(object? sender, EventArgs e)
    {
        if (sender is not ToolStripMenuItem { Tag: int value, OwnerItem: ToolStripMenuItem parent })
        {
            return;
        }

        ValueFormat.PercentageDecimals = value;
        foreach (var item in parent.DropDownItems.OfType<ToolStripMenuItem>())
        {
            item.Checked = item.Tag is int option && option == ValueFormat.PercentageDecimals;
        }
    }

    private static void TopChartSlices_Click(object? sender, EventArgs e)
    {
        if (sender is not ToolStripMenuItem { Tag: int value, OwnerItem: ToolStripMenuItem parent })
        {
            return;
        }

        AppSettings.TopChartSlices = value;
        foreach (var item in parent.DropDownItems.OfType<ToolStripMenuItem>())
        {
            item.Checked = item.Tag is int option && option == AppSettings.TopChartSlices;
        }
    }

    private void ShowOpenDialog()
    {
        using var dialog = new OpenFileDialog
        {
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            ValidateNames = true,
            Filter = "All Files (*.*)|*.*",
            Multiselect = false,
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _ = OpenFileAsync(dialog.FileName);
        }
    }

    private async Task OpenFileAsync(string path)
    {
        if (_closing)
        {
            return;
        }

        var generation = ++_loadGeneration;
        CancelAllWork();
        ClearDocumentUi();
        _session?.Dispose();
        _session = null;

        TipStage.Visible = false;
        TipStage.SendToBack();
        LoadingPanel.BringToFront();

        var cts = new CancellationTokenSource();
        _parseCts = cts;
        _nodeOverlayGeneration = 0;
        _loadingOverlay.SetCancelHandler(() => cts.Cancel());
        _loadingOverlay.Show("Loading...");

        var progress = new Progress<ParseProgress>(value =>
        {
            if (!_closing && generation == _loadGeneration)
            {
                _loadingOverlay.UpdateProgress(value);
            }
        });

        try
        {
            var session = await ParseSession.OpenAsync(
                path,
                Engine,
                ParseLimitsConfiguration.Current,
                progress,
                cts.Token).ConfigureAwait(true);

            if (_closing || generation != _loadGeneration || cts.IsCancellationRequested)
            {
                session.Dispose();
                return;
            }

            _session = session;
            BuildTree(session);
            SetStatus(
                session.Document.Diagnostics.Length == 0
                    ? $"Loaded {Path.GetFileName(path)}."
                    : $"Loaded {Path.GetFileName(path)} with {session.Document.Diagnostics.Length:N0} diagnostic(s).");
        }
        catch (OperationCanceledException)
        {
            if (!_closing && generation == _loadGeneration)
            {
                SetStatus("Parsing cancelled.");
                MessageBox.Show(this, "Parsing cancelled.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception exception)
        {
            if (!_closing && generation == _loadGeneration)
            {
                _session?.Dispose();
                _session = null;
                ClearDocumentUi();
                TipStage.Visible = true;
                TipStage.BringToFront();
                SetStatus("Open failed.");
                MessageBox.Show(
                    this,
                    $"Failed to open file: {exception.Message}",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
        finally
        {
            if (generation == _loadGeneration)
            {
                _loadingOverlay.Hide();
                _loadingOverlay.SetCancelHandler(null);
            }

            if (ReferenceEquals(_parseCts, cts))
            {
                _parseCts = null;
            }

            cts.Dispose();
        }
    }

    private void CloseCurrentFile()
    {
        _loadGeneration++;
        CancelAllWork();
        _session?.Dispose();
        _session = null;
        ClearDocumentUi();
        _loadingOverlay.Hide();
        _loadingOverlay.SetCancelHandler(null);
        TipStage.Visible = true;
        TipStage.BringToFront();
        SetStatus(string.Empty);
    }

    private void CancelAllWork()
    {
        CancelAndDispose(ref _parseCts);
        CancelAndDispose(ref _nodeCts);
        CancelAndDispose(ref _expandCts);
        CancelAndDispose(ref _exportCts);
    }

    private static void CancelAndDispose(ref CancellationTokenSource? source)
    {
        if (source is null)
        {
            return;
        }

        try
        {
            source.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already cancelled or disposed by a completing operation.
        }

        source.Dispose();
        source = null;
    }

    private void ClearDocumentUi()
    {
        _statisticsMenuItem.Enabled = false;
        _selectionGeneration++;
        _selectedTreeNode = null;
        _nodeCache.Clear();
        _cacheOrder.Clear();

        DataStructureTreeV.BeginUpdate();
        try
        {
            DataStructureTreeV.Nodes.Clear();
        }
        finally
        {
            DataStructureTreeV.EndUpdate();
        }

        VirtualFieldListView.Clear(ParseResultList);
        RawDataTextBox.Unbind();
        RawDataInfo.Text = string.Empty;
        UTitle.Text = string.Empty;
        Title.Text = string.Empty;
        Desc.Text = string.Empty;
        MainStage.Visible = false;
        MainStage.AutoScrollMinSize = Size.Empty;
    }

    private void BuildTree(ParseSession session)
    {
        _statisticsMenuItem.Enabled = true;
        var document = session.Document;
        var rootText = Path.GetFileName(session.FilePath) ?? document.Root.Name;

        DataStructureTreeV.BeginUpdate();
        TreeNode rootNode;
        try
        {
            DataStructureTreeV.Nodes.Clear();
            rootNode = new TreeNode(rootText)
            {
                Tag = document.Root,
                ImageIndex = TreeIcons.Root,
                SelectedImageIndex = TreeIcons.Checked,
                ToolTipText = $"{document.ParserDisplayName} \u00b7 {document.SourceLength:N0} bytes",
            };
            AddChildren(rootNode, document.Root);
            DataStructureTreeV.Nodes.Add(rootNode);
        }
        finally
        {
            DataStructureTreeV.EndUpdate();
        }

        DataStructureTreeV.SelectedNode = rootNode;
        DataStructureTreeV.Focus();
    }

    private static bool NeedsChildren(FormatNode node) => node.Children.Length > 0;

    private static TreeNode CreateLazyNode(FormatNode node)
    {
        var image = node.Kind == NodeKind.Container ? TreeIcons.FolderClosed : TreeIcons.File;
        var treeNode = new TreeNode(node.Name)
        {
            Tag = node,
            ImageIndex = image,
            SelectedImageIndex = TreeIcons.Checked,
            ToolTipText = $"{node.Kind} \u00b7 {FormatRange(node.Range)}",
        };

        if (NeedsChildren(node))
        {
            // Placeholder keeps the expand glyph without materialising children.
            treeNode.Nodes.Add(new TreeNode(string.Empty));
        }

        return treeNode;
    }

    private static void AddChildren(TreeNode parent, FormatNode node)
    {
        foreach (var child in node.Children)
        {
            parent.Nodes.Add(CreateLazyNode(child));
        }
    }

    private static bool HasRealChildren(TreeNode node) =>
        node.Nodes.Count > 0 && node.Nodes[0].Tag is not null;

    private void DataStructureTreeV_BeforeExpand(object? sender, TreeViewCancelEventArgs e)
    {
        if (e.Node?.Tag is not FormatNode node || HasRealChildren(e.Node))
        {
            return;
        }

        DataStructureTreeV.BeginUpdate();
        try
        {
            e.Node.Nodes.Clear();
            AddChildren(e.Node, node);
        }
        finally
        {
            DataStructureTreeV.EndUpdate();
        }

        if (node.Kind == NodeKind.Container && e.Node.Parent is not null)
        {
            // The root keeps its own artwork; the selection image stays the check icon.
            e.Node.ImageIndex = TreeIcons.FolderOpen;
        }
    }

    private void DataStructureTreeV_BeforeCollapse(object? sender, TreeViewCancelEventArgs e)
    {
        if (e.Node?.Tag is FormatNode node && node.Kind == NodeKind.Container && e.Node.Parent is not null)
        {
            e.Node.ImageIndex = TreeIcons.FolderClosed;
        }
    }

    /// <summary>
    /// Reveals <paramref name="target"/> in the tree: expands the path (materialising children on
    /// the way), selects it, and lets <c>AfterSelect</c> load its fields.
    /// </summary>
    private bool SelectNode(FormatNode target)
    {
        var session = _session;
        if (session is null)
        {
            return false;
        }

        var path = NodePath.FindChildIndices(session.Document.Root, target);
        var treeNode = DataStructureTreeV.Nodes.Count > 0 ? DataStructureTreeV.Nodes[0] : null;
        if (path is null || treeNode is null)
        {
            SetStatus("The node is no longer part of the current tree.");
            return false;
        }

        DataStructureTreeV.BeginUpdate();
        try
        {
            foreach (var childIndex in path)
            {
                treeNode = EnsureChildTreeNode(treeNode, childIndex);
                if (treeNode is null)
                {
                    SetStatus("The node could not be located in the tree view.");
                    return false;
                }
            }
        }
        finally
        {
            DataStructureTreeV.EndUpdate();
        }

        DataStructureTreeV.SelectedNode = treeNode;
        treeNode.EnsureVisible();
        DataStructureTreeV.Focus();
        return true;
    }

    /// <summary>
    /// Returns the tree node for one child index, expanding the parent so that all of its
    /// children are materialised.
    /// </summary>
    private static TreeNode? EnsureChildTreeNode(TreeNode parent, int childIndex)
    {
        if (parent.Tag is not FormatNode parentNode || parentNode.Children.Length == 0)
        {
            return null;
        }

        if (!HasRealChildren(parent))
        {
            // Expanding materialises every child of this node.
            parent.Expand();
        }

        return childIndex < parent.Nodes.Count ? parent.Nodes[childIndex] : null;
    }

    private async void DataStructureTreeV_AfterSelect(object? sender, TreeViewEventArgs e)
    {
        if (_closing || e.Node?.Tag is not FormatNode node || _session is not { } session)
        {
            return;
        }

        _selectedTreeNode = e.Node;
        var generation = ++_selectionGeneration;
        var hadNodeParse = _nodeCts is not null;
        CancelAndDispose(ref _nodeCts);
        if (hadNodeParse)
        {
            _loadingOverlay.Hide();
            _loadingOverlay.SetCancelHandler(null);
            _nodeOverlayGeneration = 0;
        }

        if (ReferenceEquals(node, session.Document.Root))
        {
            try
            {
                ShowRoot(session);
            }
            catch (Exception exception)
            {
                if (!_closing && generation == _selectionGeneration)
                {
                    ShowNodeError(node, exception);
                }
            }

            return;
        }

        if (_nodeCache.TryGetValue(node, out var cached))
        {
            try
            {
                ShowNodeResult(node, cached);
            }
            catch (Exception exception)
            {
                if (!_closing && generation == _selectionGeneration)
                {
                    ShowNodeError(node, exception);
                }
            }

            return;
        }

        var cts = new CancellationTokenSource();
        _nodeCts = cts;
        MainStage.Visible = false;

        var finished = false;
        var overlayShown = false;
        var progress = new Progress<ParseProgress>(value =>
        {
            if (finished || _closing || generation != _selectionGeneration)
            {
                return;
            }

            if (!overlayShown)
            {
                overlayShown = true;
                _nodeOverlayGeneration = generation;
                _loadingOverlay.SetCancelHandler(() => cts.Cancel());
                _loadingOverlay.Show("Loading...");
            }

            _loadingOverlay.UpdateProgress(value);
        });

        NodeParseResult result;
        try
        {
            result = await Task.Run(() => session.ParseNode(node, progress, cts.Token), cts.Token)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (ObjectDisposedException)
        {
            return;
        }
        catch (Exception exception)
        {
            if (!_closing && generation == _selectionGeneration)
            {
                ShowNodeError(node, exception);
            }

            return;
        }
        finally
        {
            finished = true;
            if (overlayShown && _nodeOverlayGeneration == generation)
            {
                _loadingOverlay.Hide();
                _loadingOverlay.SetCancelHandler(null);
                _nodeOverlayGeneration = 0;
            }

            if (ReferenceEquals(_nodeCts, cts))
            {
                _nodeCts = null;
            }

            cts.Dispose();
        }

        if (_closing
            || generation != _selectionGeneration
            || !ReferenceEquals(_session, session))
        {
            return;
        }

        try
        {
            CacheNode(node, result);
            ShowNodeResult(node, result);
        }
        catch (Exception exception)
        {
            if (!_closing && generation == _selectionGeneration)
            {
                ShowNodeError(node, exception);
            }
        }
    }

    private void CacheNode(FormatNode node, NodeParseResult result)
    {
        if (_nodeCache.ContainsKey(node))
        {
            _nodeCache[node] = result;
            return;
        }

        if (_nodeCache.Count >= AppSettings.NodeParseCacheSize && _cacheOrder.Count > 0)
        {
            var oldest = _cacheOrder.Dequeue();
            _nodeCache.Remove(oldest);
        }

        _nodeCache[node] = result;
        _cacheOrder.Enqueue(node);
    }

    private void ShowRoot(ParseSession session)
    {
        var document = session.Document;
        var fields = BuildRootFields(session);
        var rawRange = document.Root.Range;

        UTitle.Text = BuildFileCaption(session);
        Title.Text = document.Root.Name;
        Desc.Text = FormatRangeDesc(rawRange);
        VirtualFieldListView.ShowFields(ParseResultList, fields);
        LayoutStage(rawRange);
        RawDataInfo.Text = $"Raw data at offset {rawRange.Offset:N0}, length {rawRange.Length:N0}";
        RawDataTextBox.Bind(session.Source, rawRange.Offset, rawRange.Length);
        SetStatus(DescribeDiagnostics(document.Diagnostics, "No diagnostics."));
        MainStage.Visible = true;
        RefreshPanelScroll();
    }

    private void ShowNodeResult(FormatNode node, NodeParseResult result)
    {
        var session = _session;
        if (session is null)
        {
            return;
        }

        UTitle.Text = BuildFileCaption(session);
        Title.Text = result.Title;
        Desc.Text = FormatRangeDesc(result.Range);
        VirtualFieldListView.ShowFields(ParseResultList, result.Fields);
        LayoutStage(result.RawRange);
        RawDataInfo.Text = $"Raw data at offset {result.RawRange.Offset:N0}, length {result.RawRange.Length:N0}";
        RawDataTextBox.Bind(session.Source, result.RawRange.Offset, result.RawRange.Length);
        SetStatus(DescribeDiagnostics(result.Diagnostics, $"Node '{node.Name}' parsed with no diagnostics."));
        MainStage.Visible = true;
        RefreshPanelScroll();
    }

    private void ShowNodeError(FormatNode node, Exception exception)
    {
        var session = _session;
        if (session is null)
        {
            return;
        }

        var fields = ImmutableArray.Create(
            Field.Error($"Node parse failed: {exception.Message}", range: node.Range));
        UTitle.Text = BuildFileCaption(session);
        Title.Text = node.Name;
        Desc.Text = FormatRangeDesc(node.Range);
        VirtualFieldListView.ShowFields(ParseResultList, fields);
        LayoutStage(node.Range);
        RawDataInfo.Text = $"Raw data at offset {node.Range.Offset:N0}, length {node.Range.Length:N0}";
        RawDataTextBox.Bind(session.Source, node.Range.Offset, node.Range.Length);
        SetStatus("Node parse failed.");
        MainStage.Visible = true;
        RefreshPanelScroll();
    }

    private static ImmutableArray<Field> BuildRootFields(ParseSession session)
    {
        var document = session.Document;
        var builder = ImmutableArray.CreateBuilder<Field>(6);
        builder.Add(Field.Derived("File name", session.FilePath ?? document.FileName ?? "(unnamed)"));
        builder.Add(Field.Derived("File size", string.Create(
            CultureInfo.InvariantCulture, $"{document.SourceLength:N0} bytes (0x{document.SourceLength:X16})")));
        builder.Add(Field.Derived("Creation time", session.CreationTimeUtc is { } created
            ? created.ToString("yyyy-MM-dd HH:mm:ss.fffffff 'UTC'", CultureInfo.InvariantCulture)
            : "Unavailable"));
        builder.Add(Field.Derived("File attributes", session.FileAttributes is { } attributes
            ? string.Create(CultureInfo.InvariantCulture, $"{attributes} (0x{(uint)attributes:X8})")
            : "Unavailable"));
        builder.Add(Field.Derived("Detected format", $"{document.ParserDisplayName} ({document.ParserId})"));
        builder.Add(Field.Derived("Probe confidence", document.Probe.Confidence.ToString()));
        return builder.MoveToImmutable();
    }

    private static string BuildFileCaption(ParseSession session)
    {
        var document = session.Document;
        var name = Path.GetFileName(session.FilePath) ?? document.FileName ?? "(unnamed)";
        return $"{document.ParserDisplayName} : {name} ({document.SourceLength:N0} Bytes)";
    }

    private void LayoutStage(SourceRange rawRange)
    {
        var width = Math.Max(0, MainStage.ClientSize.Width - 6);

        RawDataInfo.Left = 3;
        RawDataInfo.Width = width;
        RawDataInfo.Top = ParseResultList.Top + ParseResultList.Height + 4;

        RawDataTextBox.Left = 3;
        RawDataTextBox.Width = width;
        RawDataTextBox.Top = RawDataInfo.Top + RawDataInfo.Height + 4;

        var lineCount = HexDumpFormatter.GetLineCount(rawRange.Length);
        var wanted = 110L;
        if (lineCount <= (long.MaxValue - 100) / 20)
        {
            wanted = (lineCount * 20) + 100;
        }

        RawDataTextBox.Height = (int)Math.Clamp(wanted, 110L, 1100L);

        StatusLabel.Left = 3;
        StatusLabel.Width = width;
        StatusLabel.Top = RawDataTextBox.Top + RawDataTextBox.Height + 4;
    }

    private void RefreshPanelScroll()
    {
        var maxBottom = 0;
        foreach (Control control in MainStage.Controls)
        {
            var bottom = control.Location.Y + control.Height;
            if (bottom > maxBottom)
            {
                maxBottom = bottom;
            }
        }

        MainStage.AutoScrollMinSize = new Size(0, maxBottom);
    }

    private void SetStatus(string text)
    {
        StatusLabel.Text = text;
    }

    private static string DescribeDiagnostics(ImmutableArray<Diagnostic> diagnostics, string emptyText)
    {
        if (diagnostics.IsEmpty)
        {
            return emptyText;
        }

        var errors = diagnostics.Count(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var warnings = diagnostics.Count(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning);
        return $"{diagnostics.Length:N0} diagnostic(s): {errors:N0} error(s), {warnings:N0} warning(s).";
    }

    private static string FormatRange(SourceRange range)
    {
        var offset = range.Offset > uint.MaxValue
            ? range.Offset.ToString("X16", CultureInfo.InvariantCulture)
            : range.Offset.ToString("X8", CultureInfo.InvariantCulture);
        var length = range.Length > uint.MaxValue
            ? range.Length.ToString("X16", CultureInfo.InvariantCulture)
            : range.Length.ToString("X8", CultureInfo.InvariantCulture);
        return $"0x{offset}+0x{length}";
    }

    private static string FormatRangeDesc(SourceRange range) =>
        $"<{FormatRange(range)}>";

    private async void Expand_All_Click(object? sender, EventArgs e)
    {
        if (_session is null || DataStructureTreeV.Nodes.Count == 0)
        {
            return;
        }

        CancelAndDispose(ref _expandCts);
        var cts = new CancellationTokenSource();
        _expandCts = cts;

        var stack = new Stack<TreeNode>();
        stack.Push(DataStructureTreeV.Nodes[0]);
        var visited = new HashSet<TreeNode>();
        var expanded = 0;

        try
        {
            while (stack.Count > 0)
            {
                cts.Token.ThrowIfCancellationRequested();
                var node = stack.Pop();
                if (!visited.Add(node))
                {
                    continue;
                }

                if (!node.IsExpanded)
                {
                    node.Expand();
                }

                expanded++;
                foreach (TreeNode child in node.Nodes)
                {
                    stack.Push(child);
                }

                if (expanded % AppSettings.TreeExpandBatch == 0)
                {
                    SetStatus($"Expanding... {expanded:N0} node(s).");
                    await Task.Delay(1, cts.Token).ConfigureAwait(true);
                }
            }

            SetStatus($"Expanded {expanded:N0} node(s).");
        }
        catch (OperationCanceledException)
        {
            SetStatus("Expand cancelled.");
        }
        finally
        {
            if (ReferenceEquals(_expandCts, cts))
            {
                _expandCts = null;
            }

            cts.Dispose();
        }
    }

    private void Collapse_All_Click(object? sender, EventArgs e)
    {
        CancelAndDispose(ref _expandCts);
        if (DataStructureTreeV.Nodes.Count == 0)
        {
            return;
        }

        DataStructureTreeV.BeginUpdate();
        try
        {
            DataStructureTreeV.CollapseAll();
        }
        finally
        {
            DataStructureTreeV.EndUpdate();
        }

        SetStatus(string.Empty);
    }

    private async void ExportRaw_Click(object? sender, EventArgs e)
    {
        if (_session is not { } session || _selectedTreeNode?.Tag is not FormatNode node)
        {
            MessageBox.Show(this, "Please select a node to export.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = $"Export raw data for '{node.Name}'",
            FileName = $"{ReportText.SanitizeFileName(node.Name)}.bin",
            Filter = "Binary Files (*.bin)|*.bin|All Files (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        await ExportRawAsync(session, node, dialog.FileName);
    }

    private async Task ExportRawAsync(ParseSession session, FormatNode node, string path)
    {
        var cts = new CancellationTokenSource();
        CancelAndDispose(ref _exportCts);
        _exportCts = cts;
        _nodeOverlayGeneration = 0;
        _loadingOverlay.SetCancelHandler(() => cts.Cancel());
        _loadingOverlay.Show("Exporting raw data...");
        var progress = new Progress<ParseProgress>(_loadingOverlay.UpdateProgress);

        try
        {
            await Task.Run(
                () => CopyRange(session.Source, node.Range, path, progress, cts.Token),
                cts.Token).ConfigureAwait(true);

            SetStatus($"Exported {node.Range.Length:N0} byte(s) to {path}.");
            if (!_closing)
            {
                MessageBox.Show(
                    this,
                    $"Exported {node.Range.Length:N0} bytes to:\n{path}",
                    "Success",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
        catch (OperationCanceledException)
        {
            TryDelete(path);
            SetStatus("Export cancelled.");
        }
        catch (Exception exception)
        {
            TryDelete(path);
            SetStatus("Export failed.");
            if (!_closing)
            {
                MessageBox.Show(this, $"Export failed: {exception.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        finally
        {
            if (ReferenceEquals(_exportCts, cts))
            {
                _loadingOverlay.Hide();
                _loadingOverlay.SetCancelHandler(null);
                _exportCts = null;
            }

            cts.Dispose();
        }
    }

    private static void CopyRange(
        IRandomAccessSource source,
        SourceRange range,
        string path,
        IProgress<ParseProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var target = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            AppSettings.ExportBufferSize,
            FileOptions.SequentialScan);

        _ = RawRangeWriter.Write(target, source, range, progress, cancellationToken);
    }

    private async void ExportAnalysis_Click(object? sender, EventArgs e)
    {
        if (_session is not { } session || _selectedTreeNode?.Tag is not FormatNode node)
        {
            MessageBox.Show(this, "Please select a node to export.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (node.Range.Length >= AppSettings.LargeExportWarningBytes)
        {
            var answer = MessageBox.Show(
                this,
                $"Export analysis of {node.Range.Length:N0} bytes may take a long time.\nContinue?",
                "Tips",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes)
            {
                return;
            }
        }

        using var dialog = new SaveFileDialog
        {
            Title = $"Export analysis for '{node.Name}'",
            FileName = $"{ReportText.SanitizeFileName(node.Name)}.analysis.txt",
            Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var nodePath = BuildNodePath(_selectedTreeNode);
        await ExportAnalysisAsync(session, node, nodePath, dialog.FileName);
    }

    private async Task ExportAnalysisAsync(ParseSession session, FormatNode node, string nodePath, string path)
    {
        var cts = new CancellationTokenSource();
        CancelAndDispose(ref _exportCts);
        _exportCts = cts;
        _nodeOverlayGeneration = 0;
        _loadingOverlay.SetCancelHandler(() => cts.Cancel());
        _loadingOverlay.Show("Preparing analysis...");
        var progress = new Progress<ParseProgress>(_loadingOverlay.UpdateProgress);

        try
        {
            var result = _nodeCache.TryGetValue(node, out var cached)
                ? cached
                : ReferenceEquals(node, session.Document.Root)
                    ? new NodeParseResult(
                        session.Document.Root.Name,
                        node.Range,
                        node.Range,
                        BuildRootFields(session))
                    : await Task.Run(() => session.ParseNode(node, progress, cts.Token), cts.Token).ConfigureAwait(true);

            _loadingOverlay.Show("Exporting analysis...");
            var request = new ForensicReportRequest
            {
                Source = session.Source,
                Document = session.Document,
                Node = node,
                Result = result,
                NodePath = nodePath,
            };

            var outcome = await Task.Run(
                () =>
                {
                    using var writer = new StreamWriter(path, false, TextEncodings.Utf8);
                    return ForensicReportWriter.Write(writer, request, progress, cts.Token);
                },
                cts.Token).ConfigureAwait(true);

            if (outcome.HexTruncated)
            {
                SetStatus($"Exported analysis (hex limited to the first and last 128 KiB) to {path}.");
                if (!_closing)
                {
                    MessageBox.Show(this,
                        $"The report was saved to:\n{path}\n\n"
                        + "The binary section was limited to the first and last 128 KiB. "
                        + "Use Export Raw to save the complete range separately.",
                        "Export complete",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }

                return;
            }

            SetStatus($"Exported analysis to {path}.");
            if (!_closing)
            {
                MessageBox.Show(
                    this,
                    $"Exported analysis of '{node.Name}' to:\n{path}",
                    "Success",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
        catch (OperationCanceledException)
        {
            TryDelete(path);
            SetStatus("Export cancelled.");
        }
        catch (Exception exception)
        {
            TryDelete(path);
            SetStatus("Export failed.");
            if (!_closing)
            {
                MessageBox.Show(this, $"Export failed: {exception.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        finally
        {
            if (ReferenceEquals(_exportCts, cts))
            {
                _loadingOverlay.Hide();
                _loadingOverlay.SetCancelHandler(null);
                _exportCts = null;
            }

            cts.Dispose();
        }
    }

    private static string BuildNodePath(TreeNode? node)
    {
        if (node is null)
        {
            return string.Empty;
        }

        var parts = new Stack<string>();
        for (var current = node; current is not null; current = current.Parent)
        {
            parts.Push(current.Text);
        }

        return string.Join(" / ", parts);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Leftover partial files are harmless; never mask the original failure.
        }
    }

    private void DropArea_DragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void DropArea_DragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            _ = OpenFileAsync(files[0]);
        }
    }
}
