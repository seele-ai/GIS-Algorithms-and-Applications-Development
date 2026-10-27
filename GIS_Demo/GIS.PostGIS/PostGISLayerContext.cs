using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace GIS.PostGIS
{
    /// <summary>
    /// 地图图层及其 PostGIS 来源、主键映射。通过 Feature 对象引用定位数据库记录，
    /// 避免使用会因排序、筛选或删除而变化的集合下标。
    /// </summary>
    public sealed class PostGISLayerContext
    {
        private readonly Dictionary<Feature, long> _idsByFeature;
        private readonly Dictionary<long, Feature> _featuresById;

        public PostGISLayerContext(Layer layer, PostGISLayerSource source,
            IEnumerable<PostGISFeatureRecord> records)
        {
            Layer = layer ?? throw new ArgumentNullException(nameof(layer));
            Source = source ?? throw new ArgumentNullException(nameof(source));
            if (records == null) throw new ArgumentNullException(nameof(records));

            _idsByFeature = new Dictionary<Feature, long>(ReferenceComparer<Feature>.Instance);
            _featuresById = new Dictionary<long, Feature>();
            foreach (PostGISFeatureRecord record in records)
            {
                if (record == null)
                    throw new ArgumentException("记录集合不能包含 null。", nameof(records));
                if (_idsByFeature.ContainsKey(record.Feature))
                    throw new ArgumentException("同一个 Feature 不能对应多个数据库主键。", nameof(records));
                if (_featuresById.ContainsKey(record.Id))
                    throw new ArgumentException("数据库主键不能重复：" + record.Id, nameof(records));
                _idsByFeature.Add(record.Feature, record.Id);
                _featuresById.Add(record.Id, record.Feature);
            }

            ValidateLayerFeatures();
        }

        public Layer Layer { get; }
        public PostGISLayerSource Source { get; }
        public int RecordCount { get { return _idsByFeature.Count; } }

        public bool TryGetDatabaseId(Feature feature, out long id)
        {
            if (feature == null)
            {
                id = 0;
                return false;
            }
            return _idsByFeature.TryGetValue(feature, out id);
        }

        public long GetDatabaseId(Feature feature)
        {
            long id;
            if (!TryGetDatabaseId(feature, out id))
                throw new KeyNotFoundException("该 Feature 不属于此 PostGIS 图层，或尚未保存到数据库。");
            return id;
        }

        public bool TryGetFeature(long id, out Feature feature)
        {
            return _featuresById.TryGetValue(id, out feature);
        }

        internal void ApplyCommittedChanges(PostGISCommitResult result, IList<long> deletedIds)
        {
            foreach (KeyValuePair<Feature, long> item in result.InsertedIds)
            {
                _idsByFeature.Add(item.Key, item.Value);
                _featuresById.Add(item.Value, item.Key);
            }
            foreach (long id in deletedIds)
            {
                Feature feature;
                if (_featuresById.TryGetValue(id, out feature))
                {
                    _featuresById.Remove(id);
                    _idsByFeature.Remove(feature);
                }
            }
        }

        private void ValidateLayerFeatures()
        {
            Features features = Layer.FeatureClass.Features;
            if (features.Count != _idsByFeature.Count)
                throw new ArgumentException("图层要素数量与数据库记录数量不一致。", nameof(Layer));
            for (int i = 0; i < features.Count; i++)
            {
                if (!_idsByFeature.ContainsKey(features.GetItem(i)))
                    throw new ArgumentException("图层中存在没有数据库主键映射的 Feature。", nameof(Layer));
            }
        }

        private sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
        {
            public static readonly ReferenceComparer<T> Instance = new ReferenceComparer<T>();
            public bool Equals(T x, T y) { return ReferenceEquals(x, y); }
            public int GetHashCode(T obj) { return RuntimeHelpers.GetHashCode(obj); }
        }
    }
}
