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
        private NumericUpDown maximumFeatures;
        private Label loadStatus;
        private Button cancelLoad;

        public event EventHandler TableSelected;
        public event EventHandler CloseRequested;
        public event EventHandler CancelLoadRequested;

        public PostGISConnection Connection { get { return connection; } }
        public string SelectedSchema { get; private set; }
        public string SelectedTable { get; private set; }
        public int MaximumFeatureCount { get { return (int)maximumFeatures.Value; } }

        public PostGISDataSourceForm()
        {
            InitializeComponent();
            BuildLoadOptions();
            connect.Click += (s, e) => BrowseTables();
            add.Click += (s, e) => SelectTable();
            close.Click += (s, e) => { if (CloseRequested != null) CloseRequested(this, EventArgs.Empty); };
            tables.DoubleClick += (s, e) => SelectTable();
        }

        private void BuildLoadOptions()
        {
            actions.Height = 82;
            actions.RowCount = 2;
            actions.RowStyles.Clear();
            actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            var options = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            options.Controls.Add(new Label { Text = "最多加载", AutoSize = true, Margin = new Padding(2, 7, 2, 0) });
            maximumFeatures = new NumericUpDown
            {
                Minimum = 0,
                Maximum = 10000000,
                Increment = 10000,
                Value = 50000,
                Width = 85,
                ThousandsSeparator = true,
                Margin = new Padding(2, 3, 2, 0)
            };
            options.Controls.Add(maximumFeatures);
            options.Controls.Add(new Label { Text = "个（0=全部）", AutoSize = true, Margin = new Padding(0, 7, 6, 0) });
            loadStatus = new Label { Text = "", AutoSize = true, Margin = new Padding(2, 7, 2, 0) };
            options.Controls.Add(loadStatus);
            cancelLoad = new Button { Text = "取消", Enabled = false, Width = 56, Height = 25, Margin = new Padding(5, 1, 0, 0) };
            cancelLoad.Click += (s, e) => { if (CancelLoadRequested != null) CancelLoadRequested(this, EventArgs.Empty); };
            options.Controls.Add(cancelLoad);
            actions.Controls.Add(options, 0, 1);
            actions.SetColumnSpan(options, 3);
        }

        public void BeginLayerLoading()
        {
            add.Enabled = connect.Enabled = tables.Enabled = false;
            maximumFeatures.Enabled = false;
            cancelLoad.Enabled = true;
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
            connect.Enabled = tables.Enabled = maximumFeatures.Enabled = true;
            add.Enabled = tables.Items.Count > 0;
            cancelLoad.Enabled = false;
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
                connect.Text = sameConnection ? "刷新表列表" : "测试连接并浏览表";
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
