using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using GIS;
using GIS.Display;
using GIS.Toolbox.UI;

namespace GIS.Toolbox.Tools.Proximity
{
    /// <summary>ArcGIS Pro 风格的普通缓冲区工具。</summary>
    public sealed class BufferToolForm : ProximityToolFormBase
    {
        public const string FixedDistance = "固定值";
        public const string FieldDistance = "字段值";
        public const string NoDissolve = "不融合（默认）";
        public const string DissolveAll = "全部融合";
        public const string DissolveByField = "按字段融合";
        public const string FullSide = "两侧（默认）";
        public const string LeftSide = "左侧";
        public const string RightSide = "右侧";
        public const string RoundEnd = "圆头（默认）";
        public const string FlatEnd = "平头";
        public const string SquareEnd = "方头";
        public const string OutsideOnlyText = "仅保留输入面外部的缓冲区";

        private readonly ComboBox inputFeatures;
        private readonly ComboBox distanceSourceCombo;
        private readonly TextBox distanceBox;
        private readonly ComboBox distanceFieldCombo;
        private readonly ComboBox unitCombo;
        private readonly ComboBox methodCombo;
        private readonly ComboBox dissolveCombo;
        private readonly ComboBox dissolveFieldCombo;
        private readonly ComboBox sideCombo;
        private readonly ComboBox endCombo;
        private readonly CheckBox outsideOnlyBox;
        private readonly TextBox outputName;
        private readonly Control lineOptionsPanel;
        private readonly Label lineOptionsCaption;
        private readonly Label outsideCaption;
        private readonly ToolTip helpTip;

        private string distanceSource;
        private double fixedDistance;
        private string distanceFieldName;
        private string unit;
        private string method;
        private string dissolveMode;
        private string dissolveFieldName;
        private string sideMode;
        private string endMode;
        private bool outsideOnly;

        public BufferToolForm(MapControl map) : base(map, "缓冲区（Buffer）")
        {
            inputFeatures = AddInputFeaturesField("输入要素", null);

            // ArcGIS Pro 风格：距离值/字段与单位合并成一个紧凑参数行。
            distanceSourceCombo = NewCompactCombo(108, FixedDistance, FieldDistance);
            distanceBox = new TextBox { Width = 90, Text = "100" };
            distanceFieldCombo = NewCompactCombo(132);
            unitCombo = NewCompactCombo(145, DefaultUnit, MeterUnit, KilometerUnit,
                CentimeterUnit, MillimeterUnit, FootUnit, YardUnit, MileUnit,
                NauticalMileUnit, DecimalDegreeUnit);
            AddCustomField("缓冲距离", CompactLine(distanceSourceCombo, distanceBox,
                distanceFieldCombo, unitCombo));

            // 融合字段仅在“按字段融合”时出现。
            dissolveCombo = NewCompactCombo(250, NoDissolve, DissolveAll, DissolveByField);
            dissolveFieldCombo = NewCompactCombo(164);
            AddCustomField("融合类型", CompactLine(dissolveCombo, dissolveFieldCombo));

            // 不使用折叠面板：常用方法直接显示，几何专用项按输入类型动态出现。
            methodCombo = NewCompactCombo(430, AutoMethod, PlanarMethod, GeodesicMethod);
            AddCustomField("计算方法", methodCombo);
            sideCombo = NewCompactCombo(204, FullSide, LeftSide, RightSide);
            endCombo = NewCompactCombo(204, RoundEnd, FlatEnd, SquareEnd);
            lineOptionsPanel = CompactLine(sideCombo, endCombo);
            lineOptionsCaption = AddCustomField("线参数", lineOptionsPanel);
            outsideOnlyBox = new CheckBox { Text = OutsideOnlyText, AutoSize = true };
            outsideCaption = AddCustomField("面缓冲", outsideOnlyBox);

            outputName = AddOutputNameField("输出图层名", "缓冲区");
            helpTip = new ToolTip { AutoPopDelay = 12000, InitialDelay = 350, ReshowDelay = 100 };
            helpTip.SetToolTip(distanceSourceCombo, "固定值：所有要素使用同一距离；字段值：逐要素读取数值字段。");
            helpTip.SetToolTip(unitCombo, "默认单位使用输入坐标单位；已声明的经纬度图层选择米等线性单位时自动采用测地线近似。");
            helpTip.SetToolTip(dissolveCombo, "不融合：每个输入要素保留一个缓冲区；全部融合：合成一个要素；按字段融合：相同字段值合并。");
            helpTip.SetToolTip(methodCombo, "自动：投影坐标采用平面方法，已声明的经纬度采用测地线近似；坐标系未知时必须明确选择。");
            FormClosed += (s, e) => helpTip.Dispose();

            inputFeatures.SelectedIndexChanged += (s, e) => RefreshInputDependentControls();
            inputFeatures.DropDown += (s, e) => RefreshInputDependentControls();
            distanceSourceCombo.SelectedIndexChanged += (s, e) => RefreshDistanceState();
            dissolveCombo.SelectedIndexChanged += (s, e) => RefreshDissolveFieldState();
            RefreshInputDependentControls();
            FinishLayout();
        }

