namespace GIS.Display.Demo
{
    partial class PostGISDataSourceForm
    {
        private System.Windows.Forms.Label title;
        private System.Windows.Forms.TableLayoutPanel fields;
        private System.Windows.Forms.TableLayoutPanel actions;
        private System.Windows.Forms.Label hostLabel;
        private System.Windows.Forms.Label portLabel;
        private System.Windows.Forms.Label databaseLabel;
        private System.Windows.Forms.Label usernameLabel;
        private System.Windows.Forms.Label passwordLabel;

        private void InitializeComponent()
        {
            this.title = new System.Windows.Forms.Label();
            this.fields = new System.Windows.Forms.TableLayoutPanel();
            this.hostLabel = new System.Windows.Forms.Label();
            this.portLabel = new System.Windows.Forms.Label();
            this.databaseLabel = new System.Windows.Forms.Label();
            this.usernameLabel = new System.Windows.Forms.Label();
            this.passwordLabel = new System.Windows.Forms.Label();
            this.host = new System.Windows.Forms.TextBox();
            this.port = new System.Windows.Forms.NumericUpDown();
            this.database = new System.Windows.Forms.TextBox();
            this.username = new System.Windows.Forms.TextBox();
            this.password = new System.Windows.Forms.TextBox();
            this.tables = new System.Windows.Forms.ListBox();
            this.actions = new System.Windows.Forms.TableLayoutPanel();
            this.connect = new System.Windows.Forms.Button();
            this.add = new System.Windows.Forms.Button();
            this.close = new System.Windows.Forms.Button();
            this.fields.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.port)).BeginInit();
            this.actions.SuspendLayout();
            this.SuspendLayout();
            // 
            // title
            // 
            this.title.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(247)))), ((int)(((byte)(243)))), ((int)(((byte)(239)))));
            this.title.Dock = System.Windows.Forms.DockStyle.Top;
            this.title.Font = new System.Drawing.Font("Microsoft YaHei UI", 10F, System.Drawing.FontStyle.Bold);
            this.title.Location = new System.Drawing.Point(0, 0);
            this.title.Name = "title";
            this.title.Padding = new System.Windows.Forms.Padding(8, 0, 0, 0);
            this.title.Size = new System.Drawing.Size(1891, 32);
            this.title.TabIndex = 3;
            this.title.Text = "PostGIS 数据源";
            this.title.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // fields
            // 
            this.fields.ColumnCount = 2;
            this.fields.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 72F));
            this.fields.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.fields.Controls.Add(this.hostLabel, 0, 0);
            this.fields.Controls.Add(this.portLabel, 0, 1);
            this.fields.Controls.Add(this.databaseLabel, 0, 2);
            this.fields.Controls.Add(this.usernameLabel, 0, 3);
            this.fields.Controls.Add(this.passwordLabel, 0, 4);
            this.fields.Controls.Add(this.host, 1, 0);
            this.fields.Controls.Add(this.port, 1, 1);
            this.fields.Controls.Add(this.database, 1, 2);
            this.fields.Controls.Add(this.username, 1, 3);
            this.fields.Controls.Add(this.password, 1, 4);
            this.fields.Dock = System.Windows.Forms.DockStyle.Top;
            this.fields.Location = new System.Drawing.Point(0, 32);
            this.fields.Name = "fields";
            this.fields.Padding = new System.Windows.Forms.Padding(8, 6, 8, 4);
            this.fields.RowCount = 5;
            this.fields.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 27F));
            this.fields.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 27F));
            this.fields.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 27F));
            this.fields.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 27F));
            this.fields.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 27F));
            this.fields.Size = new System.Drawing.Size(1891, 150);
            this.fields.TabIndex = 2;
            // 
            // hostLabel
            // 
            this.hostLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.hostLabel.Location = new System.Drawing.Point(11, 6);
            this.hostLabel.Name = "hostLabel";
            this.hostLabel.Size = new System.Drawing.Size(66, 27);
            this.hostLabel.TabIndex = 0;
            this.hostLabel.Text = "地址";
            this.hostLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // portLabel
            // 
            this.portLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.portLabel.Location = new System.Drawing.Point(11, 33);
            this.portLabel.Name = "portLabel";
            this.portLabel.Size = new System.Drawing.Size(66, 27);
            this.portLabel.TabIndex = 1;
            this.portLabel.Text = "端口";
            this.portLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // databaseLabel
            // 
            this.databaseLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.databaseLabel.Location = new System.Drawing.Point(11, 60);
            this.databaseLabel.Name = "databaseLabel";
            this.databaseLabel.Size = new System.Drawing.Size(66, 27);
            this.databaseLabel.TabIndex = 2;
            this.databaseLabel.Text = "数据库";
            this.databaseLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // usernameLabel
            // 
            this.usernameLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.usernameLabel.Location = new System.Drawing.Point(11, 87);
            this.usernameLabel.Name = "usernameLabel";
            this.usernameLabel.Size = new System.Drawing.Size(66, 27);
            this.usernameLabel.TabIndex = 3;
            this.usernameLabel.Text = "用户名";
            this.usernameLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // passwordLabel
            // 
            this.passwordLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.passwordLabel.Location = new System.Drawing.Point(11, 114);
            this.passwordLabel.Name = "passwordLabel";
            this.passwordLabel.Size = new System.Drawing.Size(66, 32);
            this.passwordLabel.TabIndex = 4;
            this.passwordLabel.Text = "密码";
            this.passwordLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // host
            // 
            this.host.Dock = System.Windows.Forms.DockStyle.Fill;
            this.host.Location = new System.Drawing.Point(83, 9);
            this.host.Name = "host";
            this.host.Size = new System.Drawing.Size(1797, 28);
            this.host.TabIndex = 5;
            this.host.Text = "localhost";
            // 
            // port
            // 
            this.port.Dock = System.Windows.Forms.DockStyle.Fill;
            this.port.Location = new System.Drawing.Point(83, 36);
            this.port.Maximum = new decimal(new int[] {
            65535,
            0,
            0,
            0});
            this.port.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.port.Name = "port";
            this.port.Size = new System.Drawing.Size(1797, 28);
            this.port.TabIndex = 6;
            this.port.Value = new decimal(new int[] {
            5432,
            0,
            0,
            0});
            // 
            // database
            // 
            this.database.Dock = System.Windows.Forms.DockStyle.Fill;
            this.database.Location = new System.Drawing.Point(83, 63);
            this.database.Name = "database";
            this.database.Size = new System.Drawing.Size(1797, 28);
            this.database.TabIndex = 7;
            this.database.Text = "postgres";
            // 
            // username
            // 
            this.username.Dock = System.Windows.Forms.DockStyle.Fill;
            this.username.Location = new System.Drawing.Point(83, 90);
            this.username.Name = "username";
            this.username.Size = new System.Drawing.Size(1797, 28);
            this.username.TabIndex = 8;
            this.username.Text = "postgres";
            // 
            // password
            // 
            this.password.Dock = System.Windows.Forms.DockStyle.Fill;
            this.password.Location = new System.Drawing.Point(83, 117);
            this.password.Name = "password";
            this.password.Size = new System.Drawing.Size(1797, 28);
            this.password.TabIndex = 9;
            this.password.UseSystemPasswordChar = true;
            // 
            // tables
            // 
            this.tables.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tables.HorizontalScrollbar = true;
            this.tables.ItemHeight = 18;
            this.tables.Location = new System.Drawing.Point(0, 182);
            this.tables.Name = "tables";
            this.tables.Size = new System.Drawing.Size(1891, 498);
            this.tables.TabIndex = 0;
            // 
            // actions
            // 
            this.actions.ColumnCount = 3;
            this.actions.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 33.333F));
            this.actions.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 33.333F));
            this.actions.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 33.333F));
            this.actions.Controls.Add(this.connect, 0, 0);
            this.actions.Controls.Add(this.add, 1, 0);
            this.actions.Controls.Add(this.close, 2, 0);
            this.actions.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.actions.Location = new System.Drawing.Point(0, 680);
            this.actions.Name = "actions";
            this.actions.Padding = new System.Windows.Forms.Padding(6);
            this.actions.RowCount = 1;
            this.actions.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.actions.Size = new System.Drawing.Size(1891, 48);
            this.actions.TabIndex = 1;
            // 
            // connect
            // 
            this.connect.Dock = System.Windows.Forms.DockStyle.Fill;
            this.connect.Location = new System.Drawing.Point(8, 8);
            this.connect.Margin = new System.Windows.Forms.Padding(2);
            this.connect.Name = "connect";
            this.connect.Size = new System.Drawing.Size(622, 32);
            this.connect.TabIndex = 0;
            this.connect.Text = "连接 / 刷新";
            // 
            // add
            // 
            this.add.Dock = System.Windows.Forms.DockStyle.Fill;
            this.add.Enabled = false;
            this.add.Location = new System.Drawing.Point(634, 8);
            this.add.Margin = new System.Windows.Forms.Padding(2);
            this.add.Name = "add";
            this.add.Size = new System.Drawing.Size(622, 32);
            this.add.TabIndex = 1;
            this.add.Text = "添加图层";
            // 
            // close
            // 
            this.close.Dock = System.Windows.Forms.DockStyle.Fill;
            this.close.Location = new System.Drawing.Point(1260, 8);
            this.close.Margin = new System.Windows.Forms.Padding(2);
            this.close.Name = "close";
            this.close.Size = new System.Drawing.Size(623, 32);
            this.close.TabIndex = 2;
            this.close.Text = "关闭";
            // 
            // PostGISDataSourceForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 18F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1891, 728);
            this.Controls.Add(this.tables);
            this.Controls.Add(this.actions);
            this.Controls.Add(this.fields);
            this.Controls.Add(this.title);
            this.MinimumSize = new System.Drawing.Size(300, 360);
            this.Name = "PostGISDataSourceForm";
            this.Size = new System.Drawing.Size(380, 430);
            this.Dock = System.Windows.Forms.DockStyle.Fill;
            this.fields.ResumeLayout(false);
            this.fields.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.port)).EndInit();
            this.actions.ResumeLayout(false);
            this.ResumeLayout(false);

        }
    }
}
