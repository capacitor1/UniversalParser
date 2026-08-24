using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using UniversalParser.Src.GUI;
using UniversalParser.Src.Parser;

namespace UniversalParser
{
    public partial class Form1 : Form
    {
        private FileStream? _currentFileStream;
        private IParser? _currentParser;
        private readonly ReaderWriterLockSlim _fileLock = new ReaderWriterLockSlim();
        private CancellationTokenSource? _parseCts;
        private readonly LoadingOverlay? _loadingOverlay;

        public Form1()
        {
            InitializeComponent();
            MainStage.Visible = false;
            CheckForIllegalCrossThreadCalls = false;

            // Initialize loading overlay
            _loadingOverlay = new LoadingOverlay(this, LoadingPanel, LoadingProgressBar, LoadingInfo, LoadingCancel);

            // Create and attach context menu to TreeView
            var contextMenu = new ContextMenuStrip();
            var exportRawItem = new ToolStripMenuItem("Export Raw", null, ExportRaw_Click);
            var exportAnalysisItem = new ToolStripMenuItem("Export Analysis", null, ExportAnalysis_Click);
            contextMenu.Items.Add(exportRawItem);
            contextMenu.Items.Add(exportAnalysisItem);
            DataStructureTreeV.ContextMenuStrip = contextMenu;

            //
            VirtualListViewHelper.Initialize(ParseResultList);
            TipStage.Visible = true;
            TipStage.BringToFront();//show static page
            DropArea.AllowDrop = true;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            CloseCurrentFile();
            _fileLock?.Dispose();
            base.OnFormClosing(e);
        }

        private void Menu_Open_Click(object sender, EventArgs e)
        {
            using (var openDialog = new OpenFileDialog())
            {
                openDialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                openDialog.ValidateNames = true;
                openDialog.Filter = "All Files (*.*)|*.*";
                openDialog.Multiselect = false;

                if (openDialog.ShowDialog() == DialogResult.OK)
                {
                    OpenFile(openDialog.FileName);
                }
            }
        }

        private void Menu_Close_Click(object sender, EventArgs e)
        {
            CloseCurrentFile();
        }

        private void OpenFile(string path)
        {
            try
            {
                // Close previous file
                CloseCurrentFile();

                // Open new file and start parsing
                _fileLock.EnterWriteLock();
                try
                {
                    _currentFileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
                }
                finally
                {
                    _fileLock.ExitWriteLock();
                }

                StartParse();
                //UI
                TipStage.Visible = false;
                TipStage.SendToBack();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to open file: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                CloseCurrentFile();
            }
        }

        private void CloseCurrentFile()
        {
            _parseCts?.Cancel();
            _fileLock.EnterWriteLock();
            try
            {
                // Dispose parser (which also disposes its FileStream)
                try { _currentParser?.Dispose(); } catch { }
                _currentParser = null;

                // Clear FileStream reference
                _currentFileStream = null;
            }
            finally
            {
                _fileLock.ExitWriteLock();
            }
            DataStructureTreeV.Nodes.Clear();
            MainStage.Visible = false;
            TipStage.Visible = true;
            TipStage.BringToFront();//show static page
        }