        public ComboBox InputFeaturesCombo { get { return inputFeatures; } }
        public ComboBox DistanceSourceCombo { get { return distanceSourceCombo; } }
        public TextBox DistanceBox { get { return distanceBox; } }
        public ComboBox DistanceFieldCombo { get { return distanceFieldCombo; } }
        public ComboBox UnitCombo { get { return unitCombo; } }
        public ComboBox MethodCombo { get { return methodCombo; } }
        public ComboBox DissolveCombo { get { return dissolveCombo; } }
        public ComboBox DissolveFieldCombo { get { return dissolveFieldCombo; } }
        public ComboBox SideCombo { get { return sideCombo; } }
        public ComboBox EndCombo { get { return endCombo; } }
        public CheckBox OutsideOnlyBox { get { return outsideOnlyBox; } }
        public TextBox OutputNameBox { get { return outputName; } }

        protected override bool CollectInputs(out ToolRunInputs inputs, out string error)
        {
            if (!CollectSingleLayerInput(inputFeatures, out inputs, out error)) return false;

            distanceSource = distanceSourceCombo.SelectedItem == null ? "" : distanceSourceCombo.SelectedItem.ToString();
            distanceFieldName = distanceFieldCombo.SelectedItem == null ? "" : distanceFieldCombo.SelectedItem.ToString();
            if (distanceSource == FixedDistance)
            {
                if (!TryParseFiniteNonZeroDouble(distanceBox.Text, out fixedDistance))
                {
                    error = "「距离」必须是非零的有限数值。";
                    return false;
                }
                if (fixedDistance < 0 && !IsPolygonLayer(inputs.InputLayer))
                {
                    error = "负缓冲距离只适用于面图层。";
                    return false;
                }
            }
            else if (distanceSource == FieldDistance)
            {
                Field field = inputs.InputLayer.FeatureClass.Fields.GetItem(distanceFieldName);
                if (!IsNumericField(field))
                {
                    error = "请选择一个数值类型的「距离字段」。";
                    return false;
                }
                bool hasUsableDistance = false;
                foreach (Feature feature in inputs.InputFeatures)
                {
                    double value;
                    if (!TryConvertDistanceValue(feature.Attributes == null ? null :
                        feature.Attributes.GetItem(distanceFieldName), out value))
                        continue;
                    if (value < 0 && !IsPolygonLayer(inputs.InputLayer))
                        continue;
                    hasUsableDistance = true;
                }
                if (!hasUsableDistance)
                {
                    error = "距离字段“" + distanceFieldName + "”没有可用的非零数值。";
                    return false;
                }
            }
            else
            {
                error = "请选择「距离来源」。";
                return false;
            }

            unit = unitCombo.SelectedItem == null ? DefaultUnit : unitCombo.SelectedItem.ToString();
            method = methodCombo.SelectedItem == null ? AutoMethod : methodCombo.SelectedItem.ToString();
            double sample = fixedDistance;
            if (distanceSource == FieldDistance)
            {
                foreach (Feature feature in inputs.InputFeatures)
                {
                    double value;
                    if (!TryConvertDistanceValue(feature.Attributes == null ? null :
                        feature.Attributes.GetItem(distanceFieldName), out value)) continue;
                    if (value < 0 && !IsPolygonLayer(inputs.InputLayer)) continue;
                    sample = value;
                    break;
                }
            }
            double converted;
            ProximityEngine.BufferOptions sampleOptions;
            if (!ResolveDistance(sample, unit, method, inputs.InputLayer, out converted, out sampleOptions, out error))
                return false;

            dissolveMode = dissolveCombo.SelectedItem == null ? "" : dissolveCombo.SelectedItem.ToString();
            dissolveFieldName = dissolveFieldCombo.SelectedItem == null ? "" : dissolveFieldCombo.SelectedItem.ToString();
            if (dissolveMode == DissolveByField &&
                inputs.InputLayer.FeatureClass.Fields.FindField(dissolveFieldName) < 0)
            {
                error = "请选择有效的「融合字段」。";
                return false;
            }

            sideMode = sideCombo.SelectedItem == null ? FullSide : sideCombo.SelectedItem.ToString();
            endMode = endCombo.SelectedItem == null ? RoundEnd : endCombo.SelectedItem.ToString();
            outsideOnly = outsideOnlyBox.Checked;
            if (outsideOnly && !IsPolygonLayer(inputs.InputLayer))
            {
                error = "“仅保留输入面外部”只适用于面图层。";
                return false;
            }
            if (outsideOnly && AnyNegativeDistance(inputs))
            {
                error = "负缓冲不能同时选择“仅保留输入面外部”。";
                return false;
            }

            string name;
            if (!ValidateOutputName(outputName.Text, out name, out error)) return false;
            inputs.OutputLayerName = name;
            inputs.TextParameter = distanceSource == FixedDistance ? distanceBox.Text.Trim() : distanceFieldName;
            inputs.ChoiceParameter = dissolveMode;
            return true;
        }

