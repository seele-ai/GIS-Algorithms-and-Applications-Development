using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace GIS.Display.UI
{
    /// <summary>
    /// 图层渲染设置（简单 / 唯一值 / 分级）。全程在渲染器克隆上编辑。
    /// “应用”不关闭窗体即写回图层；“确定”写回并关闭；“取消/还原”不改变原渲染。
    /// 唯一值/分级基于“默认符号”克隆生成（仅颜色变化），并避免 DataGridView 图片列触发 COM 可见性 MDA。
    /// </summary>
    public sealed class LayerRendererForm : Form
    {
        private readonly Layer layer;
        private SimpleRenderer simple = new SimpleRenderer();
        private UniqueValueRenderer unique = new UniqueValueRenderer();
        private ClassBreaksRenderer classBreaks = new ClassBreaksRenderer();
        private Renderer backup;

        private readonly Panel simplePreview = new Panel { Size = new Size(80, 40), BorderStyle = BorderStyle.FixedSingle };
        private readonly ComboBox uniqueField = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
        private readonly ComboBox classField = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
        private readonly ComboBox classCount = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70 };
        private readonly ComboBox classMethod = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
        private readonly Panel uniqueList = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        private readonly Panel classList = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        private readonly Panel uniqueDefault = new Panel { Size = new Size(52, 28), BorderStyle = BorderStyle.FixedSingle };
        private readonly Panel classDefault = new Panel { Size = new Size(52, 28), BorderStyle = BorderStyle.FixedSingle };
        private readonly ColorRampComboBox rampCombo = new ColorRampComboBox { Width = 224 };
        private readonly TabControl tabs = new TabControl { Dock = DockStyle.Fill };

        private bool suppressRamp;
        private ColorRamp currentRamp;

        /// <summary>“应用”时触发（不关闭窗体），供调用方刷新地图与图层面板。</summary>
        public event Action RendererApplied;

        private static readonly Color[] Palette =
        {
            Color.FromArgb(214, 69, 65), Color.FromArgb(61, 133, 198), Color.FromArgb(56, 160, 120),
            Color.FromArgb(230, 145, 56), Color.FromArgb(160, 90, 190), Color.FromArgb(100, 130, 60),
            Color.FromArgb(0, 150, 190), Color.FromArgb(210, 110, 160), Color.FromArgb(120, 120, 130),
            Color.FromArgb(180, 100, 60), Color.FromArgb(80, 90, 160), Color.FromArgb(140, 160, 80)
        };

        private LayerRendererForm(Layer layer)
        {
            this.layer = layer;
            Text = "图层渲染设置：" + layer.Name;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(640, 560);

            backup = layer.Renderer == null ? null : layer.Renderer.Clone();
            ResetFromLayer();

            tabs.TabPages.Add(BuildSimpleTab());
            tabs.TabPages.Add(BuildUniqueTab());
            tabs.TabPages.Add(BuildClassTab());

            var ok = new Button { Text = "确定", Width = 84 };
            ok.Click += (s, e) => { backup = ApplyToLayer().Clone(); DialogResult = DialogResult.OK; };
            var cancel = new Button { Text = "取消", Width = 84 };
            cancel.Click += (s, e) => { ResetFromLayer(); DialogResult = DialogResult.Cancel; };
            var restore = new Button { Text = "还原", Width = 84 };
            restore.Click += (s, e) => ResetFromLayer();
            var apply = new Button { Text = "应用", Width = 84 };
            apply.Click += (s, e) => { ApplyToLayer(); RendererApplied?.Invoke(); };
            var btns = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 40, Padding = new Padding(0, 6, 8, 0) };
            btns.Controls.Add(cancel); btns.Controls.Add(restore); btns.Controls.Add(apply); btns.Controls.Add(ok);
            var saveFile = new Button { Text = "保存渲染符号", AutoSize = true, Padding = new Padding(6, 2, 6, 2) };
            saveFile.Click += (s, e) => SaveRendererFile();
            var loadFile = new Button { Text = "读取渲染符号", AutoSize = true, Padding = new Padding(6, 2, 6, 2) };
            loadFile.Click += (s, e) => LoadRendererFile();
            btns.Controls.Add(loadFile);
            btns.Controls.Add(saveFile);

            Controls.Add(tabs);
            Controls.Add(btns);
            AcceptButton = ok; CancelButton = cancel;

            if (layer.Renderer is UniqueValueRenderer) tabs.SelectedIndex = 1;
            else if (layer.Renderer is ClassBreaksRenderer) tabs.SelectedIndex = 2;
            else tabs.SelectedIndex = 0;

            PopulateFields();
        }

        #region 渲染符号文件（保存 / 读取）

        private static string RendererTypeName(Renderer renderer)
        {
            if (renderer is UniqueValueRenderer) return "唯一值";
            if (renderer is ClassBreaksRenderer) return "分级";
            return "单一符号";
        }

        // 保存当前渲染器（含全部符号属性）为渲染符号文件，扩展名区分点/线/面
        private void SaveRendererFile()
        {
            GeometryTypeConstant geometryType = layer.FeatureClass.GeometryType;
            Renderer renderer = GetActiveRenderer(tabs.SelectedIndex);
            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = "保存渲染符号（" + RendererFile.GeometryName(geometryType) + "）";
                dialog.Filter = RendererFile.FilterFor(geometryType);
                dialog.DefaultExt = RendererFile.ExtensionFor(geometryType).TrimStart('.');
                dialog.AddExtension = true;
                dialog.FileName = layer.Name + "_" + RendererTypeName(renderer);
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    RendererFile.Save(dialog.FileName, renderer, geometryType);
                    MessageBox.Show(this, "已保存到：\r\n" + dialog.FileName, "渲染符号",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "保存失败：" + ex.Message, "渲染符号",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        // 读取渲染符号文件；若绑定字段在图层中不存在，符号置为不可见并在图层面板提示“绑定属性错误”
        private void LoadRendererFile()
        {
            GeometryTypeConstant geometryType = layer.FeatureClass.GeometryType;
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "读取渲染符号（" + RendererFile.GeometryName(geometryType) + "）";
                dialog.Filter = RendererFile.FilterFor(geometryType);
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                Renderer renderer;
                try
                {
                    renderer = RendererFile.Load(dialog.FileName, layer.FeatureClass);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "读取失败：" + ex.Message, "渲染符号",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (renderer.HasBindingError)
                    MessageBox.Show(this,
                        "文件中绑定的字段“" + renderer.BindingErrorField + "”在当前图层的属性表中不存在。\r\n" +
                        "符号已设为不可见（不会绘制到地图上），并在图层面板中以红色感叹号及“绑定属性错误”提示。\r\n" +
                        "请在渲染设置中重新绑定字段后再应用。",
                        "渲染符号", MessageBoxButtons.OK, MessageBoxIcon.Warning);

                if (renderer is UniqueValueRenderer ur)
                {
                    unique = ur;
                    SelectField(uniqueField, ur.Field);
                    tabs.SelectedIndex = 1;
                    ShowUnique();
                }
                else if (renderer is ClassBreaksRenderer cr)
                {
                    classBreaks = cr;
                    SelectField(classField, cr.Field);
                    if (classBreaks.BreakCount >= 2 && classBreaks.BreakCount <= 8) classCount.SelectedItem = classBreaks.BreakCount;
                    tabs.SelectedIndex = 2;
                    ShowClass();
                }
                else
                {
                    simple = renderer as SimpleRenderer ?? simple;
                    LoadSimple();
                    tabs.SelectedIndex = 0;
                }
            }
            ApplyToLayer();
            RendererApplied?.Invoke();
        }

        // 让字段下拉框选中指定字段；字段不存在时补入下拉框，保证导入的绑定信息不被覆盖
        private static void SelectField(ComboBox combo, string field)
        {
            if (string.IsNullOrEmpty(field)) return;
            if (!combo.Items.Contains(field)) combo.Items.Add(field);
            combo.SelectedItem = field;
        }

        #endregion

        private Symbol BaseSymbol()
        {
            return layer.Symbol != null ? layer.Symbol.Clone() : SymbolUI.DefaultFor(layer.FeatureClass.GeometryType);
        }

        // 从缓存的原渲染（backup）恢复三个工作渲染器，并刷新界面。
        private void ResetFromLayer()
        {
            simple = new SimpleRenderer();
            unique = new UniqueValueRenderer();
            classBreaks = new ClassBreaksRenderer();
            if (backup is SimpleRenderer sr) simple = (SimpleRenderer)sr.Clone();
            else simple.Symbol = BaseSymbol();
            if (backup is UniqueValueRenderer ur) unique = (UniqueValueRenderer)ur.Clone();
            else unique.DefaultSymbol = BaseSymbol();
            if (backup is ClassBreaksRenderer cr) classBreaks = (ClassBreaksRenderer)cr.Clone();
            else classBreaks.DefaultSymbol = BaseSymbol();
            // 还原图层的实际渲染（撤销之前的“应用”），并刷新地图与图层面板
            layer.Renderer = backup == null ? null : backup.Clone();
            LoadSimple();
            LoadUnique();
            LoadClass();
            RendererApplied?.Invoke();
        }

        private Renderer ApplyToLayer()
        {
            Renderer r = GetActiveRenderer(tabs.SelectedIndex);
            layer.Renderer = r;
            return r;
        }

        private TabPage BuildSimpleTab()
        {
            var page = new TabPage("简单渲染");
            simplePreview.Dock = DockStyle.Top;
            simplePreview.Paint += (s, e) => BasicGeometryDrawer.DrawSymbol(e.Graphics, simple.Symbol, simplePreview.ClientRectangle);
            simplePreview.DoubleClick += (s, e) => { var r = SymbolUI.EditSymbol(this, simple.Symbol); if (r != null) { simple.Symbol = r; simplePreview.Invalidate(); } };
            var hint = new Label { Text = "双击符号进行设置", Dock = DockStyle.Top, AutoSize = true };
            page.Controls.Add(simplePreview);
            page.Controls.Add(hint);
            return page;
        }

        private TabPage BuildUniqueTab()
        {
            var page = new TabPage("唯一值渲染");
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4) };
            top.Controls.Add(new Label { Text = "字段", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
            top.Controls.Add(uniqueField);
            var load = new Button { Text = "加载所有值" }; load.Click += (s, e) => LoadUniqueValues();
            var add = new Button { Text = "添加值" }; add.Click += (s, e) => AddUniqueValue();
            var remove = new Button { Text = "删除末项" }; remove.Click += (s, e) => { if (unique.ValueCount > 0) { unique.RemoveValueAt(unique.ValueCount - 1); ShowUnique(); } };
            top.Controls.Add(load); top.Controls.Add(add); top.Controls.Add(remove);

            var defRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(4) };
            defRow.Controls.Add(new Label { Text = "默认符号", AutoSize = true, Padding = new Padding(0, 4, 6, 0) });
            defRow.Controls.Add(uniqueDefault);
            uniqueDefault.Paint += (s, e) => BasicGeometryDrawer.DrawSymbol(e.Graphics, unique.DefaultSymbol, uniqueDefault.ClientRectangle);
            uniqueDefault.DoubleClick += (s, e) => { var r = SymbolUI.EditSymbol(this, unique.DefaultSymbol); if (r != null) { unique.DefaultSymbol = r; uniqueDefault.Invalidate(); } };

            page.Controls.Add(uniqueList);
            page.Controls.Add(defRow);
            page.Controls.Add(top);
            return page;
        }

        private TabPage BuildClassTab()
        {
            var page = new TabPage("分级渲染");
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4) };
            top.Controls.Add(new Label { Text = "字段", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
            top.Controls.Add(classField);
            top.Controls.Add(new Label { Text = "方法", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
            classMethod.Items.AddRange(new object[] { "等间距", "分位数", "自然间断点", "几何间隔" });
            classMethod.SelectedIndex = 0;
            top.Controls.Add(classMethod);
            top.Controls.Add(new Label { Text = "分级数", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
            for (int i = 2; i <= 8; i++) classCount.Items.Add(i);
            if (classBreaks.BreakCount >= 2 && classBreaks.BreakCount <= 8)
                classCount.SelectedItem = classBreaks.BreakCount;   // 读取当前分级数
            else
                classCount.SelectedIndex = 3;   // 默认 5 级
            top.Controls.Add(classCount);
            var gen = new Button { Text = "生成" }; gen.Click += (s, e) => GenerateClassBreaks();
            var rampC = new Button { Text = "颜色渐变" }; rampC.Click += (s, e) => RampClassColor();
            var rampS = new Button { Text = "尺寸渐变" }; rampS.Click += (s, e) => RampClassSize();
            top.Controls.Add(gen); top.Controls.Add(rampC); top.Controls.Add(rampS);

            // 右上角：色带下拉框（每一项用该色带自身的颜色渲染），最下方为“自定义色带…”
            rampCombo.SelectedIndexChanged += (s, e) => OnRampSelected();
            suppressRamp = true;
            if (rampCombo.Items.Count > 0) rampCombo.SelectedIndex = 0;
            suppressRamp = false;
            currentRamp = rampCombo.SelectedRamp;
            var rampRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top, Height = 34, FlowDirection = FlowDirection.RightToLeft, WrapContents = false,
                Padding = new Padding(4, 4, 6, 0)
            };
            rampRow.Controls.Add(rampCombo);
            rampRow.Controls.Add(new Label { Text = "色带", AutoSize = true, Padding = new Padding(0, 6, 6, 0) });

            var defRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(4) };
            defRow.Controls.Add(new Label { Text = "默认符号", AutoSize = true, Padding = new Padding(0, 4, 6, 0) });
            defRow.Controls.Add(classDefault);
            classDefault.Paint += (s, e) => BasicGeometryDrawer.DrawSymbol(e.Graphics, classBreaks.DefaultSymbol, classDefault.ClientRectangle);
            classDefault.DoubleClick += (s, e) => { var r = SymbolUI.EditSymbol(this, classBreaks.DefaultSymbol); if (r != null) { classBreaks.DefaultSymbol = r; classDefault.Invalidate(); } };

            page.Controls.Add(classList);
            page.Controls.Add(defRow);
            page.Controls.Add(top);
            page.Controls.Add(rampRow);
            return page;
        }

        #region 色带

        // 下拉框选择变化：选中内置色带则直接配色；选中“自定义色带…”则进入色带编辑器
        private void OnRampSelected()
        {
            if (suppressRamp) return;
            if (rampCombo.IsCustomSelected) { EditCustomRamp(); return; }
            ColorRamp ramp = rampCombo.SelectedRamp;
            if (ramp == null) return;
            ApplyRamp(ramp);
        }

        private void EditCustomRamp()
        {
            ColorRamp seed = currentRamp == null ? ColorRamp.CreateDefault() : currentRamp;
            ColorRamp result = ColorRampEditor.Edit(this, seed, ApplyRamp);
            if (result == null)   // 取消：恢复原来的选择
            {
                suppressRamp = true;
                if (currentRamp != null) rampCombo.SelectRamp(currentRamp);
                else if (rampCombo.Items.Count > 0) rampCombo.SelectedIndex = 0;
                suppressRamp = false;
                return;
            }
            rampCombo.AddRamp(result);
            suppressRamp = true;
            rampCombo.SelectRamp(result);
            suppressRamp = false;
            ApplyRamp(result);
        }

        private void ApplyRamp(ColorRamp ramp)
        {
            if (ramp == null) return;
            currentRamp = ramp;
            if (classBreaks.BreakCount == 0) return;
            classBreaks.ApplyColorRamp(ramp);
            // 符号面板的 Paint 会实时读取渲染器中的符号，这里只需重绘，不必重建控件（重建较慢）
            RefreshClassSymbols();
        }

        // 只重绘分级符号列表（不重建行控件），用于色带调整后的快速刷新
        private void RefreshClassSymbols()
        {
            classList.Invalidate(true);
            classList.Update();
            classDefault.Invalidate();
        }

        #endregion

        private void PopulateFields()
        {
            foreach (Field f in layer.FeatureClass.Fields)
            {
                uniqueField.Items.Add(f.Name);
                if (f.ValueType != FieldTypeConstant.Text && f.ValueType != FieldTypeConstant.Date && f.ValueType != FieldTypeConstant.Boolean)
                    classField.Items.Add(f.Name);
            }
            if (uniqueField.Items.Count > 0) uniqueField.SelectedIndex = 0;
            if (classField.Items.Count > 0) classField.SelectedIndex = 0;
        }

        private void LoadSimple() => simplePreview.Invalidate();

        private void LoadUnique()
        {
            uniqueField.SelectedItem = unique.Field;
            ShowUnique();
        }

        private void ShowUnique()
        {
            RebuildList(uniqueList, unique.ValueCount, i => unique.GetSymbol(i),
                i => LabelOf(unique.GetSymbol(i), unique.GetValue(i)), unique);
            uniqueDefault.Invalidate();
        }

        private void LoadClass() => ShowClass();

        private void ShowClass()
        {
            RebuildList(classList, classBreaks.BreakCount, i => classBreaks.GetSymbol(i),
                i => LabelOf(classBreaks.GetSymbol(i), RangeLabel(classBreaks, i)), classBreaks);
            classDefault.Invalidate();
        }

        // 自绘符号列表（避免 DataGridView 图片列），点击符号可单独编辑；外层 AutoScroll 支持上下滚动
        private void RebuildList(Panel panel, int count, Func<int, Symbol> getSymbol, Func<int, string> getLabel, Renderer renderer)
        {
            panel.Controls.Clear();
            var list = new FlowLayoutPanel
            {
                Location = new System.Drawing.Point(0, 0),
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };
            for (int i = 0; i < count; i++)
            {
                int index = i;
                var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Height = 28 };
                var sym = new Panel { Size = new Size(64, 26), BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Cursor = Cursors.Hand, Margin = new Padding(4, 1, 8, 0) };
                sym.Paint += (s, e) => { Symbol s2 = getSymbol(index); if (s2 != null) BasicGeometryDrawer.DrawSymbol(e.Graphics, s2, sym.ClientRectangle); };
                sym.Click += (s, e) => EditSymbol(renderer, index, getSymbol, getLabel, panel, count);
                var lbl = new Label { Text = getLabel(index), AutoSize = true, Margin = new Padding(0, 5, 0, 0) };
                row.Controls.Add(sym);
                row.Controls.Add(lbl);
                list.Controls.Add(row);
            }
            panel.Controls.Add(list);
        }

        private void EditSymbol(Renderer renderer, int index, Func<int, Symbol> getSymbol, Func<int, string> getLabel, Panel panel, int count)
        {
            var edited = SymbolUI.EditSymbol(this, getSymbol(index));
            if (edited != null)
            {
                if (renderer is UniqueValueRenderer u) u.SetSymbol(index, edited);
                else if (renderer is ClassBreaksRenderer c) c.SetSymbol(index, edited);
                RebuildList(panel, count, getSymbol, getLabel, renderer);
            }
        }

        private void LoadUniqueValues()
        {
            if (uniqueField.SelectedIndex < 0) return;
            string field = uniqueField.SelectedItem.ToString();
            var values = new List<string>();
            foreach (Feature f in layer.FeatureClass.Features)
            {
                object v = f.Attributes == null ? null : f.Attributes.GetItem(field);
                string s = v == null ? "" : v.ToString();
                if (!values.Contains(s)) values.Add(s);
            }
            unique.ClearValues();
            unique.ClearBindingError();   // 重新绑定字段后恢复为可见符号
            unique.Field = field;
            // 基于默认符号克隆，仅改变颜色
            for (int i = 0; i < values.Count; i++)
            {
                Symbol s = CloneSymbol(unique.DefaultSymbol);
                s.Label = values[i];
                SetSymbolColor(s, Palette[i % Palette.Length]);
                unique.AddValue(values[i], s);
            }
            ShowUnique();
        }

        private void AddUniqueValue()
        {
            if (uniqueField.SelectedIndex < 0) return;
            unique.Field = uniqueField.SelectedItem.ToString();
            string value = "新值" + (unique.ValueCount + 1);
            Symbol s = CloneSymbol(unique.DefaultSymbol);
            s.Label = value;
            SetSymbolColor(s, Palette[unique.ValueCount % Palette.Length]);
            unique.AddValue(value, s);
            ShowUnique();
        }

        private void GenerateClassBreaks()
        {
            if (classField.SelectedIndex < 0 || classCount.SelectedIndex < 0) return;
            string field = classField.SelectedItem.ToString();
            int count = (int)classCount.SelectedItem;
            var values = new List<double>();
            foreach (Feature f in layer.FeatureClass.Features)
            {
                object v = f.Attributes == null ? null : f.Attributes.GetItem(field);
                if (v != null) { double d; if (double.TryParse(v.ToString(), out d)) values.Add(d); }
            }
            if (values.Count == 0) { MessageBox.Show("该字段没有数值。"); return; }

            double[] breaks = ComputeBreaks(values, count, classMethod.SelectedIndex);
            if (breaks == null || breaks.Length == 0) { MessageBox.Show("无法生成分级。"); return; }

            classBreaks.ClearBreakValues();
            classBreaks.ClearBindingError();   // 重新绑定字段后恢复为可见符号
            classBreaks.Field = field;
            // 基于默认符号克隆，仅改变颜色
            for (int i = 0; i < breaks.Length; i++)
            {
                Symbol s = CloneSymbol(classBreaks.DefaultSymbol);
                s.Label = i == 0 ? "< " + Fmt(breaks[i]) : Fmt(breaks[i - 1]) + " - " + Fmt(breaks[i]);
                classBreaks.AddBreakValue(breaks[i], s);
            }
            // 颜色：按当前色带、依分级符号数量从左往右等间隔取色
            if (currentRamp != null)
            {
                classBreaks.ApplyColorRamp(currentRamp);
            }
            else
            {
                // 未选色带：颜色渐变由默认符号颜色的浅色过渡到深色
                Color baseColor = GetSymbolColor(classBreaks.DefaultSymbol);
                int last = breaks.Length - 1;
                SetSymbolColor(classBreaks.GetSymbol(0), Lighten(baseColor, 0.5f));
                SetSymbolColor(classBreaks.GetSymbol(last), Darken(baseColor, 0.35f));
                classBreaks.RampColor(GetSymbolColor(classBreaks.GetSymbol(0)), GetSymbolColor(classBreaks.GetSymbol(last)));
            }
            ShowClass();
        }

        private static string Fmt(double v) => v.ToString("0.##");

        /// <summary>按所选分级方法计算 count 个分割值（升序，最后一个为最大值）。供自检程序调用。</summary>
        public static double[] BuildClassBreaks(IEnumerable<double> values, int count, int method)
        {
            var list = new List<double>();
            foreach (double v in values) list.Add(v);
            return list.Count == 0 ? null : ComputeBreaks(list, count, method);
        }

        /// <summary>按所选分级方法计算 count 个分割值（升序，最后一个为最大值）。</summary>
        private static double[] ComputeBreaks(List<double> values, int count, int method)
        {
            if (count < 1) return null;
            double min = values.Min(), max = values.Max();
            if (max <= min)
            {
                // 全部要素取值相同（例如图层只有一个要素）时，围绕该值构造一个区间，
                // 仍然生成 count 个分级，使分级符号数量与色带取色数量一致。
                double span = Math.Max(Math.Abs(min) * 0.1, 1.0);
                double[] flat = EqualIntervalBreaks(min, min + span, count);
                EnsureAscending(flat);
                return flat;
            }
            double[] breaks;
            switch (method)
            {
                case 1: breaks = QuantileBreaks(values, count); break;
                case 2: breaks = NaturalBreaks(values, count); break;
                case 3: breaks = GeometricBreaks(min, max, count); break;
                default: breaks = EqualIntervalBreaks(min, max, count); break;
            }
            EnsureAscending(breaks);
            return breaks;
        }

        // 等间距
        private static double[] EqualIntervalBreaks(double min, double max, int count)
        {
            var result = new double[count];
            for (int i = 0; i < count; i++)
                result[i] = min + (max - min) * (i + 1) / count;
            result[count - 1] = max;
            return result;
        }

        // 分位数：每类要素数量大致相等
        private static double[] QuantileBreaks(List<double> values, int count)
        {
            double[] sorted = values.OrderBy(v => v).ToArray();
            int n = sorted.Length;
            var result = new double[count];
            for (int i = 0; i < count; i++)
            {
                int idx = (int)Math.Ceiling((double)n * (i + 1) / count) - 1;
                result[i] = sorted[Math.Max(0, Math.Min(n - 1, idx))];
            }
            result[count - 1] = sorted[n - 1];
            return result;
        }

        // 几何间隔（等比例）：按等比数列划分，适用于正偏数据
        private static double[] GeometricBreaks(double min, double max, int count)
        {
            if (min <= 0 || max <= min) return EqualIntervalBreaks(min, max, count);
            var result = new double[count];
            double factor = Math.Pow(max / min, 1.0 / count);
            for (int i = 0; i < count; i++)
                result[i] = min * Math.Pow(factor, i + 1);
            result[count - 1] = max;
            return result;
        }

        // 自然间断点：Fisher-Jenks 优化，使类内方差之和最小
        private static double[] NaturalBreaks(List<double> values, int count)
        {
            double[] sorted = values.OrderBy(v => v).ToArray();
            int n = sorted.Length;
            if (count >= n) return EqualIntervalBreaks(sorted[0], sorted[n - 1], count);   // 类数不少于要素数时退化为等间距

            double[,] lower = new double[n + 1, count + 1];     // 各类下界索引
            double[,] variance = new double[n + 1, count + 1];  // 类内方差组合
            for (int i = 1; i <= count; i++)
            {
                lower[1, i] = 1;
                variance[1, i] = 0;
                for (int j = 2; j <= n; j++) variance[j, i] = double.MaxValue;
            }
            for (int l = 1; l <= n; l++)
            {
                double sum = 0, sumSq = 0, w = 0, v = 0;
                for (int m = 1; m <= l; m++)
                {
                    int i3 = l - m + 1;
                    double val = sorted[i3 - 1];
                    sumSq += val * val;
                    sum += val;
                    w++;
                    v = sumSq - sum * sum / w;
                    int i4 = i3 - 1;
                    if (i4 != 0)
                    {
                        for (int j = 2; j <= count; j++)
                        {
                            if (variance[l, j] >= v + variance[i4, j - 1])
                            {
                                lower[l, j] = i3;
                                variance[l, j] = v + variance[i4, j - 1];
                            }
                        }
                    }
                }
                lower[l, 1] = 1;
                variance[l, 1] = v;
            }

            int[] kclass = new int[count + 2];
            kclass[count] = n;
            int kk = n;
            for (int j = count; j >= 2; j--)
            {
                int id = (int)lower[kk, j] - 1;
                kclass[j - 1] = id;
                kk = id;
            }
            var result = new double[count];
            for (int j = 1; j <= count; j++)
                result[j - 1] = sorted[Math.Max(0, Math.Min(n - 1, kclass[j] - 1))];
            result[count - 1] = sorted[n - 1];
            return result;
        }

        // 保证分割值严格递增，避免出现空类
        private static void EnsureAscending(double[] breaks)
        {
            for (int i = 1; i < breaks.Length; i++)
                if (breaks[i] <= breaks[i - 1])
                    breaks[i] = breaks[i - 1] + Math.Max(Math.Abs(breaks[i - 1]) * 1e-6, 1e-6);
        }

        private void RampClassColor()
        {
            if (classBreaks.BreakCount < 1) return;
            classBreaks.RampColor(GetSymbolColor(classBreaks.GetSymbol(0)), GetSymbolColor(classBreaks.GetSymbol(classBreaks.BreakCount - 1)));
            ShowClass();
        }

        private void RampClassSize()
        {
            if (classBreaks.BreakCount < 1) return;
            double a = GetSymbolSize(classBreaks.GetSymbol(0));
            double b = GetSymbolSize(classBreaks.GetSymbol(classBreaks.BreakCount - 1));
            if (a > 0 && b > 0) { classBreaks.RampSize(a, b); ShowClass(); }
        }

        private static Symbol CloneSymbol(Symbol s) => s == null ? null : s.Clone();

        private static void SetSymbolColor(Symbol s, Color c)
        {
            if (s is SimpleMarkerSymbol m) m.Color = c;
            else if (s is SimpleLineSymbol l) l.Color = c;
            else if (s is SimpleFillSymbol f) f.Color = c;
        }

        private static Color Lighten(Color c, float f)
        {
            return Color.FromArgb(c.A, (int)(c.R + (255 - c.R) * f), (int)(c.G + (255 - c.G) * f), (int)(c.B + (255 - c.B) * f));
        }

        private static Color Darken(Color c, float f)
        {
            return Color.FromArgb(c.A, (int)(c.R * (1 - f)), (int)(c.G * (1 - f)), (int)(c.B * (1 - f)));
        }

        private static string LabelOf(Symbol symbol, string fallback)
        {
            return symbol != null && !string.IsNullOrEmpty(symbol.Label) ? symbol.Label : fallback;
        }

        private static string RangeLabel(ClassBreaksRenderer cr, int i)
        {
            return i == 0 ? "< " + cr.GetBreakValue(i).ToString("0.##")
                : cr.GetBreakValue(i - 1).ToString("0.##") + " - " + cr.GetBreakValue(i).ToString("0.##");
        }

        private static Color GetSymbolColor(Symbol s)
        {
            if (s is SimpleMarkerSymbol) return ((SimpleMarkerSymbol)s).Color;
            if (s is SimpleLineSymbol) return ((SimpleLineSymbol)s).Color;
            if (s is SimpleFillSymbol) return ((SimpleFillSymbol)s).Color;
            return Color.Black;
        }

        private static double GetSymbolSize(Symbol s)
        {
            if (s is SimpleMarkerSymbol) return ((SimpleMarkerSymbol)s).Size;
            if (s is SimpleLineSymbol) return ((SimpleLineSymbol)s).Size;
            return 0;
        }

        private Renderer GetActiveRenderer(int tabIndex)
        {
            if (tabIndex == 1) { if (uniqueField.SelectedIndex >= 0) unique.Field = uniqueField.SelectedItem.ToString(); return unique.Clone(); }
            if (tabIndex == 2) { if (classField.SelectedIndex >= 0) classBreaks.Field = classField.SelectedItem.ToString(); return classBreaks.Clone(); }
            return simple.Clone();
        }

        public static bool Edit(IWin32Window owner, Layer layer, Action onApplied = null)
        {
            using (var f = new LayerRendererForm(layer))
            {
                if (onApplied != null) f.RendererApplied += onApplied;
                return f.ShowDialog(owner) == DialogResult.OK;
            }
        }
    }
}
