using System;
using System.Collections.Generic;
using GIS;
using GIS.Display;

namespace GIS.Toolbox.Tools.Overlay
{
    /// <summary>
    /// 相交（Intersect）：计算输入要素与相交图层的几何交集，
    /// 输出维数取两者中较低的维数（点&lt;线&lt;面；线×线输出交点）。
    /// </summary>
    public sealed class IntersectToolForm : OverlayToolFormBase
    {
        public IntersectToolForm(MapControl map)
            : base(map, "相交（Intersect）", "相交图层", null, "相交结果")
        {
        }

        protected override List<OverlayEngine.ResultItem> Analyze(
            IList<Geometry> inputGeometries, IList<Feature> inputSources,
            IList<Geometry> otherGeometries, Feature otherSample, out string note)
        {
            note = null;
            var results = new List<OverlayEngine.ResultItem>();
            for (int i = 0; i <= inputGeometries.Count - 1; i++)
                for (int j = 0; j <= otherGeometries.Count - 1; j++)
                    results.AddRange(OverlayEngine.Intersect(inputGeometries[i], otherGeometries[j],
                        inputSources[i], otherSample));
            return results;
        }
    }
}