        protected override void Execute(ToolRunInputs inputs)
        {
            var buffered = new List<BufferedItem>();
            var skipDetails = new List<string>();
            int skipped = 0;
            foreach (Feature source in inputs.InputFeatures)
            {
                int sourceIndex = SourceFeatureIndex(inputs.InputLayer, source);
                if (source == null || source.Geometry == null || source.Geometry.IsEmpty)
                {
                    skipped++;
                    AddSkipDetail(skipDetails, sourceIndex, "几何为空");
                    continue;
                }
                try
                {
                    double entered = RawDistance(source);
                    if (entered < 0 && !IsPolygonLayer(inputs.InputLayer))
                        throw new InvalidOperationException("负缓冲距离只适用于面图层");
                    double operationDistance;
                    ProximityEngine.BufferOptions options;
                    string error;
                    if (!ResolveDistance(entered, unit, method, inputs.InputLayer,
                        out operationDistance, out options, out error))
                        throw new InvalidOperationException(error);
                    ApplyShapeOptions(source.Geometry, options);
                    Geometry geometry = ProximityEngine.Buffer(source.Geometry, operationDistance, options);
                    if (outsideOnly) geometry = ProximityEngine.Difference(geometry, source.Geometry);
                    if (geometry == null || geometry.IsEmpty)
                    {
                        skipped++;
                        AddSkipDetail(skipDetails, sourceIndex, "缓冲结果为空，面可能已完全收缩");
                        continue;
                    }
                    buffered.Add(new BufferedItem
                    {
                        Geometry = geometry,
                        Source = source,
                        SourceIndex = sourceIndex,
                        OperationDistance = operationDistance,
                        DissolveValue = dissolveFieldName.Length == 0 || source.Attributes == null
                            ? null : source.Attributes.GetItem(dissolveFieldName)
                    });
                }
                catch (Exception ex)
                {
                    skipped++;
                    AddSkipDetail(skipDetails, sourceIndex, ex.Message);
                }
            }
            if (buffered.Count == 0)
            {
                ShowResult("运行完成：没有生成有效缓冲区。" +
                    BuildSkipNote(skipped, skipDetails, "输入要素"));
                return;
            }

            FeatureClass output = NewPolygonFeatureClass(inputs.OutputLayerName, inputs.InputLayer);
            int added = dissolveMode == NoDissolve
                ? WriteUndissolved(output, inputs.InputLayer, buffered)
                : dissolveMode == DissolveAll
                    ? WriteDissolvedAll(output, buffered)
                    : WriteDissolvedByField(output, inputs.InputLayer, buffered);
            if (added == 0)
            {
                ShowResult("运行完成：缓冲结果为空。");
                return;
            }
            AddResultLayer(output, inputs.InputLayer);
            string distanceText = distanceSource == FixedDistance
                ? fixedDistance.ToString("G", CultureInfo.CurrentCulture)
                : "字段“" + distanceFieldName + "”";
            string inferredNote = UsesInferredGeographic(inputs.InputLayer, unit, method)
                ? "\r\n注意：输入图层没有坐标系标识；本次因你明确选择测地线近似，按疑似经纬度坐标计算。建议为数据补充 EPSG:4326/4490。"
                : "";
            ShowResult("运行完成。\r\n输出图层：「" + inputs.OutputLayerName + "」（面 " + added +
                " 个）。\r\n距离：" + distanceText + " " + unit + "；方法：" + method +
                "；融合类型：" + dissolveMode + "。" + inferredNote +
                BuildSkipNote(skipped, skipDetails, "输入要素"));
        }

