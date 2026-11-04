using System;
using System.Collections.Generic;
using GIS;
using GIS.Display;

namespace GIS.Toolbox.Tools.Overlay
{
    /// <summary>
    /// 交集取反（Symmetrical Difference）：计算输入要素与更新要素中互不重叠的部分，
    /// 两组要素维数需一致（点×点 / 线×线 / 面×面）。
    /// </summary>
    public sealed class SymDiffToolForm : OverlayToolFormBase
    {
        public SymDiffToolForm(MapControl map)
            : base(map, "交集取反（Symmetrical Difference）", "更新要素", null, "交集取反结果")
        {
        }

        protected override List<OverlayEngine.ResultItem> Analyze(
            IList<Geometry> inputGeometries, IList<Feature> inputSources,
            IList<Geometry> otherGeometries, Feature otherSample, out string note)
        {
            Feature sourceA = inputSources.Count > 0 ? inputSources[0] : null;
            Feature sourceB = otherSample;
            return OverlayEngine.SymDifference(inputGeometries, otherGeometries, sourceA, sourceB, out note);
        }
    }
}
