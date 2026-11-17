#nullable enable

using UniversalParser.App.Controls;

namespace UniversalParser.App;

partial class MainForm
{
    private System.ComponentModel.IContainer? components;

    private new MenuStrip Menu = null!;
    private ToolStripMenuItem fileToolStripMenuItem = null!;
    private ToolStripMenuItem Menu_Open = null!;
    private ToolStripMenuItem Menu_Close = null!;
    private ToolStripMenuItem viewVToolStripMenuItem = null!;
    private ToolStripMenuItem Expand_All = null!;
    private ToolStripMenuItem Collapse_All = null!;
    private ToolStripMenuItem settingsToolStripMenuItem = null!;

    private TreeView DataStructureTreeV = null!;
    private ImageList DataStructureTreeIcons = null!;

    private OverlayPanel LoadingPanel = null!;
    private Label LoadingTitle = null!;
    private ProgressBar LoadingProgressBar = null!;
    private Label LoadingInfo = null!;
    private Button LoadingCancel = null!;

    private Panel MainStage = null!;
    private Label UTitle = null!;
    private Label Title = null!;
    private Label Desc = null!;
    private ListView ParseResultList = null!;
    private ColumnHeader Key = null!;
    private ColumnHeader Value = null!;
    private Label RawDataInfo = null!;
    private HexViewer RawDataTextBox = null!;
    private Label StatusLabel = null!;