        private int WriteUndissolved(FeatureClass output, Layer inputLayer, IList<BufferedItem> items)
        {
            AddFields(output, CloneFields(inputLayer.FeatureClass.Fields));
            string fidName = UniqueFieldName(output.Fields, "ORIG_FID");
            output.Fields.Add(new Field(fidName, FieldTypeConstant.Int32, "原始要素编号"));
            string distanceName = UniqueFieldName(output.Fields, "BUFF_DIST");
            output.Fields.Add(new Field(distanceName, FieldTypeConstant.Double, "缓冲距离"));
            foreach (BufferedItem item in items)
            {
                var feature = new Feature(item.Geometry, output.Fields);
                CopyAttributes(item.Source, feature);
                feature.Attributes.SetItem(fidName, item.SourceIndex);
                feature.Attributes.SetItem(distanceName, item.OperationDistance);
                output.Add(feature);
            }
            return items.Count;
        }

        private int WriteDissolvedAll(FeatureClass output, IList<BufferedItem> items)
        {
            Geometry union = ProximityEngine.Union(items.Select(i => i.Geometry));
            if (union == null || union.IsEmpty) return 0;
            var feature = new Feature(union, output.Fields);
            output.Add(feature);
            return 1;
        }

        private int WriteDissolvedByField(FeatureClass output, Layer inputLayer, IList<BufferedItem> items)
        {
            Field sourceField = inputLayer.FeatureClass.Fields.GetItem(dissolveFieldName);
            output.Fields.Add(new Field(sourceField.Name, sourceField.ValueType, sourceField.Alias));
            int count = 0;
            foreach (IGrouping<string, BufferedItem> group in items.GroupBy(i => ValueKey(i.DissolveValue)))
            {
                Geometry union = ProximityEngine.Union(group.Select(i => i.Geometry));
                if (union == null || union.IsEmpty) continue;
                var feature = new Feature(union, output.Fields);
                feature.Attributes.SetItem(sourceField.Name, group.First().DissolveValue);
                output.Add(feature);
                count++;
            }
            return count;
        }

        private void RefreshInputDependentControls()
        {
            string previousDistance = SelectedText(distanceFieldCombo);
            string previousDissolve = SelectedText(dissolveFieldCombo);
            distanceFieldCombo.Items.Clear();
            dissolveFieldCombo.Items.Clear();
            Layer layer = CurrentInputLayer();
            if (layer != null && layer.FeatureClass != null)
            {
                foreach (Field field in layer.FeatureClass.Fields)
                {
                    dissolveFieldCombo.Items.Add(field.Name);
                    if (IsNumericField(field)) distanceFieldCombo.Items.Add(field.Name);
                }
            }
            RestoreSelection(distanceFieldCombo, previousDistance);
            RestoreSelection(dissolveFieldCombo, previousDissolve);

            bool line = IsLineLayer(layer);
            lineOptionsCaption.Visible = line;
            lineOptionsPanel.Visible = line;
            bool polygon = IsPolygonLayer(layer);
            outsideCaption.Visible = polygon;
            outsideOnlyBox.Visible = polygon;
            if (!polygon) outsideOnlyBox.Checked = false;
            RefreshDistanceState();
            RefreshDissolveFieldState();
            RefreshContentLayout();
        }

        private void RefreshDistanceState()
        {
            bool fromField = SelectedText(distanceSourceCombo) == FieldDistance;
            distanceBox.Visible = !fromField;
            distanceFieldCombo.Visible = fromField;
            RefreshContentLayout();
        }

        private void RefreshDissolveFieldState()
        {
            dissolveFieldCombo.Visible = SelectedText(dissolveCombo) == DissolveByField;
            RefreshContentLayout();
        }

