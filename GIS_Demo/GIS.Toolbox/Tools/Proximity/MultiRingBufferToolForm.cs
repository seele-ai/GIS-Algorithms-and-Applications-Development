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
    /// <summary>ArcGIS Pro 风格的多环缓冲区工具。</summary>
    public sealed class MultiRingBufferToolForm : ProximityToolFormBase
    {
        public const string NonOverlappingRings = "非重叠环（默认）";
        public const string OverlappingDisks = "重叠圆盘";
        public const string OutsideOnlyText = "仅保留输入面外部的缓冲区（仅面图层）";

        private readonly ComboBox inputFeatures;
        private readonly TextBox distancesBox;
        private readonly ComboBox unitCombo;
        private readonly ComboBox methodCombo;
        private readonly TextBox distanceFieldBox;
        private readonly ComboBox dissolveCombo;
        private readonly CheckBox outsideOnlyBox;
        private readonly TextBox outputName;

        private List<double> enteredDistances;
        private List<double> coordinateDistances;
        private ProximityEngine.BufferOptions bufferOptions;
        private string distanceFieldName;
        private string dissolveMode;
        private bool outsideOnly;
        private int duplicateDistanceCount;
        private int skippedOperations;
        private List<string> skipDetails;

        public MultiRingBufferToolForm(MapControl map) : base(map, "多环缓冲区（Multiple Ring Buffer）")
        {
            inputFeatures = AddInputFeaturesField("输入要素", null);
            distancesBox = AddTextParameterField("距离列表", "例如：100, 200, 500", "100, 200, 500");
            unitCombo = AddChoiceParameterField("距离单位", DefaultUnit, MeterUnit,
                KilometerUnit, CentimeterUnit, MillimeterUnit, FootUnit, YardUnit,
                MileUnit, NauticalMileUnit, DecimalDegreeUnit);
            methodCombo = AddChoiceParameterField("计算方法", AutoMethod, PlanarMethod, GeodesicMethod);
            distanceFieldBox = AddTextParameterField("缓冲距离字段名称", "默认：distance", "distance");
            dissolveCombo = AddChoiceParameterField("融合选项", NonOverlappingRings, OverlappingDisks);
            outsideOnlyBox = AddCheckParametersField("面缓冲选项", OutsideOnlyText)[0];
            outputName = AddOutputNameField("输出图层名", "多环缓冲区");
            inputFeatures.SelectedIndexChanged += (s, e) => RefreshOutsideOnlyState();
            inputFeatures.DropDown += (s, e) => RefreshOutsideOnlyState();
            RefreshOutsideOnlyState();
            FinishLayout();
        }

        public ComboBox InputFeaturesCombo { get { return inputFeatures; } }
        public TextBox DistancesBox { get { return distancesBox; } }
        public ComboBox UnitCombo { get { return unitCombo; } }
        public ComboBox MethodCombo { get { return methodCombo; } }
        public TextBox DistanceFieldBox { get { return distanceFieldBox; } }
        public ComboBox DissolveCombo { get { return dissolveCombo; } }
        public CheckBox OutsideOnlyBox { get { return outsideOnlyBox; } }
        public TextBox OutputNameBox { get { return outputName; } }

        protected override bool CollectInputs(out ToolRunInputs inputs, out string error)
        {
            if (!CollectSingleLayerInput(inputFeatures, out inputs, out error)) return false;
            if (!TryParseDistances(distancesBox.Text, out enteredDistances,
                out duplicateDistanceCount, out error)) return false;

            coordinateDistances = new List<double>();
            string unit = unitCombo.SelectedItem == null ? DefaultUnit : unitCombo.SelectedItem.ToString();
            string method = methodCombo.SelectedItem == null ? AutoMethod : methodCombo.SelectedItem.ToString();
            foreach (double entered in enteredDistances)
            {
                double converted;
                ProximityEngine.BufferOptions options;
                if (!ResolveDistance(entered, unit, method, inputs.InputLayer,
                    out converted, out options, out error)) return false;
                coordinateDistances.Add(converted);
                bufferOptions = options;
            }

            distanceFieldName = (distanceFieldBox.Text ?? "").Trim();
            if (distanceFieldName.Length == 0)
            {
                error = "请填写「缓冲距离字段名称」。";
                return false;
            }
            dissolveMode = dissolveCombo.SelectedItem == null ? "" : dissolveCombo.SelectedItem.ToString();
            if (dissolveMode.Length == 0)
            {
                error = "请选择「融合选项」。";
                return false;
            }
            outsideOnly = outsideOnlyBox.Checked;
            if (outsideOnly && !IsPolygonLayer(inputs.InputLayer))
            {
                error = "“仅保留输入面外部”只适用于面图层。";
                return false;
            }

            string name;
            if (!ValidateOutputName(outputName.Text, out name, out error)) return false;
            inputs.OutputLayerName = name;
            inputs.TextParameter = distancesBox.Text.Trim();
            inputs.ChoiceParameter = dissolveMode;
            if (outsideOnly) inputs.CheckedOptions.Add(OutsideOnlyText);
            return true;
        }

        protected override void Execute(ToolRunInputs inputs)
        {
            skippedOperations = 0;
            skipDetails = new List<string>();
            FeatureClass output = NewPolygonFeatureClass(inputs.OutputLayerName, inputs.InputLayer);
            output.Fields.Add(new Field(distanceFieldName, FieldTypeConstant.Double, "缓冲距离"));
            int added = dissolveMode == NonOverlappingRings
                ? WriteRings(output, inputs)
                : WriteDisks(output, inputs);
            if (added == 0)
            {
                ShowResult("运行完成：没有生成有效的多环缓冲区。" +
                    BuildSkipNote(skippedOperations, skipDetails, "要素-距离计算"));
                return;
            }

            AddResultLayer(output, inputs.InputLayer);
            string normalized = string.Join(", ", enteredDistances.Select(d =>
                d.ToString("G", CultureInfo.CurrentCulture)).ToArray());
            string unit = unitCombo.SelectedItem == null ? DefaultUnit : unitCombo.SelectedItem.ToString();
            string method = methodCombo.SelectedItem == null ? AutoMethod : methodCombo.SelectedItem.ToString();
            string inferredNote = UsesInferredGeographic(inputs.InputLayer, unit, method)
                ? "\r\n注意：输入图层没有坐标系标识；本次因你明确选择测地线近似，按疑似经纬度坐标计算。建议补充 EPSG:4326/4490。"
                : "";
            string duplicateNote = duplicateDistanceCount > 0
                ? "\r\n已忽略重复距离 " + duplicateDistanceCount + " 个。"
                : "";
            ShowResult("运行完成。\r\n输出图层：「" + inputs.OutputLayerName + "」（面 " + added +
                " 个）。\r\n实际使用距离（已按从小到大排序）：" + normalized + " " + unit +
                "；方法：" + method + "；融合选项：" + dissolveMode +
                (outsideOnly ? "；仅保留输入面外部。" : "。") + duplicateNote + inferredNote +
                BuildSkipNote(skippedOperations, skipDetails, "要素-距离计算"));
        }

        private int WriteRings(FeatureClass output, ToolRunInputs inputs)
        {
            Geometry inputArea = null;
            if (outsideOnly)
                inputArea = ProximityEngine.Union(inputs.InputFeatures.Select(f => f.Geometry));

            Geometry previous = null;
            int added = 0;
            for (int distanceIndex = 0; distanceIndex < coordinateDistances.Count; distanceIndex++)
            {
                var buffers = new List<Geometry>();
                foreach (Feature source in inputs.InputFeatures)
                {
                    int sourceIndex = SourceFeatureIndex(inputs.InputLayer, source);
                    if (source == null || source.Geometry == null || source.Geometry.IsEmpty)
                    {
                        RecordSkippedOperation(sourceIndex, "几何为空");
                        continue;
                    }
                    try
                    {
                        Geometry buffer = ProximityEngine.Buffer(source.Geometry,
                            coordinateDistances[distanceIndex], bufferOptions);
                        if (buffer == null || buffer.IsEmpty)
                        {
                            RecordSkippedOperation(sourceIndex, "缓冲结果为空");
                            continue;
                        }
                        buffers.Add(buffer);
                    }
                    catch (Exception ex)
                    {
                        RecordSkippedOperation(sourceIndex, ex.Message);
                    }
                }
                Geometry full = ProximityEngine.Union(buffers);
                if (full == null || full.IsEmpty) continue;
                if (outsideOnly) full = ProximityEngine.Difference(full, inputArea);
                Geometry ring = previous == null ? full : ProximityEngine.Difference(full, previous);
                previous = full;
                if (ring == null || ring.IsEmpty) continue;

                var feature = new Feature(ring, output.Fields);
                feature.Attributes.SetItem(distanceFieldName, enteredDistances[distanceIndex]);
                output.Add(feature);
                added++;
            }
            return added;
        }

        private int WriteDisks(FeatureClass output, ToolRunInputs inputs)
        {
            string fidName = UniqueFieldName(output.Fields, "ORIG_FID");
            output.Fields.Add(new Field(fidName, FieldTypeConstant.Int32, "原始要素编号"));
            int added = 0;
            for (int distanceIndex = 0; distanceIndex < coordinateDistances.Count; distanceIndex++)
            {
                foreach (Feature source in inputs.InputFeatures)
                {
                    int sourceIndex = SourceFeatureIndex(inputs.InputLayer, source);
                    if (source == null || source.Geometry == null || source.Geometry.IsEmpty)
                    {
                        RecordSkippedOperation(sourceIndex, "几何为空");
                        continue;
                    }
                    try
                    {
                        Geometry geometry = ProximityEngine.Buffer(source.Geometry,
                            coordinateDistances[distanceIndex], bufferOptions);
                        if (outsideOnly) geometry = ProximityEngine.Difference(geometry, source.Geometry);
                        if (geometry == null || geometry.IsEmpty)
                        {
                            RecordSkippedOperation(sourceIndex, "缓冲结果为空");
                            continue;
                        }
                        var feature = new Feature(geometry, output.Fields);
                        feature.Attributes.SetItem(distanceFieldName, enteredDistances[distanceIndex]);
                        feature.Attributes.SetItem(fidName, sourceIndex);
                        output.Add(feature);
                        added++;
                    }
                    catch (Exception ex)
                    {
                        RecordSkippedOperation(sourceIndex, ex.Message);
                    }
                }
            }
            return added;
        }

        private void RecordSkippedOperation(int sourceIndex, string reason)
        {
            skippedOperations++;
            AddSkipDetail(skipDetails, sourceIndex, reason);
        }

        private void RefreshOutsideOnlyState()
        {
            bool enabled = IsPolygonLayer(CurrentInputLayer());
            outsideOnlyBox.Enabled = enabled;
            if (!enabled) outsideOnlyBox.Checked = false;
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

        private static bool IsPolygonLayer(Layer layer)
        {
            if (layer == null || layer.FeatureClass == null) return false;
            GeometryTypeConstant type = layer.FeatureClass.GeometryType;
            return type == GeometryTypeConstant.Polygon || type == GeometryTypeConstant.MultiPolygon;
        }
    }
}
