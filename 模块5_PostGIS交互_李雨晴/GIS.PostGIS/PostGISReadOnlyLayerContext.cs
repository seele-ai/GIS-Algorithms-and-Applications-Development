using System;
using System.Collections.Generic;

namespace GIS.PostGIS
{
    /// <summary>
    /// 只读 PostGIS 图层上下文。只读加载不要求数据库主键，适用于普通空间表和空间视图。
    /// </summary>
    public sealed class PostGISReadOnlyLayerContext
    {
        public PostGISReadOnlyLayerContext(Layer layer, PostGISLayerSource source,
            IEnumerable<string> warnings = null)
        {
            Layer = layer ?? throw new ArgumentNullException(nameof(layer));
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Warnings = new List<string>(warnings ?? new string[0]).AsReadOnly();
        }

        public Layer Layer { get; }
        public PostGISLayerSource Source { get; }
        public IReadOnlyList<string> Warnings { get; }
    }
}
