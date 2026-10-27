using System;
using System.Collections.Generic;

namespace GIS.PostGIS
{
    /// <summary>
    /// 一个 PostGIS 图层的本地编辑会话。
    /// 编辑期间只修改内存中的 Layer；保存时才生成变更集并提交数据库。
    /// </summary>
    public sealed class PostGISEditSession
    {
        private readonly PostGISLayerContext _context;
        private readonly Dictionary<Feature, Feature> _snapshots;
        private readonly List<Feature> _originalOrder;

        public PostGISEditSession(PostGISLayerContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _snapshots = new Dictionary<Feature, Feature>(ReferenceComparer<Feature>.Instance);
            _originalOrder = new List<Feature>();
            Capture();
            IsEditing = true;
        }

        public PostGISLayerContext Context { get { return _context; } }
        public bool IsEditing { get; private set; }

        public bool HasChanges
        {
            get { return !BuildChangeSet().IsEmpty; }
        }

        /// <summary>根据当前图层内容生成新增、修改、删除集合。</summary>
        public PostGISChangeSet BuildChangeSet()
        {
            PostGISChangeSet changes = new PostGISChangeSet();
            Features current = _context.Layer.FeatureClass.Features;
            for (int i = 0; i < current.Count; i++)
            {
                Feature feature = current.GetItem(i);
                Feature snapshot;
                if (!_snapshots.TryGetValue(feature, out snapshot))
                {
                    changes.AddInsert(feature);
                }
                else if (!Equivalent(feature, snapshot))
                {
                    changes.AddUpdate(_context, feature);
                }
            }

            foreach (Feature original in _originalOrder)
            {
                if (!ContainsReference(current, original))
                    changes.AddDelete(_context, original);
            }
            return changes;
        }

        /// <summary>保存成功后刷新快照，允许用户继续编辑。</summary>
        public void Accept()
        {
            Capture();
            IsEditing = true;
        }

        /// <summary>放弃本次本地修改，恢复到最近一次成功保存后的状态。</summary>
        public void Discard()
        {
            Features current = _context.Layer.FeatureClass.Features;
            for (int i = current.Count - 1; i >= 0; i--)
            {
                Feature feature = current.GetItem(i);
                if (!_snapshots.ContainsKey(feature)) current.RemoveAt(i);
            }

            foreach (Feature original in _originalOrder)
            {
                Feature snapshot = _snapshots[original];
                original.Geometry = snapshot.Geometry == null ? null : snapshot.Geometry.Clone();
                original.Attributes = snapshot.Attributes == null ? null :
                    new Attributes(snapshot.Attributes.Fields, snapshot.Attributes.ToArray());
                if (!ContainsReference(current, original)) current.Add(original);
            }
            IsEditing = true;
        }

        private void Capture()
        {
            _snapshots.Clear();
            _originalOrder.Clear();
            Features features = _context.Layer.FeatureClass.Features;
            for (int i = 0; i < features.Count; i++)
            {
                Feature feature = features.GetItem(i);
                _originalOrder.Add(feature);
                _snapshots.Add(feature, feature.Clone());
            }
        }

        private static bool Equivalent(Feature left, Feature right)
        {
            if (left.Geometry == null || right.Geometry == null)
            {
                if (!ReferenceEquals(left.Geometry, right.Geometry)) return false;
            }
            else if (!string.Equals(left.Geometry.ToWKT(), right.Geometry.ToWKT(), StringComparison.Ordinal))
            {
                return false;
            }

            if (left.Attributes == null || right.Attributes == null)
                return ReferenceEquals(left.Attributes, right.Attributes);
            if (left.Attributes.Count != right.Attributes.Count) return false;
            for (int i = 0; i < left.Attributes.Count; i++)
            {
                object a = left.Attributes.GetItem(i);
                object b = right.Attributes.GetItem(i);
                if (!object.Equals(a, b)) return false;
            }
            return true;
        }

        private static bool ContainsReference(Features features, Feature target)
        {
            for (int i = 0; i < features.Count; i++)
                if (ReferenceEquals(features.GetItem(i), target)) return true;
            return false;
        }

        private sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
        {
            public static readonly ReferenceComparer<T> Instance = new ReferenceComparer<T>();
            public bool Equals(T x, T y) { return ReferenceEquals(x, y); }
            public int GetHashCode(T obj) { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj); }
        }
    }
}
