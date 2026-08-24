using System;
using System.Collections.Generic;
using System.Windows.Forms;
using UniversalParser.Src.Parser;

namespace UniversalParser.Src.GUI
{
    internal static class VirtualListViewHelper
    {
        public static int DataLineMaxShowCount = 1000;

        public static void Initialize(ListView listView)
        {
            listView.VirtualMode = true;
            //listView.View = View.Details;
            //listView.FullRowSelect = true;
            //listView.GridLines = true;

            listView.RetrieveVirtualItem -= RetrieveVirtualItem;
            listView.RetrieveVirtualItem += RetrieveVirtualItem;
        }

        public static void ShowDataLines(
            ListView listView,
            IReadOnlyList<(string K, string V)> lines)
        {
            lines ??= Array.Empty<(string K, string V)>();

            listView.BeginUpdate();

            try
            {
                listView.Tag = lines;

                listView.VirtualListSize = lines.Count;

                AdjustHeight(listView, lines.Count);

                listView.Invalidate();
                listView.Update();
            }
            finally
            {
                listView.EndUpdate();
            }
        }

        private static void RetrieveVirtualItem(
            object? sender,
            RetrieveVirtualItemEventArgs e)
        {
            if (sender is not ListView listView)
            {
                e.Item = new ListViewItem();
                return;
            }

            if (listView.Tag is not IReadOnlyList<(string K, string V)> lines)
            {
                e.Item = new ListViewItem();
                return;
            }

            if ((uint)e.ItemIndex >= (uint)lines.Count)
            {
                e.Item = new ListViewItem();
                return;
            }

            var line = lines[e.ItemIndex];

            var item = new ListViewItem(
                line.K ?? string.Empty);

            item.SubItems.Add(
                line.V ?? string.Empty);

            e.Item = item;
        }

        private static void AdjustHeight(
            ListView listView,
            int itemCount)
        {
            int rowHeight = GetRowHeight(listView);

            int visibleRows =
                Math.Min(
                    itemCount,
                    DataLineMaxShowCount);

            if (visibleRows <= 0)
                visibleRows = 1;

            int headerHeight = 24;

            int tmph =
                headerHeight +
                (visibleRows * rowHeight) +
                4;
            listView.Height = Math.Min(Settings.DataLineMaxShowCount * rowHeight, tmph);
        }

        private static int GetRowHeight(
            ListView listView)
        {
            return Math.Max(
                20,
                listView.Font.Height + 5);
        }
    }
}