        private static ComboBox NewCompactCombo(int width, params string[] items)
        {
            var combo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = width,
                DropDownWidth = Math.Max(width, 360),
                DropDownHeight = 260,
                Margin = new Padding(0, 0, 6, 0)
            };
            if (items != null && items.Length > 0) combo.Items.AddRange(items);
            if (combo.Items.Count > 0) combo.SelectedIndex = 0;
            return combo;
        }

        private static FlowLayoutPanel CompactLine(params Control[] controls)
        {
            var line = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = System.Windows.Forms.Padding.Empty,
                Padding = System.Windows.Forms.Padding.Empty
            };
            if (controls != null) line.Controls.AddRange(controls);
            return line;
        }

        private Layer CurrentInputLayer()
        {
            Layer layer = SelectedLayer(inputFeatures);
            if (layer != null || Map == null) return layer;
            Layer selectedLayer = null;
            foreach (Layer candidate in Map.Layers)
            {
                Features selected = Map.GetSelection(candidate);
                if (selected == null || selected.Count == 0) continue;
                if (selectedLayer != null) return null;
                selectedLayer = candidate;
            }
            return selectedLayer;
        }

        private void ApplyShapeOptions(Geometry geometry, ProximityEngine.BufferOptions options)
        {
            bool line = geometry.GeometryType == GeometryTypeConstant.LineString ||
                geometry.GeometryType == GeometryTypeConstant.MultiLineString;
            options.Side = !line || sideMode == FullSide ? ProximityEngine.BufferSide.Full :
                sideMode == LeftSide ? ProximityEngine.BufferSide.Left : ProximityEngine.BufferSide.Right;
            options.End = endMode == FlatEnd ? ProximityEngine.BufferEnd.Flat :
                endMode == SquareEnd ? ProximityEngine.BufferEnd.Square : ProximityEngine.BufferEnd.Round;
        }

        private double RawDistance(Feature feature)
        {
            if (distanceSource == FixedDistance) return fixedDistance;
            return Convert.ToDouble(feature.Attributes.GetItem(distanceFieldName), CultureInfo.InvariantCulture);
        }

        private bool AnyNegativeDistance(ToolRunInputs inputs)
        {
            if (distanceSource == FixedDistance) return fixedDistance < 0;
            foreach (Feature feature in inputs.InputFeatures)
            {
                double value;
                if (feature != null && feature.Attributes != null &&
                    TryConvertDistanceValue(feature.Attributes.GetItem(distanceFieldName), out value) &&
                    value < 0) return true;
            }
            return false;
        }

        private static bool TryParseFiniteNonZeroDouble(string text, out double value)
        {
            text = (text ?? "").Trim();
            bool parsed = double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
                double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            return parsed && !double.IsNaN(value) && !double.IsInfinity(value) && value != 0;
        }

        private static bool TryConvertDistanceValue(object value, out double distance)
        {
            distance = 0;
            if (value == null || value == DBNull.Value) return false;
            try
            {
                distance = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return !double.IsNaN(distance) && !double.IsInfinity(distance) && distance != 0;
            }
            catch { return false; }
        }

        private static bool IsPolygonLayer(Layer layer)
        {
            if (layer == null || layer.FeatureClass == null) return false;
            GeometryTypeConstant type = layer.FeatureClass.GeometryType;
            return type == GeometryTypeConstant.Polygon || type == GeometryTypeConstant.MultiPolygon;
        }

        private static bool IsLineLayer(Layer layer)
        {
            if (layer == null || layer.FeatureClass == null) return false;
            GeometryTypeConstant type = layer.FeatureClass.GeometryType;
            return type == GeometryTypeConstant.LineString || type == GeometryTypeConstant.MultiLineString;
        }

        private static string SelectedText(ComboBox combo)
        {
            return combo == null || combo.SelectedItem == null ? "" : combo.SelectedItem.ToString();
        }

        private static void RestoreSelection(ComboBox combo, string previous)
        {
            if (!string.IsNullOrEmpty(previous) && combo.Items.Contains(previous)) combo.SelectedItem = previous;
            else if (combo.Items.Count > 0) combo.SelectedIndex = 0;
        }

        private static string ValueKey(object value)
        {
            if (value == null) return "<NULL>";
            return value.GetType().FullName + ":" + Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private sealed class BufferedItem
        {
            public Geometry Geometry;
            public Feature Source;
            public int SourceIndex;
            public double OperationDistance;
            public object DissolveValue;
        }
    }
}
