using System;

namespace GIS.PostGIS
{
    /// <summary>
    /// PostGIS 数据库主键与共享 Feature 对象的组合。
    /// 该类型只属于模块5，不修改其他模块的 Feature 定义。
    /// </summary>
    public sealed class PostGISFeatureRecord
    {
        public PostGISFeatureRecord(long id, Feature feature)
        {
            if (id <= 0)
                throw new ArgumentOutOfRangeException(nameof(id), "数据库主键必须大于 0。");
            Id = id;
            Feature = feature ?? throw new ArgumentNullException(nameof(feature));
        }

        public long Id { get; }
        public Feature Feature { get; }
    }
}