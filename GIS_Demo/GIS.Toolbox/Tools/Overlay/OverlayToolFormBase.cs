using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using GIS;
using GIS.Display;
using GIS.Toolbox.UI;

namespace GIS.Toolbox.Tools.Overlay
{
    /// <summary>
    /// 叠加分析工具的公共基类：统一处理
    /// 输入要素（已选要素 / 整层）+ 其他图层 + 输出图层名 三类输入的收集与校验，
    /// 并在运行后把结果要素组装成新图层加入地图。派生类只需实现 <see cref="Analyze"/>。
    /// </summary>
    public abstract class OverlayToolFormBase : ToolFormBase
    {
        private readonly ComboBox inputFeatures;
        private readonly ComboBox otherLayer;
        private readonly TextBox outputName;
        private readonly string otherLabel;

        protected OverlayToolFormBase(MapControl map, string title, string otherLabel,
            Func<Layer, bool> otherLayerFilter, string defaultOutputName)
            : base(map, title)
        {
            this.otherLabel = otherLabel;
            inputFeatures = AddInputFeaturesField("输入要素", null);
            otherLayer = AddLayerField(otherLabel, otherLayerFilter);
            outputName = AddOutputNameField("输出图层名", defaultOutputName);
            FinishLayout();
        }

        /// <summary>其他图层的下拉框（自动检查用）。</summary>
        public ComboBox OtherLayerCombo
        {
            get { return otherLayer; }
        }

        /// <summary>面图层过滤器（裁剪/擦除要素必须是面）。</summary>
        public static Func<Layer, bool> PolygonLayerFilter
        {
            get
            {
                return delegate(Layer layer)
                {
                    return layer.FeatureClass != null
                        && (layer.FeatureClass.GeometryType == GeometryTypeConstant.Polygon
                            || layer.FeatureClass.GeometryType == GeometryTypeConstant.MultiPolygon);
                };
            }
        }

        /// <summary>叠加运算：输入几何（连同其来源要素）对其他图层几何组，返回结果项与说明文字。</summary>
        protected abstract List<OverlayEngine.ResultItem> Analyze(
            IList<Geometry> inputGeometries, IList<Feature> inputSources,
            IList<Geometry> otherGeometries, Feature otherSample, out string note);

        protected override bool CollectInputs(out ToolRunInputs inputs, out string error)
        {
            inputs = null;
            error = null;
            var result = new ToolRunInputs();

            // ① 输入要素（已选要素优先，否则取整层）
            if (inputFeatures.SelectedIndex < 0)
            {
                error = "请选择「输入要素」。";
                return false;
            }
            if (IsSelectionOption(inputFeatures))
            {
                result.FromSelection = true;
                result.InputFeatures = new Features();
                if (Map == null)
                {
                    error = "当前没有绑定地图，无法读取已选要素。";
                    return false;
                }
                foreach (Layer layer in Map.Layers)
                {
                    Features selected = Map.GetSelection(layer);
                    if (selected == null || selected.Count == 0) continue;
                    result.InputLayers.Add(layer);
                    foreach (Feature feature in selected) result.InputFeatures.Add(feature);
                }
                if (result.InputFeatures.Count == 0)
                {
                    error = "当前没有已选要素：请先在地图上选择要素，或改选一个图层作为输入要素。";
                    return false;
                }
            }
            else
            {
                Layer layer = SelectedLayer(inputFeatures);
                if (layer == null)
                {
                    error = "请选择「输入要素」。";
                    return false;
                }
                result.InputLayer = layer;
                result.InputLayers.Add(layer);
                result.InputFeatures = new Features();
                foreach (Feature feature in layer.FeatureClass.Features) result.InputFeatures.Add(feature);
            }

            // ② 其他图层（只能是图层）
            result.OtherLayer = SelectedLayer(otherLayer);
            if (result.OtherLayer == null)
            {
                error = "请选择「" + otherLabel + "」。";
                return false;
            }
            if (result.OtherLayer.FeatureClass == null || result.OtherLayer.FeatureClass.Features.Count == 0)
            {
                error = "「" + otherLabel + "」图层没有要素。";
                return false;
            }

            // ③ 输出图层名
            result.OutputLayerName = (outputName.Text ?? "").Trim();
            if (result.OutputLayerName.Length == 0)
            {
                error = "请填写「输出图层名」。";
                return false;
            }
            if (Map != null && Map.Layers.Any(l => l != null && l.Name == result.OutputLayerName))
            {
                error = "输出图层名与已有图层重复：" + result.OutputLayerName;
                return false;
            }

            inputs = result;
            return true;
        }

