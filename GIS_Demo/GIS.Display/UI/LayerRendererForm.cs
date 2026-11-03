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
        private ContinuousRenderer continuous = new ContinuousRenderer();
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

        // 连续渲染页
        private readonly ComboBox continuousField = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
        private readonly Panel continuousDefault = new Panel { Size = new Size(52, 28), BorderStyle = BorderStyle.FixedSingle };
        private readonly ColorRampComboBox continuousRampCombo = new ColorRampComboBox { Width = 200 };
        private readonly RampStrip continuousStrip = new RampStrip { Dock = DockStyle.Fill, Height = RampStrip.ContinuousHeight };
        private readonly Label continuousInfo = new Label { Dock = DockStyle.Bottom, Height = 22, Padding = new Padding(4, 2, 0, 0) };
        private bool suppressContinuousRamp;

        private bool suppressRamp;
        private ColorRamp currentRamp;
        private int ActionButtonHeight => Math.Max(36, Font.Height + 16);

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
            Font = new Font("Microsoft YaHei UI", 10F);
            ClientSize = new Size(800, 600);

            backup = layer.Renderer == null ? null : layer.Renderer.Clone();
            ResetFromLayer();

            tabs.TabPages.Add(BuildSimpleTab());
            tabs.TabPages.Add(BuildUniqueTab());
            tabs.TabPages.Add(BuildClassTab());
            tabs.TabPages.Add(BuildContinuousTab());

            int buttonHeight = ActionButtonHeight;
            var ok = CreateActionButton("确定", () => { backup = ApplyToLayer().Clone(); DialogResult = DialogResult.OK; }, 90);
            var cancel = CreateActionButton("取消", () => { ResetFromLayer(); DialogResult = DialogResult.Cancel; }, 90);
            var restore = CreateActionButton("还原", ResetFromLayer, 90);
            var apply = CreateActionButton("应用", () => { ApplyToLayer(); RendererApplied?.Invoke(); }, 90);
            int fileButtonWidth = Math.Max(144, TextRenderer.MeasureText("保存渲染符号", Font).Width + 24);
            var saveFile = CreateActionButton("保存渲染符号", SaveRendererFile, fileButtonWidth);
            var loadFile = CreateActionButton("读取渲染符号", LoadRendererFile, fileButtonWidth);
            var footer = new Panel { Dock = DockStyle.Bottom, Height = buttonHeight + 12 };
            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Right, Width = 4 * (90 + 6) + 8,
                FlowDirection = FlowDirection.RightToLeft, WrapContents = false,
                Padding = new Padding(0, 3, 8, 0)
            };
            actions.Controls.Add(cancel); actions.Controls.Add(restore);
            actions.Controls.Add(apply); actions.Controls.Add(ok);
            var files = new FlowLayoutPanel
            {
                Dock = DockStyle.Left, Width = 2 * (fileButtonWidth + 6) + 8,
                FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
                Padding = new Padding(8, 3, 0, 0)
            };
            files.Controls.Add(loadFile); files.Controls.Add(saveFile);
            footer.Controls.Add(actions);
            footer.Controls.Add(files);

            Controls.Add(tabs);
            Controls.Add(footer);
            AcceptButton = ok; CancelButton = cancel;

            if (layer.Renderer is UniqueValueRenderer) tabs.SelectedIndex = 1;
            else if (layer.Renderer is ClassBreaksRenderer) tabs.SelectedIndex = 2;
            else if (layer.Renderer is ContinuousRenderer) tabs.SelectedIndex = 3;
            else tabs.SelectedIndex = 0;

            PopulateFields();
            tabs.SelectedIndexChanged += (s, e) => { if (tabs.SelectedIndex == 3) ShowContinuous(); };
            ShowContinuous();
            DialogLayout.Apply(this);
        }

        private Button CreateActionButton(string text, Action click, int minimumWidth = 0)
        {
            int width = Math.Max(minimumWidth, TextRenderer.MeasureText(text, Font).Width + 24);
            var button = new Button
            {
                Text = text,
                Size = new Size(width, ActionButtonHeight),
                Margin = new Padding(3)
            };
            button.Click += (s, e) => click();
            return button;
        }

        private Label CreateToolbarLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Margin = new Padding(3, 3 + Math.Max(0, (ActionButtonHeight - Font.Height) / 2), 4, 3)
            };
        }

        private void AlignToolbarControl(Control control)
        {
            control.Font = Font;
            control.Margin = new Padding(3, 3 +
                Math.Max(0, (ActionButtonHeight - control.PreferredSize.Height) / 2), 3, 3);
        }

        #region 渲染符号文件（保存 / 读取）

        private static string RendererTypeName(Renderer renderer)
        {
            if (renderer is UniqueValueRenderer) return "唯一值";
            if (renderer is ClassBreaksRenderer) return "分级";
            if (renderer is ContinuousRenderer) return "连续";
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
                    EnsureDefaultSymbols();
                    tabs.SelectedIndex = 1;
                    ShowUnique();
                }
                else if (renderer is ClassBreaksRenderer cr)
                {
                    classBreaks = cr;
                    SelectField(classField, cr.Field);
                    EnsureDefaultSymbols();
                    if (classBreaks.BreakCount >= 2 && classBreaks.BreakCount <= 8) classCount.SelectedItem = classBreaks.BreakCount;
                    tabs.SelectedIndex = 2;
                    ShowClass();
                }
                else if (renderer is ContinuousRenderer cont)
                {
                    continuous = cont;
                    SelectField(continuousField, cont.Field);
                    continuousStrip.Context = null;   // 让 ShowContinuous 按文件里的字段重建上下文
                    EnsureDefaultSymbols();
                    SelectContinuousRampCombo(cont.Ramp);
                    tabs.SelectedIndex = 3;
                    ShowContinuous();
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

        /// <summary>
        /// 保证唯一值/分级渲染器都有一个可用的默认符号。
        /// 旧版渲染符号文件、或处于“绑定属性错误”状态的渲染器可能没有默认符号（为 null），
        /// 此时默认符号预览是空白、双击无法设置，点“生成/加载所有值”还会因为克隆 null 而抛
        /// NullReferenceException。这里统一补一个基于图层当前符号的默认符号。
        /// </summary>
        private void EnsureDefaultSymbols()
        {
            if (unique.DefaultSymbol == null) unique.DefaultSymbol = BaseSymbol();
            if (classBreaks.DefaultSymbol == null) classBreaks.DefaultSymbol = BaseSymbol();
            // 连续渲染：模板符号（默认符号）与色带都要有，否则画不出要素
            if (continuous.Symbol == null) continuous.Symbol = BaseSymbol();
            if (continuous.Ramp == null) continuous.Ramp = currentRamp != null ? currentRamp.Clone() : ColorRamp.CreateDefault();
        }

        // 从缓存的原渲染（backup）恢复四个工作渲染器，并刷新界面。
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
            continuous = new ContinuousRenderer();
            if (backup is ContinuousRenderer cont) continuous = (ContinuousRenderer)cont.Clone();
            else continuous.Symbol = BaseSymbol();
            EnsureDefaultSymbols();
            // 还原图层的实际渲染（撤销之前的“应用”），并刷新地图与图层面板
            layer.Renderer = backup == null ? null : backup.Clone();
            LoadSimple();
            LoadUnique();
            LoadClass();
            ShowContinuous();
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
            var top = new FlowLayoutPanel
            {
                Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true, Padding = new Padding(4), MinimumSize = new Size(0, ActionButtonHeight + 8)
            };
            top.Controls.Add(CreateToolbarLabel("字段"));
            AlignToolbarControl(uniqueField);
            top.Controls.Add(uniqueField);
            var load = CreateActionButton("加载所有值", LoadUniqueValues);
            var add = CreateActionButton("添加值", AddUniqueValue);
            var remove = CreateActionButton("删除末项", () =>
            {
                if (unique.ValueCount > 0) { unique.RemoveValueAt(unique.ValueCount - 1); ShowUnique(); }
            });
            top.Controls.Add(load); top.Controls.Add(add); top.Controls.Add(remove);

            var defRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(4) };
            defRow.Controls.Add(new Label { Text = "默认符号", AutoSize = true, Padding = new Padding(0, 4, 6, 0) });
            defRow.Controls.Add(uniqueDefault);
            uniqueDefault.Paint += (s, e) => BasicGeometryDrawer.DrawSymbol(e.Graphics, unique.DefaultSymbol, uniqueDefault.ClientRectangle);
            uniqueDefault.DoubleClick += (s, e) => EditDefaultSymbol(() => unique.DefaultSymbol, v => unique.DefaultSymbol = v, uniqueDefault);

            page.Controls.Add(uniqueList);
            page.Controls.Add(defRow);
            page.Controls.Add(top);
            return page;
        }

        private TabPage BuildClassTab()
        {
            var page = new TabPage("分级渲染");
            var top = new FlowLayoutPanel
            {
                Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true, Padding = new Padding(4), MinimumSize = new Size(0, ActionButtonHeight + 8)
            };
            top.Controls.Add(CreateToolbarLabel("字段"));
            AlignToolbarControl(classField);
            top.Controls.Add(classField);
            top.Controls.Add(CreateToolbarLabel("方法"));
            classMethod.Items.AddRange(new object[] { "等间距", "分位数", "自然间断点", "几何间隔" });
            classMethod.SelectedIndex = 0;
            AlignToolbarControl(classMethod);
            top.Controls.Add(classMethod);
            top.Controls.Add(CreateToolbarLabel("分级数"));
            for (int i = 2; i <= 8; i++) classCount.Items.Add(i);
            if (classBreaks.BreakCount >= 2 && classBreaks.BreakCount <= 8)
                classCount.SelectedItem = classBreaks.BreakCount;   // 读取当前分级数
            else
                classCount.SelectedIndex = 3;   // 默认 5 级
            AlignToolbarControl(classCount);
            top.Controls.Add(classCount);
            var gen = CreateActionButton("生成", GenerateClassBreaks);
            var rampC = CreateActionButton("颜色渐变", RampClassColor);
            var rampS = CreateActionButton("尺寸渐变", RampClassSize);
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
            classDefault.DoubleClick += (s, e) => EditDefaultSymbol(() => classBreaks.DefaultSymbol, v => classBreaks.DefaultSymbol = v, classDefault);

            page.Controls.Add(classList);
            page.Controls.Add(defRow);
            page.Controls.Add(top);
            page.Controls.Add(rampRow);
            return page;
        }

        #region 连续渲染

        /// <summary>
        /// 连续渲染页：最上方是字段选择（只列数值型字段）与色带；
        /// 中间是 KDE 数值分布（带 X/Y 轴、特殊值虚线、可拖上去绑定的色带标签）+ 色带；
        /// 色带的节点编辑（新增/删除/均匀分布/位置/颜色/插值/存取文件）直接集成在本页，不再另开窗口；
        /// 左下角是默认符号（模板符号，只换颜色）；底部按钮与其他渲染方式共用同一行。
        /// </summary>
        private TabPage BuildContinuousTab()
        {
            var page = new TabPage("连续渲染");

            var top = new FlowLayoutPanel
            {
                Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true, Padding = new Padding(4), MinimumSize = new Size(0, ActionButtonHeight + 8)
            };
            top.Controls.Add(CreateToolbarLabel("字段"));
            AlignToolbarControl(continuousField);
            continuousField.SelectedIndexChanged += (s, e) => OnContinuousFieldChanged();
            top.Controls.Add(continuousField);
            top.Controls.Add(CreateActionButton("均匀分布节点", () =>
            {
                if (continuous.Ramp == null) return;
                continuous.Ramp.DistributeEvenlyRespectingBindings();
                RefreshContinuousRamp();
            }));
            top.Controls.Add(CreateActionButton("清除全部绑定", () =>
            {
                if (continuous.Ramp == null) return;
                continuous.Ramp.ClearBindings();
                RefreshContinuousRamp();
            }));

            // 色带下拉框：每一项用该色带自身的颜色渲染
            continuousRampCombo.SelectedIndexChanged += (s, e) => OnContinuousRampSelected();
            suppressContinuousRamp = true;
            if (continuousRampCombo.Items.Count > 0) continuousRampCombo.SelectedIndex = 0;
            suppressContinuousRamp = false;
            var rampRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top, Height = 34, FlowDirection = FlowDirection.RightToLeft, WrapContents = false,
                Padding = new Padding(4, 4, 6, 0)
            };
            rampRow.Controls.Add(continuousRampCombo);
            rampRow.Controls.Add(new Label { Text = "色带", AutoSize = true, Padding = new Padding(0, 6, 6, 0) });

            // 色带节点编辑（原来在“编辑色带…”窗口里的功能，现直接放在本页）
            var editRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true, Padding = new Padding(4, 0, 4, 2)
            };
            editRow.Controls.Add(CreateActionButton("新增节点", AddContinuousNode));
            editRow.Controls.Add(CreateActionButton("删除节点", DeleteContinuousNode));
            editRow.Controls.Add(CreateActionButton("设置位置", SetContinuousNodePosition));
            editRow.Controls.Add(CreateActionButton("设置颜色", SetContinuousNodeColor));
            editRow.Controls.Add(CreateActionButton("设置插值", SetContinuousNodeInterpolation));
            editRow.Controls.Add(CreateActionButton("保存色带", SaveContinuousRamp));
            editRow.Controls.Add(CreateActionButton("读取色带", LoadContinuousRamp));
            editRow.Controls.Add(CreateActionButton("内置色带", LoadBuiltInContinuousRamp));

            var defRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(4) };
            defRow.Controls.Add(new Label { Text = "默认符号", AutoSize = true, Padding = new Padding(0, 4, 6, 0) });
            defRow.Controls.Add(continuousDefault);
            continuousDefault.Paint += (s, e) => BasicGeometryDrawer.DrawSymbol(e.Graphics, continuous.Symbol, continuousDefault.ClientRectangle);
            continuousDefault.DoubleClick += (s, e) => EditDefaultSymbol(() => continuous.Symbol, v => continuous.Symbol = v, continuousDefault);
            continuousInfo.Text = "选择数值型字段后，上方显示数值分布，下方色带按数值取色。";

            page.Controls.Add(continuousStrip);
            page.Controls.Add(continuousInfo);
            page.Controls.Add(defRow);
            page.Controls.Add(editRow);
            page.Controls.Add(rampRow);
            page.Controls.Add(top);
            return page;
        }

        #region 连续渲染：色带节点编辑（集成在页面里）

        private void AddContinuousNode()
        {
            ColorRamp ramp = continuous.Ramp;
            if (ramp == null) return;
            ColorRampNode sel = continuousStrip.SelectedNode;
            double position;
            RampInterpolation interpolation = RampInterpolation.Rgb;
            if (ramp.Count == 0) position = 0.5;
            else if (sel == null) position = ramp.Tail.Position >= 1 ? 0.5 : (ramp.Tail.Position + 1) / 2;
            else if (sel.Next == null)
                position = sel.Position >= 1
                    ? (sel.Previous == null ? 0.5 : (sel.Previous.Position + sel.Position) / 2)
                    : (sel.Position + 1) / 2;
            else position = (sel.Position + sel.Next.Position) / 2;
            if (sel != null && sel.Next != null) interpolation = sel.Next.Interpolation;
            ColorRampNode node = ramp.InsertNode(position, ramp.GetColor(position), interpolation);
            continuousStrip.SelectedNode = node;
            RefreshContinuousRamp();
        }

        private void DeleteContinuousNode()
        {
            ColorRamp ramp = continuous.Ramp;
            if (ramp == null) return;
            if (ramp.Count <= 2)
            {
                MessageBox.Show(this, "色带至少需要保留 2 个节点。", "连续渲染", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            ColorRampNode sel = continuousStrip.SelectedNode;
            if (sel == null) return;
            ColorRampNode next = sel.Next ?? sel.Previous;
            ramp.RemoveNode(sel);
            continuousStrip.SelectedNode = next;
            RefreshContinuousRamp();
        }

        private void SetContinuousNodePosition()
        {
            ColorRamp ramp = continuous.Ramp;
            ColorRampNode sel = continuousStrip.SelectedNode;
            if (ramp == null || sel == null)
            {
                MessageBox.Show(this, "请先在色带上点选一个标签。", "连续渲染");
                return;
            }
            decimal value = (decimal)(sel.Position * 100);
            if (!AskNumber("设置节点位置（0~100）", "位置：", ref value, 0, 100, 1)) return;
            sel.Position = (double)value / 100.0;
            if (sel.IsBound) sel.BindingKey = null;   // 手改位置后就不再算绑定
            ramp.Sort();
            RefreshContinuousRamp();
        }

        private void SetContinuousNodeColor()
        {
            ColorRampNode sel = continuousStrip.SelectedNode;
            if (sel == null)
            {
                MessageBox.Show(this, "请先在色带上点选一个标签。", "连续渲染");
                return;
            }
            Color color = ColorPickerForm.Pick(this, sel.Color, "选择色带节点颜色（可设置透明度）");
            if (color == Color.Empty) return;
            sel.Color = color;
            RefreshContinuousRamp();
        }

        private void SetContinuousNodeInterpolation()
        {
            ColorRampNode sel = continuousStrip.SelectedNode;
            if (sel == null || sel.Previous == null)
            {
                MessageBox.Show(this, "请先选择一个不是首个节点的节点。", "连续渲染");
                return;
            }
            int choice = sel.Interpolation == RampInterpolation.Hsv ? 1 : 0;
            if (!AskChoice("设置节点与上一个节点之间的插值方式", "插值方式：", new[] { "RGB 线性插值", "HSV 插值" }, ref choice)) return;
            sel.Interpolation = choice == 1 ? RampInterpolation.Hsv : RampInterpolation.Rgb;
            RefreshContinuousRamp();
        }

        private void SaveContinuousRamp()
        {
            ColorRamp ramp = continuous.Ramp;
            if (ramp == null) return;
            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = "保存为自定义色带文件";
                dialog.Filter = ColorRamp.FileFilter;
                dialog.DefaultExt = "gcr";
                dialog.AddExtension = true;
                dialog.FileName = string.IsNullOrEmpty(ramp.Name) ? "自定义色带" : ramp.Name;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    ramp.Save(dialog.FileName);
                    MessageBox.Show(this, "已保存到：\r\n" + dialog.FileName, "连续渲染",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "保存失败：" + ex.Message, "连续渲染",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void LoadContinuousRamp()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "读取自定义色带文件";
                dialog.Filter = ColorRamp.FileFilter;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    ColorRamp ramp = ColorRamp.Load(dialog.FileName);
                    continuous.Ramp = ramp;
                    continuousRampCombo.AddRamp(ramp);
                    SelectContinuousRampCombo(ramp);
                    continuousStrip.Ramp = ramp;
                    RefreshContinuousRamp();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "读取失败：" + ex.Message, "连续渲染",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void LoadBuiltInContinuousRamp()
        {
            ColorRamp ramp = BuiltInRampForm.Pick(this, ColorRamp.BuiltIn());
            if (ramp == null) return;
            continuous.Ramp = ramp;
            continuousRampCombo.AddRamp(ramp);
            SelectContinuousRampCombo(ramp);
            continuousStrip.Ramp = ramp;
            RefreshContinuousRamp();
        }

        // 简单数值输入框（固定尺寸，避免 AutoSize + Dock 造成控件压盖）
        private bool AskNumber(string title, string caption, ref decimal value, decimal min, decimal max, int decimals)
        {
            FlowLayoutPanel buttons;
            using (var form = CreateSmallDialog(title, 420, 150, out buttons))
            {
                var input = new NumericUpDown
                {
                    Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, value)),
                    DecimalPlaces = decimals, Increment = decimals == 0 ? 1 : 0.1m, Width = 180
                };
                var grid = CreateDialogGrid();
                grid.Controls.Add(new Label { Text = caption, AutoSize = true, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
                grid.Controls.Add(input, 1, 0);
                // 先加内容（Fill）再加按钮条（Bottom）：后加的 Dock=Bottom 先布置，内容区才不会被压住
                form.Controls.Add(grid);
                form.Controls.Add(buttons);
                DialogLayout.Apply(form);
                if (form.ShowDialog(this) != DialogResult.OK) return false;
                value = input.Value;
                return true;
            }
        }

        private bool AskChoice(string title, string caption, string[] choices, ref int selected)
        {
            FlowLayoutPanel buttons;
            using (var form = CreateSmallDialog(title, 440, 150, out buttons))
            {
                var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
                combo.Items.AddRange(choices);
                combo.SelectedIndex = Math.Max(0, Math.Min(choices.Length - 1, selected));
                var grid = CreateDialogGrid();
                grid.Controls.Add(new Label { Text = caption, AutoSize = true, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
                grid.Controls.Add(combo, 1, 0);
                form.Controls.Add(grid);
                form.Controls.Add(buttons);
                DialogLayout.Apply(form);
                if (form.ShowDialog(this) != DialogResult.OK) return false;
                selected = combo.SelectedIndex;
                return true;
            }
        }

        // 小对话框骨架：固定客户区 + 内容区（由调用方以 Fill 加入）+ 下方按钮条（Dock=Bottom）
        private Form CreateSmallDialog(string title, int width, int height, out FlowLayoutPanel buttons)
        {
            var form = new Form
            {
                Text = title,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false,
                Font = Font,
                Padding = new Padding(12),
                ClientSize = new Size(width, height),
                AutoSize = false
            };
            var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Width = 84 };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 84 };
            buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom, Height = 46,
                FlowDirection = FlowDirection.RightToLeft, WrapContents = false,
                Padding = new Padding(0, 8, 0, 0)
            };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            form.AcceptButton = ok;
            form.CancelButton = cancel;
            return form;
        }

        private TableLayoutPanel CreateDialogGrid()
        {
            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                AutoSize = false,
                Padding = new Padding(0, 6, 0, 0)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return grid;
        }

        #endregion

        // 切换字段：重算数值、拉伸区间、统计量与 KDE 曲线（曲线只与数值有关，改色带不会动它）。
        // 换了字段，原来的“标签绑定到某个特殊值”已失去意义，一并解除。
        private void OnContinuousFieldChanged()
        {
            if (continuousField.SelectedIndex < 0) return;
            string field = continuousField.SelectedItem.ToString();
            bool fieldChanged = continuous.Field != field;
            continuous.Field = field;
            continuous.ClearBindingError();
            if (continuous.Symbol == null) continuous.Symbol = BaseSymbol();
            if (continuous.Ramp == null) continuous.Ramp = currentRamp != null ? currentRamp.Clone() : ColorRamp.CreateDefault();
            if (fieldChanged) continuous.Ramp.ClearBindings();   // 换字段 → 取消全部绑定

            double[] values = ContinuousRenderer.ValuesOf(layer.FeatureClass, field);
            if (values.Length == 0)
            {
                continuousStrip.Context = null;
                continuousInfo.Text = "字段“" + field + "”没有可用的数值。";
                continuousDefault.Invalidate();
                return;
            }
            continuous.SetRange(values.Min(), values.Max());
            var context = ContinuousStripContext.Create(values, field);
            continuousStrip.Context = context;
            continuousStrip.Ramp = continuous.Ramp;
            SelectContinuousRampCombo(continuous.Ramp);
            string outside = context.OutsideText();
            continuousInfo.Text = string.Format(
                "字段“{0}”：{1} 个数值，范围 {2} ~ {3}，均值 {4}，中位数 {5}，σ {6}；把标签向上拖到虚线可绑定特殊值。{7}",
                field, context.Values.Length,
                ContinuousMarks.FormatValue(context.Min), ContinuousMarks.FormatValue(context.Max),
                ContinuousMarks.FormatValue(context.Statistics.Mean),
                ContinuousMarks.FormatValue(context.Statistics.Median),
                ContinuousMarks.FormatValue(context.Statistics.Sigma),
                string.IsNullOrEmpty(outside) ? "" : "  " + outside);
            continuousDefault.Invalidate();
            continuousStrip.Invalidate();
        }

        // 色带选择变化：只换颜色，曲线与特殊值不变
        private void OnContinuousRampSelected()
        {
            if (suppressContinuousRamp) return;
            if (continuousRampCombo.IsCustomSelected)
            {
                // 色带节点编辑已集成在本页，这里不再打开色带编辑器，恢复原选择即可
                SelectContinuousRampCombo(continuous.Ramp);
                return;
            }
            ColorRamp ramp = continuousRampCombo.SelectedRamp;
            if (ramp == null) return;
            continuous.Ramp = ramp.Clone();
            RefreshContinuousRamp();
        }

        private void ApplyContinuousRamp(ColorRamp ramp)
        {
            if (ramp == null) return;
            continuous.Ramp = ramp.Clone();
            RefreshContinuousRamp();
        }

        // 色带（或节点绑定）变化后：只重画色带与 KDE 的填充色，曲线与数值不变
        private void RefreshContinuousRamp()
        {
            if (continuousStrip.Ramp != continuous.Ramp) continuousStrip.Ramp = continuous.Ramp;
            continuousStrip.NotifyRampChanged();
            continuousDefault.Invalidate();
        }

        private void SelectContinuousRampCombo(ColorRamp ramp)
        {
            if (continuousRampCombo == null || ramp == null) return;
            suppressContinuousRamp = true;
            continuousRampCombo.SelectRamp(ramp);
            suppressContinuousRamp = false;
        }

        // 按当前图层与字段刷新连续渲染页（打开窗口、还原、读取渲染符号文件后调用）
        private void ShowContinuous()
        {
            if (continuous.Symbol == null) continuous.Symbol = BaseSymbol();
            if (continuous.Ramp == null) continuous.Ramp = currentRamp != null ? currentRamp.Clone() : ColorRamp.CreateDefault();
            SelectField(continuousField, continuous.Field);
            if (continuousField.SelectedIndex < 0 && continuousField.Items.Count > 0) continuousField.SelectedIndex = 0;
            continuousStrip.Ramp = continuous.Ramp;
            // 字段没变时不重建上下文（保留用户已经拖好的标签绑定）
            if (continuousStrip.Context == null
                || continuousStrip.Context.FieldName != continuous.Field)
                OnContinuousFieldChanged();
            else
            {
                continuousStrip.Invalidate();
                continuousDefault.Invalidate();
            }
        }

        #endregion

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
                {
                    classField.Items.Add(f.Name);
                    continuousField.Items.Add(f.Name);   // 连续渲染只允许数值型字段
                }
            }
            if (uniqueField.Items.Count > 0) uniqueField.SelectedIndex = 0;
            if (classField.Items.Count > 0) classField.SelectedIndex = 0;
            if (continuousField.Items.Count > 0) continuousField.SelectedIndex = 0;
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
                bool canEdit = LayerControl.CanEditSymbolDirectly(renderer);
                var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Height = 28 };
                var sym = new Panel { Size = new Size(64, 26), BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Cursor = canEdit ? Cursors.Hand : Cursors.Default, Margin = new Padding(4, 1, 8, 0) };
                sym.Paint += (s, e) => { Symbol s2 = getSymbol(index); if (s2 != null) BasicGeometryDrawer.DrawSymbol(e.Graphics, s2, sym.ClientRectangle); };
                // 绑定属性错误时符号不可单独设置（画的是红色感叹号提示符号），重新绑定字段并生成后即可编辑
                if (canEdit) sym.Click += (s, e) => EditSymbol(renderer, index, getSymbol, getLabel, panel, count);
                var lbl = new Label { Text = getLabel(index), AutoSize = true, Margin = new Padding(0, 5, 0, 0) };
                row.Controls.Add(sym);
                row.Controls.Add(lbl);
                list.Controls.Add(row);
            }
            panel.Controls.Add(list);
        }

        private void EditSymbol(Renderer renderer, int index, Func<int, Symbol> getSymbol, Func<int, string> getLabel, Panel panel, int count)
        {
            if (!LayerControl.CanEditSymbolDirectly(renderer)) return;   // 绑定属性错误时不允许单独设置符号
            var edited = SymbolUI.EditSymbol(this, getSymbol(index));
            if (edited != null)
            {
                if (renderer is UniqueValueRenderer u) u.SetSymbol(index, edited);
                else if (renderer is ClassBreaksRenderer c) c.SetSymbol(index, edited);
                RebuildList(panel, count, getSymbol, getLabel, renderer);
            }
        }

        /// <summary>
        /// 编辑“默认符号”。即使当前默认符号为空（旧文件读入或绑定属性错误后），
        /// 也先用图层符号兜底打开编辑器，保证默认符号始终可以设置。
        /// </summary>
        private void EditDefaultSymbol(Func<Symbol> get, Action<Symbol> set, Panel panel)
        {
            Symbol current = get() ?? BaseSymbol();
            var edited = SymbolUI.EditSymbol(this, current);
            if (edited != null)
            {
                set(edited);
                panel.Invalidate();
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
            // 基于默认符号克隆，仅改变颜色（默认符号可能为空：用一个基于图层符号的副本兜底）
            for (int i = 0; i < values.Count; i++)
            {
                Symbol s = CloneSymbol(unique.DefaultSymbol) ?? BaseSymbol();
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
            Symbol s = CloneSymbol(unique.DefaultSymbol) ?? BaseSymbol();
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
            // 基于默认符号克隆，仅改变颜色（默认符号可能为空：用一个基于图层符号的副本兜底）
            for (int i = 0; i < breaks.Length; i++)
            {
                Symbol s = CloneSymbol(classBreaks.DefaultSymbol) ?? BaseSymbol();
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
            if (tabIndex == 3) { if (continuousField.SelectedIndex >= 0) continuous.Field = continuousField.SelectedItem.ToString(); return continuous.Clone(); }
            return simple.Clone();
        }

        /// <summary>创建渲染设置窗口（对话框与自动检查共用同一套布局与初始化逻辑）。</summary>
        public static LayerRendererForm Create(Layer layer, Action onApplied = null)
        {
            var form = new LayerRendererForm(layer);
            if (onApplied != null) form.RendererApplied += onApplied;
            return form;
        }

        public static bool Edit(IWin32Window owner, Layer layer, Action onApplied = null)
        {
            using (var f = Create(layer, onApplied))
                return f.ShowDialog(owner) == DialogResult.OK;
        }
    }
}


