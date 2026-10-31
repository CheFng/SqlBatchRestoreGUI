using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Reflection;

[assembly: AssemblyTitle("SQL Server Batch Restore")]
[assembly: AssemblyDescription("Batch restore SQL Server .bak files")]
[assembly: AssemblyCompany("OpenAI")]
[assembly: AssemblyProduct("SQL Server Batch Restore")]
[assembly: AssemblyVersion("1.0.7.0")]
[assembly: AssemblyFileVersion("1.0.7.0")]

namespace SqlBatchRestoreGUI
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    public class BackupItem
    {
        public bool Selected { get; set; }
        public string FileName { get; set; }
        public string FullPath { get; set; }
        public string DatabaseName { get; set; }
        public long FileSize { get; set; }
        public bool Exists { get; set; }
        public string Status { get; set; }
        public int Progress { get; set; }
        public int BackupPosition { get; set; }
        public DateTime? BackupFinishDate { get; set; }
        public long DataSize { get; set; }
        public long LogSize { get; set; }
        public bool? SpaceEnough { get; set; }
        public string TargetFreeText { get; set; }
        public string SpaceStatus { get; set; }
        public List<BackupFileEntry> BackupFiles { get; set; }
        public int GridRowIndex { get; set; }
    }

    public class BackupFileEntry
    {
        public string LogicalName { get; set; }
        public string PhysicalName { get; set; }
        public string Type { get; set; }
        public long Size { get; set; }
    }

    public class SpaceCheckResult
    {
        public bool? Enough { get; set; }
        public string FreeText { get; set; }
        public string StatusText { get; set; }
    }

    public class LogShrinkResult
    {
        public long BeforeBytes { get; set; }
        public long AfterBytes { get; set; }
        public long FreedBytes { get; set; }
        public int FileCount { get; set; }
    }

    public class MainForm : Form
    {
        private TextBox txtBackupFolder;
        private Button btnBrowse;
        private TextBox txtServer;
        private ComboBox cboAuth;
        private TextBox txtUser;
        private TextBox txtPassword;
        private CheckBox chkSubfolders;
        private CheckBox chkDropExisting;
        private TextBox txtDataPath;
        private TextBox txtLogPath;
        private Button btnDefaultDataPath;
        private Button btnDefaultLogPath;
        private CheckBox chkShrinkLog;
        private NumericUpDown numLogTargetMb;
        private Label lblLogTargetMb;
        private Button btnTest;
        private Button btnScan;
        private Button btnRestore;
        private Button btnCancel;
        private DataGridView grid;
        private TextBox txtLog;
        private ToolStripStatusLabel lblStatus;
        private ToolStripProgressBar overallProgress;
        private Label lblUser;
        private Label lblPassword;

        private readonly List<BackupItem> items = new List<BackupItem>();
        private volatile bool cancelRequested;
        private SqlCommand currentRestoreCommand;
        private int currentRestoreSpid;
        private readonly object currentCommandLock = new object();

        public MainForm()
        {
            Text = "SQL Server BAK 批量还原工具";
            StartPosition = FormStartPosition.CenterScreen;
            // Show the embedded executable icon on the title bar and taskbar.
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            MinimumSize = new Size(1040, 720);
            Size = new Size(1220, 820);
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point, 134);
            BuildUi();
            UpdateAuthUi();
            FormClosing += MainForm_FormClosing;
        }

        private void BuildUi()
        {
            var root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 4;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 294));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            Controls.Add(root);

            var config = new GroupBox();
            config.Text = "连接、备份与目标目录";
            config.Dock = DockStyle.Fill;
            config.Padding = new Padding(12);
            root.Controls.Add(config, 0, 0);

            var cfg = new TableLayoutPanel();
            cfg.Dock = DockStyle.Fill;
            cfg.ColumnCount = 6;
            cfg.RowCount = 7;
            cfg.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
            cfg.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            cfg.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
            cfg.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            cfg.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            cfg.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            for (int i = 0; i < 7; i++) cfg.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            config.Controls.Add(cfg);

            cfg.Controls.Add(MakeLabel("BAK 目录："), 0, 0);
            txtBackupFolder = new TextBox { Dock = DockStyle.Fill };
            cfg.Controls.Add(txtBackupFolder, 1, 0);
            cfg.SetColumnSpan(txtBackupFolder, 4);
            btnBrowse = new Button { Text = "选择目录...", Dock = DockStyle.Fill, Margin = new Padding(6, 2, 0, 2) };
            btnBrowse.Click += BtnBrowse_Click;
            cfg.Controls.Add(btnBrowse, 5, 0);

            cfg.Controls.Add(MakeLabel("SQL Server："), 0, 1);
            txtServer = new TextBox { Dock = DockStyle.Fill, Text = "." };
            cfg.Controls.Add(txtServer, 1, 1);

            cfg.Controls.Add(MakeLabel("认证方式："), 2, 1);
            cboAuth = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            cboAuth.Items.AddRange(new object[] { "Windows 身份认证", "SQL Server 身份认证" });
            cboAuth.SelectedIndex = 0;
            cboAuth.SelectedIndexChanged += delegate { UpdateAuthUi(); };
            cfg.Controls.Add(cboAuth, 3, 1);

            btnTest = new Button { Text = "测试连接", Dock = DockStyle.Fill, Margin = new Padding(8, 2, 0, 2) };
            btnTest.Click += BtnTest_Click;
            cfg.Controls.Add(btnTest, 4, 1);
            cfg.SetColumnSpan(btnTest, 2);

            lblUser = MakeLabel("用户名：");
            cfg.Controls.Add(lblUser, 0, 2);
            txtUser = new TextBox { Dock = DockStyle.Fill };
            cfg.Controls.Add(txtUser, 1, 2);

            lblPassword = MakeLabel("密码：");
            cfg.Controls.Add(lblPassword, 2, 2);
            txtPassword = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
            cfg.Controls.Add(txtPassword, 3, 2);

            chkSubfolders = new CheckBox { Text = "扫描子目录", AutoSize = true, MinimumSize = new Size(110, 0), Anchor = AnchorStyles.Left };
            cfg.Controls.Add(chkSubfolders, 4, 2);
            chkDropExisting = new CheckBox { Text = "同名数据库先删除后还原", Checked = true, AutoSize = true, Anchor = AnchorStyles.Left };
            cfg.Controls.Add(chkDropExisting, 5, 2);

            cfg.Controls.Add(MakeLabel("数据目录："), 0, 3);
            txtDataPath = new TextBox { Dock = DockStyle.Fill };
            cfg.Controls.Add(txtDataPath, 1, 3);
            cfg.SetColumnSpan(txtDataPath, 4);
            btnDefaultDataPath = new Button { Text = "SQL 默认", Dock = DockStyle.Fill, Margin = new Padding(6, 2, 0, 2) };
            btnDefaultDataPath.Click += BtnDefaultDataPath_Click;
            cfg.Controls.Add(btnDefaultDataPath, 5, 3);

            cfg.Controls.Add(MakeLabel("日志目录："), 0, 4);
            txtLogPath = new TextBox { Dock = DockStyle.Fill };
            cfg.Controls.Add(txtLogPath, 1, 4);
            cfg.SetColumnSpan(txtLogPath, 4);
            btnDefaultLogPath = new Button { Text = "SQL 默认", Dock = DockStyle.Fill, Margin = new Padding(6, 2, 0, 2) };
            btnDefaultLogPath.Click += BtnDefaultLogPath_Click;
            cfg.Controls.Add(btnDefaultLogPath, 5, 4);

            var tip = new ToolTip();
            tip.SetToolTip(txtDataPath, "留空表示使用 SQL Server 默认数据目录。自定义路径必须是 SQL Server 主机能够访问的目录，例如 E:\\SQLData\\。");
            tip.SetToolTip(txtLogPath, "留空表示使用 SQL Server 默认日志目录。自定义路径必须是 SQL Server 主机能够访问的目录，例如 F:\\SQLLog\\。");

            chkShrinkLog = new CheckBox
            {
                Text = "还原成功后自动清理日志",
                Checked = false,
                AutoSize = true,
                Anchor = AnchorStyles.Left
            };
            chkShrinkLog.CheckedChanged += delegate
            {
                bool enabled = chkShrinkLog.Checked && chkShrinkLog.Enabled;
                if (numLogTargetMb != null) numLogTargetMb.Enabled = enabled;
                if (lblLogTargetMb != null) lblLogTargetMb.Enabled = enabled;
            };
            cfg.Controls.Add(chkShrinkLog, 0, 5);
            cfg.SetColumnSpan(chkShrinkLog, 2);

            lblLogTargetMb = MakeLabel("LDF 目标：");
            lblLogTargetMb.TextAlign = ContentAlignment.MiddleRight;
            lblLogTargetMb.Enabled = false;
            cfg.Controls.Add(lblLogTargetMb, 2, 5);

            numLogTargetMb = new NumericUpDown
            {
                Dock = DockStyle.Fill,
                Minimum = 64,
                Maximum = 102400,
                Increment = 256,
                Value = 1024,
                ThousandsSeparator = true,
                Enabled = false
            };
            cfg.Controls.Add(numLogTargetMb, 3, 5);

            var shrinkUnit = new Label
            {
                Text = "MB（会设为 SIMPLE）",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.DimGray
            };
            cfg.Controls.Add(shrinkUnit, 4, 5);
            cfg.SetColumnSpan(shrinkUnit, 2);

            tip.SetToolTip(chkShrinkLog, "仅建议测试/开发环境使用。还原成功后会将数据库恢复模式设为 SIMPLE，执行 CHECKPOINT，并对每个日志文件执行 DBCC SHRINKFILE。");
            tip.SetToolTip(numLogTargetMb, "每个日志文件的目标大小（MB）。例如 1024 表示每个 LDF 尝试收缩到约 1 GB。SQL Server 可能因活动 VLF 等原因无法精确达到目标。");

            var actions = new FlowLayoutPanel();
            actions.Dock = DockStyle.Fill;
            actions.FlowDirection = FlowDirection.LeftToRight;
            actions.WrapContents = false;
            actions.Padding = new Padding(0, 2, 0, 0);
            btnScan = new Button { Text = "① 扫描 BAK", Width = 130, Height = 30 };
            btnScan.Click += BtnScan_Click;
            btnRestore = new Button { Text = "② 全部还原", Width = 130, Height = 30, Enabled = false };
            btnRestore.Click += BtnRestore_Click;
            btnCancel = new Button { Text = "中止当前任务", Width = 130, Height = 30, Enabled = false };
            btnCancel.Click += BtnCancel_Click;
            actions.Controls.Add(btnScan);
            actions.Controls.Add(btnRestore);
            actions.Controls.Add(btnCancel);
            var note = new Label
            {
                Text = "目录留空=SQL 默认；自动日志清理仅建议测试环境使用。扫描会预估还原空间并检查目标磁盘。",
                AutoSize = true,
                Margin = new Padding(20, 7, 0, 0),
                ForeColor = Color.DimGray
            };
            actions.Controls.Add(note);
            cfg.Controls.Add(actions, 0, 6);
            cfg.SetColumnSpan(actions, 6);

            grid = new DataGridView();
            grid.Dock = DockStyle.Fill;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.RowHeadersVisible = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            grid.AutoGenerateColumns = false;
            grid.BackgroundColor = SystemColors.Window;
            grid.BorderStyle = BorderStyle.Fixed3D;
            grid.ColumnHeadersHeight = 32;
            grid.RowTemplate.Height = 28;
            grid.CellValueChanged += Grid_CellValueChanged;
            grid.CurrentCellDirtyStateChanged += delegate
            {
                if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };

            var colSelect = new DataGridViewCheckBoxColumn { HeaderText = "选择", Width = 52, DataPropertyName = "Selected" };
            var colFile = new DataGridViewTextBoxColumn { HeaderText = "BAK 文件", Width = 210, DataPropertyName = "FileName", ReadOnly = true };
            var colDb = new DataGridViewTextBoxColumn { HeaderText = "数据库名", Width = 165, DataPropertyName = "DatabaseName", ReadOnly = true };
            var colBakSize = new DataGridViewTextBoxColumn { HeaderText = "BAK 大小", Width = 85, ReadOnly = true };
            var colDataSize = new DataGridViewTextBoxColumn { HeaderText = "数据文件", Width = 88, ReadOnly = true };
            var colLogSize = new DataGridViewTextBoxColumn { HeaderText = "日志文件", Width = 88, ReadOnly = true };
            var colRestoreSize = new DataGridViewTextBoxColumn { HeaderText = "预计还原", Width = 92, ReadOnly = true };
            var colTargetFree = new DataGridViewTextBoxColumn { HeaderText = "目标盘可用", Width = 155, ReadOnly = true };
            var colSpace = new DataGridViewTextBoxColumn { HeaderText = "空间检查", Width = 115, ReadOnly = true };
            var colExists = new DataGridViewTextBoxColumn { HeaderText = "已存在", Width = 70, ReadOnly = true };
            var colStatus = new DataGridViewTextBoxColumn { HeaderText = "当前状态", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 210, ReadOnly = true };
            var colProgress = new DataGridViewTextBoxColumn { HeaderText = "还原进度", Width = 82, ReadOnly = true };
            grid.Columns.AddRange(colSelect, colFile, colDb, colBakSize, colDataSize, colLogSize, colRestoreSize, colTargetFree, colSpace, colExists, colStatus, colProgress);
            root.Controls.Add(grid, 0, 1);

            var logGroup = new GroupBox { Text = "运行日志", Dock = DockStyle.Fill, Padding = new Padding(8) };
            txtLog = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                BackColor = Color.White,
                Font = new Font("Consolas", 9F)
            };
            logGroup.Controls.Add(txtLog);
            root.Controls.Add(logGroup, 0, 2);

            var status = new StatusStrip();
            status.SizingGrip = false;
            lblStatus = new ToolStripStatusLabel("就绪") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            overallProgress = new ToolStripProgressBar { Width = 180, Minimum = 0, Maximum = 100, Value = 0 };
            status.Items.Add(lblStatus);
            status.Items.Add(overallProgress);
            root.Controls.Add(status, 0, 3);
        }

        private Label MakeLabel(string text)
        {
            return new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, AutoSize = false };
        }

        private void UpdateAuthUi()
        {
            bool sqlAuth = cboAuth != null && cboAuth.SelectedIndex == 1;
            if (txtUser != null) txtUser.Enabled = sqlAuth;
            if (txtPassword != null) txtPassword.Enabled = sqlAuth;
            if (lblUser != null) lblUser.Enabled = sqlAuth;
            if (lblPassword != null) lblPassword.Enabled = sqlAuth;
        }

        private void BtnBrowse_Click(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "选择包含 SQL Server .bak 文件的目录";
                if (Directory.Exists(txtBackupFolder.Text)) dlg.SelectedPath = txtBackupFolder.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK) txtBackupFolder.Text = dlg.SelectedPath;
            }
        }

        private async void BtnDefaultDataPath_Click(object sender, EventArgs e)
        {
            await FillDefaultPathAsync(true);
        }

        private async void BtnDefaultLogPath_Click(object sender, EventArgs e)
        {
            await FillDefaultPathAsync(false);
        }

        private async Task FillDefaultPathAsync(bool dataPath)
        {
            if (!ValidateInputs(false)) return;
            try
            {
                SetBusy(true, "正在读取 SQL Server 默认目录...");
                string[] paths = await GetServerDefaultPathsAsync();
                if (dataPath) txtDataPath.Text = paths[0]; else txtLogPath.Text = paths[1];
                Log((dataPath ? "数据目录" : "日志目录") + "已填入 SQL Server 默认路径：" + (dataPath ? paths[0] : paths[1]));
            }
            catch (Exception ex)
            {
                Log("读取 SQL Server 默认目录失败：" + ex.Message, true);
                MessageBox.Show(this, ex.Message, "读取默认目录失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false, "就绪");
            }
        }

        private async void BtnTest_Click(object sender, EventArgs e)
        {
            SetBusy(true, "正在测试连接...");
            try
            {
                using (var conn = new SqlConnection(BuildConnectionString()))
                {
                    await conn.OpenAsync();
                    using (var cmd = new SqlCommand("SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(100))", conn))
                    {
                        cmd.CommandTimeout = 15;
                        var version = Convert.ToString(await cmd.ExecuteScalarAsync());
                        Log("连接成功。SQL Server 版本：" + version);
                        MessageBox.Show(this, "连接成功。\r\nSQL Server 版本：" + version, "测试连接", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                Log("连接失败：" + ex.Message, true);
                MessageBox.Show(this, ex.Message, "连接失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false, "就绪");
            }
        }

        private async void BtnScan_Click(object sender, EventArgs e)
        {
            if (!ValidateInputs(true)) return;
            cancelRequested = false;
            items.Clear();
            grid.Rows.Clear();
            txtLog.Clear();
            overallProgress.Value = 0;
            SetBusy(true, "正在扫描 BAK...");
            btnCancel.Enabled = true;

            try
            {
                string[] targetPaths = await GetEffectiveTargetPathsAsync();
                string dataPath = targetPaths[0];
                string logPath = targetPaths[1];
                Log("目标数据目录：" + dataPath);
                Log("目标日志目录：" + logPath);

                Dictionary<string, long> freeMap = await GetServerFreeDriveMapAsync();
                if (freeMap.Count == 0)
                    Log("无法读取 SQL Server 主机磁盘可用空间；空间检查将显示“无法检测”，但仍允许还原。", true);

                var option = chkSubfolders.Checked ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                string[] files = Directory.GetFiles(txtBackupFolder.Text.Trim(), "*.bak", option)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();

                if (files.Length == 0)
                {
                    Log("目录中没有找到 .bak 文件。", true);
                    MessageBox.Show(this, "指定目录中没有找到 .bak 文件。", "扫描完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Log("找到 " + files.Length + " 个 BAK 文件，开始读取备份信息和文件尺寸。\r\n");
                for (int i = 0; i < files.Length; i++)
                {
                    if (cancelRequested) break;
                    var path = files[i];
                    var item = new BackupItem
                    {
                        Selected = true,
                        FileName = Path.GetFileName(path),
                        FullPath = path,
                        FileSize = new FileInfo(path).Length,
                        DatabaseName = "读取中...",
                        Status = "读取备份头...",
                        Progress = 0,
                        BackupPosition = 1,
                        TargetFreeText = "-",
                        SpaceStatus = "-",
                        GridRowIndex = grid.Rows.Count
                    };
                    items.Add(item);
                    int row = grid.Rows.Add(item.Selected, item.FileName, item.DatabaseName, FormatBytes(item.FileSize), "-", "-", "-", "-", "-", "-", item.Status, "-");
                    item.GridRowIndex = row;
                    grid.Rows[row].Tag = item;
                    grid.FirstDisplayedScrollingRowIndex = row;

                    try
                    {
                        await LoadBackupMetadataAsync(item);
                        item.BackupFiles = await GetBackupFileListAsync(item.FullPath, item.BackupPosition);
                        item.DataSize = item.BackupFiles.Where(x => !String.Equals(x.Type, "L", StringComparison.OrdinalIgnoreCase)).Sum(x => x.Size);
                        item.LogSize = item.BackupFiles.Where(x => String.Equals(x.Type, "L", StringComparison.OrdinalIgnoreCase)).Sum(x => x.Size);
                        item.Exists = await DatabaseExistsAsync(item.DatabaseName);
                        await ApplySpaceCheckAsync(item, dataPath, logPath, freeMap);

                        if (item.SpaceEnough == false)
                        {
                            item.Selected = false;
                            item.Status = "空间不足，默认不选择";
                        }
                        else
                        {
                            item.Status = "待还原";
                        }
                        UpdateGridRow(item);
                        Log("[扫描] " + item.FileName + " -> " + item.DatabaseName
                            + "，数据=" + FormatBytes(item.DataSize)
                            + "，日志=" + FormatBytes(item.LogSize)
                            + "，预计还原=" + FormatBytes(item.DataSize + item.LogSize)
                            + "，空间=" + item.SpaceStatus
                            + "，数据库已存在=" + (item.Exists ? "是" : "否"));
                    }
                    catch (Exception ex)
                    {
                        item.Selected = false;
                        item.DatabaseName = "读取失败";
                        item.Status = "错误：" + ShortError(ex.Message);
                        UpdateGridRow(item);
                        Log("[扫描失败] " + item.FileName + "：" + ex.Message, true);
                    }

                    overallProgress.Value = Math.Min(100, (i + 1) * 100 / files.Length);
                    lblStatus.Text = "扫描中 " + (i + 1) + "/" + files.Length;
                }

                var okCount = items.Count(x => x.DatabaseName != "读取失败");
                var lowSpaceCount = items.Count(x => x.SpaceEnough == false);
                btnRestore.Enabled = items.Any(x => x.Selected && x.DatabaseName != "读取失败") && !cancelRequested;
                lblStatus.Text = cancelRequested
                    ? "扫描已中止"
                    : "扫描完成，共 " + okCount + " 个备份" + (lowSpaceCount > 0 ? "，" + lowSpaceCount + " 个空间不足" : "");
                Log(cancelRequested ? "扫描已由用户中止。" : "扫描完成，可识别备份数：" + okCount + "；空间不足：" + lowSpaceCount + "。\r\n提示：批量还原时每个数据库开始前还会重新检查一次磁盘空间。");
            }
            catch (Exception ex)
            {
                Log("扫描发生错误：" + ex, true);
                MessageBox.Show(this, ex.Message, "扫描失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false, lblStatus.Text);
                btnCancel.Enabled = false;
                btnRestore.Enabled = items.Any(x => x.Selected && x.DatabaseName != "读取失败");
            }
        }

        private async void BtnRestore_Click(object sender, EventArgs e)
        {
            var selected = items.Where(x => x.Selected && x.DatabaseName != "读取失败").ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show(this, "请至少选择一个可还原的备份。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (chkDropExisting.Checked && selected.Any(x => x.Exists))
            {
                var result = MessageBox.Show(this,
                    "选中的备份中存在同名数据库。\r\n\r\n继续后，同名数据库会被强制断开连接并删除，然后使用 BAK 重新还原。\r\n该操作不可撤销。\r\n\r\n确定继续吗？",
                    "确认删除并还原", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (result != DialogResult.Yes) return;
            }

            if (chkShrinkLog.Checked)
            {
                int targetMb = Decimal.ToInt32(numLogTargetMb.Value);
                var shrinkConfirm = MessageBox.Show(this,
                    "已启用“还原成功后自动清理日志”。\r\n\r\n程序会把每个成功还原的数据库恢复模式改为 SIMPLE，然后执行 CHECKPOINT 和 DBCC SHRINKFILE。\r\n这会中断原有 FULL/BULK_LOGGED 日志备份链。\r\n\r\n目标大小：" + targetMb.ToString("N0") + " MB / 每个 LDF\r\n\r\n确定继续吗？",
                    "确认自动清理日志", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (shrinkConfirm != DialogResult.Yes) return;
            }

            cancelRequested = false;
            btnRestore.Enabled = false;
            SetBusy(true, "正在批量还原...");
            btnCancel.Enabled = true;
            overallProgress.Value = 0;
            int success = 0;
            int failed = 0;
            int cleanupFailed = 0;

            try
            {
                string[] paths = await GetEffectiveTargetPathsAsync();
                Log("目标数据目录：" + paths[0]);
                Log("目标日志目录：" + paths[1]);
                Log("开始顺序还原 " + selected.Count + " 个数据库。\r\n");

                for (int i = 0; i < selected.Count; i++)
                {
                    if (cancelRequested) break;
                    var item = selected[i];
                    item.Progress = 0;
                    item.Status = "检查磁盘空间";
                    UpdateGridRow(item);
                    lblStatus.Text = "正在还原 " + (i + 1) + "/" + selected.Count + "：" + item.DatabaseName;
                    Log("------------------------------------------------------------");
                    Log("[开始] " + item.DatabaseName + " <- " + item.FullPath);

                    try
                    {
                        Dictionary<string, long> freeMap = await GetServerFreeDriveMapAsync();
                        item.Exists = await DatabaseExistsAsync(item.DatabaseName);
                        await ApplySpaceCheckAsync(item, paths[0], paths[1], freeMap);
                        UpdateGridRow(item);
                        Log("[空间检查] " + item.DatabaseName + "：" + item.SpaceStatus + "；目标盘可用：" + item.TargetFreeText);
                        if (item.SpaceEnough == false)
                            throw new InvalidOperationException("目标磁盘空间不足。预计需要 " + FormatBytes(item.DataSize + item.LogSize) + "；" + item.SpaceStatus + "。为避免破坏现有数据库，本次未执行 DROP/RESTORE。");

                        await RestoreOneAsync(item, paths[0], paths[1]);
                        if (cancelRequested)
                        {
                            item.Status = "已中止";
                            UpdateGridRow(item);
                            break;
                        }

                        bool shrinkWarning = false;
                        string shrinkWarningText = null;
                        if (chkShrinkLog.Checked)
                        {
                            item.Progress = 100;
                            item.Status = "还原成功，正在清理日志";
                            UpdateGridRow(item);
                            int targetMb = Decimal.ToInt32(numLogTargetMb.Value);
                            Log("[日志清理] " + item.DatabaseName + "：目标 " + targetMb.ToString("N0") + " MB/每个 LDF；将恢复模式设为 SIMPLE。");

                            try
                            {
                                LogShrinkResult shrink = await ShrinkDatabaseLogsAsync(item.DatabaseName, targetMb);
                                item.Status = "还原成功，日志已清理";
                                Log("[日志清理完成] " + item.DatabaseName
                                    + "：清理前 " + FormatBytes(shrink.BeforeBytes)
                                    + "，清理后 " + FormatBytes(shrink.AfterBytes)
                                    + "，释放约 " + FormatBytes(shrink.FreedBytes)
                                    + "；日志文件数 " + shrink.FileCount.ToString() + "。");
                            }
                            catch (Exception shrinkEx)
                            {
                                cleanupFailed++;
                                shrinkWarning = true;
                                shrinkWarningText = ShortError(shrinkEx.Message);
                                item.Status = cancelRequested ? "还原成功，日志清理已中止" : "还原成功，日志清理失败";
                                Log("[日志清理失败] " + item.DatabaseName + "：" + shrinkEx, true);
                            }
                        }
                        else
                        {
                            item.Status = "还原成功";
                        }

                        item.Progress = 100;
                        item.Exists = true;
                        success++;
                        Log("[成功] " + item.DatabaseName + (shrinkWarning ? "；但日志清理失败：" + shrinkWarningText : ""));
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        item.Status = cancelRequested ? "已中止" : "还原失败：" + ShortError(ex.Message);
                        Log("[失败] " + item.DatabaseName + "：" + ex, true);
                    }
                    finally
                    {
                        UpdateGridRow(item);
                    }

                    int done = i + 1;
                    overallProgress.Value = Math.Min(100, done * 100 / selected.Count);
                }

                string cleanupSummary = cleanupFailed > 0 ? "，日志清理失败 " + cleanupFailed : "";
                lblStatus.Text = cancelRequested
                    ? "任务已中止。成功 " + success + "，失败 " + failed + cleanupSummary
                    : "批量还原完成。成功 " + success + "，失败 " + failed + cleanupSummary;
                Log(lblStatus.Text);

                if (!cancelRequested)
                {
                    string message = "批量还原完成。\r\n成功：" + success + "\r\n失败：" + failed;
                    if (chkShrinkLog.Checked)
                        message += "\r\n日志清理失败：" + cleanupFailed;
                    message += "\r\n\r\n详细信息请查看运行日志。";

                    MessageBox.Show(this, message,
                        "还原完成", MessageBoxButtons.OK, (failed == 0 && cleanupFailed == 0) ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                Log("批量还原任务失败：" + ex, true);
                MessageBox.Show(this, ex.Message, "还原任务失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                lock (currentCommandLock)
                {
                    currentRestoreCommand = null;
                    currentRestoreSpid = 0;
                }
                SetBusy(false, lblStatus.Text);
                btnCancel.Enabled = false;
                btnRestore.Enabled = items.Any(x => x.Selected && x.DatabaseName != "读取失败");
            }
        }

        private async void BtnCancel_Click(object sender, EventArgs e)
        {
            cancelRequested = true;
            btnCancel.Enabled = false;
            lblStatus.Text = "正在中止当前任务...";
            Log("用户请求中止当前任务。", true);

            SqlCommand cmd = null;
            int spid = 0;
            lock (currentCommandLock)
            {
                cmd = currentRestoreCommand;
                spid = currentRestoreSpid;
            }

            try
            {
                if (cmd != null) cmd.Cancel();
            }
            catch { }

            if (spid > 0)
            {
                try
                {
                    using (var conn = new SqlConnection(BuildConnectionString()))
                    {
                        await conn.OpenAsync();
                        using (var kill = new SqlCommand("KILL " + spid.ToString(), conn))
                        {
                            kill.CommandTimeout = 15;
                            await kill.ExecuteNonQueryAsync();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log("中止会话返回：" + ex.Message, true);
                }
            }
        }

        private async Task LoadBackupMetadataAsync(BackupItem item)
        {
            string sql = "RESTORE HEADERONLY FROM DISK = " + SqlString(item.FullPath);
            using (var conn = new SqlConnection(BuildConnectionString()))
            {
                await conn.OpenAsync();
                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.CommandTimeout = 0;
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        int ordPos = reader.GetOrdinal("Position");
                        int ordDb = reader.GetOrdinal("DatabaseName");
                        int ordType = reader.GetOrdinal("BackupType");
                        int ordFinish = reader.GetOrdinal("BackupFinishDate");
                        bool found = false;
                        DateTime bestDate = DateTime.MinValue;
                        int bestPos = 0;
                        string bestDb = null;
                        while (await reader.ReadAsync())
                        {
                            int backupType = Convert.ToInt32(reader.GetValue(ordType));
                            if (backupType != 1) continue; // Full database backup
                            int pos = Convert.ToInt32(reader.GetValue(ordPos));
                            string db = Convert.ToString(reader.GetValue(ordDb));
                            DateTime finish = reader.IsDBNull(ordFinish) ? DateTime.MinValue : Convert.ToDateTime(reader.GetValue(ordFinish));
                            if (!found || finish >= bestDate)
                            {
                                found = true;
                                bestDate = finish;
                                bestPos = pos;
                                bestDb = db;
                            }
                        }
                        if (!found) throw new InvalidOperationException("BAK 中没有找到完整数据库备份集（BackupType=1）。");
                        item.BackupPosition = bestPos;
                        item.DatabaseName = bestDb;
                        item.BackupFinishDate = bestDate == DateTime.MinValue ? (DateTime?)null : bestDate;
                    }
                }
            }
        }

        private async Task<bool> DatabaseExistsAsync(string databaseName)
        {
            using (var conn = new SqlConnection(BuildConnectionString()))
            {
                await conn.OpenAsync();
                using (var cmd = new SqlCommand("SELECT CASE WHEN DB_ID(@db) IS NULL THEN 0 ELSE 1 END", conn))
                {
                    cmd.Parameters.Add("@db", SqlDbType.NVarChar, 128).Value = databaseName;
                    return Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1;
                }
            }
        }

        private async Task<string[]> GetServerDefaultPathsAsync()
        {
            using (var conn = new SqlConnection(BuildConnectionString()))
            {
                await conn.OpenAsync();
                string data = null;
                string log = null;
                using (var cmd = new SqlCommand("SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(4000)), CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS nvarchar(4000))", conn))
                using (var r = await cmd.ExecuteReaderAsync())
                {
                    if (await r.ReadAsync())
                    {
                        if (!r.IsDBNull(0)) data = Convert.ToString(r.GetValue(0));
                        if (!r.IsDBNull(1)) log = Convert.ToString(r.GetValue(1));
                    }
                }

                if (String.IsNullOrWhiteSpace(data))
                    data = await QueryMasterFileFolderAsync(conn, 1);
                if (String.IsNullOrWhiteSpace(log))
                    log = await QueryMasterFileFolderAsync(conn, 2);
                if (String.IsNullOrWhiteSpace(log)) log = data;
                if (String.IsNullOrWhiteSpace(data)) throw new InvalidOperationException("无法读取 SQL Server 默认数据文件目录。");
                return new[] { EnsureTrailingSlash(data), EnsureTrailingSlash(log) };
            }
        }

        private async Task<string> QueryMasterFileFolderAsync(SqlConnection conn, int fileId)
        {
            const string sql = "SELECT TOP (1) physical_name FROM master.sys.master_files WHERE database_id = 1 AND file_id = @id";
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.Add("@id", SqlDbType.Int).Value = fileId;
                object v = await cmd.ExecuteScalarAsync();
                if (v == null || v == DBNull.Value) return null;
                string p = Convert.ToString(v);
                int idx = Math.Max(p.LastIndexOf('\\'), p.LastIndexOf('/'));
                return idx >= 0 ? p.Substring(0, idx + 1) : null;
            }
        }

        private async Task<List<BackupFileEntry>> GetBackupFileListAsync(string path, int position)
        {
            string sql = "RESTORE FILELISTONLY FROM DISK = " + SqlString(path) + " WITH FILE = " + position.ToString();
            var list = new List<BackupFileEntry>();
            using (var conn = new SqlConnection(BuildConnectionString()))
            {
                await conn.OpenAsync();
                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.CommandTimeout = 0;
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        int ordLogical = reader.GetOrdinal("LogicalName");
                        int ordPhysical = reader.GetOrdinal("PhysicalName");
                        int ordType = reader.GetOrdinal("Type");
                        int ordSize = reader.GetOrdinal("Size");
                        while (await reader.ReadAsync())
                        {
                            list.Add(new BackupFileEntry
                            {
                                LogicalName = Convert.ToString(reader.GetValue(ordLogical)),
                                PhysicalName = Convert.ToString(reader.GetValue(ordPhysical)),
                                Type = Convert.ToString(reader.GetValue(ordType)),
                                Size = reader.IsDBNull(ordSize) ? 0L : Convert.ToInt64(reader.GetValue(ordSize))
                            });
                        }
                    }
                }
            }
            if (list.Count == 0) throw new InvalidOperationException("无法读取备份中的数据/日志文件列表。");
            return list;
        }

        private async Task RestoreOneAsync(BackupItem item, string dataPath, string logPath)
        {
            item.Status = "读取文件列表";
            UpdateGridRow(item);
            var files = item.BackupFiles;
            if (files == null || files.Count == 0)
            {
                files = await GetBackupFileListAsync(item.FullPath, item.BackupPosition);
                item.BackupFiles = files;
                item.DataSize = files.Where(x => !String.Equals(x.Type, "L", StringComparison.OrdinalIgnoreCase)).Sum(x => x.Size);
                item.LogSize = files.Where(x => String.Equals(x.Type, "L", StringComparison.OrdinalIgnoreCase)).Sum(x => x.Size);
            }

            using (var conn = new SqlConnection(BuildConnectionString()))
            {
                await conn.OpenAsync();

                bool exists = await DatabaseExistsOnConnectionAsync(conn, item.DatabaseName);
                if (exists)
                {
                    if (!chkDropExisting.Checked)
                        throw new InvalidOperationException("同名数据库已存在，而“同名数据库先删除后还原”未勾选。");

                    item.Status = "删除同名数据库";
                    UpdateGridRow(item);
                    await DropExistingDatabaseAsync(conn, item.DatabaseName);
                }

                if (cancelRequested) throw new OperationCanceledException("用户中止任务。");

                string restoreSql = BuildRestoreSql(item, files, dataPath, logPath);
                item.Status = "正在还原";
                UpdateGridRow(item);

                int spid;
                using (var sp = new SqlCommand("SELECT @@SPID", conn))
                    spid = Convert.ToInt32(await sp.ExecuteScalarAsync());

                using (var cmd = new SqlCommand(restoreSql, conn))
                {
                    cmd.CommandTimeout = 0;
                    lock (currentCommandLock)
                    {
                        currentRestoreCommand = cmd;
                        currentRestoreSpid = spid;
                    }

                    Task restoreTask = cmd.ExecuteNonQueryAsync();
                    while (!restoreTask.IsCompleted)
                    {
                        if (cancelRequested)
                        {
                            try { cmd.Cancel(); } catch { }
                        }

                        try
                        {
                            decimal pct = await QueryRestorePercentAsync(spid);
                            int progress = (int)Math.Max(0, Math.Min(99, Math.Floor(pct)));
                            if (progress > item.Progress)
                            {
                                item.Progress = progress;
                                item.Status = "正在还原 " + progress + "%";
                                UpdateGridRow(item);
                            }
                        }
                        catch { }
                        await Task.Delay(700);
                    }

                    await restoreTask;
                    lock (currentCommandLock)
                    {
                        currentRestoreCommand = null;
                        currentRestoreSpid = 0;
                    }
                }
            }
        }

        private async Task<LogShrinkResult> ShrinkDatabaseLogsAsync(string databaseName, int targetMb)
        {
            if (String.IsNullOrWhiteSpace(databaseName))
                throw new InvalidOperationException("数据库名为空，无法清理日志。");
            if (targetMb < 64)
                throw new InvalidOperationException("日志目标大小不能小于 64 MB。");

            var result = new LogShrinkResult();
            result.BeforeBytes = await GetDatabaseLogAllocatedBytesAsync(databaseName);

            using (var conn = new SqlConnection(BuildConnectionString()))
            {
                await conn.OpenAsync();

                // This intentionally switches restored test databases to SIMPLE.
                // It breaks the original log backup chain, so the UI keeps this feature opt-in.
                using (var simple = new SqlCommand("ALTER DATABASE " + SqlIdentifier(databaseName) + " SET RECOVERY SIMPLE;", conn))
                {
                    simple.CommandTimeout = 0;
                    await simple.ExecuteNonQueryAsync();
                }

                var logNames = new List<string>();
                string listSql = "SELECT name FROM " + SqlIdentifier(databaseName) + ".sys.database_files WHERE type = 1 ORDER BY file_id;";
                using (var list = new SqlCommand(listSql, conn))
                {
                    list.CommandTimeout = 30;
                    using (var reader = await list.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                            logNames.Add(Convert.ToString(reader.GetValue(0)));
                    }
                }

                result.FileCount = logNames.Count;
                if (logNames.Count == 0)
                    throw new InvalidOperationException("没有找到数据库日志文件。");

                using (var checkpoint = new SqlCommand("USE " + SqlIdentifier(databaseName) + "; CHECKPOINT;", conn))
                {
                    checkpoint.CommandTimeout = 0;
                    await checkpoint.ExecuteNonQueryAsync();
                }

                int spid;
                using (var sp = new SqlCommand("SELECT @@SPID", conn))
                    spid = Convert.ToInt32(await sp.ExecuteScalarAsync());

                foreach (string logicalName in logNames)
                {
                    if (cancelRequested) throw new OperationCanceledException("用户中止任务。");

                    string shrinkSql = "USE " + SqlIdentifier(databaseName)
                        + "; DBCC SHRINKFILE (" + SqlString(logicalName) + ", " + targetMb.ToString() + ") WITH NO_INFOMSGS;";
                    using (var shrink = new SqlCommand(shrinkSql, conn))
                    {
                        shrink.CommandTimeout = 0;
                        lock (currentCommandLock)
                        {
                            currentRestoreCommand = shrink;
                            currentRestoreSpid = spid;
                        }

                        try
                        {
                            await shrink.ExecuteNonQueryAsync();
                        }
                        finally
                        {
                            lock (currentCommandLock)
                            {
                                if (Object.ReferenceEquals(currentRestoreCommand, shrink))
                                {
                                    currentRestoreCommand = null;
                                    currentRestoreSpid = 0;
                                }
                            }
                        }
                    }
                }
            }

            result.AfterBytes = await GetDatabaseLogAllocatedBytesAsync(databaseName);
            result.FreedBytes = Math.Max(0L, result.BeforeBytes - result.AfterBytes);
            return result;
        }

        private async Task<long> GetDatabaseLogAllocatedBytesAsync(string databaseName)
        {
            using (var conn = new SqlConnection(BuildConnectionString()))
            {
                await conn.OpenAsync();
                string sql = "SELECT ISNULL(SUM(CAST(size AS bigint)), 0) * 8192 FROM "
                    + SqlIdentifier(databaseName) + ".sys.database_files WHERE type = 1;";
                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.CommandTimeout = 30;
                    object value = await cmd.ExecuteScalarAsync();
                    if (value == null || value == DBNull.Value) return 0L;
                    return Convert.ToInt64(value);
                }
            }
        }

        private async Task DropExistingDatabaseAsync(SqlConnection conn, string databaseName)
        {
            string state = await GetDatabaseStateAsync(conn, databaseName);
            if (state == null) return;

            Log("检测到同名数据库 " + databaseName + "，当前状态=" + state + "，正在删除。");
            string dbId = SqlIdentifier(databaseName);
            Exception last = null;

            for (int attempt = 1; attempt <= 5; attempt++)
            {
                bool failed = false;

                try
                {
                    string sql;
                    if (String.Equals(state, "ONLINE", StringComparison.OrdinalIgnoreCase))
                    {
                        sql = "ALTER DATABASE " + dbId + " SET SINGLE_USER WITH ROLLBACK IMMEDIATE;\r\n"
                            + "DROP DATABASE " + dbId + ";";
                    }
                    else
                    {
                        sql = "DROP DATABASE " + dbId + ";";
                    }

                    using (var drop = new SqlCommand(sql, conn))
                    {
                        drop.CommandTimeout = 0;
                        await drop.ExecuteNonQueryAsync();
                    }
                    Log("已删除同名数据库：" + databaseName);
                    return;
                }
                catch (Exception ex)
                {
                    last = ex;
                    failed = true;
                }

                // .NET Framework 自带的旧版 csc 只支持到 C# 5，
                // C# 5 不允许在 catch 块中使用 await，因此异步重试逻辑放到 catch 之后。
                if (failed)
                {
                    if (attempt == 1 && !String.Equals(state, "ONLINE", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            await KillDatabaseSessionsAsync(conn, databaseName);
                        }
                        catch
                        {
                            // Ignore session cleanup failures here; DROP retry below will surface the real error.
                        }
                    }

                    await Task.Delay(600);
                    state = await GetDatabaseStateAsync(conn, databaseName);
                    if (state == null) return;
                }
            }

            throw new InvalidOperationException("删除同名数据库失败（状态=" + state + "）：" + (last == null ? "未知错误" : last.Message), last);
        }

        private async Task<string> GetDatabaseStateAsync(SqlConnection conn, string databaseName)
        {
            using (var cmd = new SqlCommand("SELECT state_desc FROM sys.databases WHERE name=@db", conn))
            {
                cmd.Parameters.Add("@db", SqlDbType.NVarChar, 128).Value = databaseName;
                object v = await cmd.ExecuteScalarAsync();
                return v == null || v == DBNull.Value ? null : Convert.ToString(v);
            }
        }

        private async Task KillDatabaseSessionsAsync(SqlConnection conn, string databaseName)
        {
            var ids = new List<int>();
            const string sql = @"
DECLARE @dbid int = DB_ID(@db);
SELECT DISTINCT s.session_id
FROM sys.dm_exec_sessions s
LEFT JOIN sys.dm_exec_requests r ON r.session_id = s.session_id
WHERE s.is_user_process = 1
  AND s.session_id <> @@SPID
  AND (@dbid IS NOT NULL)
  AND (s.database_id = @dbid OR r.database_id = @dbid);";
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.Add("@db", SqlDbType.NVarChar, 128).Value = databaseName;
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync()) ids.Add(Convert.ToInt32(reader.GetValue(0)));
                }
            }
            foreach (int id in ids.Distinct())
            {
                try
                {
                    using (var kill = new SqlCommand("KILL " + id.ToString(), conn))
                    {
                        kill.CommandTimeout = 15;
                        await kill.ExecuteNonQueryAsync();
                    }
                    Log("已终止占用数据库的会话 SPID=" + id.ToString());
                }
                catch { }
            }
        }

        private async Task<bool> DatabaseExistsOnConnectionAsync(SqlConnection conn, string databaseName)
        {
            using (var cmd = new SqlCommand("SELECT CASE WHEN DB_ID(@db) IS NULL THEN 0 ELSE 1 END", conn))
            {
                cmd.Parameters.Add("@db", SqlDbType.NVarChar, 128).Value = databaseName;
                return Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1;
            }
        }

        private async Task<decimal> QueryRestorePercentAsync(int spid)
        {
            using (var conn = new SqlConnection(BuildConnectionString()))
            {
                await conn.OpenAsync();
                const string sql = "SELECT CAST(percent_complete AS decimal(9,2)) FROM sys.dm_exec_requests WHERE session_id = @sid AND command LIKE 'RESTORE%'";
                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.CommandTimeout = 5;
                    cmd.Parameters.Add("@sid", SqlDbType.Int).Value = spid;
                    object value = await cmd.ExecuteScalarAsync();
                    if (value == null || value == DBNull.Value) return 0m;
                    return Convert.ToDecimal(value);
                }
            }
        }

        private async Task<string[]> GetEffectiveTargetPathsAsync()
        {
            string data = String.IsNullOrWhiteSpace(txtDataPath.Text) ? null : txtDataPath.Text.Trim();
            string log = String.IsNullOrWhiteSpace(txtLogPath.Text) ? null : txtLogPath.Text.Trim();
            if (String.IsNullOrWhiteSpace(data) || String.IsNullOrWhiteSpace(log))
            {
                string[] defaults = await GetServerDefaultPathsAsync();
                if (String.IsNullOrWhiteSpace(data)) data = defaults[0];
                if (String.IsNullOrWhiteSpace(log)) log = defaults[1];
            }
            return new[] { EnsureTrailingSlash(data), EnsureTrailingSlash(log) };
        }

        private async Task<Dictionary<string, long>> GetServerFreeDriveMapAsync()
        {
            var map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            using (var conn = new SqlConnection(BuildConnectionString()))
            {
                await conn.OpenAsync();

                try
                {
                    using (var cmd = new SqlCommand("EXEC master.dbo.xp_fixeddrives;", conn))
                    {
                        cmd.CommandTimeout = 10;
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                string drive = Convert.ToString(reader.GetValue(0));
                                long mb = Convert.ToInt64(reader.GetValue(1));
                                string key = NormalizeVolumeKey(drive);
                                if (!String.IsNullOrWhiteSpace(key)) map[key] = mb * 1024L * 1024L;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log("xp_fixeddrives 空间检测不可用：" + ShortError(ex.Message), true);
                }

                try
                {
                    const string sql = @"
SELECT DISTINCT vs.volume_mount_point, vs.available_bytes
FROM sys.master_files mf
CROSS APPLY sys.dm_os_volume_stats(mf.database_id, mf.file_id) vs
WHERE vs.volume_mount_point IS NOT NULL;";
                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        cmd.CommandTimeout = 10;
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                string volume = reader.IsDBNull(0) ? null : Convert.ToString(reader.GetValue(0));
                                long bytes = reader.IsDBNull(1) ? 0L : Convert.ToInt64(reader.GetValue(1));
                                string key = NormalizeVolumeKey(volume);
                                if (!String.IsNullOrWhiteSpace(key) && (!map.ContainsKey(key) || bytes > 0)) map[key] = bytes;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (map.Count == 0) Log("dm_os_volume_stats 空间检测不可用：" + ShortError(ex.Message), true);
                }
            }
            return map;
        }

        private async Task<Dictionary<string, long>> GetExistingDatabaseReclaimableAsync(string databaseName)
        {
            var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            if (String.IsNullOrWhiteSpace(databaseName)) return result;
            using (var conn = new SqlConnection(BuildConnectionString()))
            {
                await conn.OpenAsync();
                const string sql = @"
SELECT physical_name, CAST(size AS bigint) * 8192
FROM master.sys.master_files
WHERE database_id = DB_ID(@db);";
                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.Add("@db", SqlDbType.NVarChar, 128).Value = databaseName;
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            string path = reader.IsDBNull(0) ? null : Convert.ToString(reader.GetValue(0));
                            long bytes = reader.IsDBNull(1) ? 0L : Convert.ToInt64(reader.GetValue(1));
                            string key = GetPathVolumeKey(path);
                            if (String.IsNullOrWhiteSpace(key)) continue;
                            if (!result.ContainsKey(key)) result[key] = 0L;
                            result[key] += bytes;
                        }
                    }
                }
            }
            return result;
        }

        private async Task ApplySpaceCheckAsync(BackupItem item, string dataPath, string logPath, Dictionary<string, long> freeMap)
        {
            Dictionary<string, long> reclaim = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            if (item.Exists && chkDropExisting.Checked)
            {
                try { reclaim = await GetExistingDatabaseReclaimableAsync(item.DatabaseName); }
                catch (Exception ex) { Log("读取现有数据库占用空间失败：" + ShortError(ex.Message), true); }
            }

            SpaceCheckResult r = EvaluateSpace(item.DataSize, item.LogSize, dataPath, logPath, freeMap, reclaim);
            item.SpaceEnough = r.Enough;
            item.TargetFreeText = r.FreeText;
            item.SpaceStatus = r.StatusText;
        }

        private SpaceCheckResult EvaluateSpace(long dataBytes, long logBytes, string dataPath, string logPath,
            Dictionary<string, long> freeMap, Dictionary<string, long> reclaimMap)
        {
            string dataKey = GetPathVolumeKey(dataPath);
            string logKey = GetPathVolumeKey(logPath);
            var required = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            if (!String.IsNullOrWhiteSpace(dataKey)) required[dataKey] = dataBytes;
            if (!String.IsNullOrWhiteSpace(logKey))
            {
                if (!required.ContainsKey(logKey)) required[logKey] = 0L;
                required[logKey] += logBytes;
            }

            if (String.IsNullOrWhiteSpace(dataKey) || String.IsNullOrWhiteSpace(logKey))
            {
                return new SpaceCheckResult { Enough = null, FreeText = "无法检测", StatusText = "无法检测目标卷" };
            }

            bool unknown = false;
            long totalDeficit = 0L;
            var freeParts = new List<string>();
            foreach (var pair in required)
            {
                long free;
                if (!freeMap.TryGetValue(pair.Key, out free))
                {
                    unknown = true;
                    freeParts.Add(pair.Key + " 无法检测");
                    continue;
                }
                long reclaim = 0L;
                reclaimMap.TryGetValue(pair.Key, out reclaim);
                long effective = free;
                if (reclaim > 0 && long.MaxValue - effective > reclaim) effective += reclaim;
                if (effective < pair.Value) totalDeficit += pair.Value - effective;

                string text = pair.Key + " " + FormatBytes(free);
                if (reclaim > 0) text += "（删库后≈" + FormatBytes(effective) + "）";
                freeParts.Add(text);
            }

            if (totalDeficit > 0)
            {
                return new SpaceCheckResult
                {
                    Enough = false,
                    FreeText = String.Join("；", freeParts.ToArray()),
                    StatusText = "不足，约差 " + FormatBytes(totalDeficit)
                };
            }
            if (unknown)
            {
                return new SpaceCheckResult
                {
                    Enough = null,
                    FreeText = String.Join("；", freeParts.ToArray()),
                    StatusText = "部分卷无法检测"
                };
            }
            return new SpaceCheckResult
            {
                Enough = true,
                FreeText = String.Join("；", freeParts.ToArray()),
                StatusText = "充足"
            };
        }

        private static string GetPathVolumeKey(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) return null;
            string p = path.Trim();
            if (p.Length >= 2 && Char.IsLetter(p[0]) && p[1] == ':')
                return Char.ToUpperInvariant(p[0]) + ":";
            if (p.StartsWith("\\\\"))
            {
                string rest = p.Substring(2);
                string[] parts = rest.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2) return "\\\\" + parts[0] + "\\" + parts[1];
            }
            return null;
        }

        private static string NormalizeVolumeKey(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            string v = value.Trim();
            if (v.Length == 1 && Char.IsLetter(v[0])) return Char.ToUpperInvariant(v[0]) + ":";
            return GetPathVolumeKey(v) ?? v.TrimEnd('\\', '/');
        }

        private string BuildRestoreSql(BackupItem item, List<BackupFileEntry> files, string dataPath, string logPath)
        {
            var moves = new List<string>();
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int dataIndex = 0;
            int logIndex = 0;

            foreach (var f in files)
            {
                bool isLog = String.Equals(f.Type, "L", StringComparison.OrdinalIgnoreCase);
                string originalExt = Path.GetExtension(f.PhysicalName ?? "");
                string ext;
                if (isLog)
                {
                    logIndex++;
                    ext = String.IsNullOrWhiteSpace(originalExt) ? ".ldf" : originalExt;
                }
                else
                {
                    dataIndex++;
                    if (!String.IsNullOrWhiteSpace(originalExt)) ext = originalExt;
                    else ext = dataIndex == 1 ? ".mdf" : ".ndf";
                }

                string baseName = SafeFilePart(item.DatabaseName) + "_" + SafeFilePart(f.LogicalName);
                if (String.IsNullOrWhiteSpace(baseName)) baseName = "DatabaseFile";
                string fileName = baseName + ext;
                int suffix = 2;
                while (!usedNames.Add(fileName))
                {
                    fileName = baseName + "_" + suffix.ToString() + ext;
                    suffix++;
                }
                string target = (isLog ? logPath : dataPath) + fileName;
                moves.Add("MOVE " + SqlString(f.LogicalName) + " TO " + SqlString(target));
                Log("  MOVE " + f.LogicalName + " -> " + target);
            }

            var sb = new StringBuilder();
            sb.AppendLine("RESTORE DATABASE " + SqlIdentifier(item.DatabaseName));
            sb.AppendLine("FROM DISK = " + SqlString(item.FullPath));
            sb.AppendLine("WITH FILE = " + item.BackupPosition.ToString() + ",");
            for (int i = 0; i < moves.Count; i++)
                sb.AppendLine("    " + moves[i] + ",");
            sb.AppendLine("    REPLACE,");
            sb.AppendLine("    RECOVERY,");
            sb.AppendLine("    STATS = 5;");
            return sb.ToString();
        }

        private string BuildConnectionString()
        {
            string server = (txtServer.Text ?? "").Trim();
            if (String.IsNullOrWhiteSpace(server)) throw new InvalidOperationException("请输入 SQL Server 实例。例：.、localhost、SERVER\\INSTANCE");
            var b = new SqlConnectionStringBuilder();
            b.DataSource = server;
            b.InitialCatalog = "master";
            b.ConnectTimeout = 15;
            b.ApplicationName = "SQL Server BAK Batch Restore";
            b.TrustServerCertificate = true;
            if (cboAuth.SelectedIndex == 1)
            {
                if (String.IsNullOrWhiteSpace(txtUser.Text)) throw new InvalidOperationException("请输入 SQL Server 用户名。");
                b.IntegratedSecurity = false;
                b.UserID = txtUser.Text.Trim();
                b.Password = txtPassword.Text ?? "";
            }
            else
            {
                b.IntegratedSecurity = true;
            }
            return b.ConnectionString;
        }

        private bool ValidateInputs(bool requireFolder)
        {
            try
            {
                if (requireFolder)
                {
                    string folder = (txtBackupFolder.Text ?? "").Trim();
                    if (String.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                    {
                        MessageBox.Show(this, "请选择有效的 BAK 目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return false;
                    }
                }
                BuildConnectionString();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "参数错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        private void Grid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 0) return;
            var row = grid.Rows[e.RowIndex];
            var item = row.Tag as BackupItem;
            if (item == null) return;
            item.Selected = Convert.ToBoolean(row.Cells[0].Value ?? false);
            RefreshRestoreButtonState();
        }

        private void UpdateGridRow(BackupItem item)
        {
            Ui(delegate
            {
                if (item.GridRowIndex < 0 || item.GridRowIndex >= grid.Rows.Count) return;
                var row = grid.Rows[item.GridRowIndex];
                row.Cells[0].Value = item.Selected;
                row.Cells[1].Value = item.FileName;
                row.Cells[2].Value = item.DatabaseName;
                row.Cells[3].Value = FormatBytes(item.FileSize);
                row.Cells[4].Value = item.DataSize > 0 ? FormatBytes(item.DataSize) : "-";
                row.Cells[5].Value = item.LogSize > 0 ? FormatBytes(item.LogSize) : "-";
                row.Cells[6].Value = (item.DataSize + item.LogSize) > 0 ? FormatBytes(item.DataSize + item.LogSize) : "-";
                row.Cells[7].Value = String.IsNullOrWhiteSpace(item.TargetFreeText) ? "-" : item.TargetFreeText;
                row.Cells[8].Value = String.IsNullOrWhiteSpace(item.SpaceStatus) ? "-" : item.SpaceStatus;
                row.Cells[9].Value = item.DatabaseName == "读取失败" ? "-" : (item.Exists ? "是" : "否");
                row.Cells[10].Value = item.Status;
                row.Cells[11].Value = item.Progress > 0 || item.Status == "还原成功" ? item.Progress.ToString() + "%" : "-";

                row.DefaultCellStyle.BackColor = SystemColors.Window;
                if (item.SpaceEnough == false)
                {
                    row.DefaultCellStyle.ForeColor = Color.Firebrick;
                    row.DefaultCellStyle.BackColor = Color.MistyRose;
                }
                else if (item.Status != null && (item.Status.StartsWith("错误") || item.Status.StartsWith("还原失败")))
                {
                    row.DefaultCellStyle.ForeColor = Color.Firebrick;
                }
                else if (item.Status != null && item.Status.StartsWith("还原成功"))
                {
                    row.DefaultCellStyle.ForeColor = item.Status.Contains("失败") ? Color.DarkOrange : Color.DarkGreen;
                }
                else
                {
                    row.DefaultCellStyle.ForeColor = SystemColors.ControlText;
                }
            });
        }

        private bool HasRestorableSelection()
        {
            return items.Any(x => x.Selected && x.DatabaseName != "读取失败");
        }

        private void RefreshRestoreButtonState()
        {
            // btnScan.Enabled is false while the UI is busy.  Once the temporary
            // operation (test connection/read default path/etc.) finishes, keep
            // the existing scan result usable instead of forcing another scan.
            btnRestore.Enabled = btnScan.Enabled && HasRestorableSelection();
        }

        private void SetBusy(bool busy, string statusText)
        {
            btnBrowse.Enabled = !busy;
            txtBackupFolder.Enabled = !busy;
            txtServer.Enabled = !busy;
            cboAuth.Enabled = !busy;
            txtUser.Enabled = !busy && cboAuth.SelectedIndex == 1;
            txtPassword.Enabled = !busy && cboAuth.SelectedIndex == 1;
            chkSubfolders.Enabled = !busy;
            chkDropExisting.Enabled = !busy;
            txtDataPath.Enabled = !busy;
            txtLogPath.Enabled = !busy;
            btnDefaultDataPath.Enabled = !busy;
            btnDefaultLogPath.Enabled = !busy;
            chkShrinkLog.Enabled = !busy;
            if (numLogTargetMb != null) numLogTargetMb.Enabled = !busy && chkShrinkLog.Checked;
            if (lblLogTargetMb != null) lblLogTargetMb.Enabled = !busy && chkShrinkLog.Checked;
            btnTest.Enabled = !busy;
            btnScan.Enabled = !busy;
            if (busy)
                btnRestore.Enabled = false;
            else
                RefreshRestoreButtonState();
            lblStatus.Text = statusText;
        }

        private void Ui(Action action)
        {
            if (IsDisposed) return;
            if (InvokeRequired) BeginInvoke(action); else action();
        }

        private void Log(string message, bool error = false)
        {
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + (error ? "[!] " : "") + message;
            Ui(delegate
            {
                txtLog.AppendText(line + Environment.NewLine);
                txtLog.SelectionStart = txtLog.TextLength;
                txtLog.ScrollToCaret();
            });
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (btnCancel.Enabled)
            {
                var r = MessageBox.Show(this, "当前仍有任务正在执行。关闭窗口将尝试中止当前 SQL Server 操作。确定退出吗？", "确认退出", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
                cancelRequested = true;
                try
                {
                    lock (currentCommandLock)
                    {
                        if (currentRestoreCommand != null) currentRestoreCommand.Cancel();
                    }
                }
                catch { }
            }
        }

        private static string SqlString(string value)
        {
            return "N'" + (value ?? "").Replace("'", "''") + "'";
        }

        private static string SqlIdentifier(string value)
        {
            return "[" + (value ?? "").Replace("]", "]]" ) + "]";
        }

        private static string EnsureTrailingSlash(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) return path;
            if (path.EndsWith("\\") || path.EndsWith("/")) return path;
            return path + "\\";
        }

        private static string SafeFilePart(string s)
        {
            if (String.IsNullOrWhiteSpace(s)) return "file";
            var invalid = Path.GetInvalidFileNameChars();
            var b = new StringBuilder(s.Length);
            foreach (char c in s)
                b.Append(invalid.Contains(c) ? '_' : c);
            string result = b.ToString().Trim().TrimEnd('.');
            return String.IsNullOrWhiteSpace(result) ? "file" : result;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1024L * 1024L * 1024L) return (bytes / (1024d * 1024d * 1024d)).ToString("0.00") + " GB";
            if (bytes >= 1024L * 1024L) return (bytes / (1024d * 1024d)).ToString("0.00") + " MB";
            if (bytes >= 1024L) return (bytes / 1024d).ToString("0.00") + " KB";
            return bytes + " B";
        }

        private static string ShortError(string message)
        {
            if (String.IsNullOrWhiteSpace(message)) return "未知错误";
            string s = message.Replace("\r", " ").Replace("\n", " ").Trim();
            return s.Length > 120 ? s.Substring(0, 117) + "..." : s;
        }
    }
}
