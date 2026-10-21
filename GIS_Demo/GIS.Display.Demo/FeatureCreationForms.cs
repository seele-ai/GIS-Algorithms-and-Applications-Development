using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace GIS.Display.Demo
{
    public sealed class CreateLayerForm : Form
    {
        private readonly TextBox name = new TextBox { Text = "新建图层", Left = 85, Top = 18, Width = 190 };
        private readonly ComboBox type = new ComboBox { Left = 345, Top = 18, Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly DataGridView fields = new DataGridView { Dock = DockStyle.Fill, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AllowUserToAddRows = true, AllowUserToDeleteRows = true, RowHeadersWidth = 30 };
        public Layer CreatedLayer { get; private set; }
        public CreateLayerForm()
        {
            Text = "新建要素图层"; Font = new Font("Microsoft YaHei UI", 9); ClientSize = new Size(550, 360); MinimumSize = Size; StartPosition = FormStartPosition.CenterParent;
            var top = new Panel { Dock = DockStyle.Top, Height = 85 };
            top.Controls.Add(new Label { Text = "图层名称", Left = 15, Top = 22, AutoSize = true }); top.Controls.Add(name);
            top.Controls.Add(new Label { Text = "几何类型", Left = 280, Top = 22, AutoSize = true }); top.Controls.Add(type);
            type.Items.AddRange(new object[] { "点", "线", "面", "多点", "复合线", "复合面" }); type.SelectedIndex = 0;
            top.Controls.Add(new Label { Text = "属性字段（可增删行）；新图层使用 WGS84，完成后通过“保存编辑”保存。", Left = 15, Top = 58, Width = 520 });
            fields.Columns.Add("name", "字段名");
            var types = new DataGridViewComboBoxColumn { Name = "type", HeaderText = "类型" }; types.Items.AddRange(Enum.GetNames(typeof(FieldTypeConstant))); fields.Columns.Add(types);
            fields.Rows.Add("名称", "Text");
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 45, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(5) };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel }; var ok = new Button { Text = "创建图层", AutoSize = true };
            ok.Click += (s,e) => {
                try
                {
                    fields.EndEdit();
                    var schema = new Fields();
                    foreach (DataGridViewRow row in fields.Rows)
                    {
                        if (row.IsNewRow) continue;
                        string fieldName = Convert.ToString(row.Cells[0].Value).Trim(), fieldType = Convert.ToString(row.Cells[1].Value);
                        if (fieldName.Length == 0 || schema.Any(f => string.Equals(f.Name, fieldName, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("字段名不能为空或重复。");
                        FieldTypeConstant valueType;
                        if (!Enum.TryParse(fieldType, out valueType)) throw new ArgumentException("请为每个字段选择类型。");
                        schema.Add(new Field(fieldName, valueType));
                    }
                    if (string.IsNullOrWhiteSpace(name.Text)) throw new ArgumentException("请输入图层名称。");
                    var fc = new FeatureClass(name.Text.Trim(), (GeometryTypeConstant)type.SelectedIndex);
                    foreach (Field field in schema) fc.Fields.Add(field);
                    CreatedLayer = new Layer(fc); CreatedLayer.CaptureGeographic(null); DialogResult = DialogResult.OK;
                }
                catch (ArgumentException ex) { MessageBox.Show(this, ex.Message, "无法创建图层", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            };
            bottom.Controls.Add(cancel); bottom.Controls.Add(ok); Controls.Add(fields); Controls.Add(top); Controls.Add(bottom); AcceptButton = ok; CancelButton = cancel;
            GIS.Display.UI.DialogLayout.Apply(this);
        }
    }
    public sealed class NewFeatureAttributesForm : Form
    {
        private readonly Fields schema;
        private readonly DataGridView grid = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = false };
        public Attributes Values { get; private set; }
        public NewFeatureAttributesForm(Fields fields, Attributes initial = null)
        {
            schema = fields; Text = initial == null ? "新要素属性" : "编辑要素属性"; Font = new Font("Microsoft YaHei UI", 9); ClientSize = new Size(520, 340); MinimumSize = Size; StartPosition = FormStartPosition.CenterParent;
            grid.Columns.Add("field", "字段"); grid.Columns.Add("type", "类型"); grid.Columns.Add("value", "值（留空为 NULL）");
            grid.Columns[0].ReadOnly = grid.Columns[1].ReadOnly = true;
            var originals = fields.Select((f,i) => initial == null ? "" : initial.GetItem(i) is DateTime date ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : Convert.ToString(initial.GetItem(i), CultureInfo.InvariantCulture)).ToArray();
            for (int i = 0; i < fields.Count; i++) grid.Rows.Add(fields[i].Name, fields[i].ValueType.ToString(), originals[i]);
            var hint = new Label { Dock = DockStyle.Top, Height = 40, Padding = new Padding(8), Text = "日期：yyyy-MM-dd；布尔：true/false；小数使用英文小数点。" };
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 45, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(5) };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel }; var ok = new Button { Text = initial == null ? "创建要素" : "保存修改", AutoSize = true };
            ok.Click += (s,e) => {
                grid.EndEdit(); var values = new object[schema.Count];
                for (int i = 0; i < schema.Count; i++)
                    try { string text = Convert.ToString(grid.Rows[i].Cells[2].Value); values[i] = initial != null && text == originals[i] ? initial.GetItem(i) : ParseValue(text, schema[i].ValueType); }
                    catch (Exception ex) when (ex is FormatException || ex is OverflowException)
                    { MessageBox.Show(this, "字段“" + schema[i].Name + "”：" + ex.Message, "属性值无效", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                Values = new Attributes(schema, values); DialogResult = DialogResult.OK;
            };
            bottom.Controls.Add(cancel); bottom.Controls.Add(ok); Controls.Add(grid); Controls.Add(hint); Controls.Add(bottom); AcceptButton = ok; CancelButton = cancel;
            GIS.Display.UI.DialogLayout.Apply(this);
        }
        public static object ParseValue(string text, FieldTypeConstant type)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var culture = CultureInfo.InvariantCulture;
            switch (type)
            {
                case FieldTypeConstant.Text: return text;
                case FieldTypeConstant.Byte: return byte.Parse(text, NumberStyles.Integer, culture);
                case FieldTypeConstant.Int16: return short.Parse(text, NumberStyles.Integer, culture);
                case FieldTypeConstant.Int32: return int.Parse(text, NumberStyles.Integer, culture);
                case FieldTypeConstant.Single:
                    float f = float.Parse(text, NumberStyles.Float, culture); if (float.IsInfinity(f) || float.IsNaN(f)) throw new FormatException("必须为有限数值。"); return f;
                case FieldTypeConstant.Double:
                    double d = double.Parse(text, NumberStyles.Float, culture); if (double.IsInfinity(d) || double.IsNaN(d)) throw new FormatException("必须为有限数值。"); return d;
                case FieldTypeConstant.Date: return DateTime.ParseExact(text.Trim(), "yyyy-MM-dd", culture);
                case FieldTypeConstant.Boolean: return bool.Parse(text.Trim());
                default: throw new FormatException("不支持的字段类型。");
            }
        }
    }
}
