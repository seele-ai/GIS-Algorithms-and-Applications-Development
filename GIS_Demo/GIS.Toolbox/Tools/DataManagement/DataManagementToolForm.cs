using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using GIS.Display;
using GIS.Toolbox.UI;

namespace GIS.Toolbox.Tools.DataManagement
{
    /// <summary>数据管理工具统一参数页，按输入、参数、输出的顺序布置。</summary>
    public class DataManagementToolForm : ToolFormBase
    {
        public static readonly string[] Titles = { "要素包络矩形转面", "最小边界几何", "要素转线", "要素转点", "要素转面", "要素折点转点", "多部分到单部分", "在折点处分割线", "概化" };
        public readonly ComboBox InputCombo;
        public readonly ComboBox ModeCombo;
        public readonly ComboBox GroupCombo;
        public readonly CheckBox InsideCheck;
        public readonly TextBox OutputBox;
        public readonly TextBox ToleranceBox;
        public ManagementResult LastResult { get; private set; }
        private readonly ManagementOperation operation;
        public DataManagementToolForm(MapControl map, ManagementOperation operation)
            : base(map, Titles[(int)operation])
        {
            this.operation = operation;
            bool linear = operation == ManagementOperation.ToLine || operation == ManagementOperation.ToPolygon || operation == ManagementOperation.Vertices || operation == ManagementOperation.SplitAtVertices || operation == ManagementOperation.Generalize;
            if (linear) AcceptedInputTypes = new[] { GeometryTypeConstant.LineString, GeometryTypeConstant.MultiLineString, GeometryTypeConstant.Polygon, GeometryTypeConstant.MultiPolygon };
            InputCombo = AddInputFeaturesField("输入要素", linear ? (Func<Layer, bool>)(l => AcceptedInputTypes.Contains(l.FeatureClass.GeometryType)) : null);
            if (operation == ManagementOperation.MinimumBoundary)
            {
                ModeCombo = AddChoiceParameterField("几何类型", "按面积矩形", "按宽度矩形", "凸包", "圆形", "包络");
                GroupCombo = AddChoiceParameterField("分组方式", "每个要素分别计算", "全部输入要素作为一组");
            }
            if (operation == ManagementOperation.ToPoint)
                InsideCheck = AddCheckParametersField("内部（可选）", "确保输出点位于输入要素内")[0];
            if (operation == ManagementOperation.Vertices)
                ModeCombo = AddChoiceParameterField("点类型", "所有折点", "中点", "起始折点", "结束折点", "起点和结束折点");
            if (operation == ManagementOperation.Generalize) ToleranceBox = AddTextParameterField("最大允许偏移量", "输入坐标单位；经纬度为度，投影坐标按其单位", "0");
            OutputBox = AddOutputNameField("输出图层名", Titles[(int)operation] + "_结果");
            AddCustomField("处理说明", new Label { AutoSize = true, MaximumSize = new System.Drawing.Size(510, 0), Text =
                "所选图层有已选要素时仅处理选择集，否则处理全层。\r\n" +
                (operation == ManagementOperation.MultipartToSinglepart ? "复合要素按部件拆分，保留属性；孔洞随所属面保留。" :
                operation == ManagementOperation.SplitAtVertices ? "按相邻折点分线，包含面内外环；重复点形成的零长度段跳过。" :
                operation == ManagementOperation.Generalize ? "Douglas-Peucker 概化，仅删除顶点；容差为输入坐标单位，0保持原形。无效结果保留原几何。" :
                operation == ManagementOperation.ToPolygon ? "对线或面边界打断并构建闭合区域；开放线不会生成面。" :
                operation == ManagementOperation.ToLine ? "面边界转线，交点处分割；重叠线段合并并记录来源。" :
                operation == ManagementOperation.Vertices ? "按各部件和内外环提取；中点按沿线长度的一半计算。" :
                operation == ManagementOperation.MinimumBoundary ? "按当前坐标平面计算；圆形使用外切 128 边形近似。" :
                operation == ManagementOperation.ToPoint ? "默认输出质心；勾选内部可避免凹面质心落在面外。" : "每个输入要素生成一个与坐标轴平行的包络矩形。") });
            FinishLayout();
        }
        protected override bool CollectInputs(out ToolRunInputs inputs, out string error)
        {
            inputs = null; error = null;
            var result = new ToolRunInputs { InputFeatures = new Features() };
            if (InputCombo.SelectedIndex < 0) { error = "请选择输入要素。"; return false; }
            if (IsSelectionOption(InputCombo))
            {
                result.FromSelection = true;
                foreach (Layer layer in Map.Layers)
                {
                    var selected = Map.GetSelection(layer);
                    if (selected == null || selected.Count == 0) continue;
                    result.InputLayers.Add(layer);
                    foreach (Feature f in selected) result.InputFeatures.Add(f);
                }
            }
            else
            {
                Layer layer = SelectedLayer(InputCombo);
                if (layer == null) { error = "输入图层已不可用。"; return false; }
                result.InputLayers.Add(layer);
                var selected = Map.GetSelection(layer);
                result.FromSelection = selected != null && selected.Count > 0;
                foreach (Feature f in result.FromSelection ? selected : layer.FeatureClass.Features) result.InputFeatures.Add(f);
            }
            if (result.InputFeatures.Count == 0) { error = "没有输入要素，请选择图层或先在地图上选择要素。"; return false; }
            result.InputLayer = result.InputLayers[0];
            var first = result.InputLayer.FeatureClass;
            foreach (Layer layer in result.InputLayers.Skip(1))
            {
                var fc = layer.FeatureClass;
                bool fieldsMatch = first.Fields.Count == fc.Fields.Count && first.Fields.Cast<Field>().Zip(fc.Fields.Cast<Field>(), (a,b) => a.Name == b.Name && a.ValueType == b.ValueType).All(v => v);
                if (!fieldsMatch || first.Srid != fc.Srid || !ReferenceEquals(first.ProjectionCS, fc.ProjectionCS) || layer.GeographicDatum != result.InputLayer.GeographicDatum)
                { error = "跨图层选择要求字段及坐标系一致；请只选择一个图层的要素，或先统一字段与坐标系。"; return false; }
            }
            result.OutputLayerName = OutputBox.Text.Trim();
            if (result.OutputLayerName.Length == 0) { error = "请填写输出图层名。"; return false; }
            if (Map.Layers.Any(l => string.Equals(l.Name, result.OutputLayerName, StringComparison.OrdinalIgnoreCase)))
            { error = "输出图层名已存在，请修改名称。"; return false; }
            inputs = result; return true;
        }
        protected override void Execute(ToolRunInputs inputs)
        {
            double tolerance = 0;
            if (ToleranceBox != null && (!double.TryParse(ToleranceBox.Text, out tolerance) || double.IsNaN(tolerance) || double.IsInfinity(tolerance) || tolerance < 0)) throw new ArgumentException("容差必须为有限的非负数。");
            if (operation >= ManagementOperation.MultipartToSinglepart)
                LastResult = FeatureProcessingEngine.Run(operation, inputs.InputFeatures.Cast<Feature>().ToList(), inputs.InputLayer.FeatureClass, inputs.OutputLayerName, tolerance);
            else LastResult = DataManagementEngine.Run(operation, inputs.InputFeatures.Cast<Feature>().ToList(), inputs.InputLayer.FeatureClass,
                inputs.OutputLayerName, ModeCombo == null ? 0 : ModeCombo.SelectedIndex,
                InsideCheck != null && InsideCheck.Checked, GroupCombo != null && GroupCombo.SelectedIndex == 1);
            var layer = new Layer(LastResult.FeatureClass) { GeographicDatum = inputs.InputLayer.GeographicDatum };
            layer.CaptureGeographic(LastResult.FeatureClass.ProjectionCS);
            Map.AddLayer(layer);
            ShowResult("运行成功：" + Titles[(int)operation] + "\r\n输入 " + inputs.InputFeatures.Count + " 个要素" +
                (inputs.FromSelection ? "（选择集）" : "（整个图层）") + " → 输出 " + LastResult.FeatureClass.Features.Count + " 个要素。\r\n输出图层：" +
                inputs.OutputLayerName + "\r\n" + LastResult.Note);
        }
    }
}
