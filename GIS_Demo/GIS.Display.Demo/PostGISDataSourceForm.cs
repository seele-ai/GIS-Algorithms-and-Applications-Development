using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using GIS.PostGIS;

namespace GIS.Display.Demo
{
    /// <summary>
    /// 数据库数据源面板：连接 PostgreSQL/PostGIS 并选择要加入地图的空间表。
    /// 连接数据库的可视化 UserControl，作为主窗口右侧的可停靠子面板使用。
    /// </summary>
    public sealed partial class PostGISDataSourceForm : UserControl
    {
        private TextBox host;
        private NumericUpDown port;
        private TextBox database;
        private TextBox username;
        private TextBox password;
        private ListBox tables;
        private Button connect;
        private Button add;
        private Button close;
        private PostGISConnection connection;
        private string connectionSignature;
        private bool tableListLoaded;
        private Label loadStatus;
        private Button cancelLoad;

        public event EventHandler TableSelected;
        public event EventHandler CloseRequested;
        public event EventHandler CancelLoadRequested;

        public PostGISConnection Connection { get { return connection; } }
        public string SelectedSchema { get; private set; }
        public string SelectedTable { get; private set; }
        public PostGISDataSourceForm()
        {
            InitializeComponent();
            foreach (TextBox input in new[] { host, database, username, password })
            {
                input.AutoSize = false;
                input.MinimumSize = new Size(0, 30);
            }
            port.AutoSize = false;
            port.MinimumSize = new Size(0, 30);
            BuildLoadOptions();
            host.TextChanged += ConnectionSettingsChanged;
            port.ValueChanged += ConnectionSettingsChanged;
            database.TextChanged += ConnectionSettingsChanged;
            username.TextChanged += ConnectionSettingsChanged;
            password.TextChanged += ConnectionSettingsChanged;
            connect.Click += (s, e) => BrowseTables();
            add.Click += (s, e) => SelectTable();
            close.Click += (s, e) => { if (CloseRequested != null) CloseRequested(this, EventArgs.Empty); };
            tables.DoubleClick += (s, e) => SelectTable();
            GIS.Display.UI.DialogLayout.Apply(this);
        }

        private void BuildLoadOptions()
        {
            actions.SuspendLayout();
            actions.Controls.Clear();
            actions.ColumnCount = 2;
            actions.ColumnStyles.Clear();
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            actions.Height = 104;
            actions.RowCount = 2;
            actions.RowStyles.Clear();
            actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));

            connect.Dock = DockStyle.Fill;
            add.Dock = DockStyle.Fill;
            actions.Controls.Add(connect, 0, 0);
            actions.Controls.Add(add, 1, 0);

            loadStatus = new Label { Text = "", Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(6, 4, 4, 4) };
            actions.Controls.Add(loadStatus, 0, 1);
            var secondaryActions = new FlowLayoutPanel { Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = new Padding(0) };
            close.AutoSize = true;
            close.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            close.Dock = DockStyle.None;
            close.Margin = new Padding(4);
            cancelLoad = new Button { Text = "取消加载", Enabled = false, Visible = false,
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(4) };
            cancelLoad.Click += (s, e) => { if (CancelLoadRequested != null) CancelLoadRequested(this, EventArgs.Empty); };
            secondaryActions.Controls.Add(close);
            secondaryActions.Controls.Add(cancelLoad);
            actions.Controls.Add(secondaryActions, 1, 1);
            actions.ResumeLayout();
        }

        private void ConnectionSettingsChanged(object sender, EventArgs e)
        {
            if (connection == null || BuildConnectionSignature() != connectionSignature)
                connect.Text = "连接数据库";
        }

        public void BeginLayerLoading()
        {
            add.Enabled = connect.Enabled = tables.Enabled = false;
            cancelLoad.Enabled = true;
            cancelLoad.Visible = true;
            loadStatus.Text = "正在读取…";
        }

        public void ReportLayerLoading(int count)
        {
            loadStatus.Text = "已读取 " + count + " 个";
        }

        public void ShowLayerLoadingCancelled()
        {
            loadStatus.Text = "已取消";
        }

        public void EndLayerLoading()
        {
            connect.Enabled = tables.Enabled = true;
            add.Enabled = tables.Items.Count > 0;
            cancelLoad.Enabled = false;
            cancelLoad.Visible = false;
        }

        private void BrowseTables()
        {
            try
            {
                string signature = BuildConnectionSignature();
                bool sameConnection = connection != null && signature == connectionSignature;
                if (!sameConnection)
                {
                    connection = new PostGISConnection(host.Text.Trim(), (int)port.Value,
                        database.Text.Trim(), username.Text.Trim(), password.Text);
                    connectionSignature = signature;
                    tableListLoaded = false;
                }

                string version = sameConnection && tableListLoaded ? null : connection.TestConnection();
                ListBox.ObjectCollection items = tables.Items;
                items.Clear();
                items.AddRange(new PostGISImporter(connection).GetSpatialTables().Cast<object>().ToArray());
                add.Enabled = items.Count > 0;
                tableListLoaded = true;
                Text = version == null ? "PostGIS 数据源（已连接）" : "PostGIS 数据源（PostGIS " + version + "）";
                connect.Text = "刷新列表";
                if (items.Count > 0) tables.SelectedIndex = 0;
                MessageBox.Show(this, (sameConnection ? "已复用当前连接配置。" : "连接成功。") +
                    "\r\n共发现 " + items.Count + " 张空间表。", "PostGIS", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                add.Enabled = false;
                MessageBox.Show(this, ex.Message, "连接失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SelectTable()
        {
            if (connection == null || tables.SelectedItem == null) return;
            string qualified = Convert.ToString(tables.SelectedItem);
            int separator = qualified.IndexOf('.');
            if (separator <= 0 || separator == qualified.Length - 1)
            {
                MessageBox.Show(this, "空间表名称格式无效。", "PostGIS", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            SelectedSchema = qualified.Substring(0, separator);
            SelectedTable = qualified.Substring(separator + 1);
            if (TableSelected != null) TableSelected(this, EventArgs.Empty);
        }

        private string BuildConnectionSignature()
        {
            return host.Text.Trim() + "|" + port.Value + "|" + database.Text.Trim() + "|" +
                username.Text.Trim() + "|" + password.Text;
        }
    }
}
