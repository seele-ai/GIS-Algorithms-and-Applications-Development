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
    /// <summary>三个邻近分析工具共用的输入、字段、坐标系与输出图层辅助逻辑。</summary>
    public abstract class ProximityToolFormBase : ToolFormBase
    {
        public const string DefaultUnit = "默认单位";
        public const string MeterUnit = "米";
        public const string KilometerUnit = "千米";
        public const string CentimeterUnit = "厘米";
        public const string MillimeterUnit = "毫米";
        public const string FootUnit = "英尺";
        public const string YardUnit = "码";
        public const string MileUnit = "英里";
        public const string NauticalMileUnit = "海里";
        public const string DecimalDegreeUnit = "十进制度";

        public const string AutoMethod = "自动（推荐）";
        public const string PlanarMethod = "平面";
        public const string GeodesicMethod = "测地线（球面近似）";

        protected ProximityToolFormBase(MapControl map, string title) : base(map, title)
        {
        }

        protected bool CollectSingleLayerInput(ComboBox combo, out ToolRunInputs inputs, out string error)
        {
            inputs = null;
            error = null;
            var result = new ToolRunInputs { InputFeatures = new Features() };
            if (combo == null || combo.SelectedIndex < 0)
            {
                error = "请选择「输入要素」。";
                return false;
            }

            if (IsSelectionOption(combo))
            {
                result.FromSelection = true;
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
                    error = "当前没有已选要素：请先选择要素，或改选一个图层。";
                    return false;
                }
                if (result.InputLayers.Count != 1)
                {
                    error = "邻近分析一次只能处理一个图层；当前已选要素来自 " +
                        result.InputLayers.Count + " 个图层，请只保留一个图层的选择。";
                    return false;
                }
                result.InputLayer = result.InputLayers[0];
            }
            else
            {
                Layer layer = SelectedLayer(combo);
                if (layer == null)
                {
                    error = "请选择「输入要素」。";
                    return false;
                }
                result.InputLayer = layer;
                result.InputLayers.Add(layer);
                foreach (Feature feature in layer.FeatureClass.Features) result.InputFeatures.Add(feature);
            }

            if (result.InputFeatures.Count == 0)
            {
                error = "输入图层没有要素。";
                return false;
            }
            inputs = result;
            return true;
        }

        protected bool ValidateOutputName(string value, out string name, out string error)
        {
            name = (value ?? "").Trim();
            error = null;
            if (name.Length == 0)
            {
                error = "请填写「输出图层名」。";
                return false;
            }
            string checkedName = name;
            if (Map != null && Map.Layers.Any(l => l != null &&
                string.Equals(l.Name, checkedName, StringComparison.OrdinalIgnoreCase)))
            {
                error = "输出图层名与已有图层重复：" + name;
                return false;
            }
            return true;
        }

        protected static bool TryParsePositiveDouble(string text, out double value)
        {
            text = (text ?? "").Trim();
            bool parsed = double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
                double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            return parsed && !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;
        }

        protected static bool TryParseDistances(string text, out List<double> distances, out string error)
        {
            int duplicateCount;
            return TryParseDistances(text, out distances, out duplicateCount, out error);
        }

        protected static bool TryParseDistances(string text, out List<double> distances,
            out int duplicateCount, out string error)
        {
            distances = new List<double>();
            duplicateCount = 0;
            error = null;
            string[] parts = (text ?? "").Split(new[] { ',', '，', ';', '；', ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                error = "请填写至少一个缓冲距离，例如 100, 200, 500。";
                return false;
            }
            foreach (string part in parts)
            {
                double value;
                if (!TryParsePositiveDouble(part, out value))
                {
                    error = "缓冲距离“" + part + "”不是大于 0 的有效数值。";
                    return false;
                }
                if (!distances.Any(d => Math.Abs(d - value) <= Math.Max(1d, value) * 1e-12))
                    distances.Add(value);
                else
                    duplicateCount++;
            }
            distances.Sort();
            return true;
        }

        protected static bool ConvertDistance(double value, string unit, Layer layer,
            out double coordinateDistance, out string error)
        {
            coordinateDistance = value;
            error = null;
            if (unit == DefaultUnit || string.IsNullOrEmpty(unit)) return true;
            if (layer == null || layer.FeatureClass == null || layer.FeatureClass.ProjectionCS == null)
            {
                error = "输入图层没有投影坐标系，不能把“" + unit +
                    "”换算为坐标单位；请选择“默认单位”或先投影图层。";
                return false;
            }
            double meters;
            switch (unit)
            {
                case KilometerUnit: meters = value * 1000d; break;
                case CentimeterUnit: meters = value / 100d; break;
                case MillimeterUnit: meters = value / 1000d; break;
                default: meters = value; break;
            }
            coordinateDistance = layer.FeatureClass.ProjectionCS.ToUnits(meters);
            return true;
        }

        /// <summary>把用户距离解析为平面坐标单位或测地线米，并给出最终缓冲方法。</summary>
        protected static bool ResolveDistance(double value, string unit, string method, Layer layer,
            out double operationDistance, out ProximityEngine.BufferOptions options, out string error)
        {
            operationDistance = value;
            options = new ProximityEngine.BufferOptions();
            error = null;
            if (double.IsNaN(value) || double.IsInfinity(value) || value == 0)
            {
                error = "缓冲距离不能为 0，且必须是有限数值。";
                return false;
            }
            if (layer == null || layer.FeatureClass == null)
            {
                error = "输入图层无效。";
                return false;
            }

            ProjectionCS projection = layer.FeatureClass.ProjectionCS;
            int? srid = layer.FeatureClass.Srid;
            bool knownGeographic = projection == null && IsKnownGeographicSrid(srid);
            bool inferredGeographic = projection == null &&
                (!srid.HasValue || srid.Value <= 0) && LooksLikeGeographicCoordinates(layer);
            bool geodesic;
            if (method == GeodesicMethod)
                geodesic = true;
            else if (method == PlanarMethod)
                geodesic = false;
            else
                geodesic = knownGeographic;

            if (method == AutoMethod && inferredGeographic && IsExplicitLinearUnit(unit))
            {
                error = "输入图层没有坐标系标识，坐标范围疑似经纬度。为避免误把工程坐标当作经纬度，请明确选择“" +
                    GeodesicMethod + "”，或先为图层定义坐标系。";
                return false;
            }

            options.Method = geodesic ? ProximityEngine.BufferMethod.Geodesic :
                ProximityEngine.BufferMethod.Planar;
            options.Projection = projection;
            options.Srid = srid;

            if (geodesic)
            {
                if (projection == null && !knownGeographic && !inferredGeographic)
                {
                    error = "测地线近似方法需要经纬度坐标。当前图层既没有 EPSG:4326/4490，坐标范围也不像经纬度；请检查坐标系或先投影图层。";
                    return false;
                }
                if (unit == DecimalDegreeUnit)
                {
                    error = "测地线近似距离必须使用线性单位，不能使用十进制度。";
                    return false;
                }
                if (unit == DefaultUnit || string.IsNullOrEmpty(unit))
                    operationDistance = projection == null ? value : projection.ToMeters(value);
                else
                    operationDistance = value * MetersPerUnit(unit);
                return true;
            }

            if (unit == DefaultUnit || string.IsNullOrEmpty(unit))
            {
                operationDistance = value;
                return true;
            }
            if (unit == DecimalDegreeUnit)
            {
                if (projection != null)
                {
                    error = "投影图层的平面缓冲不能使用十进制度。";
                    return false;
                }
                operationDistance = value;
                return true;
            }
            if (projection == null)
            {
                error = "平面方法无法把“" + unit + "”换算到经纬度或未知坐标；请选择“" +
                    GeodesicMethod + "”，或先投影图层。";
                return false;
            }
            operationDistance = projection.ToUnits(value * MetersPerUnit(unit));
            return true;
        }

        protected static bool IsKnownGeographicSrid(int? srid)
        {
            return srid == 4326 || srid == 4490;
        }

        /// <summary>图层缺少坐标系标识时，根据合法经纬度范围做保守推断。</summary>
        protected static bool LooksLikeGeographicCoordinates(Layer layer)
        {
            if (layer == null || layer.FeatureClass == null || layer.FeatureClass.Features.Count == 0)
                return false;
            Envelope extent = layer.FeatureClass.GetEnvelope();
            if (extent == null || extent.IsNull) return false;
            return extent.MinX >= -180d && extent.MaxX <= 180d &&
                extent.MinY >= -90d && extent.MaxY <= 90d;
        }

        /// <summary>是否因缺少 SRID 而仅根据坐标范围采用测地线，供结果提示使用。</summary>
        protected static bool UsesInferredGeographic(Layer layer, string unit, string method)
        {
            if (layer == null || layer.FeatureClass == null || layer.FeatureClass.ProjectionCS != null)
                return false;
            int? srid = layer.FeatureClass.Srid;
            if (IsKnownGeographicSrid(srid) || (srid.HasValue && srid.Value > 0)) return false;
            if (!LooksLikeGeographicCoordinates(layer)) return false;
            return method == GeodesicMethod;
        }

        /// <summary>返回泰森计算的坐标系风险提示；投影坐标系返回空字符串。</summary>
        protected static string ThiessenCoordinateNotice(Layer layer)
        {
            if (layer == null || layer.FeatureClass == null || layer.FeatureClass.ProjectionCS != null)
                return "";
            int? srid = layer.FeatureClass.Srid;
            if (IsKnownGeographicSrid(srid))
                return "警告：输入是经纬度坐标，泰森多边形仍按二维坐标计算，距离和边界可能失真；正式分析建议先投影到适合本地区的投影坐标系。";
            if (!srid.HasValue || srid.Value <= 0)
            {
                return LooksLikeGeographicCoordinates(layer)
                    ? "注意：输入图层未定义坐标系，坐标范围疑似经纬度，也可能是小范围工程坐标；本次按当前二维坐标计算，正式分析前请确认或定义坐标系。"
                    : "注意：输入图层未定义坐标系，本次按当前二维坐标计算；正式分析前请确认坐标单位。";
            }
            return "注意：当前坐标系没有可用的投影参数，本次按当前二维坐标计算；正式分析前请确认坐标单位。";
        }

        protected static void AddSkipDetail(IList<string> details, int sourceIndex, string reason)
        {
            if (details == null || details.Count >= 5) return;
            string indexText = sourceIndex >= 0 ? (sourceIndex + 1).ToString(CultureInfo.CurrentCulture) : "未知";
            string message = string.IsNullOrWhiteSpace(reason) ? "几何计算失败" : reason.Trim();
            details.Add("第 " + indexText + " 个要素：" + message);
        }

        protected static string BuildSkipNote(int skippedCount, IList<string> details, string itemName)
        {
            if (skippedCount <= 0) return "";
            string note = "\r\n已跳过 " + skippedCount + " 个" + itemName + "。";
            if (details != null && details.Count > 0)
                note += "\r\n原因示例：" + string.Join("；", details.ToArray());
            if (details != null && skippedCount > details.Count)
                note += "；其余 " + (skippedCount - details.Count) + " 个未展开";
            return note;
        }

        private static bool IsExplicitLinearUnit(string unit)
        {
            return !string.IsNullOrEmpty(unit) && unit != DefaultUnit && unit != DecimalDegreeUnit;
        }

        protected static bool IsNumericField(Field field)
        {
            if (field == null) return false;
            switch (field.ValueType)
            {
                case FieldTypeConstant.Byte:
                case FieldTypeConstant.Int16:
                case FieldTypeConstant.Int32:
                case FieldTypeConstant.Int64:
                case FieldTypeConstant.Single:
                case FieldTypeConstant.Double:
                case FieldTypeConstant.Decimal:
                    return true;
                default:
                    return false;
            }
        }

        private static double MetersPerUnit(string unit)
        {
            switch (unit)
            {
                case KilometerUnit: return 1000d;
                case CentimeterUnit: return 0.01d;
                case MillimeterUnit: return 0.001d;
                case FootUnit: return 0.3048d;
                case YardUnit: return 0.9144d;
                case MileUnit: return 1609.344d;
                case NauticalMileUnit: return 1852d;
                default: return 1d;
            }
        }

        protected static Fields CloneFields(Fields source)
        {
            var fields = new Fields();
            if (source == null) return fields;
            foreach (Field field in source)
                fields.Add(new Field(field.Name, field.ValueType, field.Alias));
            return fields;
        }

        protected static void AddFields(FeatureClass featureClass, Fields fields)
        {
            if (fields == null) return;
            foreach (Field field in fields)
                featureClass.Fields.Add(new Field(field.Name, field.ValueType, field.Alias));
        }

        protected static void CopyAttributes(Feature source, Feature target)
        {
            if (source == null || source.Attributes == null || target == null ||
                target.Attributes == null || target.Attributes.Fields == null) return;
            foreach (Field field in target.Attributes.Fields)
            {
                object value = source.Attributes.GetItem(field.Name);
                if (value != null) target.Attributes.SetItem(field.Name, value);
            }
        }

        protected static string UniqueFieldName(Fields fields, string preferred)
        {
            string name = preferred;
            int suffix = 1;
            while (fields != null && fields.FindField(name) >= 0)
                name = preferred + "_" + suffix++;
            return name;
        }

        protected static FeatureClass NewPolygonFeatureClass(string name, Layer sourceLayer)
        {
            var result = new FeatureClass(name, GeometryTypeConstant.Polygon);
            if (sourceLayer != null && sourceLayer.FeatureClass != null)
            {
                result.ProjectionCS = sourceLayer.FeatureClass.ProjectionCS;
                result.Srid = sourceLayer.FeatureClass.Srid;
            }
            return result;
        }

        protected void AddResultLayer(FeatureClass featureClass, Layer sourceLayer)
        {
            var layer = new Layer(featureClass.Name, featureClass);
            if (sourceLayer != null) layer.GeographicDatum = sourceLayer.GeographicDatum;
            layer.CaptureGeographic(featureClass.ProjectionCS);
            Map.AddLayer(layer);
        }

        protected static int SourceFeatureIndex(Layer layer, Feature source)
        {
            if (layer == null || layer.FeatureClass == null || source == null) return -1;
            int index = 0;
            foreach (Feature feature in layer.FeatureClass.Features)
            {
                if (ReferenceEquals(feature, source)) return index;
                index++;
            }
            return -1;
        }
    }
}
