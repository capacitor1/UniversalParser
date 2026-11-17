using System.Collections.Immutable;
using UniversalParser.Core.Model;

namespace UniversalParser.App.Controls;

/// <summary>
/// Virtual-mode field list. Only the visible rows are materialised, so a node with thousands of
/// fields never allocates thousands of ListViewItem objects. Owner-draw colour-codes field kinds
/// while keeping the legacy two-column layout.
/// </summary>
internal static class VirtualFieldListView
{
    public static void Initialize(ListView listView)
    {
        ArgumentNullException.ThrowIfNull(listView);
        listView.VirtualMode = true;
        listView.OwnerDraw = true;
        listView.RetrieveVirtualItem += RetrieveVirtualItem;
        listView.DrawColumnHeader += DrawColumnHeader;
        listView.DrawItem += DrawItem;
        listView.DrawSubItem += DrawSubItem;
        listView.ContextMenuStrip = CreateContextMenu(listView);
    }

    /// <summary>Copy-only context menu; the field list deliberately has no search or filter.</summary>
    public static ContextMenuStrip CreateContextMenu(ListView listView)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Copy Key", null, (_, _) => Copy(listView, 0));
        menu.Items.Add("Copy Value", null, (_, _) => Copy(listView, 1));
        menu.Items.Add("Copy Row", null, (_, _) => Copy(listView, 2));
        menu.Opening += (_, e) => e.Cancel = GetSelectedField(listView) is null;
        return menu;
    }

    public static void ShowFields(ListView listView, ImmutableArray<Field> fields)
    {
        ArgumentNullException.ThrowIfNull(listView);
        if (fields.IsDefault)
        {
            fields = ImmutableArray<Field>.Empty;
        }

        listView.BeginUpdate();
        try
        {
            listView.Tag = fields;
            listView.VirtualListSize = fields.Length;
            AdjustHeight(listView, fields.Length);
            listView.Invalidate();
        }
        finally
        {
            listView.EndUpdate();
        }
    }

    public static void Clear(ListView listView)
    {
        listView.Tag = null;
        listView.VirtualListSize = 0;
        listView.Invalidate();
    }

    public static int GetRowHeight(ListView listView) =>
        Math.Max(20, listView.Font.Height + 5);

    private static void RetrieveVirtualItem(object? sender, RetrieveVirtualItemEventArgs e)
    {
        if (sender is not ListView listView
            || listView.Tag is not ImmutableArray<Field> fields
            || (uint)e.ItemIndex >= (uint)fields.Length)
        {
            e.Item = new ListViewItem(string.Empty);
            return;
        }

        var field = fields[e.ItemIndex];
        var item = new ListViewItem(field.Key) { Tag = field };
        item.SubItems.Add(field.Value);
        e.Item = item;
    }

    private static void AdjustHeight(ListView listView, int itemCount)
    {
        var rowHeight = GetRowHeight(listView);
        var visibleRows = Math.Min(itemCount, AppSettings.DataLineMaxShowCount);
        if (visibleRows <= 0)
        {
            visibleRows = 1;
        }

        const int headerHeight = 24;
        var wanted = headerHeight + (visibleRows * rowHeight) + 4;
        var cap = headerHeight + (AppSettings.DataLineMaxShowCount * rowHeight) + 4;
        listView.Height = Math.Min(cap, wanted);
    }

    private static void DrawColumnHeader(object? sender, DrawListViewColumnHeaderEventArgs e) =>
        e.DrawDefault = true;

    private static void DrawItem(object? sender, DrawListViewItemEventArgs e)
    {
        // Details view paints per sub-item; keep the item-level event from double painting.
        e.DrawDefault = false;
    }

    private static void DrawSubItem(object? sender, DrawListViewSubItemEventArgs e)
    {
        if (sender is not ListView listView)
        {
            e.DrawDefault = true;
            return;
        }

        var field = e.Item?.Tag as Field;
        var selected = e.Item?.Selected == true;
        var background = selected ? SystemColors.Highlight : listView.BackColor;
        var foreground = selected ? SystemColors.HighlightText : ColorFor(field);

        using (var brush = new SolidBrush(background))
        {
            e.Graphics.FillRectangle(brush, e.Bounds);
        }

        var text = e.SubItem?.Text ?? string.Empty;
        var bounds = new Rectangle(e.Bounds.X + 2, e.Bounds.Y, Math.Max(0, e.Bounds.Width - 4), e.Bounds.Height);
        TextRenderer.DrawText(
            e.Graphics,
            text,
            listView.Font,
            bounds,
            foreground,
            TextFormatFlags.Left
            | TextFormatFlags.VerticalCenter
            | TextFormatFlags.EndEllipsis
            | TextFormatFlags.NoPrefix);
    }

    private static Color ColorFor(Field? field) => field?.Kind switch
    {
        FieldKind.Derived => Color.FromArgb(96, 96, 96),
        FieldKind.Note => Color.FromArgb(38, 79, 120),
        FieldKind.Warning => Color.FromArgb(176, 108, 0),
        FieldKind.Error => Color.FromArgb(168, 32, 32),
        _ => SystemColors.ControlText,
    };

    private static Field? GetSelectedField(ListView listView)
    {
        if (listView.Tag is not ImmutableArray<Field> fields || fields.IsEmpty)
        {
            return null;
        }

        var index = listView.FocusedItem?.Index ?? -1;
        if (index < 0 && listView.SelectedIndices.Count > 0)
        {
            index = listView.SelectedIndices[0];
        }

        return (uint)index < (uint)fields.Length ? fields[index] : null;
    }

    private static void Copy(ListView listView, int part)
    {
        var field = GetSelectedField(listView);
        if (field is null)
        {
            return;
        }

        var text = part switch
        {
            0 => field.Key,
            1 => field.Value,
            _ => $"{field.Key} = {field.Value}",
        };

        try
        {
            Clipboard.SetText(text);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Another process can hold the clipboard; copying is best effort.
        }
    }
}
