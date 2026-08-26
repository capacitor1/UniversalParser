using UniversalParser.Src.GUI;

namespace UniversalParser
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            DataStructureTreeV = new BufferedTreeView();
            DataStructureTreeIcons = new ImageList(components);
            Menu = new MenuStrip();
            fileToolStripMenuItem = new ToolStripMenuItem();
            Menu_Open = new ToolStripMenuItem();
            Menu_Close = new ToolStripMenuItem();
            viewVToolStripMenuItem = new ToolStripMenuItem();
            Expand_All = new ToolStripMenuItem();
            Collapse_All = new ToolStripMenuItem();
            LoadingPanel = new Panel();
            LoadingTitle = new Label();
            LoadingProgressBar = new ProgressBar();
            LoadingInfo = new Label();
            LoadingCancel = new Button();
            MainStage = new Panel();
            PlaceHolder = new Label();
            RawDataTextBox = new HexViewer();
            RawDataInfo = new Label();
            ParseResultList = new BufferedListView();
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
            DataStructureTreeV.Size = new Size(300, 641);
            DataStructureTreeV.TabIndex = 0;
            DataStructureTreeV.BeforeCollapse += DataStructureTreeV_BeforeCollapse;
            DataStructureTreeV.BeforeExpand += DataStructureTreeV_BeforeExpand;
            DataStructureTreeV.AfterSelect += DataStructureTreeV_AfterSelect;
            // 
            // DataStructureTreeIcons
            // 
            DataStructureTreeIcons.ColorDepth = ColorDepth.Depth32Bit;
            DataStructureTreeIcons.ImageStream = (ImageListStreamer)resources.GetObject("DataStructureTreeIcons.ImageStream");
            DataStructureTreeIcons.TransparentColor = Color.Transparent;
            DataStructureTreeIcons.Images.SetKeyName(0, "Root48.png");
            DataStructureTreeIcons.Images.SetKeyName(1, "Folder48.png");
            DataStructureTreeIcons.Images.SetKeyName(2, "OpenedFolder48.png");
            DataStructureTreeIcons.Images.SetKeyName(3, "File48.png");
            DataStructureTreeIcons.Images.SetKeyName(4, "Checked48.png");
            // 
            // Menu
            // 
            Menu.Items.AddRange(new ToolStripItem[] { fileToolStripMenuItem, viewVToolStripMenuItem });
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
            Menu_Open.Size = new Size(126, 22);
            Menu_Open.Text = "Open(&O)";
            Menu_Open.Click += Menu_Open_Click;
            // 
            // Menu_Close
            // 
            Menu_Close.Name = "Menu_Close";
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
            Expand_All.Size = new Size(160, 22);
            Expand_All.Text = "Expand All(&E)";
            Expand_All.Click += Expand_All_Click;
            // 
            // Collapse_All
            // 
            Collapse_All.Name = "Collapse_All";
            Collapse_All.Size = new Size(160, 22);
            Collapse_All.Text = "Collapse All(&C)";
            Collapse_All.Click += Collapse_All_Click;
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
            MainStage.Controls.Add(PlaceHolder);
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
            // PlaceHolder
            // 
            PlaceHolder.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            PlaceHolder.Location = new Point(3, 403);
            PlaceHolder.Name = "PlaceHolder";
            PlaceHolder.Size = new Size(928, 23);
            PlaceHolder.TabIndex = 8;
            PlaceHolder.Text = ".";
            PlaceHolder.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // RawDataTextBox
            // 
            RawDataTextBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            RawDataTextBox.BackColor = SystemColors.Control;
            RawDataTextBox.Font = new Font("Fira Code", 8.999999F, FontStyle.Regular, GraphicsUnit.Point, 0);
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
            Desc.Font = new Font("Fira Code", 10.4999981F, FontStyle.Regular, GraphicsUnit.Point, 0);
            Desc.Location = new Point(3, 59);
            Desc.Name = "Desc";
            Desc.Size = new Size(928, 18);
            Desc.TabIndex = 2;
            Desc.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // Title
            // 
            Title.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Title.Font = new Font("Fira Code", 14.25F, FontStyle.Regular, GraphicsUnit.Point, 0);
            Title.Location = new Point(3, 31);
            Title.Name = "Title";
            Title.Size = new Size(928, 28);
            Title.TabIndex = 1;
            Title.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // UTitle
            // 
            UTitle.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            UTitle.Font = new Font("Fira Code", 8.999999F, FontStyle.Regular, GraphicsUnit.Point, 0);
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
            Lbl2.Font = new Font("Fira Code", 10.4999981F, FontStyle.Regular, GraphicsUnit.Point, 0);
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
            Lbl1.Font = new Font("Fira Code", 26.2499962F, FontStyle.Regular, GraphicsUnit.Point, 0);
            Lbl1.Location = new Point(3, 31);
            Lbl1.Name = "Lbl1";
            Lbl1.Size = new Size(928, 43);
            Lbl1.TabIndex = 0;
            Lbl1.Text = "Universal Parser";
            Lbl1.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // Form1
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
            Icon = (Icon)resources.GetObject("$this.Icon");
            MainMenuStrip = Menu;
            Name = "Form1";
            Text = "Universal Parser";
            Load += Form1_Load;
            Menu.ResumeLayout(false);
            Menu.PerformLayout();
            LoadingPanel.ResumeLayout(false);
            MainStage.ResumeLayout(false);
            TipStage.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private BufferedTreeView DataStructureTreeV;
        private ImageList DataStructureTreeIcons;
        private new MenuStrip Menu;
        private ToolStripMenuItem fileToolStripMenuItem;
        private ToolStripMenuItem Menu_Open;
        private ToolStripMenuItem Menu_Close;
        private Panel LoadingPanel;
        private Label LoadingTitle;
        private ProgressBar LoadingProgressBar;
        private Label LoadingInfo;
        private Button LoadingCancel;
        private ToolStripMenuItem viewVToolStripMenuItem;
        private ToolStripMenuItem Expand_All;
        private ToolStripMenuItem Collapse_All;

        internal sealed class BufferedTreeView : TreeView
        {
            public BufferedTreeView()
            {
                SetStyle(
                    ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.AllPaintingInWmPaint,
                    true);

                UpdateStyles();
            }
        }
        internal sealed class BufferedListView : ListView
        {
            public BufferedListView()
            {
                SetStyle(
                    ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.AllPaintingInWmPaint,
                    true);

                UpdateStyles();
            }
        }

        private Panel MainStage;
        private Label UTitle;
        private Label Title;
        private Label Desc;
        private BufferedListView ParseResultList;
        private ColumnHeader Key;
        private ColumnHeader Value;
        private Label RawDataInfo;
        private HexViewer RawDataTextBox;
        private Label PlaceHolder;
        private Panel TipStage;
        private Label Lbl1;
        private Button M_Open;
        private Label Lbl2;
        private GroupBox DropArea;
    }
}
