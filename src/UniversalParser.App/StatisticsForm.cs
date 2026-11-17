using System.ComponentModel;
using System.Globalization;
using System.Text;
using UniversalParser.App.Controls;
using UniversalParser.Core.Formatting;
using UniversalParser.Core.Model;
using UniversalParser.Core.Statistics;

namespace UniversalParser.App;

/// <summary>Asynchronous tree statistics, with disjoint byte shares and exact ranking lists.</summary>
internal sealed class StatisticsForm : Form
{
    private static readonly Color UniqueNameColor = Color.FromArgb(156, 163, 175);

    private readonly IContainer _components = new Container();
    private readonly ParseDocument _document;
    private readonly Label _summary;
    private readonly Label _status;
    private readonly ListView _largest;
    private readonly FlowLayoutPanel _largestFilter;
    private readonly ListView _names;
    private readonly StatisticsPieChart _bytesChart;
    private readonly StatisticsPieChart _kindsChart;
    private CancellationTokenSource? _cts;
    private StatisticsSnapshot? _snapshot;
    private bool _updatingFilter;

    private StatisticsForm(ParseDocument document)
    {
        _document = document;
        Text = "Statistics";
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        Font = UiFonts.Small();
        ClientSize = new Size(1040, 760);
        MinimumSize = new Size(860, 740);
        BackColor = Color.White;
        Padding = new Padding(12);

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 72,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(6, 0, 6, 4),
        };
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        header.Controls.Add(new Label
        {
            Text = document.FileName ?? document.ParserDisplayName,
            Font = UiFonts.Title(),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
        });
        _summary = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(107, 114, 128),
            Text = "Computing...",
            AutoEllipsis = true,
        };
        header.Controls.Add(_summary);

        _bytesChart = new StatisticsPieChart("File composition", "Node name", "Bytes", "bytes");
        _kindsChart = new StatisticsPieChart("Node types", "Node type", "Count", "nodes");
        var overview = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        overview.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        overview.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        overview.Controls.Add(_bytesChart, 0, 0);
        overview.Controls.Add(_kindsChart, 0, 1);

        _largest = CreateList(["Node", "Node type", "Offset", "Size (bytes)", "File share"], [32, 14, 18, 20, 16], goToNode: true);
        _largest.Columns[1].TextAlign = HorizontalAlignment.Left;
        _largestFilter = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            Padding = new Padding(4, 0, 4, 4),
        };
        var largestPage = new Panel { Dock = DockStyle.Fill };
        largestPage.Controls.Add(_largest);
        largestPage.Controls.Add(_largestFilter);
        _names = CreateList(["Name", "Count", "Node share", "Summed size", "Largest"], [32, 12, 14, 23, 19], goToNode: false);
        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(CreatePage("Overview", overview));
        tabs.TabPages.Add(CreatePage("Largest nodes", largestPage));
        tabs.TabPages.Add(CreatePage("Repeated names", _names));

        var footer = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 40, ColumnCount = 2, RowCount = 1 };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        _status = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(107, 114, 128),
            AutoEllipsis = true,
        };
        var close = new Button { Text = "Close", Dock = DockStyle.Fill, DialogResult = DialogResult.OK, Margin = new Padding(4, 6, 0, 0) };
        footer.Controls.Add(_status, 0, 0);
        footer.Controls.Add(close, 1, 0);
        CancelButton = close;
        tabs.SelectedIndexChanged += (_, _) => _status.Text = tabs.SelectedIndex switch
        {
            1 => "Sizes include child ranges. Select a row and press Ctrl+C to copy.",
            _ => string.Empty,
        };

        Controls.Add(tabs);
        Controls.Add(header);
        Controls.Add(footer);
    }

    /// <summary>The node the user asked to reveal in the main tree, or null when the window was just closed.</summary>
    public FormatNode? NavigationTarget { get; private set; }

    public static FormatNode? ShowFor(Form owner, ParseDocument document)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(document);
        using var form = new StatisticsForm(document) { Icon = owner.Icon };
        return form.ShowDialog(owner) == DialogResult.OK ? form.NavigationTarget : null;
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        using var cts = new CancellationTokenSource();
        _cts = cts;
        try
        {
            var topSlices = AppSettings.TopChartSlices;
            var snapshot = await Task.Run(
                () => StatisticsSnapshot.Compute(_document.Root, topSlices, cts.Token),
                cts.Token).ConfigureAwait(true);
            if (!IsDisposed && !cts.IsCancellationRequested)
            {
                Populate(snapshot);
            }
        }
        catch (OperationCanceledException)
        {
            // Closing the window cancels the tree walk without retaining its snapshot.
        }
        catch (Exception exception)
        {
            if (!IsDisposed)
            {
                _summary.Text = "Statistics could not be computed.";
                _status.Text = exception.Message;
            }
        }
        finally
        {
            if (ReferenceEquals(_cts, cts))
            {
                _cts = null;
            }
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _cts?.Cancel();
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cts?.Cancel();
            _components.Dispose();
        }

        base.Dispose(disposing);
    }

    private ListView CreateList(string[] headings, int[] widths, bool goToNode)
    {
        var list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = true,
            ShowItemToolTips = true,
            BorderStyle = BorderStyle.None,
        };
        for (var index = 0; index < headings.Length; index++)
        {
            list.Columns.Add(headings[index], 100, index == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right);
        }

        list.Resize += (_, _) =>
        {
            var available = Math.Max(1, list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4);
            for (var index = 0; index < widths.Length; index++)
            {
                list.Columns[index].Width = Math.Max(1, available * widths[index] / 100);
            }
        };
        var menu = new ContextMenuStrip(_components);
        if (goToNode)
        {
            var goTo = new ToolStripMenuItem("Go to node", null, (_, _) => NavigateFromSelection());
            menu.Items.Add(goTo);
            menu.Opening += (_, _) => goTo.Enabled = CanNavigateFromSelection();
            menu.Items.Add(new ToolStripSeparator());
        }

        menu.Items.Add("Copy Row", null, (_, _) => CopyRows(list));
        list.ContextMenuStrip = menu;
        list.KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.C)
            {
                CopyRows(list);
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Enter && goToNode)
            {
                NavigateFromSelection();
                e.SuppressKeyPress = true;
            }
        };
        if (goToNode)
        {
            list.DoubleClick += (_, _) => NavigateFromSelection();
        }

        return list;
    }

    private static TabPage CreatePage(string title, Control content)
    {
        var page = new TabPage(title) { Padding = new Padding(6), BackColor = Color.White };
        page.Controls.Add(content);
        return page;
    }

    private void Populate(StatisticsSnapshot snapshot)
    {
        _snapshot = snapshot;
        _summary.Text = string.Create(CultureInfo.InvariantCulture,
            $"{snapshot.NodeCount:N0} nodes    |    Max depth: {snapshot.MaxDepth:N0}    |    {_document.SourceLength:N0} bytes");
        _bytesChart.SetData(snapshot.ByteShares);
        _kindsChart.SetData(snapshot.KindShares);

        BuildLargestFilter(snapshot);
        RenderLargest();

        _names.BeginUpdate();
        try
        {
            foreach (var row in snapshot.Names)
            {
                var item = new ListViewItem(
                [
                    row.Name,
                    Number(row.Count),
                    Share(row.Count, snapshot.NodeCount),
                    Number(row.TotalSize),
                    Number(row.MaxSize),
                ])
                {
                    ToolTipText = $"{row.Name}\nExclusive bytes: {Number(row.OwnedBytes)}",
                };
                if (row.IsUnique)
                {
                    item.ForeColor = UniqueNameColor;
                }

                _names.Items.Add(item);
            }
        }
        finally
        {
            _names.EndUpdate();
        }
    }

    private void BuildLargestFilter(StatisticsSnapshot snapshot)
    {
        _updatingFilter = true;
        _largestFilter.SuspendLayout();
        try
        {
            foreach (var control in _largestFilter.Controls.Cast<Control>().ToArray())
            {
                control.Dispose();
            }

            _largestFilter.Controls.Clear();
            _largestFilter.Controls.Add(new Label
            {
                Text = "Node types:",
                AutoSize = true,
                ForeColor = Color.FromArgb(107, 114, 128),
                Margin = new Padding(0, 6, 8, 0),
            });

            foreach (var rank in snapshot.LargestByKind)
            {
                var check = new CheckBox
                {
                    Text = NodeKindNames.Singular(rank.Kind),
                    Tag = rank.Kind,
                    Checked = true,
                    AutoSize = true,
                    Margin = new Padding(0, 4, 12, 0),
                };
                check.CheckedChanged += LargestTypeCheckChanged;
                _largestFilter.Controls.Add(check);
            }
        }
        finally
        {
            _updatingFilter = false;
            _largestFilter.ResumeLayout();
        }
    }

    private void LargestTypeCheckChanged(object? sender, EventArgs e)
    {
        if (!_updatingFilter)
        {
            RenderLargest();
        }
    }

    private void RenderLargest()
    {
        if (_snapshot is null)
        {
            return;
        }

        var selectedKinds = _largestFilter.Controls
            .OfType<CheckBox>()
            .Where(static check => check.Checked)
            .Select(static check => (NodeKind)check.Tag!)
            .ToHashSet();

        var nodes = selectedKinds.Count == 0
            ? Array.Empty<FormatNode>()
            : _snapshot.LargestByKind
                .Where(rank => selectedKinds.Contains(rank.Kind))
                .SelectMany(static rank => rank.Nodes)
                .OrderByDescending(static node => node.Range.Length)
                .ThenBy(static node => node.Range.Offset)
                .Take(StatisticsSnapshot.LargestNodeCount)
                .ToArray();

        _largest.BeginUpdate();
        try
        {
            _largest.Items.Clear();
            foreach (var node in nodes)
            {
                var item = new ListViewItem(
                [
                    node.Name,
                    NodeKindNames.Singular(node.Kind),
                    $"0x{node.Range.Offset:X}",
                    Number(node.Range.Length),
                    Share(node.Range.Length, _document.SourceLength),
                ])
                {
                    Tag = node,
                    ToolTipText =
                        $"{node.Name}\nType: {NodeKindNames.Singular(node.Kind)}\n" +
                        $"Offset: 0x{node.Range.Offset:X}\n{Number(node.Range.Length)} bytes",
                };
                _largest.Items.Add(item);
            }
        }
        finally
        {
            _largest.EndUpdate();
        }
    }

    private bool CanNavigateFromSelection() =>
        _largest.SelectedItems.Count == 1 && _largest.SelectedItems[0].Tag is FormatNode;

    private void NavigateFromSelection()
    {
        if (!CanNavigateFromSelection())
        {
            return;
        }

        NavigationTarget = (FormatNode)_largest.SelectedItems[0].Tag!;
        DialogResult = DialogResult.OK;
        Close();
    }

    private static string Number(decimal value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Share(long value, long total) => ValueFormat.Percentage(value, total);

    private static void CopyRows(ListView list)
    {
        var text = new StringBuilder();
        foreach (ListViewItem item in list.SelectedItems)
        {
            text.AppendLine(string.Join('\t', item.SubItems.Cast<ListViewItem.ListViewSubItem>().Select(static cell => cell.Text)));
        }

        if (text.Length == 0)
        {
            return;
        }

        try
        {
            Clipboard.SetText(text.ToString());
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Another process can briefly hold the clipboard.
        }
    }
}
