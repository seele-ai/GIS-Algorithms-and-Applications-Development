using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace GIS.Display.UI
{
    /// <summary>
    /// 统一修正运行时创建的对话框控件尺寸，避免中文字体和高 DPI 下文字被上下裁切。
    /// </summary>
    public static class DialogLayout
    {
        public static void Apply(Control root)
        {
            if (root == null) return;
            Normalize(root);
            root.PerformLayout();
        }

        private static void Normalize(Control control)
        {
            foreach (Control child in control.Controls.Cast<Control>().ToArray())
                Normalize(child);

            if (control is Button button)
            {
                int height = Math.Max(34, button.Font.Height + 14);
                button.MinimumSize = new Size(button.MinimumSize.Width, height);
                if (button.Height < height) button.Height = height;
            }
            else if (control is CheckBox checkBox)
            {
                EnsureTextControlHeight(checkBox);
            }
            else if (control is RadioButton radioButton)
            {
                EnsureTextControlHeight(radioButton);
            }
            else if (control is Label label && !label.AutoSize)
            {
                int lines = Math.Max(1, (label.Text ?? "").Split('\n').Length);
                int height = lines * label.Font.Height + label.Padding.Vertical + 2;
                label.MinimumSize = new Size(label.MinimumSize.Width, height);
                if (label.Dock != DockStyle.Fill && label.Height < height) label.Height = height;
            }

            if (control is FlowLayoutPanel flow && !flow.AutoSize)
            {
                int contentHeight = flow.Controls.Cast<Control>()
                    .Select(child => child.Height + child.Margin.Vertical)
                    .DefaultIfEmpty(0)
                    .Max();
                int required = flow.Padding.Vertical + contentHeight;
                if (flow.Height < required) flow.Height = required;
            }

            if (control is TableLayoutPanel table)
                NormalizeAbsoluteRows(table);

            // 固定面板中常放置“列表 + 按钮行”；按钮行增高后，外层面板也必须同步增高。
            if (control is Panel panel && !(control is FlowLayoutPanel) && !(control is TableLayoutPanel) &&
                (panel.Dock == DockStyle.Top || panel.Dock == DockStyle.Bottom))
            {
                int required = panel.Controls.Cast<Control>()
                    .Select(child => child.Bottom + child.Margin.Bottom)
                    .DefaultIfEmpty(0)
                    .Max() + panel.Padding.Bottom;
                if (panel.Height < required) panel.Height = required;
            }
        }

        private static void EnsureTextControlHeight(Control control)
        {
            int height = control.Font.Height + 6;
            control.MinimumSize = new Size(control.MinimumSize.Width, height);
            if (control.Height < height) control.Height = height;
        }

        private static void NormalizeAbsoluteRows(TableLayoutPanel table)
        {
            int addedHeight = 0;
            for (int row = 0; row < table.RowCount && row < table.RowStyles.Count; row++)
            {
                RowStyle style = table.RowStyles[row];
                if (style.SizeType != SizeType.Absolute) continue;
                int required = table.Controls.Cast<Control>()
                    .Where(child => table.GetRow(child) == row && table.GetRowSpan(child) == 1)
                    .Select(child => Math.Max(child.MinimumSize.Height, child.PreferredSize.Height) + child.Margin.Vertical)
                    .DefaultIfEmpty(0)
                    .Max();
                if (required <= style.Height) continue;
                addedHeight += (int)Math.Ceiling(required - style.Height);
                style.Height = required;
            }
            if (addedHeight > 0 && !table.AutoSize) table.Height += addedHeight;
        }
    }
}