        protected override void Execute(ToolRunInputs inputs)
        {
            // 收集两侧几何（连同来源要素，用于拷贝属性）
            var inputGeometries = new List<Geometry>();
            var inputSources = new List<Feature>();
            foreach (Feature feature in inputs.InputFeatures)
            {
                if (feature.Geometry == null) continue;
                inputGeometries.Add(feature.Geometry);
                inputSources.Add(feature);
            }
            var otherGeometries = new List<Geometry>();
            Feature otherSample = null;
            foreach (Feature feature in inputs.OtherLayer.FeatureClass.Features)
            {
                if (feature.Geometry == null) continue;
                if (otherSample == null) otherSample = feature;
                otherGeometries.Add(feature.Geometry);
            }
            if (inputGeometries.Count == 0)
            {
                ShowResult("运行失败：输入要素中没有有效几何。");
                return;
            }
            if (otherGeometries.Count == 0)
            {
                ShowResult("运行失败：「" + otherLabel + "」图层中没有有效几何。");
                return;
            }

            string note;
            List<OverlayEngine.ResultItem> results = Analyze(inputGeometries, inputSources, otherGeometries, otherSample, out note);
            if (results.Count == 0)
            {
                string empty = "运行完成：没有生成结果要素（两组图形不重叠，或叠加后剩余部分为空）。";
                if (string.IsNullOrEmpty(note) == false) empty += "\r\n说明：" + note;
                ShowResult(empty);
                return;
            }

            // 结果几何类型与字段模式
            GeometryTypeConstant resultType = ResultType(results);
            Fields fields = PickFields(inputs, results);
            var featureClass = new FeatureClass(inputs.OutputLayerName, resultType);
            foreach (Field field in fields)
                featureClass.Fields.Add(new Field(field.Name, field.ValueType, field.Alias));

            int added = 0;
            foreach (OverlayEngine.ResultItem item in results)
            {
                var feature = new Feature(item.Geometry, featureClass.Fields);
                CopyAttributes(item.Source, feature);
                try
                {
                    featureClass.Add(feature);      // 校验不通过的要素跳过
                    added++;
                }
                catch
                {
                    // 忽略个别几何类型不符的输出（例如面×线降维结果）
                }
            }
            if (added == 0)
            {
                ShowResult("运行完成：没有生成结果要素。");
                return;
            }

            Map.AddLayer(new Layer(inputs.OutputLayerName, featureClass));

            var summary = new System.Text.StringBuilder();
            summary.AppendLine("运行完成。");
            if (string.IsNullOrEmpty(note) == false)
                summary.AppendLine("说明：" + note);
            summary.AppendLine("输出图层：「" + inputs.OutputLayerName + "」（" +
                SummarizeGeometry(featuresToSummarize(featureClass)) + "，共 " + added + " 个要素）。");
            summary.Append("输入要素 " + inputs.FeatureCount + " 个（" + inputs.GeometrySummary + "），其他图层「" +
                inputs.OtherLayer.Name + "」共 " + inputs.OtherLayer.FeatureClass.Features.Count + " 个要素。");
            ShowResult(summary.ToString());
        }

        // 输出要素集合（仅用于结果摘要统计）
        private static Features featuresToSummarize(FeatureClass featureClass)
        {
            var features = new Features();
            foreach (Feature feature in featureClass.Features) features.Add(feature);
            return features;
        }

        // 结果几何类型：含面取面、否则含线取线、否则点
        private static GeometryTypeConstant ResultType(List<OverlayEngine.ResultItem> results)
        {
            bool hasPolygon = false, hasLine = false;
            foreach (OverlayEngine.ResultItem item in results)
            {
                if (item.Geometry == null) continue;
                if (item.Geometry.GeometryType == GeometryTypeConstant.Polygon ||
                    item.Geometry.GeometryType == GeometryTypeConstant.MultiPolygon) hasPolygon = true;
                else if (item.Geometry.GeometryType == GeometryTypeConstant.LineString ||
                         item.Geometry.GeometryType == GeometryTypeConstant.MultiLineString) hasLine = true;
            }
            if (hasPolygon) return GeometryTypeConstant.Polygon;
            if (hasLine) return GeometryTypeConstant.LineString;
            return GeometryTypeConstant.Point;
        }

        // 输出图层字段：优先取输入图层的字段模式，已选要素跨图层时取第一个带模式的来源
        private Fields PickFields(ToolRunInputs inputs, List<OverlayEngine.ResultItem> results)
        {
            if (inputs.InputLayer != null)
                return CloneFields(inputs.InputLayer.FeatureClass.Fields);
            foreach (OverlayEngine.ResultItem item in results)
            {
                if (item.Source != null && item.Source.Attributes != null && item.Source.Attributes.Fields != null)
                    return CloneFields(item.Source.Attributes.Fields);
            }
            return new Fields();
        }

        private static Fields CloneFields(Fields fields)
        {
            var output = new Fields();
            if (fields == null)
                return output;
            foreach (Field field in fields)
                output.Add(new Field(field.Name, field.ValueType, field.Alias));
            return output;
        }

        // 按字段名把来源要素的属性值拷贝到输出要素
        private static void CopyAttributes(Feature source, Feature target)
        {
            if (source == null || source.Attributes == null || target.Attributes == null || target.Attributes.Fields == null)
                return;
            foreach (Field field in target.Attributes.Fields)
            {
                object value = source.Attributes.GetItem(field.Name);
                if (value != null)
                    target.Attributes.SetItem(field.Name, value);
            }
        }
    }
}