    private Panel TipStage = null!;
    private Label Lbl1 = null!;
    private Label Lbl2 = null!;
    private Button M_Open = null!;
    private GroupBox DropArea = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ParseResultList.ContextMenuStrip?.Dispose();
            RawDataTextBox.ContextMenuStrip?.Dispose();
            components?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        DataStructureTreeV = new TreeView();
        DataStructureTreeIcons = TreeIcons.Create();
        components.Add(DataStructureTreeIcons);
        Menu = new MenuStrip();
        fileToolStripMenuItem = new ToolStripMenuItem();
        Menu_Open = new ToolStripMenuItem();
        Menu_Close = new ToolStripMenuItem();
        viewVToolStripMenuItem = new ToolStripMenuItem();
        Expand_All = new ToolStripMenuItem();
        Collapse_All = new ToolStripMenuItem();
        settingsToolStripMenuItem = new ToolStripMenuItem();
        LoadingPanel = new OverlayPanel();
        LoadingTitle = new Label();
        LoadingProgressBar = new ProgressBar();
        LoadingInfo = new Label();
        LoadingCancel = new Button();
        MainStage = new Panel();
        StatusLabel = new Label();
        RawDataTextBox = new HexViewer();
        RawDataInfo = new Label();
        ParseResultList = new ListView();
        Key = new ColumnHeader();
        Value = new ColumnHeader();
        Desc = new Label();
        Title = new Label();
        UTitle = new Label();
        TipStage = new Panel();
        DropArea = new GroupBox();
        M_Open = new Button();
        Lbl2 = new Label();
        Lbl1 = new Label();
        Menu.SuspendLayout();
        LoadingPanel.SuspendLayout();
        MainStage.SuspendLayout();
        TipStage.SuspendLayout();
        SuspendLayout();
        // 
        // DataStructureTreeV
        // 
        DataStructureTreeV.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
        DataStructureTreeV.ImageIndex = 0;
        DataStructureTreeV.ImageList = DataStructureTreeIcons;
        DataStructureTreeV.Location = new Point(12, 28);
        DataStructureTreeV.Name = "DataStructureTreeV";
        DataStructureTreeV.SelectedImageIndex = 4;
        DataStructureTreeV.ShowNodeToolTips = true;
        DataStructureTreeV.Size = new Size(300, 641);
        DataStructureTreeV.TabIndex = 0;
        DataStructureTreeV.BeforeCollapse += DataStructureTreeV_BeforeCollapse;
        DataStructureTreeV.BeforeExpand += DataStructureTreeV_BeforeExpand;
        DataStructureTreeV.AfterSelect += DataStructureTreeV_AfterSelect;
        // 
        // Menu
        // 
        Menu.Items.AddRange(new ToolStripItem[] { fileToolStripMenuItem, viewVToolStripMenuItem, settingsToolStripMenuItem });
        Menu.Location = new Point(0, 0);
        Menu.Name = "Menu";
        Menu.Size = new Size(1264, 25);
        Menu.TabIndex = 1;
        Menu.Text = "menuStrip1";
        // 
        // fileToolStripMenuItem
        // 
        fileToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { Menu_Open, Menu_Close });
        fileToolStripMenuItem.Name = "fileToolStripMenuItem";
        fileToolStripMenuItem.Size = new Size(53, 21);
        fileToolStripMenuItem.Text = "File(&F)";
        // 
        // Menu_Open
        // 
        Menu_Open.Name = "Menu_Open";
        Menu_Open.ShortcutKeys = Keys.Control | Keys.O;
        Menu_Open.Size = new Size(126, 22);
        Menu_Open.Text = "Open(&O)";
        Menu_Open.Click += Menu_Open_Click;
        // 
        // Menu_Close
        // 
        Menu_Close.Name = "Menu_Close";
        Menu_Close.ShortcutKeys = Keys.Control | Keys.W;
        Menu_Close.Size = new Size(126, 22);
        Menu_Close.Text = "Close(&C)";
        Menu_Close.Click += Menu_Close_Click;
        // 
        // viewVToolStripMenuItem
        // 
        viewVToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { Expand_All, Collapse_All });
        viewVToolStripMenuItem.Name = "viewVToolStripMenuItem";
        viewVToolStripMenuItem.Size = new Size(63, 21);
        viewVToolStripMenuItem.Text = "View(&V)";
        // 
        // Expand_All
        // 
        Expand_All.Name = "Expand_All";
        Expand_All.ShortcutKeys = Keys.Control | Keys.Shift | Keys.E;
        Expand_All.Size = new Size(160, 22);
        Expand_All.Text = "Expand All(&E)";
        Expand_All.Click += Expand_All_Click;
        // 
        // Collapse_All
        // 
        Collapse_All.Name = "Collapse_All";
        Collapse_All.ShortcutKeys = Keys.Control | Keys.Shift | Keys.C;
        Collapse_All.Size = new Size(160, 22);
        Collapse_All.Text = "Collapse All(&C)";
        Collapse_All.Click += Collapse_All_Click;
        // 
        // settingsToolStripMenuItem
        // 
        settingsToolStripMenuItem.Name = "settingsToolStripMenuItem";
        settingsToolStripMenuItem.Size = new Size(74, 21);
        settingsToolStripMenuItem.Text = "Settings(&S)";
        // 
        // LoadingPanel
        // 
        LoadingPanel.BackColor = Color.FromArgb(230, 255, 255, 255);
        LoadingPanel.BorderStyle = BorderStyle.FixedSingle;
        LoadingPanel.Controls.Add(LoadingTitle);
        LoadingPanel.Controls.Add(LoadingProgressBar);
        LoadingPanel.Controls.Add(LoadingInfo);
        LoadingPanel.Controls.Add(LoadingCancel);
        LoadingPanel.Location = new Point(0, 139);
        LoadingPanel.Name = "LoadingPanel";
        LoadingPanel.Size = new Size(420, 120);
        LoadingPanel.TabIndex = 2;
        LoadingPanel.Visible = false;
        // 
        // LoadingTitle
        // 
        LoadingTitle.Dock = DockStyle.Top;
        LoadingTitle.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
        LoadingTitle.Location = new Point(0, 0);
        LoadingTitle.Name = "LoadingTitle";
        LoadingTitle.Size = new Size(418, 24);
        LoadingTitle.TabIndex = 0;
        LoadingTitle.Text = "Loading...";
        LoadingTitle.TextAlign = ContentAlignment.MiddleCenter;
        // 
        // LoadingProgressBar
        // 
        LoadingProgressBar.Location = new Point(20, 36);
        LoadingProgressBar.Maximum = 1000;
        LoadingProgressBar.Name = "LoadingProgressBar";
        LoadingProgressBar.Size = new Size(380, 20);
        LoadingProgressBar.Style = ProgressBarStyle.Continuous;
        LoadingProgressBar.TabIndex = 1;
        // 
        // LoadingInfo
        // 
        LoadingInfo.Location = new Point(20, 62);
        LoadingInfo.Name = "LoadingInfo";
        LoadingInfo.Size = new Size(380, 20);
        LoadingInfo.TabIndex = 2;
        LoadingInfo.Text = "0 / 0 @ 0 B/s";
        LoadingInfo.TextAlign = ContentAlignment.MiddleCenter;
        // 
        // LoadingCancel
        // 
        LoadingCancel.Location = new Point(170, 88);
        LoadingCancel.Name = "LoadingCancel";
        LoadingCancel.Size = new Size(80, 26);
        LoadingCancel.TabIndex = 3;
        LoadingCancel.Text = "Cancel";
        // 
        // MainStage
        // 
        MainStage.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        MainStage.AutoScroll = true;
        MainStage.Controls.Add(StatusLabel);
        MainStage.Controls.Add(RawDataTextBox);
        MainStage.Controls.Add(RawDataInfo);
        MainStage.Controls.Add(ParseResultList);
        MainStage.Controls.Add(Desc);
        MainStage.Controls.Add(Title);
        MainStage.Controls.Add(UTitle);
        MainStage.Location = new Point(318, 28);
        MainStage.Name = "MainStage";
        MainStage.Size = new Size(934, 641);
        MainStage.TabIndex = 4;
        // 
        // StatusLabel
        // 
        StatusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        StatusLabel.Location = new Point(3, 403);
        StatusLabel.Name = "StatusLabel";
        StatusLabel.Size = new Size(928, 23);
        StatusLabel.TabIndex = 8;
        StatusLabel.Text = string.Empty;
        StatusLabel.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // RawDataTextBox
        // 
        RawDataTextBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        RawDataTextBox.BackColor = SystemColors.Control;
        RawDataTextBox.Font = UiFonts.Small();
        RawDataTextBox.ForeColor = SystemColors.ControlText;
        RawDataTextBox.Location = new Point(3, 305);
        RawDataTextBox.Name = "RawDataTextBox";
        RawDataTextBox.Size = new Size(928, 95);
        RawDataTextBox.TabIndex = 7;
        // 
        // RawDataInfo
        // 
        RawDataInfo.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        RawDataInfo.Location = new Point(3, 282);
        RawDataInfo.Name = "RawDataInfo";
        RawDataInfo.Size = new Size(928, 20);
        RawDataInfo.TabIndex = 5;
        RawDataInfo.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // ParseResultList
        // 
        ParseResultList.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        ParseResultList.Columns.AddRange(new ColumnHeader[] { Key, Value });
        ParseResultList.FullRowSelect = true;
        ParseResultList.GridLines = true;
        ParseResultList.Location = new Point(3, 97);
        ParseResultList.Name = "ParseResultList";
        ParseResultList.Size = new Size(928, 182);
        ParseResultList.TabIndex = 4;
        ParseResultList.UseCompatibleStateImageBehavior = false;
        ParseResultList.View = View.Details;
        // 
        // Key
        // 
        Key.Text = "Key";
        Key.Width = 150;
        // 
        // Value
        // 
        Value.Text = "Value";
        Value.Width = 720;
        // 
        // Desc
        // 
        Desc.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        Desc.Font = UiFonts.Body();
        Desc.Location = new Point(3, 59);
        Desc.Name = "Desc";
        Desc.Size = new Size(928, 18);
        Desc.TabIndex = 2;
        Desc.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // Title
        // 
        Title.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        Title.Font = UiFonts.Title();
        Title.Location = new Point(3, 31);
        Title.Name = "Title";
        Title.Size = new Size(928, 28);
        Title.TabIndex = 1;
        Title.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // UTitle
        // 
        UTitle.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        UTitle.Font = UiFonts.Small();
        UTitle.Location = new Point(3, 13);
        UTitle.Name = "UTitle";
        UTitle.Size = new Size(928, 18);
        UTitle.TabIndex = 0;
        UTitle.TextAlign = ContentAlignment.MiddleCenter;
        // 
        // TipStage
        // 
        TipStage.AllowDrop = true;
        TipStage.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        TipStage.Controls.Add(DropArea);
        TipStage.Controls.Add(M_Open);
        TipStage.Controls.Add(Lbl2);
        TipStage.Controls.Add(Lbl1);
        TipStage.Location = new Point(318, 28);
        TipStage.Name = "TipStage";
        TipStage.Size = new Size(934, 641);
        TipStage.TabIndex = 3;
        // 
        // DropArea
        // 
        DropArea.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        DropArea.Location = new Point(3, 148);
        DropArea.Name = "DropArea";
        DropArea.Size = new Size(928, 490);
        DropArea.TabIndex = 3;
        DropArea.TabStop = false;
        DropArea.Text = "Drag and Drop";
        DropArea.DragDrop += DropArea_DragDrop;
        DropArea.DragEnter += DropArea_DragEnter;
        // 
        // M_Open
        // 
        M_Open.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        M_Open.Location = new Point(3, 111);
        M_Open.Name = "M_Open";
        M_Open.Size = new Size(928, 31);
        M_Open.TabIndex = 2;
        M_Open.Text = "Open";
        M_Open.UseVisualStyleBackColor = true;
        M_Open.Click += M_Open_Click;
        // 
        // Lbl2
        // 
        Lbl2.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        Lbl2.Font = UiFonts.Body();
        Lbl2.Location = new Point(3, 77);
        Lbl2.Name = "Lbl2";
        Lbl2.Size = new Size(928, 31);
        Lbl2.TabIndex = 1;
        Lbl2.Text = "Open a file using the button below or drop a file onto the area below.";
        Lbl2.TextAlign = ContentAlignment.MiddleCenter;
        // 
        // Lbl1
        // 
        Lbl1.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        Lbl1.Font = UiFonts.Heading();
        Lbl1.Location = new Point(3, 31);
        Lbl1.Name = "Lbl1";
        Lbl1.Size = new Size(928, 43);
        Lbl1.TabIndex = 0;
        Lbl1.Text = "Universal Parser";
        Lbl1.TextAlign = ContentAlignment.MiddleCenter;
        // 
        // MainForm
        // 
        AllowDrop = true;
        AutoScaleDimensions = new SizeF(7F, 17F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1264, 681);
        Controls.Add(TipStage);
        Controls.Add(DataStructureTreeV);
        Controls.Add(Menu);
        Controls.Add(LoadingPanel);
        Controls.Add(MainStage);
        Icon = LoadAppIcon();
        MainMenuStrip = Menu;
        Name = "MainForm";
        Text = "Universal Parser";
        Menu.ResumeLayout(false);
        Menu.PerformLayout();
        LoadingPanel.ResumeLayout(false);
        MainStage.ResumeLayout(false);
        TipStage.ResumeLayout(false);
        ResumeLayout(false);
        PerformLayout();
    }

    private static Icon? LoadAppIcon()
    {
        using var stream = typeof(MainForm).Assembly.GetManifestResourceStream("app.ico");
        return stream is null ? null : new Icon(stream);
    }
}
