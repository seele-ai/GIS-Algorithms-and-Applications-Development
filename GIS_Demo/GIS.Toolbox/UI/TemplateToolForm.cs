using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using GIS.Display;

namespace GIS.Toolbox.UI
{
    /// <summary>
    /// 模板工具：工具界面的**模板**（后续同学照这个骨架写自己的空间分析工具）。
    ///
    /// 界面严格按下面的顺序排列（与需求一致）：
    ///   输入要素（下拉：第一项“（当前所有图层的已选要素）”，其后是按图层顺序的候选图层）
    ///   其他图层（下拉：只能选图层）
    ///   必要参数（手动输入：文本框）
    ///   必要参数（选择：下拉）
    ///   框选参数（一组勾选框）
    ///   输出图层名（文本框）
    ///   最右下角：运行
    ///
    /// 目前“运行”只做**校验 + 收集输入 + 打印结果摘要**（工具主体是空的），
    /// 后续同学把 <see cref="Execute"/> 里的占位换成真正的算法即可，其余界面与校验都复用本类。
    /// </summary>
    public sealed class TemplateToolForm : ToolFormBase
    {
        /// <summary>模板里的三个“框选参数”选项（示例用，真实工具自行替换）。</summary>
        public static readonly string[] TemplateCheckOptions =
        {
            "保留输入要素的全部属性",
            "只输出与「其他图层」相交的要素",
            "运行后缩放到输出图层"
        };

        private readonly ComboBox inputFeatures;
        private readonly ComboBox otherLayer;
        private readonly TextBox textParameter;
        private readonly ComboBox choiceParameter;
        private readonly CheckBox[] checkOptions;
        private readonly TextBox outputName;

        public TemplateToolForm(MapControl map)
            : base(map, "模板工具（工具界面模板）")
        {
            // ① 输入要素：第一项是“已选要素”，其后是当前所有图层（顶层在前）
            inputFeatures = AddInputFeaturesField("输入要素", null);
            // ② 其他图层：只能选图层（没有“已选要素”）
            otherLayer = AddLayerField("其他图层");
            // ③ 必要参数（手动输入）
            textParameter = AddTextParameterField("必要参数（手动输入）", "例如：100", "100");
            // ④ 必要参数（选择）
            choiceParameter = AddChoiceParameterField("必要参数（选择）", "选项一", "选项二", "选项三");
            // ⑤ 框选参数
            checkOptions = AddCheckParametersField("框选参数", TemplateCheckOptions);
            // ⑥ 输出图层名
            outputName = AddOutputNameField("输出图层名", "分析结果");
            // ⑦ 最右下角“运行”（由基类 FinishLayout 放置）
            FinishLayout();
        }

        #region 自动检查用的只读访问

        public ComboBox InputFeaturesCombo
        {
            get { return inputFeatures; }
        }

        public ComboBox OtherLayerCombo
        {
            get { return otherLayer; }
        }

        public TextBox TextParameterBox
        {
            get { return textParameter; }
        }

        public ComboBox ChoiceParameterCombo
        {
            get { return choiceParameter; }
        }

        public CheckBox[] CheckOptionBoxes
        {
            get { return checkOptions; }
        }

        public TextBox OutputNameBox
        {
            get { return outputName; }
        }

        #endregion

        /// <summary>把界面上的选择读成 <see cref="ToolRunInputs"/> 并做校验。</summary>
        protected override bool CollectInputs(out ToolRunInputs inputs, out string error)
        {
            inputs = null;
            error = null;

            var result = new ToolRunInputs();

            // ① 输入要素
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
                error = "请选择「其他图层」。";
                return false;
            }

            // ③ 必要参数（手动输入）
            result.TextParameter = (textParameter.Text ?? "").Trim();
            if (result.TextParameter.Length == 0)
            {
                error = "请填写「必要参数（手动输入）」。";
                return false;
            }

            // ④ 必要参数（选择）
            result.ChoiceParameter = choiceParameter.SelectedItem == null ? "" : choiceParameter.SelectedItem.ToString();
            if (result.ChoiceParameter.Length == 0)
            {
                error = "请选择「必要参数（选择）」。";
                return false;
            }

            // ⑤ 框选参数
            for (int i = 0; i < checkOptions.Length; i++)
                if (checkOptions[i].Checked) result.CheckedOptions.Add(checkOptions[i].Text);

            // ⑥ 输出图层名
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

        /// <summary>
        /// 工具主体（模板里是占位）：真实工具在这里调用空间分析算法，
        /// 把结果组成新的 <see cref="Layer"/> 后 Map.AddLayer(...) 即可。
        /// </summary>
        protected override void Execute(ToolRunInputs inputs)
        {
            ShowResult(inputs.Describe()
                + "——"
                + "以上输入已全部收集并通过校验。\r\n"
                + "工具主体待实现：在此处调用空间分析算法（例如叠加 / 邻近 / 数据管理），"
                + "把结果组成新图层「" + inputs.OutputLayerName + "」后加到地图上。");
        }
    }
}
