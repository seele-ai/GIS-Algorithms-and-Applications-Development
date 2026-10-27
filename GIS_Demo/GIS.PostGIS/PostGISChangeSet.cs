using System;
using System.Collections.Generic;

namespace GIS.PostGIS
{
    /// <summary>一次“保存编辑”中需要原子提交的新增、修改和删除。</summary>
    public sealed class PostGISChangeSet
    {
        private readonly List<Feature> _inserts = new List<Feature>();
        private readonly List<PostGISFeatureUpdate> _updates = new List<PostGISFeatureUpdate>();
        private readonly List<long> _deletes = new List<long>();

        public IList<Feature> Inserts { get { return _inserts.AsReadOnly(); } }
        public IList<PostGISFeatureUpdate> Updates { get { return _updates.AsReadOnly(); } }
        public IList<long> Deletes { get { return _deletes.AsReadOnly(); } }
        public bool IsEmpty { get { return _inserts.Count == 0 && _updates.Count == 0 && _deletes.Count == 0; } }

        public void AddInsert(Feature feature)
        {
            if (feature == null) throw new ArgumentNullException(nameof(feature));
            foreach (Feature item in _inserts)
                if (ReferenceEquals(item, feature))
                    throw new InvalidOperationException("同一个新增 Feature 不能重复加入变更集合。");
            _inserts.Add(feature);
        }

        public void AddUpdate(long id, Feature feature)
        {
            ValidateId(id);
            if (feature == null) throw new ArgumentNullException(nameof(feature));
            if (_deletes.Contains(id))
                throw new InvalidOperationException("同一数据库记录不能同时修改和删除。");
            foreach (PostGISFeatureUpdate item in _updates)
                if (item.Id == id)
                    throw new InvalidOperationException("同一数据库记录不能重复加入修改集合。");
            _updates.Add(new PostGISFeatureUpdate(id, feature));
        }

        public void AddUpdate(PostGISLayerContext context, Feature feature)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            AddUpdate(context.GetDatabaseId(feature), feature);
        }

        public void AddDelete(long id)
        {
            ValidateId(id);
            foreach (PostGISFeatureUpdate item in _updates)
                if (item.Id == id)
                    throw new InvalidOperationException("同一数据库记录不能同时修改和删除。");
            if (_deletes.Contains(id))
                throw new InvalidOperationException("同一数据库记录不能重复加入删除集合。");
            _deletes.Add(id);
        }

        public void AddDelete(PostGISLayerContext context, Feature feature)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            AddDelete(context.GetDatabaseId(feature));
        }

        private static void ValidateId(long id)
        {
            // integer/bigint 主键允许 0 或负值；具体合法性由数据库约束决定。
        }
    }

    public sealed class PostGISFeatureUpdate
    {
        internal PostGISFeatureUpdate(long id, Feature feature) { Id = id; Feature = feature; }
        public long Id { get; }
        public Feature Feature { get; }
    }
}
