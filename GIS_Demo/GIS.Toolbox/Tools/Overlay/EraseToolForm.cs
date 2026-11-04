using System;
using System.Collections.Generic;
using GIS;
using GIS.Display;

namespace GIS.Toolbox.Tools.Overlay
{
    /// <summary>
    /// 擦除（Erase）：输入要素与擦除面叠加后，只保留位于擦除面之外的部分。
    /// </summary>
    public sealed class EraseToolForm : OverlayToolFormBase
    {
        public EraseToolForm(MapControl map)
            : base(map, "擦除（Erase）", "擦除要素", PolygonLayerFilter, "擦除结果")
        {
        }

        protected override List<OverlayEngine.ResultItem> Analyze(
            IList<Geometry> inputGeometries, IList<Feature> inputSources,
            IList<Geometry> otherGeometries, Feature otherSample, out string note)
        {
            note = null;
            var erasePolygons = new List<Polygon>();
            foreach (Geometry geometry in otherGeometries)
                erasePolygons.AddRange(OverlayEngine.PolygonsOf(geometry));

            var results = new List<OverlayEngine.ResultItem>();
            for (int i = 0; i <= inputGeometries.Count - 1; i++)
                results.AddRange(OverlayEngine.Erase(inputGeometries[i], erasePolygons, inputSources[i]));
            return results;
        }
    }
}