        private async void StartParse()
        {
            _parseCts?.Cancel();
            _parseCts = new CancellationTokenSource();
            var ct = _parseCts.Token;

            DataStructureTreeV.Nodes.Clear();

            // Show loading overlay
            _loadingOverlay?.SetCancelHandler(() => _parseCts?.Cancel());
            _loadingOverlay?.Show();

            try
            {
                // Create parser with FileStream
                FileStream? fileStream;

                _fileLock.EnterReadLock();

                try
                {
                    fileStream = _currentFileStream;
                }
                finally
                {
                    _fileLock.ExitReadLock();
                }

                if (fileStream == null) return;

                _currentParser = ParserFactory.CreateParser(fileStream);

                var progress = new Progress<ParserProgress>(p =>
                {
                    _loadingOverlay?.UpdateProgress(p.Fraction, p.BytesRead, p.TotalBytes, p.BytesPerSecond);
                });

                // Parse without passing FileStream (parser owns it now)
                var root = await _currentParser.ParseAsync(progress, ct);

                //
                if (!ct.IsCancellationRequested)
                {
                    DataStructureTreeV.BeginUpdate();

                    try
                    {
                        DataStructureTreeV.Nodes.Clear();

                        var rootTreeNode = new TreeNode
                        {
                            Text = root.NodeName,
                            ImageIndex = 0,
                            SelectedImageIndex = 0,
                            Tag = root
                        };

                        PopulateTreeWithoutExpand(root, rootTreeNode);

                        DataStructureTreeV.Nodes.Add(rootTreeNode);
                    }
                    finally
                    {
                        DataStructureTreeV.EndUpdate();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // User cancelled
                DataStructureTreeV.Nodes.Clear();
                MessageBox.Show(this, "Parsing cancelled.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                DataStructureTreeV.Nodes.Clear();
                MessageBox.Show(this, $"Parsing error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _loadingOverlay?.Hide();
            }
        }

        #region GUI_NodeTreeView
        private readonly HashSet<TreeNode> _loadedNodes = [];

        // Populate tree WITHOUT expanding (to avoid freeze)
        private void PopulateTreeWithoutExpand(Node rootNode, TreeNode rootTreeNode)
        {
            rootTreeNode.Nodes.Clear();

            rootTreeNode.Text = rootNode.NodeName;

            // Root固定图标
            rootTreeNode.ImageIndex = 0;
            rootTreeNode.SelectedImageIndex = 0;

            rootTreeNode.Tag = rootNode;

            foreach (var child in rootNode.SubNodes)
            {
                rootTreeNode.Nodes.Add(CreateLazyNode(child));
            }

            _loadedNodes.Add(rootTreeNode);
        }
        private TreeNode CreateLazyNode(Node node)
        {
            bool hasChildren = node.SubNodes.Count > 0;

            var treeNode = new TreeNode(node.NodeName)
            {
                Tag = node,
                ImageIndex = hasChildren ? 1 : 3,
                SelectedImageIndex = hasChildren ? 1 : 3
            };

            if (hasChildren)
            {
                // 仅用于显示展开箭头
                treeNode.Nodes.Add(string.Empty);
            }

            return treeNode;
        }
        private void LoadChildren(TreeNode treeNode)
        {
            if (_loadedNodes.Contains(treeNode))
                return;

            if (treeNode.Tag is not Node node)
                return;

            treeNode.Nodes.Clear();

            foreach (var child in node.SubNodes)
            {
                treeNode.Nodes.Add(CreateLazyNode(child));
            }

            _loadedNodes.Add(treeNode);
        }

        private void DataStructureTreeV_BeforeExpand(object? sender, TreeViewCancelEventArgs e)
        {
            if (e.Node == null) return;
            LoadChildren(e.Node);
        }

        private void DataStructureTreeV_BeforeCollapse(object? sender, TreeViewCancelEventArgs e)
        {
            if (e.Node != null)
            {
                // Restore closed folder icon (only if not root)
                if (e.Node != DataStructureTreeV.Nodes[0] && e.Node.ImageIndex == 2)
                {
                    e.Node.ImageIndex = 1; // Closed folder
                }
            }
        }

        #endregion

        #region ContextMenu_Operations

        private void ExportRaw_Click(object? sender, EventArgs e)
        {
            // Only allow single-node selection
            if (DataStructureTreeV.SelectedNode == null)
            {
                MessageBox.Show(this, "Please select a box to export.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var selectedNode = DataStructureTreeV.SelectedNode;
            if (selectedNode.Tag is not Node node)
            {
                MessageBox.Show(this, "Invalid selection.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Show save dialog
            using (var saveDialog = new SaveFileDialog())
            {
                saveDialog.Title = $"Export raw data for '{node.NodeName}'";
                saveDialog.FileName = $"{node.NodeName}.bin";
                saveDialog.Filter = "Binary Files (*.bin)|*.bin|All Files (*.*)|*.*";

                if (saveDialog.ShowDialog() == DialogResult.OK)
                {
                    ExportRawData(node, saveDialog.FileName);
                }
            }
        }

        private void ExportRawData(Node node, string targetPath)
        {
            try
            {
                _fileLock.EnterReadLock();
                try
                {
                    if (_currentFileStream == null || !_currentFileStream.CanRead)
                        throw new InvalidOperationException("Source file is not available.");

                    // Open target file for writing (truncate if exists)
                    using (var targetStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        // Seek to source position
                        _currentFileStream.Seek((long)node.Position, SeekOrigin.Begin);

                        // Copy data
                        long remaining = (long)node.Length;
                        byte[] buffer = new byte[64 * 1024]; // 64KB buffer

                        while (remaining > 0)
                        {
                            int toRead = (int)Math.Min(buffer.Length, remaining);
                            int read = _currentFileStream.Read(buffer, 0, toRead);
                            if (read <= 0) break;

                            targetStream.Write(buffer, 0, read);
                            remaining -= read;
                        }
                    }

                    MessageBox.Show(this, $"Exported {node.Length} bytes to:\n{targetPath}", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                finally
                {
                    _fileLock.ExitReadLock();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Export failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ExportAnalysis_Click(object? sender, EventArgs e)
        {
            // Only allow single-node selection
            if (DataStructureTreeV.SelectedNode == null)
            {
                MessageBox.Show(this, "Please select a box to export.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var selectedNode = DataStructureTreeV.SelectedNode;
            if (selectedNode.Tag is not Node node)
            {
                MessageBox.Show(this, "Invalid selection.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Show save dialog
            using (var saveDialog = new SaveFileDialog())
            {
                saveDialog.Title = $"Export analysis for '{node.NodeName}'";
                saveDialog.FileName = $"{node.NodeName}.analysis.txt";
                saveDialog.Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*";

                if (saveDialog.ShowDialog() == DialogResult.OK)
                {
                    ParseResult result = _currentParser!.ParseNode(node);
                    if (result.Length >= 128 * 1024 * 1024)//128MB
                    {
                        DialogResult dr = MessageBox.Show(this, $"Export analysis of {result.Length:N0} bytes data may take a long time.\nContinue?", "Tips", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                        if (dr == DialogResult.No)
                        {
                            result.RawData?.Dispose();
                            return;
                        }
                    }
                    ExportAnalysisData(node, saveDialog.FileName, result);
                    result.RawData?.Dispose();
                }
            }
        }
        public void ExportAnalysisData(Node node, string targetPath, ParseResult result)
        {
            try
            {
                _fileLock.EnterReadLock();
                try
                {
                    if (_currentFileStream == null || !_currentFileStream.CanRead)
                        throw new InvalidOperationException("Source file is not available.");

                    // Open target file for writing (truncate if exists)
                    using (var targetStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        //write
                        StreamWriter sw = new(targetStream);
                        sw.WriteLine($"Analysis Of '{node.NodeName}' In File '{_currentFileStream.Name}'");
                        sw.WriteLine();
                        sw.WriteLine($"Overall:");
                        sw.WriteLine($"File Name : {Path.GetFileName(_currentFileStream.Name)}");
                        sw.WriteLine($"File Size : {_currentFileStream.Length:N0} Bytes (0x{_currentFileStream.Length:X16})");
                        sw.WriteLine();
                        sw.WriteLine($"Parser Info:");
                        sw.WriteLine($"Type : {_currentParser!.ContainerTypeName}");
                        sw.WriteLine();
                        sw.WriteLine($"MetaData:");
                        sw.WriteLine($"Title : {result.Title}");
                        sw.WriteLine($"Node Position : {result.Position} (0x{(result.Position > uint.MaxValue ? result.Position.ToString("X16") : result.Position.ToString("X8"))})");
                        sw.WriteLine($"Node Length : {result.Length} (0x{(result.Length > uint.MaxValue ? result.Length.ToString("X16") : result.Length.ToString("X8"))})");
                        sw.WriteLine();
                        sw.WriteLine($"Data:");
                        foreach (var (K, V) in result.DataLines)
                        {
                            sw.WriteLine($"\t{K}\t{V}");
                        }
                        sw.WriteLine();
                        sw.WriteLine($"Hex Dump:");
                        int visibleLines = HexDumpCore.GetLineCount((long)result.Length);
                        Span<char> buffer = stackalloc char[256];

                        OffsetStream copy = new(_currentFileStream, (long)result.Position, (long)result.Length);
                        for (int i = 0; i < visibleLines; i++)
                        {
                            HexDumpCore.RenderLine(
                                copy,
                                _currentFileStream.Length > uint.MaxValue,
                                (long)result.Position,
                                i,
                                buffer,
                                out int len);
                            sw.WriteLine(buffer[..len]);
                        }
                        sw.Dispose();
                    }

                    MessageBox.Show(this, $"Exported analysis of '{node.NodeName}' to:\n{targetPath}", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                finally
                {
                    _fileLock.ExitReadLock();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Export failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        #endregion

        private void Expand_All_Click(object sender, EventArgs e)
        {
            DataStructureTreeV.BeginUpdate();
            try
            {
                //TODO:性能问题
                DataStructureTreeV.ExpandAll();
            }
            finally
            {
                DataStructureTreeV.EndUpdate();
            }
        }

        private void Collapse_All_Click(object sender, EventArgs e)
        {
            if (DataStructureTreeV.Nodes.Count > 0)
            {
                DataStructureTreeV.BeginUpdate();
                try
                {
                    DataStructureTreeV.CollapseAll();
                }
                finally
                {
                    DataStructureTreeV.EndUpdate();
                }
            }
        }
        private ParseResult? ParseResult;
        private void DataStructureTreeV_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (e.Node?.Tag is not Node node)
                return;

            if (_currentParser is null)
                return;
            if (e.Node == DataStructureTreeV.Nodes[0])
            {
                MainStage.Visible = false;
                //TODO:Metadata
                return;
            }
            var T_S = DateTime.UtcNow;
            MainStage.Visible = false;
            ParseResult?.RawData?.Dispose();//1
            try
            {
                ParseResult =
                    _currentParser.ParseNode(node);

                ShowParseResult(ParseResult);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.ToString(),
                    "Parse Error");
            }
            MainStage.Visible = true;
            var T_E = DateTime.UtcNow;

            //
            PlaceHolder.Text = $"Page loaded in {(T_E - T_S).TotalMilliseconds:F2} ms. UI height is {PlaceHolder.Top} + {PlaceHolder.Height}.";

            RefreshPanelScroll();
        }
        private void ShowParseResult(ParseResult result)
        {
            MainStage.Visible = true;

            UTitle.Text = $"{_currentParser!.ContainerTypeName} : {Path.GetFileName(_currentParser.FileStream.Name)} ({_currentParser.FileStream.Length:N0} Bytes)";
            Title.Text = result.Title;
            Desc.Text = $"<0x{(result.Position > uint.MaxValue ? result.Position.ToString("X16") : result.Position.ToString("X8"))}+0x{(result.Length > uint.MaxValue ? result.Length.ToString("X16") : result.Length.ToString("X8"))}>";
            // 使用虚拟列表显示DataLines
            VirtualListViewHelper.ShowDataLines(ParseResultList, result.DataLines);
            //
            RawDataTextBox.Bind(result.RawData!, (long)result.Position, (long)result.Length, _currentFileStream!.Length);
            // 计算位置
            RawDataInfo.Top = ParseResultList.Top + ParseResultList.Height + 4;
            RawDataTextBox.Top = RawDataInfo.Top + RawDataInfo.Height + 4;
            ulong h = 20 * ((ulong)result.RawData!.Length / 16) + 100;
            RawDataTextBox.Height = (int)Math.Clamp(h, 110, 1100);
            PlaceHolder.Top = RawDataTextBox.Top + RawDataTextBox.Height + 4;
            //
            RawDataInfo.Text = $"Raw data at offset {result.Position}, length {result.Length}";
            //
        }
        private void RefreshPanelScroll()
        {
            int maxBottom = 0;
            foreach (Control ctrl in MainStage.Controls)
            {
                int ctrlBottom = ctrl.Location.Y + ctrl.Height;
                if (ctrlBottom > maxBottom)
                    maxBottom = ctrlBottom;
            }
            MainStage.AutoScrollMinSize = new Size(0, maxBottom);
        }

        private async void Form1_Load(object sender, EventArgs e)
        {
            await Task.Delay(100);
            if (File.Exists(Program.FileToLoad)) OpenFile(Program.FileToLoad);
        }

        private void M_Open_Click(object sender, EventArgs e)
        {
            using (var openDialog = new OpenFileDialog())
            {
                openDialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                openDialog.ValidateNames = true;
                openDialog.Filter = "All Files (*.*)|*.*";
                openDialog.Multiselect = false;

                if (openDialog.ShowDialog() == DialogResult.OK)
                {
                    OpenFile(openDialog.FileName);
                }
            }
        }

        private void DropArea_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data == null) return;
            // 检查拖拽的内容是否为文件
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy; // 允许拖放操作
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        }

        private void DropArea_DragDrop(object sender, DragEventArgs e)
        {
            if (e.Data == null) return;
            // 获取拖拽进来的文件路径数组
            string[]? files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
            if (files == null) return;

            if (files != null && files.Length > 0)
            {
                // 获取第一个文件的路径
                string filePath = files[0];

                OpenFile(filePath);
            }
        }
    }
}