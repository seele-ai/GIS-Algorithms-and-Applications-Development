using System;
using System.Collections.Generic;
using GIS;
using GIS.Display;

namespace GIS.Toolbox.Tools.Overlay
{
    /// <summary>
    /// 裁剪（Clip）：提取与裁剪要素相重叠的输入要素。
    /// 输入要素（点线面）中与裁剪面（其他图层）重叠的部分被输出为"输出图层名"图层。
    /// </summary>
    public sealed class ClipToolForm : OverlayToolFormBase
    {
        public ClipToolForm(MapControl map)
            : base(map, "裁剪（Clip）", "裁剪要素", PolygonLayerFilter, "裁剪结果")
        {
        }

        protected override List<OverlayEngine.ResultItem> Analyze(
            IList<Geometry> inputGeometries, IList<Feature> inputSources,
            IList<Geometry> otherGeometries, Feature otherSample, out string note)
        {
            note = null;
            var clipPolygons = new List<Polygon>();
            foreach (Geometry geometry in otherGeometries)
                clipPolygons.AddRange(OverlayEngine.PolygonsOf(geometry));

            var results = new List<OverlayEngine.ResultItem>();
            for (int i = 0; i <= inputGeometries.Count - 1; i++)
                results.AddRange(OverlayEngine.Clip(inputGeometries[i], clipPolygons, inputSources[i]));
            return results;
        }
    }
}
