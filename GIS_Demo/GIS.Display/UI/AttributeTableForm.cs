using System;
using System.Drawing;
using System.Windows.Forms;

namespace GIS.Display.UI
{
    /// <summary>
    /// 图层属性表：以表格显示图层的字段与要素属性；双击行可定位到对应要素。
    /// </summary>
    public sealed class AttributeTableForm : Form
    {
        private readonly Layer layer;
        private readonly DataGridView grid = new DataGridView
        {
            Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
            RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, BackgroundColor = Color.White
        };

        /// <summary>双击某行要素时触发。</summary>
        public event Action<Feature> FeatureSelected;

        public AttributeTableForm(Layer layer)
        {
            this.layer = layer;
            Text = "属性表：" + layer.Name;
            ClientSize = new Size(680, 420);
            MinimumSize = new Size(480, 300);
            StartPosition = FormStartPosition.CenterParent;

            foreach (Field f in layer.FeatureClass.Fields)
                grid.Columns.Add(new DataGridViewTextBoxColumn
                {
                    HeaderText = string.IsNullOrEmpty(f.Alias) ? f.Name : f.Alias,
                    Name = f.Name
                });
            foreach (Feature feature in layer.FeatureClass.Features)
            {
                var values = new object[layer.FeatureClass.Fields.Count];
                for (int i = 0; i < values.Length; i++)
                    values[i] = feature.Attributes == null ? null : feature.Attributes.GetItem(i);
                grid.Rows.Add(values);
            }
            grid.CellDoubleClick += (s, e) => {
                if (e.RowIndex >= 0 && e.RowIndex < layer.FeatureClass.Features.Count)
                    FeatureSelected?.Invoke(layer.FeatureClass.Features[e.RowIndex]);
            };
            Controls.Add(grid);
        }
    }
}
