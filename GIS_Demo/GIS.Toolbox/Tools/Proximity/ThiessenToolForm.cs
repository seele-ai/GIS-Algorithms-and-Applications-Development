using System;
using System.Collections.Generic;
using System.Windows.Forms;
using GIS;
using GIS.Display;
using GIS.Toolbox.UI;

namespace GIS.Toolbox.Tools.Proximity
{
    /// <summary>从点要素创建泰森（Voronoi）多边形。</summary>
    public sealed class ThiessenToolForm : ProximityToolFormBase
    {
        public const string OnlyFid = "仅要素编号（默认）";
        public const string AllFields = "全部字段";
        public const string DefaultExtent = "默认边界（四周外扩 10%）";
        public const string CurrentMapExtent = "当前地图范围（裁剪外围单元）";

        private readonly ComboBox inputFeatures;
        private readonly ComboBox outputFields;
        private readonly ComboBox boundaryExtent;
        private readonly TextBox outputName;
        private string outputFieldMode;
        private string extentMode;

        public ThiessenToolForm(MapControl map) : base(map, "泰森多边形（Create Thiessen Polygons）")
        {
            inputFeatures = AddInputFeaturesField("输入要素（点）", layer =>
                layer.FeatureClass != null && layer.FeatureClass.GeometryType == GeometryTypeConstant.Point);
            outputFields = AddChoiceParameterField("输出字段", OnlyFid, AllFields);
            boundaryExtent = AddChoiceParameterField("外部边界", DefaultExtent, CurrentMapExtent);
            outputName = AddOutputNameField("输出图层名", "泰森多边形");
            AcceptedInputTypes = new[] { GeometryTypeConstant.Point };
            FinishLayout();
        }

        public ComboBox InputFeaturesCombo { get { return inputFeatures; } }
        public ComboBox OutputFieldsCombo { get { return outputFields; } }
        public ComboBox BoundaryExtentCombo { get { return boundaryExtent; } }
        public TextBox OutputNameBox { get { return outputName; } }

        protected override bool CollectInputs(out ToolRunInputs inputs, out string error)
        {
            if (!CollectSingleLayerInput(inputFeatures, out inputs, out error)) return false;
            outputFieldMode = outputFields.SelectedItem == null ? "" : outputFields.SelectedItem.ToString();
            if (outputFieldMode.Length == 0)
            {
                error = "请选择「输出字段」。";
                return false;
            }
            extentMode = boundaryExtent.SelectedItem == null ? DefaultExtent : boundaryExtent.SelectedItem.ToString();
            if (extentMode == CurrentMapExtent && (Map == null || Map.GetExtent() == null || Map.GetExtent().IsNull ||
                Map.GetExtent().Width <= 0 || Map.GetExtent().Height <= 0))
            {
                error = "当前地图显示范围无效，不能作为泰森多边形外部边界。";
                return false;
            }
            string name;
            if (!ValidateOutputName(outputName.Text, out name, out error)) return false;
            inputs.OutputLayerName = name;
            inputs.ChoiceParameter = outputFieldMode;
            return true;
        }

        protected override void Execute(ToolRunInputs inputs)
        {
            var sites = new List<ProximityEngine.ThiessenSite>();
            var skipDetails = new List<string>();
            int skippedInvalid = 0;
            foreach (Feature feature in inputs.InputFeatures)
            {
                var point = feature == null ? null : feature.Geometry as GIS.Point;
                int sourceIndex = SourceFeatureIndex(inputs.InputLayer, feature);
                if (point == null || point.IsEmpty ||
                    double.IsNaN(point.Coordinate.X) || double.IsInfinity(point.Coordinate.X) ||
                    double.IsNaN(point.Coordinate.Y) || double.IsInfinity(point.Coordinate.Y))
                {
                    skippedInvalid++;
                    AddSkipDetail(skipDetails, sourceIndex, "点几何为空或坐标无效");
                    continue;
                }
                sites.Add(new ProximityEngine.ThiessenSite
                {
                    Coordinate = point.Coordinate,
                    Source = feature,
                    SourceIndex = sourceIndex
                });
            }
            int duplicateCount;
            Envelope requestedExtent = extentMode == CurrentMapExtent ? Map.GetExtent() : null;
            List<ProximityEngine.ThiessenCell> cells = ProximityEngine.CreateThiessen(
                sites, requestedExtent, out duplicateCount);
            if (cells.Count == 0)
            {
                ShowResult("运行完成：没有生成泰森多边形。请检查输入点。" +
                    BuildSkipNote(skippedInvalid, skipDetails, "无效输入点"));
                return;
            }

            FeatureClass output = NewPolygonFeatureClass(inputs.OutputLayerName, inputs.InputLayer);
            if (outputFieldMode == AllFields)
                AddFields(output, CloneFields(inputs.InputLayer.FeatureClass.Fields));
            string fidName = UniqueFieldName(output.Fields, "ORIG_FID");
            output.Fields.Add(new Field(fidName, FieldTypeConstant.Int32, "原始要素编号"));

            int added = 0;
            foreach (ProximityEngine.ThiessenCell cell in cells)
            {
                if (cell.Geometry == null || cell.Geometry.IsEmpty) continue;
                var feature = new Feature(cell.Geometry, output.Fields);
                if (outputFieldMode == AllFields) CopyAttributes(cell.Site.Source, feature);
                feature.Attributes.SetItem(fidName, cell.Site.SourceIndex);
                output.Add(feature);
                added++;
            }
            if (added == 0)
            {
                ShowResult("运行完成：泰森多边形结果为空。");
                return;
            }

            AddResultLayer(output, inputs.InputLayer);
            string duplicateNote = duplicateCount == 0 ? "" :
                "\r\n已忽略坐标重复的输入点 " + duplicateCount + " 个（保留每组中的第一个点）。";
            string coordinateNotice = ThiessenCoordinateNotice(inputs.InputLayer);
            if (coordinateNotice.Length > 0) coordinateNotice = "\r\n" + coordinateNotice;
            string extentDescription = extentMode == DefaultExtent
                ? "输入点外包矩形四周各外扩 10%（仅用于截断外围无限延伸的泰森单元）"
                : "当前地图显示范围（用于截断外围泰森单元）";
            ShowResult("运行完成。\r\n输出图层：「" + inputs.OutputLayerName + "」（面 " + added +
                " 个）。\r\n外部边界：" + extentDescription + "；输出字段：" +
                outputFieldMode + "。" + duplicateNote + coordinateNotice +
                BuildSkipNote(skippedInvalid, skipDetails, "无效输入点"));
        }
    }
}
